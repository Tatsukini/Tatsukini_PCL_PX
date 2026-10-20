Imports System.IO.Compression

''' <summary>
''' 自动生成 Minecraft 服务端（原版与整合包）。
''' </summary>
Public Module ModServerTool

    Public Enum ServerGenMode
        Vanilla
        ModPack
    End Enum

    Private Enum ServerLoaderKind
        None
        Forge
        NeoForge
        Fabric
        Quilt
    End Enum

    Private Class ServerPackInfo
        Public PackFile As String = ""
        Public ArchiveBase As String = ""
        Public PackName As String = ""
        Public McVersion As String = ""
        Public Loader As ServerLoaderKind = ServerLoaderKind.None
        Public LoaderVersion As String = ""
        Public OverridesFolder As String = "overrides"
        Public ServerOverridesFolder As String = ""
        Public ModrinthFiles As JArray = Nothing
        Public CurseForgeFiles As JArray = Nothing
    End Class

    ''' <summary>
    ''' 开始生成服务端。必须在 UI 线程调用。
    ''' </summary>
    Public Sub StartServerGen(Mode As ServerGenMode, VersionId As String, ModpackFile As String,
                              OutputRoot As String, ServerName As String)
        Try
            '检查参数
            If Mode = ServerGenMode.Vanilla AndAlso String.IsNullOrWhiteSpace(VersionId) Then
                Hint("请先选择要生成服务端的 Minecraft 版本！", HintType.Red)
                Return
            End If
            If Mode = ServerGenMode.ModPack AndAlso Not FileUtils.Exists(ModpackFile) Then
                Hint("请选择有效的整合包文件！", HintType.Red)
                Return
            End If
            If String.IsNullOrWhiteSpace(OutputRoot) Then
                Hint("请选择输出目录！", HintType.Red)
                Return
            End If
            OutputRoot = PathUtils.AddSlashSuffix(OutputRoot.Replace("/", "\"))
            DirectoryUtils.Create(OutputRoot)
            If Not DirectoryUtils.Exists(OutputRoot) Then
                Hint("输出目录不存在：" & OutputRoot, HintType.Red)
                Return
            End If

            '确定服务端名称
            If String.IsNullOrWhiteSpace(ServerName) Then
                If Mode = ServerGenMode.Vanilla Then
                    ServerName = "Minecraft " & VersionId & " 服务端"
                Else
                    ServerName = SanitizeFolderName(PathUtils.GetFileNameWithoutExtension(ModpackFile))
                    If ServerName = "" Then ServerName = "整合包服务端"
                End If
            Else
                ServerName = ServerName.Trim()
            End If

            '校验名称
            Dim NameError = New ValidateFolderName(OutputRoot, IgnoreList:=New List(Of String) From {ServerName}).Validate(ServerName)
            If NameError <> "" Then
                Hint("服务端名称无效：" & NameError, HintType.Red)
                Return
            End If
            Dim TargetFolder = PathUtils.AddSlashSuffix(OutputRoot & ServerName)
            If DirectoryUtils.Exists(TargetFolder) AndAlso Not DirectoryUtils.IsEmpty(TargetFolder) Then
                Hint("目标文件夹已存在且不为空，请更换服务端名称或输出目录！" & vbCrLf & TargetFolder, HintType.Red)
                Return
            End If

            '重复任务检查
            Dim LoaderName As String = "生成服务端：" & ServerName
            If LoaderTaskbar.Any(Function(l) l.Name = LoaderName) Then
                Hint("该服务端正在生成中！", HintType.Red)
                Return
            End If

            '构建任务
            Dim Loaders As List(Of LoaderBase)
            If Mode = ServerGenMode.Vanilla Then
                Loaders = BuildVanillaServerLoaders(VersionId, ServerName, TargetFolder)
            Else
                Loaders = BuildModpackServerLoaders(ModpackFile, ServerName, TargetFolder)
            End If
            Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders) With {.OnStateChanged = AddressOf ServerGenState}
            Loader.Start(ServerName)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
        Catch ex As Exception
            Logger.Error(ex, "开始生成服务端失败")
            Hint("开始生成服务端失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub ServerGenState(Loader As LoaderBase)
        Select Case Loader.State
            Case LoadState.Finished
                Hint(Loader.Name & "成功！", HintType.Green)
            Case LoadState.Failed
                Hint(Loader.Name & "失败：" & Loader.Error.GetDisplay(False), HintType.Red)
                Logger.Error(Loader.Error, Loader.Name & "失败")
            Case LoadState.Canceled
                Hint(Loader.Name & "已取消！", HintType.Blue)
        End Select
    End Sub

#Region "原版服务端"

    Private Function BuildVanillaServerLoaders(Id As String, ServerName As String, TargetFolder As String) As List(Of LoaderBase)
        Dim Loaders As New List(Of LoaderBase)
        Dim ServerJarName As String = ""
        Dim StartScriptName As String = "启动服务端.bat"

        Loaders.Add(New LoaderTask(Of String, List(Of NetFile))("获取 Minecraft " & Id & " 服务端下载信息",
        Sub(Task As LoaderTask(Of String, List(Of NetFile)))
            DirectoryUtils.Create(TargetFolder)
            Task.CreateCancellationToken().ThrowIfCancellationRequested()
            Dim VersionJson As JObject = GetVersionJson(Id)
            Dim ServerJson = VersionJson("downloads")?("server")
            If ServerJson Is Nothing OrElse ServerJson("url") Is Nothing Then
                Throw New Exception($"Mojang 没有为 Minecraft {Id} 提供官方服务端下载")
            End If
            ServerJarName = Id & "-server.jar"
            Dim Checker As New FileChecker With {
                .MinSize = 1024,
                .ActualSize = If(ServerJson("size"), -1),
                .Hash = If(ServerJson("sha1"), Nothing)
            }
            Task.Output = New List(Of NetFile) From {
                New NetFile(DlSourceLauncherOrMetaGet(ServerJson("url").ToString), TargetFolder & ServerJarName, Checker)
            }
        End Sub) With {.ProgressWeight = 1.5, .Show = False})

        Loaders.Add(New LoaderDownload("下载 Minecraft " & Id & " 服务端核心", New List(Of NetFile)) With {.ProgressWeight = 8})

        Loaders.Add(New LoaderTask(Of String, String)("生成服务端配置文件",
        Sub(Task As LoaderTask(Of String, String))
            Task.CreateCancellationToken().ThrowIfCancellationRequested()
            If Not FileUtils.Exists(TargetFolder & ServerJarName) Then
                Throw New FileNotFoundException("服务端核心下载失败：" & ServerJarName)
            End If
            WriteServerFiles(TargetFolder, ServerName, ServerLoaderKind.None, ServerJarName, StartScriptName, IsModpack:=False)
            Task.Output = TargetFolder
        End Sub) With {.ProgressWeight = 0.3, .Show = False})

        Return Loaders
    End Function

#End Region

#Region "整合包服务端"

    Private Function BuildModpackServerLoaders(PackFile As String, ServerName As String, TargetFolder As String) As List(Of LoaderBase)
        Dim Loaders As New List(Of LoaderBase)
        Dim Info As ServerPackInfo = Nothing
        Dim StartScriptName As String = "启动服务端.bat"

        '读取整合包信息
        Loaders.Add(New LoaderTask(Of String, String)("读取整合包信息",
        Sub(Task As LoaderTask(Of String, String))
            DirectoryUtils.Create(TargetFolder)
            Task.CreateCancellationToken().ThrowIfCancellationRequested()
            Info = ParseModpackFile(PackFile)
            Logger.Info($"整合包信息：Minecraft {Info.McVersion}，加载器 {Info.Loader} {Info.LoaderVersion}，基础目录 {If(Info.ArchiveBase, "<根目录>")}")
            Task.Output = TargetFolder
        End Sub) With {.ProgressWeight = 1, .Show = False})

        '准备服务端核心
        Loaders.Add(New LoaderTask(Of String, List(Of NetFile))("准备服务端核心下载信息",
        Sub(Task As LoaderTask(Of String, List(Of NetFile)))
            Task.CreateCancellationToken().ThrowIfCancellationRequested()
            Task.Output = PrepareModpackCoreFiles(Info, TargetFolder)
        End Sub) With {.ProgressWeight = 1.5, .Show = False})

        Loaders.Add(New LoaderDownload("下载服务端核心", New List(Of NetFile)) With {.ProgressWeight = 10})

        '应用覆写文件
        Loaders.Add(New LoaderTask(Of String, String)("应用整合包覆写文件",
        Sub(Task As LoaderTask(Of String, String))
            ExtractModpackOverrides(Info, TargetFolder, Task)
            Task.Output = TargetFolder
        End Sub) With {.ProgressWeight = 3, .Show = False})

        '准备 Mod 下载信息
        Loaders.Add(New LoaderTask(Of String, List(Of NetFile))("准备 Mod 下载信息",
        Sub(Task As LoaderTask(Of String, List(Of NetFile)))
            Task.Output = PrepareModpackModFiles(Info, TargetFolder, Task)
        End Sub) With {.ProgressWeight = 2, .Show = False})

        Loaders.Add(New LoaderDownload("下载整合包 Mod", New List(Of NetFile)) With {.ProgressWeight = 20})

        '安装 Forge / NeoForge
        Loaders.Add(New LoaderTask(Of String, String)("安装服务端加载器",
        Sub(Task As LoaderTask(Of String, String))
            InstallForgelikeServer(Info, TargetFolder, Task)
            Task.Output = TargetFolder
        End Sub) With {.ProgressWeight = 30})

        '生成启动脚本
        Loaders.Add(New LoaderTask(Of String, String)("生成服务端配置文件",
        Sub(Task As LoaderTask(Of String, String))
            Task.CreateCancellationToken().ThrowIfCancellationRequested()
            WriteServerFiles(TargetFolder, ServerName, Info.Loader, "", StartScriptName, IsModpack:=True)
            Task.Output = TargetFolder
        End Sub) With {.ProgressWeight = 0.5, .Show = False})

        '清理临时文件
        Loaders.Add(New LoaderTask(Of String, String)("清理临时文件",
        Sub(Task As LoaderTask(Of String, String))
            For Each TempFile In {"_installer.jar", "_installer.jar.log", "_version.json"}
                FileUtils.Delete(TargetFolder & TempFile)
            Next
            Task.Output = TargetFolder
        End Sub) With {.ProgressWeight = 0.1, .Show = False})

        Return Loaders
    End Function

    ''' <summary>
    ''' 读取整合包的基础信息。仅支持 CurseForge 与 Modrinth 格式。
    ''' </summary>
    Private Function ParseModpackFile(PackFile As String) As ServerPackInfo
        If Not FileUtils.Exists(PackFile) Then Throw New FileNotFoundException("整合包文件不存在", PackFile)
        Dim Extension = PathUtils.GetExtension(PackFile)
        If Extension <> "zip" AndAlso Extension <> "mrpack" Then
            Throw New Exception("仅支持 zip 或 mrpack 格式的整合包，请先将整合包转换为 zip 格式")
        End If

        Using Archive = FileUtils.OpenZip(PackFile)
            '确定整合包的关键文件所在目录（可能在根目录或一级文件夹中）
            Dim Bases As New List(Of String) From {""}
            For Each Entry In Archive.Entries
                Dim Parts = Entry.FullName.Split("/"c)
                If Parts.Length >= 2 AndAlso Parts(0) <> "" Then
                    Dim Base = Parts(0) & "/"
                    If Not Bases.Contains(Base) Then Bases.Add(Base)
                End If
            Next
            Dim BaseFolder As String = Nothing
            Dim IsModrinth As Boolean = False
            For Each Base In Bases
                If Archive.GetEntry(Base & "modrinth.index.json") IsNot Nothing Then
                    BaseFolder = Base
                    IsModrinth = True
                    Exit For
                End If
                Dim ManifestEntry = Archive.GetEntry(Base & "manifest.json")
                If ManifestEntry IsNot Nothing Then
                    Dim ManifestJson As JObject = ReadZipJson(ManifestEntry)
                    If ManifestJson("addons") Is Nothing Then '排除 MCBBS 整合包
                        BaseFolder = Base
                        IsModrinth = False
                        Exit For
                    End If
                End If
            Next
            If BaseFolder Is Nothing Then
                Throw New Exception("无法识别整合包格式。目前仅支持 CurseForge（manifest.json）与 Modrinth（modrinth.index.json）整合包")
            End If

            Dim Info As New ServerPackInfo With {.PackFile = PackFile, .ArchiveBase = BaseFolder}
            If IsModrinth Then
                ParseModrinthIndex(Info, ReadZipJson(Archive.GetEntry(BaseFolder & "modrinth.index.json")), Archive)
            Else
                ParseCurseForgeManifest(Info, ReadZipJson(Archive.GetEntry(BaseFolder & "manifest.json")), Archive)
            End If
            If String.IsNullOrWhiteSpace(Info.McVersion) Then Throw New Exception("整合包未提供 Minecraft 版本信息")
            Return Info
        End Using
    End Function

    Private Sub ParseModrinthIndex(Info As ServerPackInfo, Json As JObject, Archive As ZipArchive)
        Info.PackName = If(Json("name"), "").ToString
        Dim Dependencies = Json("dependencies")
        If Dependencies Is Nothing OrElse Dependencies("minecraft") Is Nothing Then
            Throw New Exception("Modrinth 整合包未提供 Minecraft 版本信息")
        End If
        Info.McVersion = Dependencies("minecraft").ToString
        For Each Entry As JProperty In Dependencies
            Select Case Entry.Name.ToLower
                Case "forge"
                    Info.Loader = ServerLoaderKind.Forge
                    Info.LoaderVersion = Entry.Value.ToString
                Case "neoforge", "neo-forge"
                    Info.Loader = ServerLoaderKind.NeoForge
                    Info.LoaderVersion = Entry.Value.ToString
                Case "fabric-loader"
                    Info.Loader = ServerLoaderKind.Fabric
                    Info.LoaderVersion = Entry.Value.ToString
                Case "quilt-loader"
                    Info.Loader = ServerLoaderKind.Quilt
                    Info.LoaderVersion = Entry.Value.ToString
            End Select
        Next
        If Info.Loader <> ServerLoaderKind.None AndAlso String.IsNullOrWhiteSpace(Info.LoaderVersion) Then
            Throw New Exception("整合包未提供 " & Info.Loader.ToString & " 的版本信息")
        End If
        Info.ModrinthFiles = TryCast(Json("files"), JArray)
        Info.OverridesFolder = "overrides"
        If HasZipFolder(Archive, Info.ArchiveBase & "server-overrides/") Then Info.ServerOverridesFolder = "server-overrides"
    End Sub

    Private Sub ParseCurseForgeManifest(Info As ServerPackInfo, Json As JObject, Archive As ZipArchive)
        Info.PackName = If(Json("name"), "").ToString
        Dim Minecraft = Json("minecraft")
        If Minecraft Is Nothing OrElse Minecraft("version") Is Nothing Then
            Throw New Exception("CurseForge 整合包未提供 Minecraft 版本信息")
        End If
        Info.McVersion = Minecraft("version").ToString

        Dim LoaderId As String = ""
        For Each LoaderEntry In If(Minecraft("modLoaders"), New JArray)
            Dim Id = If(LoaderEntry("id"), "").ToString.ToLower
            If Not (Id.StartsWithF("forge-") OrElse Id.StartsWithF("neoforge-") OrElse
                    Id.StartsWithF("fabric-") OrElse Id.StartsWithF("quilt-")) Then Continue For
            Dim IsPrimary = LoaderEntry("primary") IsNot Nothing AndAlso LoaderEntry("primary").ToObject(Of Boolean)
            If LoaderId = "" OrElse IsPrimary Then LoaderId = Id
            If IsPrimary Then Exit For
        Next
        If LoaderId.StartsWithF("forge-") Then
            Info.Loader = ServerLoaderKind.Forge
            Info.LoaderVersion = LoaderId.Substring("forge-".Length)
        ElseIf LoaderId.StartsWithF("neoforge-") Then
            Info.Loader = ServerLoaderKind.NeoForge
            Info.LoaderVersion = LoaderId.Substring("neoforge-".Length)
        ElseIf LoaderId.StartsWithF("fabric-") Then
            Info.Loader = ServerLoaderKind.Fabric
            Info.LoaderVersion = LoaderId.Substring("fabric-".Length)
        ElseIf LoaderId.StartsWithF("quilt-") Then
            Info.Loader = ServerLoaderKind.Quilt
            Info.LoaderVersion = LoaderId.Substring("quilt-".Length)
        End If

        Info.CurseForgeFiles = TryCast(Json("files"), JArray)
        Dim OverridesPath = If(Json("overrides"), "").ToString.Replace("\", "/").Trim("/"c)
        Info.OverridesFolder = If(OverridesPath = "" OrElse OverridesPath = ".", "", OverridesPath)
        If HasZipFolder(Archive, Info.ArchiveBase & "server-overrides/") Then Info.ServerOverridesFolder = "server-overrides"
    End Sub

    ''' <summary>
    ''' 准备整合包服务端核心文件。
    ''' </summary>
    Private Function PrepareModpackCoreFiles(Info As ServerPackInfo, TargetFolder As String) As List(Of NetFile)
        Dim Files As New List(Of NetFile)
        Select Case Info.Loader
            Case ServerLoaderKind.Forge
                Dim FullVersion = NormalizeForgeVersion(Info.McVersion, Info.LoaderVersion)
                Dim UrlPath = $"net/minecraftforge/forge/{FullVersion}/forge-{FullVersion}-installer.jar"
                Files.Add(New NetFile({
                    $"https://bmclapi2.bangbang93.com/maven/{UrlPath}",
                    $"https://maven.minecraftforge.net/{UrlPath}",
                    $"https://files.minecraftforge.net/maven/{UrlPath}"
                }, TargetFolder & "_installer.jar", New FileChecker With {.MinSize = 64 * 1024}))
            Case ServerLoaderKind.NeoForge
                Dim ApiName = NormalizeNeoForgeVersion(Info.McVersion, Info.LoaderVersion)
                Dim Entry As New DlNeoForgeListEntry(ApiName)
                Files.Add(New NetFile(DlNeoForgeFileGet(Entry.UrlBase & "-installer.jar"),
                    TargetFolder & "_installer.jar", New FileChecker With {.MinSize = 64 * 1024}))
            Case ServerLoaderKind.Fabric
                AddVanillaServerFile(Files, Info.McVersion, TargetFolder)
                Dim LauncherUrl = $"https://meta.fabricmc.net/v2/versions/loader/{Info.McVersion}/{Info.LoaderVersion}/server/jar"
                Files.Add(New NetFile({
                    LauncherUrl.Replace("meta.fabricmc.net/v2", "bmclapi2.bangbang93.com/fabric-meta/v2"),
                    LauncherUrl
                }, TargetFolder & "fabric-server-launch.jar", New FileChecker With {.MinSize = 32 * 1024}))
            Case ServerLoaderKind.Quilt
                AddVanillaServerFile(Files, Info.McVersion, TargetFolder)
                Dim LauncherUrl = $"https://meta.quiltmc.org/v3/versions/loader/{Info.McVersion}/{Info.LoaderVersion}/server/jar"
                Files.Add(New NetFile({
                    LauncherUrl.Replace("https://meta.quiltmc.org/v3", "https://bmclapi2.bangbang93.com/quilt-meta/v3"),
                    LauncherUrl
                }, TargetFolder & "quilt-server-launch.jar", New FileChecker With {.MinSize = 32 * 1024}))
            Case Else
                AddVanillaServerFile(Files, Info.McVersion, TargetFolder)
        End Select
        Return Files
    End Function

    ''' <summary>
    ''' 添加原版服务端 JAR 的下载信息。
    ''' </summary>
    Private Sub AddVanillaServerFile(Files As List(Of NetFile), McVersion As String, TargetFolder As String)
        Dim VersionJson = GetVersionJson(McVersion)
        Dim ServerJson = VersionJson("downloads")?("server")
        If ServerJson Is Nothing OrElse ServerJson("url") Is Nothing Then
            Throw New Exception($"Mojang 没有为 Minecraft {McVersion} 提供官方服务端下载")
        End If
        Dim Checker As New FileChecker With {
            .MinSize = 1024,
            .ActualSize = If(ServerJson("size"), -1),
            .Hash = If(ServerJson("sha1"), Nothing)
        }
        'Fabric、Quilt 的服务端启动器默认查找 server.jar
        Files.Add(New NetFile(DlSourceLauncherOrMetaGet(ServerJson("url").ToString), TargetFolder & "server.jar", Checker))
    End Sub

    ''' <summary>
    ''' 准备整合包中 Mod 的下载信息。
    ''' </summary>
    Private Function PrepareModpackModFiles(Info As ServerPackInfo, TargetFolder As String, Task As LoaderTask(Of String, List(Of NetFile))) As List(Of NetFile)
        Dim Files As New List(Of NetFile)
        Dim ModsFolder = PathUtils.AddSlashSuffix(TargetFolder & "mods")
        Dim TrackLoader As ModLoaders = GetTrackLoader(Info.Loader)

        If Info.ModrinthFiles IsNot Nothing Then
            For Each File In Info.ModrinthFiles
                Task.CreateCancellationToken().ThrowIfCancellationRequested()
                '跳过仅客户端或完全不支持的文件
                If File("env") IsNot Nothing AndAlso File("env")("server") IsNot Nothing Then
                    Dim ServerSide = File("env")("server").ToString.ToLower
                    If ServerSide = "unsupported" OrElse ServerSide = "optional" Then Continue For
                End If
                If File("downloads") Is Nothing Then Continue For
                Dim Urls = File("downloads").Select(Function(u) u.ToString).ToList
                If Not Urls.Any Then Continue For
                Urls.AddRange(Urls.Select(Function(u) DlSourceModGet(u)).ToList)
                Urls = Urls.Distinct.ToList
                Urls = ResourceVersion.ParseModrinthTrackArguments(Urls, ResourceVersion.DownloadReason.ModPack, Info.McVersion, TrackLoader).ToList
                Dim TargetPath = Path.Combine(TargetFolder, File("path").ToString.Replace("/", "\").TrimStart("\"c))
                If Not PathUtils.IsParentOf(TargetFolder, TargetPath) Then
                    Throw New Exception("整合包中的文件路径超出了服务端文件夹：" & File("path").ToString)
                End If
                Dim Hash As String = Nothing
                If File("hashes") IsNot Nothing AndAlso File("hashes")("sha1") IsNot Nothing Then Hash = File("hashes")("sha1").ToString
                Files.Add(New NetFile(Urls, TargetPath, New FileChecker With {.Hash = Hash}, True))
            Next
        ElseIf Info.CurseForgeFiles IsNot Nothing Then
            Dim FileIds As New List(Of Integer)
            For Each ModEntry In Info.CurseForgeFiles
                If ModEntry("fileID") Is Nothing Then Continue For
                If ModEntry("required") IsNot Nothing AndAlso Not ModEntry("required").ToObject(Of Boolean) Then Continue For
                Dim FileId = ModEntry("fileID").ToObject(Of Integer)
                If Not FileIds.Contains(FileId) Then FileIds.Add(FileId)
            Next
            If FileIds.Any Then
                Dim Result As JToken
                Try
                    Result = DlModRequest("https://api.curseforge.com/v1/mods/files", HttpMethod.Post,
                                          "{""fileIds"": [" & FileIds.Join(",") & "]}", "application/json")
                Catch ex As Exception
                    Throw New Exception("获取 CurseForge Mod 下载信息失败。请设置环境变量 PCL_CURSEFORGE_API_KEY（CurseForge API Key）后重试，或检查网络连接", ex)
                End Try
                Dim Data = Result("data")
                If Data Is Nothing Then Throw New Exception("CurseForge API 未返回任何 Mod 下载信息")
                For Each ModJson As JObject In Data
                    Task.CreateCancellationToken().ThrowIfCancellationRequested()
                    Dim FileInfo As ResourceVersion
                    Try
                        FileInfo = ResourceVersion.FromPlatformJson(ModJson, ResourceTypes.Mod)
                    Catch ex As Exception
                        Logger.Warn(ex, $"解析 CurseForge Mod 信息失败：{ModJson("fileName")}")
                        Continue For
                    End Try
                    If Not FileInfo.DownloadAvailable Then Continue For
                    If String.IsNullOrEmpty(FileInfo.FileName) OrElse Not FileInfo.FileName.EndsWithF(".jar", True) Then
                        Logger.Info($"跳过非 Mod 文件：{FileInfo.FileName}")
                        Continue For
                    End If
                    Files.Add(FileInfo.ToNetFile(ModsFolder, ResourceVersion.DownloadReason.ModPack, Info.McVersion, TrackLoader))
                Next
            End If
        End If

        Logger.Info($"整合包服务端需要下载 {Files.Count} 个额外文件")
        Return Files
    End Function

    ''' <summary>
    ''' 将整合包中的覆写文件解压到服务端文件夹。
    ''' </summary>
    Private Sub ExtractModpackOverrides(Info As ServerPackInfo, TargetFolder As String, Task As LoaderTask(Of String, String))
        Dim Folders As New List(Of String)
        For Each Folder In {Info.OverridesFolder, Info.ServerOverridesFolder}
            If String.IsNullOrEmpty(Folder) Then Continue For
            Dim Prefix = Info.ArchiveBase & Folder.Replace("\", "/").Trim("/"c) & "/"
            If Not Folders.Contains(Prefix) Then Folders.Add(Prefix)
        Next
        If Not Folders.Any Then Return

        Using Archive = FileUtils.OpenZip(Info.PackFile)
            Dim Entries = Archive.Entries.Where(Function(e) Not String.IsNullOrEmpty(e.Name) AndAlso
                Folders.Any(Function(p) e.FullName.StartsWithF(p, True))).ToList
            If Not Entries.Any Then
                Logger.Info("整合包中没有需要应用的覆写文件")
                Return
            End If
            Logger.Info($"开始应用整合包覆写文件，共 {Entries.Count} 项")
            Dim C = Task.CreateCancellationToken()
            Dim Done As Integer = 0
            For Each Entry In Entries
                C.ThrowIfCancellationRequested()
                Dim Prefix = Folders.First(Function(p) Entry.FullName.StartsWithF(p, True))
                Dim RelativePath = Entry.FullName.Substring(Prefix.Length).Replace("/", "\")
                If RelativePath = "" Then Continue For
                Dim TargetPath = Path.GetFullPath(Path.Combine(TargetFolder, RelativePath))
                If Not PathUtils.IsParentOf(TargetFolder, TargetPath) Then
                    Throw New Exception("整合包覆写文件的路径超出了服务端文件夹：" & Entry.FullName)
                End If
                DirectoryUtils.Create(Path.GetDirectoryName(TargetPath))
                Entry.ExtractToFile(TargetPath, True)
                Done += 1
                If Done Mod 20 = 0 Then Task.Progress = Done / Entries.Count
            Next
        End Using
    End Sub

#End Region

#Region "加载器安装"

    ''' <summary>
    ''' 对于 Forge 与 NeoForge，调用官方安装器安装服务端。其他加载器无需安装。
    ''' </summary>
    Private Sub InstallForgelikeServer(Info As ServerPackInfo, TargetFolder As String, Task As LoaderTask(Of String, String))
        If Info.Loader <> ServerLoaderKind.Forge AndAlso Info.Loader <> ServerLoaderKind.NeoForge Then Return
        Dim Installer = TargetFolder & "_installer.jar"
        If Not FileUtils.Exists(Installer) Then Throw New FileNotFoundException("服务端加载器安装文件下载失败")

        '选择 Java
        Dim JavaInstance As Java = SelectOrDownloadJava(GetJavaRange(Info.McVersion), True,
            Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(0, 0.15))
        If Task.IsCanceled Then Throw New OperationCanceledException()
        If JavaInstance Is Nothing Then Throw New OperationCanceledException()
        Task.Progress = 0.15

        Dim LoaderName = If(Info.Loader = ServerLoaderKind.NeoForge, "NeoForge", "Forge")
        Dim Psi As New ProcessStartInfo With {
            .FileName = JavaInstance.JavaExePath,
            .Arguments = $"-jar ""{Installer}"" --installServer",
            .WorkingDirectory = TargetFolder,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True
        }
        Logger.Info($"开始安装 {LoaderName} 服务端：{Psi.FileName} {Psi.Arguments}")

        Dim LastLines As New Queue(Of String)
        Dim Proc As New Process With {.StartInfo = Psi}
        Try
            AddHandler Proc.OutputDataReceived,
                Sub(sender, e)
                    If e.Data Is Nothing Then Return
                    Logger.Trace(e.Data)
                    LastLines.Enqueue(e.Data)
                    While LastLines.Count > 20
                        LastLines.Dequeue()
                    End While
                    If Task.Progress < 0.05 Then Task.Progress = 0.05
                    Task.Progress = Math.Min(0.92, Task.Progress + 0.001)
                End Sub
            AddHandler Proc.ErrorDataReceived,
                Sub(sender, e)
                    If e.Data Is Nothing Then Return
                    Logger.Trace(e.Data)
                    LastLines.Enqueue(e.Data)
                    While LastLines.Count > 20
                        LastLines.Dequeue()
                    End While
                End Sub
            Proc.Start()
            Proc.BeginOutputReadLine()
            Proc.BeginErrorReadLine()
            Dim C = Task.CreateCancellationToken()
            Do While Not Proc.WaitForExit(200)
                If C.IsCancellationRequested OrElse Task.State = LoadState.Canceled Then
                    Try
                        Proc.Kill()
                    Catch
                    End Try
                    Throw New OperationCanceledException()
                End If
            Loop
            Proc.WaitForExit()
            If Proc.ExitCode <> 0 Then
                Throw New Exception($"{LoaderName} 安装器返回错误代码 {Proc.ExitCode}：{vbCrLf}{LastLines.Join(vbCrLf)}")
            End If
        Finally
            Proc.Dispose()
        End Try

        Task.Progress = 0.95
        '检查安装结果
        If FileUtils.Exists(TargetFolder & "run.bat") Then Return
        If FindForgelikeArgsFile(TargetFolder) <> "" Then Return
        If FindForgelikeServerJar(TargetFolder) <> "" Then Return
        Throw New Exception($"{LoaderName} 安装器运行完成，但未找到服务端启动文件。请检查安装器的输出日志，或改用整合包作者推荐的方式安装服务端")
    End Sub

    Private Function FindForgelikeServerJar(TargetFolder As String) As String
        For Each File In DirectoryUtils.EnumerateFiles(TargetFolder, searchPattern:="*.jar")
            Dim Name = PathUtils.GetLastPart(File)
            If Name.ContainsIgnoreCase("-universal.jar") OrElse Name.ContainsIgnoreCase("-server.jar") Then Return Name
        Next
        Return ""
    End Function

    Private Function FindForgelikeArgsFile(TargetFolder As String) As String
        Dim LibrariesFolder = TargetFolder & "libraries\"
        If Not DirectoryUtils.Exists(LibrariesFolder) Then Return ""
        For Each File In DirectoryUtils.EnumerateFiles(LibrariesFolder, includeSubDirectories:=True, searchPattern:="win_args.txt")
            Return File.Substring(TargetFolder.Length)
        Next
        Return ""
    End Function

#End Region

#Region "配置文件与启动脚本"

    Private Sub WriteServerFiles(TargetFolder As String, ServerName As String, Loader As ServerLoaderKind,
                                 JarName As String, StartScriptName As String, IsModpack As Boolean)
        'EULA：必须由用户自行同意
        If Not FileUtils.Exists(TargetFolder & "eula.txt") Then
            FileUtils.Write(TargetFolder & "eula.txt",
                "# 将下一行的 eula=false 改为 eula=true，即表示你同意 Minecraft EULA：" & vbCrLf &
                "# https://aka.ms/MinecraftEULA" & vbCrLf &
                "eula=false" & vbCrLf, Encoding.UTF8)
        End If

        '服务端配置：如果整合包已经提供了配置，则不要覆盖
        If Not FileUtils.Exists(TargetFolder & "server.properties") Then
            Dim Properties As String =
                "# Generated by PCL PX" & vbCrLf &
                "server-port=25565" & vbCrLf &
                "online-mode=true" & vbCrLf &
                "max-players=20" & vbCrLf &
                "motd=Minecraft Server" & vbCrLf &
                "level-name=world" & vbCrLf &
                "view-distance=10" & vbCrLf &
                "simulation-distance=10" & vbCrLf &
                If(IsModpack, "allow-flight=true", "allow-flight=false") & vbCrLf
            FileUtils.Write(TargetFolder & "server.properties", Properties, Encoding.UTF8)
        End If

        'Java 内存参数
        If (Loader = ServerLoaderKind.Forge OrElse Loader = ServerLoaderKind.NeoForge) AndAlso
           Not FileUtils.Exists(TargetFolder & "user_jvm_args.txt") Then
            FileUtils.Write(TargetFolder & "user_jvm_args.txt", "-Xmx4G -Xms1G" & vbCrLf, Encoding.UTF8)
        End If

        '启动脚本
        Dim Bat As String
        Dim Header As String =
            "@echo off" & vbCrLf &
            "title Minecraft 服务端" & vbCrLf &
            "cd /d ""%~dp0""" & vbCrLf &
            "echo 如果服务端立即停止，请右键编辑该脚本，将 java 替换为完整的 java.exe 路径。" & vbCrLf &
            "echo 首次启动前，请打开 eula.txt 并同意 Minecraft EULA。" & vbCrLf &
            "echo ------------------------------" & vbCrLf
        Select Case Loader
            Case ServerLoaderKind.Forge, ServerLoaderKind.NeoForge
                If FileUtils.Exists(TargetFolder & "run.bat") Then
                    Bat = Header & "call run.bat nogui" & vbCrLf
                Else
                    Dim ArgsFile = FindForgelikeArgsFile(TargetFolder)
                    If ArgsFile <> "" Then
                        Bat = Header & """java"" -server -XX:+UseG1GC @user_jvm_args.txt @""" & ArgsFile & """ nogui" & vbCrLf
                    Else
                        Dim ServerJar = FindForgelikeServerJar(TargetFolder)
                        If ServerJar = "" Then Throw New Exception("未找到 Forge / NeoForge 服务端启动文件")
                        Bat = Header & """java"" -server -XX:+UseG1GC -Xmx4G -Xms1G -jar """ & ServerJar & """ nogui" & vbCrLf
                    End If
                End If
            Case ServerLoaderKind.Fabric
                Bat = Header & """java"" -server -XX:+UseG1GC -Xmx4G -Xms1G -jar ""fabric-server-launch.jar"" nogui" & vbCrLf
            Case ServerLoaderKind.Quilt
                Bat = Header & """java"" -server -XX:+UseG1GC -Xmx4G -Xms1G -jar ""quilt-server-launch.jar"" nogui" & vbCrLf
            Case Else
                Bat = Header & """java"" -server -XX:+UseG1GC -Xmx4G -Xms1G -jar """ & JarName & """ nogui" & vbCrLf
        End Select
        Bat &= "echo ----------------------" & vbCrLf &
               "echo 服务端已停止。" & vbCrLf &
               "pause"
        FileUtils.Write(TargetFolder & StartScriptName, Bat,
            encoding:=If(Encoding.Default.Equals(Encoding.UTF8), Encoding.UTF8, Encoding.GetEncoding("GB18030")))
        Logger.Info("服务端生成完成：" & TargetFolder)
    End Sub

#End Region

#Region "工具方法"

    ''' <summary>
    ''' 获取指定 Minecraft 版本的版本 JSON。
    ''' </summary>
    Private Function GetVersionJson(McVersion As String) As JObject
        Dim JsonUrl = DlClientListGet(McVersion)
        If String.IsNullOrEmpty(JsonUrl) Then Throw New Exception("未能获取 Minecraft " & McVersion & " 的版本信息")
        Return NetRequestByLoader(DlSourceLauncherOrMetaGet(JsonUrl), IsJson:=True).DeserializeJson()
    End Function

    Private Function ReadZipJson(Entry As ZipArchiveEntry) As JObject
        If Entry Is Nothing Then Throw New FileNotFoundException("整合包中缺少必要的 JSON 文件")
        Return Entry.Open().ReadString(Encoding.UTF8).DeserializeJson()
    End Function

    Private Function HasZipFolder(Archive As ZipArchive, Prefix As String) As Boolean
        Return Archive.Entries.Any(Function(e) e.FullName.StartsWithF(Prefix, True))
    End Function

    Private Function SanitizeFolderName(Name As String) As String
        If String.IsNullOrEmpty(Name) Then Return ""
        Dim InvalidChars = Path.GetInvalidFileNameChars().Concat({"!"c, ";"c}).ToArray
        Dim Result As New StringBuilder
        For Each c In Name.Trim()
            If InvalidChars.Contains(c) Then Result.Append(" "c) Else Result.Append(c)
        Next
        Return Result.ToString().Trim()
    End Function

    Private Function NormalizeForgeVersion(McVersion As String, LoaderVersion As String) As String
        LoaderVersion = LoaderVersion.Trim()
        If LoaderVersion.StartsWithF("1.") AndAlso LoaderVersion.Contains("-") Then Return LoaderVersion
        If LoaderVersion.StartsWithF(McVersion & "-") Then Return LoaderVersion
        Return McVersion & "-" & LoaderVersion
    End Function

    Private Function NormalizeNeoForgeVersion(McVersion As String, LoaderVersion As String) As String
        LoaderVersion = LoaderVersion.Trim()
        If McVersion = "1.20.1" AndAlso Not LoaderVersion.StartsWithF("1.20.1-") Then Return "1.20.1-" & LoaderVersion
        Return LoaderVersion
    End Function

    Private Function GetTrackLoader(Loader As ServerLoaderKind) As ModLoaders
        Select Case Loader
            Case ServerLoaderKind.Forge
                Return ModLoaders.Forge
            Case ServerLoaderKind.NeoForge
                Return ModLoaders.NeoForge
            Case ServerLoaderKind.Fabric, ServerLoaderKind.Quilt
                Return ModLoaders.Fabric
            Case Else
                Return ModLoaders.None
        End Select
    End Function

    ''' <summary>
    ''' 获取运行指定 Minecraft 版本服务端所需的 Java 范围。
    ''' </summary>
    Private Function GetJavaRange(McVersion As String) As ValueRange(Of Version)
        If McUtils.CompareVersionGE(McVersion, "1.20.5") Then Return ValueRange(Of Version).AtLeast(New Version(21, 0))
        If McUtils.CompareVersionGE(McVersion, "1.18") Then Return ValueRange(Of Version).ClosedOpen(New Version(17, 0), New Version(22, 0))
        If McUtils.CompareVersionGE(McVersion, "1.17") Then Return ValueRange(Of Version).ClosedOpen(New Version(16, 0), New Version(18, 0))
        Return ValueRange(Of Version).ClosedOpen(New Version(8, 0), New Version(17, 0))
    End Function

#End Region

End Module
