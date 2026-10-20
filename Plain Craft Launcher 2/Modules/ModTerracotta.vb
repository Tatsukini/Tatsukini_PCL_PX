Imports System.IO
Imports System.IO.Compression
Imports System.Text

''' <summary>
''' 陶瓦联机（Terracotta）内核：下载、启动，以及与其内嵌的 HTTP API 通信。
''' 陶瓦只监听 127.0.0.1，端口随机，因此必须先拿到端口才能调用接口。
''' </summary>
Public Module ModTerracotta

#Region "常量"

    ''' <summary>
    ''' 随包分发的陶瓦版本。更新时需要同步下载地址与校验值。
    ''' </summary>
    Public Const TerraVersion As String = "0.4.2"
    ''' <summary>
    ''' 官方发布包的 SHA256。
    ''' AGPL 例外条款只允许分发未经修改的二进制，所以必须校验，以免镜像站篡改后被直接执行。
    ''' </summary>
    Public Const TerraArchiveHash As String = "07ebe139e3ca5f74576e58b1a96efe59abdfbe148d3f1a49bfdca8b6f70745f0"
    Public Const TerraArchiveSize As Long = 8135914

    Public Const TerraDisplayName As String = "Terracotta | 陶瓦联机"
    Public Const TerraCopyright As String = "Copyright © burningtnt"
    Public Const TerraWebsite As String = "https://github.com/burningtnt/Terracotta"
    Public Const TerraLicenseText As String =
        "联机功能由 " & TerraDisplayName & "（" & TerraCopyright & "）提供，" &
        "以未经修改的二进制形式分发，并通过其 HTTP API 与之交互。" &
        "陶瓦联机采用 AGPL-3.0-or-later 许可，并附有上述例外条款。"
    ''' <summary>
    ''' 陶瓦 README 中的例外条款原文，逐字保留。
    ''' </summary>
    Public Const TerraLicenseDetail As String =
        "AGPL 的例外情况：" & vbCrLf &
        "作为特例，如果您的程序通过以下方式利用本作品，则相应的行为不会导致您的作品被 AGPL 协议涵盖。" & vbCrLf &
        "1. 您的程序通过打包的方式包含本作品未经修改的二进制形式，而没有静态或动态地链接到本作品；或" & vbCrLf &
        "2. 您的程序通过本作品提供的进程间通信接口（如 HTTP API）与未经修改的本作品应用程序进行交互，且在您的程序用户界面明显处标识了本作品的版权信息。" & vbCrLf & vbCrLf &
        "完整许可文本见 " & TerraWebsite & "/blob/master/LICENSE"

    ''' <summary>
    ''' 房间邀请码格式：U/XXXX-XXXX-XXXX-XXXX。
    ''' </summary>
    Public Const TerraRoomHint As String = "U/XXXX-XXXX-XXXX-XXXX"

    ''' <summary>
    ''' 陶瓦 HTTP 服务所在目录下的端口发现文件。
    ''' </summary>
    Private ReadOnly Property TerraPortFile As String
        Get
            Return TerraFolder & "port.json"
        End Get
    End Property
    ''' <summary>
    ''' 陶瓦自己写入的锁文件，内容是 2 字节大端端口号。可用于发现其他启动器已经拉起的陶瓦。
    ''' </summary>
    Private ReadOnly Property TerraLockFile As String
        Get
            Return Path.Combine(Path.GetTempPath(), "terracotta\terracotta.lock")
        End Get
    End Property

    Public ReadOnly Property TerraFolder As String
        Get
            Return Paths.AppDataThenName & "Terracotta\"
        End Get
    End Property
    ''' <summary>
    ''' 发布包的临时存放位置，解压成功后会被删除。
    ''' </summary>
    Public ReadOnly Property TerraArchivePath As String
        Get
            Return TerraFolder & "terracotta.tar.gz"
        End Get
    End Property

    ''' <summary>
    ''' 当前陶瓦的 CPU 架构名称，与发布文件名一致。不支持时抛出异常。
    ''' </summary>
    Public Function TerraArch() As String
        'WOW64 下 PROCESSOR_ARCHITECTURE 是本进程的架构，需要优先读 PROCESSOR_ARCHITEW6432
        Dim Name = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432")
        If String.IsNullOrEmpty(Name) Then Name = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")
        Select Case If(Name, "").Trim.ToUpperInvariant
            Case "AMD64", "X64"
                Return "x86_64"
            Case "ARM64", "AARCH64"
                Return "arm64"
            Case Else
                Throw New Exception($"$陶瓦联机只支持 64 位（x86_64 / arm64）的 Windows，当前架构为 {If(Name, "未知")}。")
        End Select
    End Function

    ''' <summary>
    ''' 发布包中未经修改的可执行文件名。
    ''' </summary>
    Public Function TerraExeName(Optional Version As String = Nothing) As String
        Return $"terracotta-{If(Version, TerraVersion)}-windows-{TerraArch()}.exe"
    End Function
    Public Function TerraExePath() As String
        Return TerraFolder & TerraExeName()
    End Function
    ''' <summary>
    ''' 发布包的下载地址。直连优先，失败后自动改用国内镜像。
    ''' </summary>
    Public Function TerraDownloadUrls() As List(Of String)
        Dim Asset = $"{TerraWebsite}/releases/download/v{TerraVersion}/{TerraExeName.Replace(".exe", "")}-pkg.tar.gz"
        Return {
            Asset,
            "https://gh-proxy.com/" & Asset,
            "https://ghproxy.net/" & Asset,
            "https://gh-proxy.org/" & Asset
        }.ToList()
    End Function

#End Region

#Region "运行状态"

    ''' <summary>
    ''' 陶瓦 HTTP 服务的端口。0 表示尚未取得。
    ''' </summary>
    Public TerraPort As Integer = 0
    ''' <summary>
    ''' GET /meta 的结果，包含版本与内置 EasyTier 版本。
    ''' </summary>
    Public TerraMeta As JObject = Nothing
    ''' <summary>
    ''' 由本启动器拉起的引导进程。引导进程写完端口文件就会退出，真正的工作进程是它派生的子进程。
    ''' </summary>
    Private TerraBootstrap As Process = Nothing

    Public Function TerraIsDownloaded() As Boolean
        Return FileUtils.Exists(TerraExePath())
    End Function

    ''' <summary>
    ''' 陶瓦的 Web 控制面板地址，可以在浏览器里打开。
    ''' </summary>
    Public Function TerraPanelUrl() As String
        If TerraPort = 0 Then Return Nothing
        Return $"http://127.0.0.1:{TerraPort}/"
    End Function

    ''' <summary>
    ''' 读取锁文件里记录的端口。文件不存在或进程已退出时返回 0。
    ''' </summary>
    Private Function TerraReadLockPort() As Integer
        Try
            If Not FileUtils.Exists(TerraLockFile) Then Return 0
            Dim Bytes = FileUtils.ReadAsBytes(TerraLockFile)
            If Bytes.Length < 2 Then Return 0
            Return CType(Bytes(0), Integer) << 8 Or Bytes(1)
        Catch ex As Exception
            Logger.Warn(ex, "读取陶瓦锁文件失败")
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' 询问指定端口上是否有陶瓦在运行。
    ''' </summary>
    Public Function TerraProbe(Port As Integer) As JObject
        If Port <= 0 OrElse Port > 65535 Then Return Nothing
        Try
            Return TerraRequest("meta", Port, 1500).DeserializeJson(Of JObject)()
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 取得需要下载的文件。内核已就绪时返回空列表。
    ''' 把它交给 LoaderDownload，就能得到进度、重试与取消支持。
    ''' </summary>
    Public Function TerraRequiredFiles() As List(Of NetFile)
        Dim Arch = TerraArch() '先检查架构，不支持时不必浪费带宽
        If TerraIsDownloaded() Then
            Logger.Info($"陶瓦已就绪：{TerraExeName()}")
            Return New List(Of NetFile)
        End If
        DirectoryUtils.Create(TerraFolder)
        Return {New NetFile(TerraDownloadUrls(), TerraArchivePath,
            New FileChecker With {.ActualSize = TerraArchiveSize, .Hash = TerraArchiveHash})}.ToList()
    End Function

    ''' <summary>
    ''' 把已下载的发布包解压到内核目录。
    ''' 必须在工作线程执行。
    ''' </summary>
    Public Sub TerraExtractDownloaded()
        If TerraIsDownloaded() Then Return
        If Not FileUtils.Exists(TerraArchivePath) Then Throw New Exception("$没有找到陶瓦的下载文件。")
        TerraExtract(TerraArchivePath, TerraFolder)
        FileUtils.Delete(TerraArchivePath)
        If Not TerraIsDownloaded() Then Throw New Exception($"$陶瓦解压后找不到 {TerraExeName()}，发布包结构可能已变化。")
        Logger.Info($"陶瓦内核已就绪：{TerraExeName()}")
    End Sub

    ''' <summary>
    ''' 启动陶瓦并取得端口。已有实例在运行时直接复用，不会重复启动进程。
    ''' 必须在工作线程执行。
    ''' </summary>
    Public Sub TerraStart(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '已有实例？HMCL 等启动器可能已经拉起了陶瓦，直接复用它
        Dim LockPort = TerraReadLockPort()
        Dim Meta = TerraProbe(LockPort)
        If Meta IsNot Nothing Then
            TerraPort = LockPort
            TerraMeta = Meta
            Logger.Info($"复用了已在运行的陶瓦，端口 {TerraPort}")
            Return
        End If
        If Not TerraIsDownloaded() Then Throw New Exception($"$尚未找到陶瓦内核 {TerraExeName()}。")
        '引导进程会把端口写进 PortFile，然后自行退出；真正工作的是它派生出的子进程
        FileUtils.Delete(TerraPortFile)
        Dim Info As New ProcessStartInfo With {
            .FileName = TerraExePath(),
            .Arguments = $"--hmcl ""{TerraPortFile}""",
            .WorkingDirectory = TerraFolder,
            .UseShellExecute = False,
            .CreateNoWindow = True
        }
        Logger.Info($"正在启动陶瓦：{Info.Arguments}")
        TerraBootstrap = Process.Start(Info)
        '等待端口文件
        Dim StartTicks = Environment.TickCount
        Do
            Thread.Sleep(100)
            If FileUtils.Exists(TerraPortFile) Then
                Dim Port = CInt(FileUtils.ReadAsJson(TerraPortFile)("port"))
                Dim NewMeta = TerraProbe(Port)
                If NewMeta IsNot Nothing Then
                    TerraPort = Port
                    TerraMeta = NewMeta
                    Logger.Info($"陶瓦已启动，端口 {Port}")
                    Exit Do
                End If
            End If
            '引导进程 panic 退出（退出码非 0）时立刻失败，不必等满 15 秒
            If TerraBootstrap.HasExited AndAlso TerraBootstrap.ExitCode <> 0 Then
                Throw New Exception($"$陶瓦启动失败，退出码 {TerraBootstrap.ExitCode}。{vbCrLf}你可以在 {TerraLockFile.Replace("terracotta.lock", "")} 下查看它的日志。")
            End If
            If LoaderToSyncProgress IsNot Nothing Then LoaderToSyncProgress.Progress = 0.5 + 0.5 * Math.Min(1, (Environment.TickCount - StartTicks) / 15000.0)
            If Environment.TickCount - StartTicks > 15000 Then
                Throw New Exception($"$陶瓦在 15 秒内没有响应。{vbCrLf}你可以在 {TerraLockFile.Replace("terracotta.lock", "")} 下查看它的日志。{vbCrLf}如果问题一直存在，可以点击下方的【重新下载内核】。")
            End If
        Loop
    End Sub

    ''' <summary>
    ''' 让陶瓦自行退出。它可能是在别的启动器里被拉起的，所以只调用接口，不杀进程。
    ''' 必须在工作线程执行。
    ''' </summary>
    Public Sub TerraQuit()
        If TerraPort = 0 Then Return
        Try
            TerraRequest("panic?peaceful=true", TerraPort, 3000)
        Catch ex As Exception
            Logger.Warn(ex, "通知陶瓦退出失败")
        End Try
        TerraPort = 0
        TerraMeta = Nothing
    End Sub

    ''' <summary>
    ''' 重新下载内核。
    ''' </summary>
    Public Sub TerraReinstall()
        FileUtils.Delete(TerraExePath())
        FileUtils.Delete(TerraFolder & "VCRUNTIME140.DLL")
    End Sub

#End Region

#Region "HTTP API"

    ''' <summary>
    ''' 向陶瓦发送一次 GET 请求。陶瓦在本地回环地址上，不需要重试与日志。
    ''' </summary>
    Public Function TerraRequest(Path As String, Optional Port As Integer = -1, Optional Timeout As Integer = 5000) As String
        If Port = -1 Then Port = TerraPort
        If Port <= 0 Then Throw New Exception("陶瓦联机尚未启动。")
        Return NetRequestByClient($"http://127.0.0.1:{Port}/{Path}", Timeout:=Timeout, MakeLog:=False)
    End Function

    ''' <summary>
    ''' 读取当前房间状态。每次状态变化都会使 index 递增，轮询方应当忽略 index 没变的结果。
    ''' </summary>
    Public Function TerraGetState() As TerraState
        Dim Json = TerraRequest("state", Timeout:=3000).DeserializeJson(Of JObject)()
        Return TerraState.Parse(Json)
    End Function

    ''' <summary>
    ''' 以房主身份开房。Room 为空则由陶瓦生成新房间码。
    ''' </summary>
    Public Sub TerraHost(Room As String, Player As String, Nodes As IEnumerable(Of String))
        TerraRequest("state/scanning" & TerraQuery(Room, Player, Nodes))
    End Sub

    ''' <summary>
    ''' 以加入方身份加入房间。邀请码无效时陶瓦返回 400。
    ''' </summary>
    Public Sub TerraJoin(Room As String, Player As String, Nodes As IEnumerable(Of String))
        Try
            TerraRequest("state/guesting" & TerraQuery(Room, Player, Nodes))
        Catch ex As HttpRequestCodeException
            If ex.StatusCode = HttpStatusCode.BadRequest Then Throw New Exception($"$房间码 {Room} 无效，请检查是否输错了。")
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' 回到待机状态，同时关闭当前房间。
    ''' </summary>
    Public Sub TerraReset()
        TerraRequest("state/ide")
    End Sub

    Private Function TerraQuery(Room As String, Player As String, Nodes As IEnumerable(Of String)) As String
        Dim Result = ""
        If Not String.IsNullOrEmpty(Room) Then Result &= $"&room={Uri.EscapeDataString(Room)}"
        If Not String.IsNullOrEmpty(Player) Then Result &= $"&player={Uri.EscapeDataString(Player)}"
        For Each Node In If(Nodes, New String(0) {})
            If Not String.IsNullOrEmpty(Node) Then Result &= $"&public_nodes={Uri.EscapeDataString(Node.Trim)}"
        Next
        If Result.Length > 0 Then Result = "?" & Result.Substring(1)
        Return Result
    End Function

#End Region

#Region "状态模型"

    Public Class TerraProfile
        Public MachineId As String
        Public Name As String
        Public Vendor As String
        Public Kind As String
        Public ReadOnly Property IsHost As Boolean
            Get
                Return Kind = "HOST"
            End Get
        End Property
    End Class

    ''' <summary>
    ''' GET /state 的解析结果。
    ''' </summary>
    Public Class TerraState
        ''' <summary>陶瓦报告的状态字符串，例如 waiting、host-ok。</summary>
        Public Name As String
        Public Index As Long
        Public Room As String
        ''' <summary>加入成功后可用于多人游戏的直连地址。</summary>
        Public DirectUrl As String
        Public Difficulty As String
        Public ExceptionType As Integer = -1
        Public Profiles As New List(Of TerraProfile)

        Public ReadOnly Property IsHostSide As Boolean
            Get
                Return Name.StartsWithF("host")
            End Get
        End Property
        Public ReadOnly Property IsReady As Boolean
            Get
                Return Name = "host-ok" OrElse Name = "guest-ok"
            End Get
        End Property

        Public Shared Function Parse(Json As JObject) As TerraState
            Dim Result As New TerraState With {.Name = Json("state")?.ToString()}
            If Result.Name Is Nothing Then Throw New Exception($"陶瓦返回了无法理解的内容：{Json}")
            Result.Index = If(Json("index") Is Nothing, -1, CLng(Json("index")))
            Result.Room = Json("room")?.ToString()
            Result.DirectUrl = Json("url")?.ToString()
            Result.Difficulty = Json("difficulty")?.ToString()
            If Json("type") IsNot Nothing Then Result.ExceptionType = CInt(Json("type"))
            If Json("profiles") IsNot Nothing Then
                For Each Profile In Json("profiles")
                    Result.Profiles.Add(New TerraProfile With {
                        .MachineId = Profile("machine_id")?.ToString(),
                        .Name = Profile("name")?.ToString(),
                        .Vendor = Profile("vendor")?.ToString(),
                        .Kind = Profile("kind")?.ToString()})
                Next
            End If
            Return Result
        End Function

        ''' <summary>
        ''' 状态的中文描述。
        ''' </summary>
        Public Function Description() As String
            Select Case Name
                Case "waiting" : Return "待机中"
                Case "host-scanning" : Return "正在查找本机已开启的局域网世界……请先在游戏内按 Esc 选择【对局域网开放】。"
                Case "host-starting" : Return "正在创建房间……"
                Case "host-ok" : Return "房间已就绪，把邀请码发给朋友吧。"
                Case "guest-connecting" : Return "正在连接到节点……"
                Case "guest-starting" : Return "正在连接到房主……"
                Case "guest-ok" : Return "已成功加入房间，进入游戏的多人游戏列表即可看到房主。"
                Case "exception"
                    Select Case ExceptionType
                        Case 0 : Return "无法连接到房主，房间可能已经关闭。"
                        Case 1 : Return "与房主的连接中断了。"
                        Case 2 : Return "加入方的联机模块崩溃了。"
                        Case 3 : Return "房主的联机模块崩溃了。"
                        Case 4 : Return "房主退出了存档，房间已自动关闭。"
                        Case 5 : Return "脚手架协议出错，请向陶瓦开发者反馈。"
                        Case Else : Return $"发生未知错误（类型 {ExceptionType}）。"
                    End Select
                Case Else : Return Name
            End Select
        End Function

        ''' <summary>
        ''' NAT 连接难度的中文描述。
        ''' </summary>
        Public Function DifficultyDescription() As String
            Select Case Difficulty
                Case "EASIEST" : Return "网络环境很好，几乎一定连得上"
                Case "SIMPLE" : Return "网络环境良好"
                Case "MEDIUM" : Return "网络环境一般，可能需要一点时间"
                Case "TOUGH" : Return "网络环境较差，可能连不上"
                Case Else : Return Nothing
            End Select
        End Function
    End Class

#End Region

#Region "tar.gz 解压"

    ''' <summary>
    ''' 解压 tar.gz。陶瓦的发布包是 tar.gz，而 MeloongCore 只支持 zip 与单个 gz 文件，所以自己实现。
    ''' 只处理普通文件与目录，并拒绝压缩包之外的路径。
    ''' </summary>
    Private Sub TerraExtract(TarGzPath As String, OutputDir As String)
        DirectoryUtils.Create(OutputDir)
        Using RawStream = FileUtils.ReadAsStream(TarGzPath),
              GzStream As New GZipStream(RawStream, CompressionMode.Decompress)
            Dim Block(511) As Byte
            Dim LongName As String = Nothing
            While TerraReadBlock(GzStream, Block)
                If Block.All(Function(b) b = 0) Then Exit While '归档结束标记
                Dim HeaderName = TerraField(Block, 0, 100)
                Dim Prefix = TerraField(Block, 345, 155)
                Dim TypeFlag = ChrW(Block(156))
                Dim Size As Long = CLng(Convert.ToInt64(TerraField(Block, 124, 12).Trim(), 8))
                Dim DataBlocks = CType((Size + 511) \ 512, Integer)
                Select Case TypeFlag
                    Case "L"c 'GNU 长文件名，作用于下一个条目
                        LongName = TerraReadText(GzStream, Size, DataBlocks)
                    Case "0"c, "7"c, ControlChars.NullChar
                        Dim Name = If(LongName Is Nothing, If(Prefix.Length > 0, Prefix & "/" & HeaderName, HeaderName), LongName)
                        LongName = Nothing
                        Dim Target = TerraSafePath(OutputDir, Name)
                        DirectoryUtils.Create(Path.GetDirectoryName(Target))
                        Using OutStream As FileStream = File.Create(Target)
                            TerraStreamCopy(GzStream, OutStream, Size, DataBlocks)
                        End Using
                    Case "5"c
                        Dim Name = If(LongName Is Nothing, HeaderName, LongName)
                        LongName = Nothing
                        DirectoryUtils.Create(TerraSafePath(OutputDir, Name))
                        TerraSkip(GzStream, DataBlocks * 512)
                    Case Else '符号链接、PAX 扩展头等，直接跳过
                        LongName = Nothing
                        TerraSkip(GzStream, DataBlocks * 512)
                End Select
            End While
        End Using
    End Sub

    ''' <summary>
    ''' 把归档里的路径转成绝对路径，拒绝绝对路径与跳出目标目录的路径。
    ''' </summary>
    Private Function TerraSafePath(OutputDir As String, Name As String) As String
        Dim Full = Path.GetFullPath(Path.Combine(OutputDir, Name.Replace(ChrW(92), "/"c)))
        If Not PathUtils.IsParentOf(OutputDir, Full) Then Throw New InvalidDataException($"压缩文件项 {Name} 的路径在解压目录之外！")
        Return Full
    End Function

    Private Function TerraField(Block() As Byte, Offset As Integer, Length As Integer) As String
        Dim Text = Encoding.UTF8.GetString(Block, Offset, Length)
        Dim EndIndex = Text.IndexOf(vbNullChar)
        Return If(EndIndex < 0, Text, Text.Substring(0, EndIndex)).Trim()
    End Function

    ''' <summary>读满一个 512 字节的头块。流结束时返回 False。</summary>
    Private Function TerraReadBlock(Stream As Stream, Block() As Byte) As Boolean
        Dim Done As Integer = 0
        While Done < 512
            Dim Read = Stream.Read(Block, Done, 512 - Done)
            If Read = 0 Then
                If Done = 0 Then Return False
                Throw New InvalidDataException("tar 文件在头部中间就结束了。")
            End If
            Done += Read
        End While
        Return True
    End Function

    Private Function TerraReadText(Stream As Stream, Size As Long, DataBlocks As Integer) As String
        Dim Buffer(CType(Size - 1, Integer)) As Byte
        TerraReadExact(Stream, Buffer, Buffer.Length)
        TerraSkip(Stream, DataBlocks * 512 - Size)
        Return Encoding.UTF8.GetString(Buffer).Trim(vbNullChar)
    End Function

    Private Sub TerraStreamCopy(Stream As Stream, Target As Stream, Size As Long, DataBlocks As Integer)
        Dim Buffer(8191) As Byte
        Dim Left = Size
        While Left > 0
            Dim Read = Stream.Read(Buffer, 0, CInt(Math.Min(Left, Buffer.Length)))
            If Read = 0 Then Throw New InvalidDataException("tar 文件在数据中间就结束了。")
            Target.Write(Buffer, 0, Read)
            Left -= Read
        End While
        TerraSkip(Stream, DataBlocks * 512 - Size)
    End Sub

    Private Sub TerraReadExact(Stream As Stream, Buffer() As Byte, Count As Integer)
        Dim Done As Integer = 0
        While Done < Count
            Dim Read = Stream.Read(Buffer, Done, Count - Done)
            If Read = 0 Then Throw New InvalidDataException("tar 文件比预期更短。")
            Done += Read
        End While
    End Sub

    Private Sub TerraSkip(Stream As Stream, Count As Long)
        If Count <= 0 Then Return
        If Stream.CanSeek Then
            Stream.Seek(Count, SeekOrigin.Current)
        Else
            Dim Buffer(511) As Byte
            Dim Left = Count
            While Left > 0
                Dim Read = Stream.Read(Buffer, 0, CInt(Math.Min(Left, Buffer.Length)))
                If Read = 0 Then Exit While
                Left -= Read
            End While
        End If
    End Sub

#End Region

End Module
