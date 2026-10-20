Public Class PageDownloadJava

    Private Const ApiBase As String = "https://api.adoptium.net"
    Private Const GhProxy As String = "https://gh-proxy.com/"
    Private Const TunaBase As String = "https://mirrors.tuna.tsinghua.edu.cn/Adoptium/"

    Private IsLoad As Boolean = False
    Private IsVersionLoading As Boolean = False
    ''' <summary>用于丢弃过期的信息请求结果。</summary>
    Private InfoTick As Integer = 0

    '当前选中的下载信息
    Private Majors As New List(Of Integer)
    Private DownUrl As String = ""
    Private DownName As String = ""
    Private DownRelease As String = ""
    Private DownSize As Long = -1
    Private DownSha256 As String = ""

    ''' <summary>
    ''' PCL 会自动扫描该文件夹下的 Java。
    ''' </summary>
    Public Shared Function DefaultFolder() As String
        Return Paths.AppData & ".minecraft\runtime\"
    End Function

    Private Sub Page_Loaded() Handles Me.Loaded
        If TxtJavaFolder.Text = "" Then TxtJavaFolder.Text = DefaultFolder()
        If Not IsLoad Then
            IsLoad = True
            LoadVersionList()
        End If
        RefreshInstalledList()
    End Sub

#Region "版本列表"

    Private Sub BtnJavaRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnJavaRefresh.Click
        LoadVersionList(True)
    End Sub

    ''' <summary>
    ''' 由边栏刷新按钮触发，重新获取版本列表并刷新已下载列表。
    ''' </summary>
    Public Sub Reload()
        LoadVersionList(True)
        RefreshInstalledList()
    End Sub

    Private Sub LoadVersionList(Optional ForceRefresh As Boolean = False)
        If IsVersionLoading Then Return
        IsVersionLoading = True
        BtnJavaDownload.IsEnabled = False
        LabJavaInfo.Text = "正在获取 Java 版本列表……"
        RunInThread(
        Sub()
            Try
                Dim Raw As String = NetRequestByClientRetry(ApiBase & "/v3/info/available_releases", RequireJson:=True)
                Dim Json As JObject = Raw.DeserializeJson()
                '不同版本的 Adoptium 接口用的键名并不一致，逐个尝试
                Dim ReleaseArray = TryCast(JsonItem(Json, "available_ga_releases", "available_releases"), JArray)
                If ReleaseArray Is Nothing Then Throw New Exception("接口返回结果中没有版本列表：" & ShortText(Raw))
                Dim NewMajors As New List(Of Integer)
                For Each Item In ReleaseArray
                    Dim Major As Integer = Val(Item.ToString)
                    If Major > 0 AndAlso Not NewMajors.Contains(Major) Then NewMajors.Add(Major)
                Next
                If Not NewMajors.Any Then Throw New Exception("接口返回结果中没有版本列表：" & ShortText(Raw))
                NewMajors.Sort()
                Dim LtsList As New List(Of Integer)
                Dim LtsArray = TryCast(JsonItem(Json, "available_lts_releases"), JArray)
                If LtsArray IsNot Nothing Then
                    For Each Item In LtsArray
                        Dim Major As Integer = Val(Item.ToString)
                        If Major > 0 AndAlso Not LtsList.Contains(Major) Then LtsList.Add(Major)
                    Next
                End If
                Dim MostLts As Integer = Val(TokenText(Json, "most_recent_lts"))
                RunInUi(Sub() FillVersionList(NewMajors, LtsList, MostLts))
            Catch ex As Exception
                Logger.Warn(ex, "获取 Java 版本列表失败")
                RunInUi(
                Sub()
                    LabJavaInfo.Text = "获取 Java 版本列表失败：" & ex.Message
                    Hint("获取 Java 版本列表失败：" & ex.Message, HintType.Red)
                End Sub)
            Finally
                RunInUi(Sub() IsVersionLoading = False)
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 安全地取出某个 Json 项的文本，缺失时返回空字符串。
    ''' </summary>
    Private Function TokenText(Json As JObject, Name As String) As String
        If Json Is Nothing OrElse Json(Name) Is Nothing Then Return ""
        Return Json(Name).ToString
    End Function

    ''' <summary>
    ''' 依次尝试多个键名，返回第一个存在的 Json 项；都不存在时返回 Nothing。
    ''' </summary>
    Private Function JsonItem(Json As JObject, ParamArray Names() As String) As JToken
        For Each Key In Names
            Dim Value = Json?(Key)
            If Value IsNot Nothing AndAlso Value.Type <> JTokenType.Null Then Return Value
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' 截断过长的文本，避免提示框被塞满。
    ''' </summary>
    Private Function ShortText(Text As String) As String
        If Text Is Nothing Then Return ""
        Text = Text.ReplaceLineEndings(" ").Trim
        If Text.Length > 160 Then Return Text.Substring(0, 160) & "……"
        Return Text
    End Function

    Private Sub FillVersionList(NewMajors As List(Of Integer), LtsList As List(Of Integer), MostLts As Integer)
        Try
            '尽量保留原本选中的版本
            Dim Keep As Integer = MostLts
            If ComboJavaVersion.SelectedIndex >= 0 AndAlso ComboJavaVersion.SelectedIndex < Majors.Count Then Keep = Majors(ComboJavaVersion.SelectedIndex)
            Majors = NewMajors
            ComboJavaVersion.Items.Clear()
            For Each Major In NewMajors
                ComboJavaVersion.Items.Add(New MyComboBoxItem With {.Content = "Java " & Major & If(LtsList.Contains(Major), "（LTS）", "")})
            Next
            Dim Index As Integer = NewMajors.IndexOf(Keep)
            If Index < 0 Then Index = NewMajors.Count - 1
            ComboJavaVersion.SelectedIndex = Index
        Catch ex As Exception
            Logger.Error(ex, "填充 Java 版本列表失败")
        End Try
    End Sub

#End Region

#Region "版本信息"

    Private Sub ComboJava_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) _
        Handles ComboJavaVersion.SelectionChanged, ComboJavaType.SelectionChanged, ComboJavaArch.SelectionChanged, ComboJavaSource.SelectionChanged
        If Majors.Count = 0 Then Return
        If ComboJavaVersion.SelectedIndex < 0 OrElse ComboJavaType.SelectedIndex < 0 OrElse ComboJavaArch.SelectedIndex < 0 Then Return
        RefreshJavaInfo()
    End Sub

    Private Sub RefreshJavaInfo()
        Dim Tick As Integer = InfoTick + 1
        InfoTick = Tick
        DownUrl = ""
        BtnJavaDownload.IsEnabled = False
        Dim Major As Integer = Majors(ComboJavaVersion.SelectedIndex)
        Dim Type As String = ItemType(ComboJavaType)
        Dim Arch As String = ItemType(ComboJavaArch)
        LabJavaInfo.Text = $"正在获取 Java {Major}（{Arch} {Type.ToUpperInvariant()}）的信息……"
        RunInThread(
        Sub()
            Try
                Dim Raw As String = NetRequestByClientRetry(
                    ApiBase & "/v3/assets/latest/" & Major & "/hotspot?os=windows&architecture=" & Arch & "&image_type=" & Type, RequireJson:=True)
                Dim Entries As JArray = CType(Raw.DeserializeJson(), JArray)
                Dim Best As JObject = Nothing
                For Each Entry As JObject In Entries
                    Dim Link As String = TokenJson(Entry, "binary", "package", "link")
                    If Link = "" OrElse Not Link.EndsWith(".zip", True, Nothing) Then Continue For
                    Best = Entry
                    Exit For
                Next
                If Best Is Nothing Then
                    FinishInfo(Tick, "", "", "", -1, "",
                        $"Adoptium 没有 Java {Major} 的 {Arch} {Type.ToUpperInvariant()} Windows 构建，请更换版本、架构或文件类型。")
                    Return
                End If
                Dim Url As String = TokenJson(Best, "binary", "package", "link")
                Dim Name As String = TokenJson(Best, "binary", "package", "name")
                Dim Release As String = TokenJson(Best, "release_name")
                Dim Sha256 As String = TokenJson(Best, "binary", "package", "checksum")
                Dim Size As Long = Val(TokenJson(Best, "binary", "package", "size"))
                Dim OpenJdkVersion As String = TokenJson(Best, "version", "openjdk_version")
                Dim TimeText As String = ""
                Dim Timestamp As Long = Val(TokenJson(Best, "binary", "timestamp"))
                If Timestamp > 0 Then TimeText = DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).LocalDateTime.ToString("yyyy'/'MM'/'dd HH':'mm")
                Dim CountText As String = ""
                Dim Count As Long = Val(TokenJson(Best, "binary", "package", "download_count"))
                If Count > 0 Then CountText = Count.ToString("N0")
                If Url = "" OrElse Name = "" Then
                    FinishInfo(Tick, "", "", "", -1, "", $"Java {Major} 的下载信息不完整，请稍后再试。")
                    Return
                End If
                FinishInfo(Tick, Url, Name, Release, Size, Sha256,
                    BuildInfoText(Release, OpenJdkVersion, Size, TimeText, CountText, Name, Major, Arch, Type))
            Catch ex As Exception
                Logger.Warn(ex, $"获取 Java {Major} 信息失败")
                FinishInfo(Tick, "", "", "", -1, "", "获取该 Java 的信息失败：" & ex.Message)
            End Try
        End Sub)
    End Sub

    Private Function BuildInfoText(Release As String, OpenJdkVersion As String, Size As Long, TimeText As String, CountText As String,
                                   Name As String, Major As Integer, Arch As String, Type As String) As String
        If Release = "" Then Release = "Java " & Major
        Dim Lines As New List(Of String)
        Lines.Add("版本：" & Release & If(OpenJdkVersion = "", "", "（OpenJDK " & OpenJdkVersion & "）"))
        Lines.Add("大小：" & StringUtils.FormatByteSize(If(Size > 0, Size, 0)) & "，解压后约占两倍空间")
        If TimeText <> "" Then Lines.Add("发布时间：" & TimeText)
        If CountText <> "" Then Lines.Add("总下载量：" & CountText)
        Lines.Add("文件名：" & Name)
        Lines.Add("下载完成后会解压到上方选择的目录，并自动登记到 Java 列表中。")
        Return Lines.Join(vbCrLf)
    End Function

    ''' <summary>
    ''' 递归取出某个 Json 项的文本，缺失时返回空字符串。
    ''' </summary>
    Private Function TokenJson(Token As JToken, ParamArray Names() As String) As String
        Try
            If Token Is Nothing Then Return ""
            For Each Key In Names
                If Token(Key) Is Nothing Then Return ""
                Token = Token(Key)
            Next
            If TypeOf Token Is JValue Then Return CType(Token, JValue).ToString
            Return Token.ToString
        Catch ex As Exception
            Logger.Warn(ex, "解析 Json 项失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 获取某个下拉框选中项的 Tag。
    ''' </summary>
    Private Function ItemType(Combo As MyComboBox) As String
        Dim Item = TryCast(Combo.SelectedItem, MyComboBoxItem)
        If Item Is Nothing OrElse Item.Tag Is Nothing Then Return ""
        Return Item.Tag.ToString
    End Function

    ''' <summary>
    ''' 将后台获取到的信息写回界面。若请求已经过期则忽略。
    ''' </summary>
    Private Sub FinishInfo(Tick As Integer, Url As String, Name As String, Release As String, Size As Long, Sha256 As String, Info As String)
        RunInUi(
        Sub()
            If Tick <> InfoTick Then Return
            DownUrl = Url
            DownName = Name
            DownRelease = Release
            DownSize = Size
            DownSha256 = Sha256
            LabJavaInfo.Text = Info
            BtnJavaDownload.IsEnabled = Url <> ""
        End Sub)
    End Sub

#End Region

#Region "下载"

    Private Sub BtnJavaFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnJavaFolder.Click
        Dim Folder = Dialogs.SelectFolder("选择 Java 保存目录", False, DefaultFolder()).FirstOrDefault
        If Folder IsNot Nothing Then TxtJavaFolder.Text = PathUtils.AddSlashSuffix(Folder)
    End Sub

    ''' <summary>
    ''' 根据所选下载源，返回该压缩包的候选地址。
    ''' </summary>
    Private Function JavaAddressGet(Official As String, Major As Integer, Type As String, Arch As String, Name As String) As List(Of String)
        Dim Tuna = TunaBase & Major & "/" & Type & "/" & Arch & "/windows/" & Name
        Dim Proxied = GhProxy & Official
        Select Case ItemType(ComboJavaSource)
            Case "official"
                Return New List(Of String) From {Official}
            Case "mirror"
                Return New List(Of String) From {Proxied, Tuna, Official}
            Case Else
                Return DlSourceOrder({Official}, {Proxied, Tuna}).ToList
        End Select
    End Function

    Private Sub BtnJavaDownload_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnJavaDownload.Click
        Try
            '检查参数
            If DownUrl = "" OrElse DownName = "" Then
                Hint("还没有获取到可下载的 Java 信息，请稍候或点击【刷新列表】。", HintType.Red)
                Return
            End If
            If ComboJavaVersion.SelectedIndex < 0 Then
                Hint("请先选择要下载的 Java 版本！", HintType.Red)
                Return
            End If
            Dim Major As Integer = Majors(ComboJavaVersion.SelectedIndex)
            Dim Type As String = ItemType(ComboJavaType)
            Dim Arch As String = ItemType(ComboJavaArch)
            Dim TargetRoot As String = PathUtils.AddSlashSuffix(TxtJavaFolder.Text.Trim.Replace("/", "\"))
            If TargetRoot.Length < 3 Then
                Hint("请选择一个有效的保存目录！", HintType.Red)
                Return
            End If
            Dim JavaFolder As String = TargetRoot & PathUtils.GetFileNameWithoutExtension(DownName) & "\"
            If DirectoryUtils.Exists(JavaFolder) Then
                If MyMsgBox($"该目录下已经存在 {PathUtils.GetFileNameWithoutExtension(DownName)}。" & vbCrLf & vbCrLf &
                    "继续下载会覆盖其中的文件，已有的多余文件不会被删除。" & vbCrLf &
                    "若要重新下载一个干净的 Java，建议先删除该文件夹。" & vbCrLf & vbCrLf &
                    "是否继续？", "文件夹已存在", "继续", "取消") <> 1 Then Return
            End If

            '重复任务检查
            Dim LoaderName As String = "下载 Java：" & If(DownRelease = "", DownName, DownRelease)
            If LoaderTaskbar.Any(Function(Old) Old.Name = LoaderName) Then
                Hint("该 Java 正在下载中！", HintType.Red)
                Return
            End If

            '构建任务
            Dim TempFolder As String = PathTemp & "JavaDownload\"
            Dim ZipPath As String = TempFolder & DownName
            Dim Loaders As New List(Of LoaderBase)
            Loaders.Add(New LoaderDownload("下载 Java 压缩包",
                New List(Of NetFile) From {
                    New NetFile(JavaAddressGet(DownUrl, Major, Type, Arch, DownName), ZipPath,
                        New FileChecker With {
                            .MinSize = 1024 * 256,
                            .ActualSize = If(DownSize > 0, DownSize, -1),
                            .Hash = If(DownSha256 = "", Nothing, DownSha256)})}) With {.ProgressWeight = 12})
            Loaders.Add(New LoaderTask(Of String, String)("解压 Java",
            Sub(Task As LoaderTask(Of String, String))
                DirectoryUtils.Create(TargetRoot)
                Task.CreateCancellationToken().ThrowIfCancellationRequested()
                FileUtils.ExtractToDirectory(ZipPath, TargetRoot, Task.CreateCancellationToken(), Task.CreateSyncProgressProvider(0, 1))
                FileUtils.Delete(ZipPath)
                If Not DirectoryUtils.Exists(JavaFolder & "bin") Then
                    Throw New Exception($"解压后没有找到 Java 的 bin 文件夹：{JavaFolder}")
                End If
                Task.Output = JavaFolder
            End Sub) With {.ProgressWeight = 4})
            Loaders.Add(New LoaderWorker("刷新 Java 列表", JavaListRefreshWorker))

            Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders) With {.OnStateChanged = AddressOf JavaDownloadState}
            Loader.Start(TargetRoot)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
            Hint($"已开始下载 {If(DownRelease = "", DownName, DownRelease)}，可在右下角的任务列表中查看进度。", HintType.Blue)
        Catch ex As Exception
            Logger.Error(ex, "开始下载 Java 失败")
            Hint("开始下载 Java 失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub JavaDownloadState(Loader As LoaderBase)
        Select Case Loader.State
            Case LoadState.Finished
                Hint(Loader.Name & "成功！现在可以在 Java 列表中选择它了。", HintType.Green)
                RunInUi(AddressOf RefreshInstalledList)
            Case LoadState.Failed
                Hint(Loader.Name & "失败：" & Loader.Error.GetDisplay(False), HintType.Red)
                Logger.Error(Loader.Error, Loader.Name & "失败")
            Case LoadState.Canceled
                Hint(Loader.Name & "已取消！", HintType.Blue)
        End Select
    End Sub

#End Region

#Region "已下载的 Java"

    Private Sub BtnJavaInstalledRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnJavaInstalledRefresh.Click
        RefreshInstalledList()
    End Sub

    Private Sub RefreshInstalledList()
        Try
            Dim Folder = If(TxtJavaFolder.Text = "", DefaultFolder(), PathUtils.AddSlashSuffix(TxtJavaFolder.Text.Trim.Replace("/", "\")))
            If Not DirectoryUtils.Exists(Folder) Then
                LabJavaInstalled.Text = "还没有下载过任何 Java。"
                Return
            End If
            Dim Names As New List(Of String)
            For Each SubFolder In DirectoryUtils.EnumerateDirectories(Folder)
                Dim Name = PathUtils.GetLastPart(SubFolder)
                If FileUtils.Exists(SubFolder & "\java.exe") OrElse FileUtils.Exists(SubFolder & "\bin\java.exe") Then Names.Add(Name)
            Next
            If Not Names.Any Then
                LabJavaInstalled.Text = "还没有下载过任何 Java。"
                Return
            End If
            Names.Sort()
            LabJavaInstalled.Text = $"共有 {Names.Count} 个 Java：{vbCrLf}- " & Names.Join(vbCrLf & "- ")
        Catch ex As Exception
            Logger.Warn(ex, "列举已下载的 Java 失败")
            LabJavaInstalled.Text = "列举已下载的 Java 失败：" & ex.Message
        End Try
    End Sub

#End Region

End Class
