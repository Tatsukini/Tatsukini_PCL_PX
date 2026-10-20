Public Class PageOtherTest

    Private ServerVersionsLoading As Boolean = False

    Public Sub New()
        InitializeComponent()
        Dim DesktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        If DirectoryUtils.Exists(DesktopFolder) Then
            TxtServerOutput.Text = PathUtils.AddSlashSuffix(DesktopFolder)
            TxtServerPackOutput.Text = TxtServerOutput.Text
        End If
        CheckMemoryKill.Checked = Settings.Get(Of Boolean)("MemoryOptimizeKillEnabled")
        ComboMemoryMode.SelectedIndex = Settings.Get(Of Integer)("MemoryOptimizeMode")
        CheckModGraphEnable.Checked = Settings.Get(Of Boolean)("ToolModGraphEnable")
        CheckModGraphAutoPopup.Checked = Settings.Get(Of Boolean)("ToolModGraphAutoPopup")
        RefreshModGraphEnable()
        IsLoad = False
        RefreshMemoryInfo()
    End Sub

#Region "生成服务端界面"

    Private Sub BtnOpenServerGen_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenServerGen.Click
        PanToolHome.Visibility = Visibility.Collapsed
        PanServerGen.Visibility = Visibility.Visible
        If ComboServerVersion.Items.Count = 0 AndAlso Not ServerVersionsLoading Then LoadServerVersions()
    End Sub

    Private Sub BtnServerGenBack_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerGenBack.Click
        PanServerGen.Visibility = Visibility.Collapsed
        PanToolHome.Visibility = Visibility.Visible
    End Sub

    Private Sub BtnServerVersionRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerVersionRefresh.Click
        LoadServerVersions(ForceRefresh:=True)
    End Sub

    Private Sub LoadServerVersions(Optional ForceRefresh As Boolean = False)
        If ServerVersionsLoading Then Return
        ServerVersionsLoading = True
        ComboServerVersion.IsEnabled = False
        BtnServerVersionRefresh.IsEnabled = False
        BtnServerVersionRefresh.Text = "加载中..."
        RunInThread(
        Sub()
            Try
                If ForceRefresh Then
                    DlClientListLoader.WaitForExit(Nothing, IsForceRestart:=True)
                Else
                    DlClientListLoader.WaitForExit()
                End If
                Dim Ids As New List(Of String)
                For Each Version As JObject In DlClientListLoader.Output.Value("versions")
                    If Version("type") Is Nothing OrElse Version("type").ToString <> "release" Then Continue For
                    Ids.Add(Version("id").ToString)
                Next
                If Not Ids.Any Then Throw New Exception("版本列表中没有正式版")
                RunInUi(
                Sub()
                    Dim SelectedId As String = ""
                    Dim SelectedItem = TryCast(ComboServerVersion.SelectedItem, MyComboBoxItem)
                    If SelectedItem IsNot Nothing Then SelectedId = SelectedItem.Content.ToString
                    ComboServerVersion.Items.Clear()
                    For Each Id In Ids
                        ComboServerVersion.Items.Add(New MyComboBoxItem With {.Content = Id})
                    Next
                    Dim SelectedIndex = Ids.IndexOf(SelectedId)
                    ComboServerVersion.SelectedIndex = If(SelectedIndex >= 0, SelectedIndex, 0)
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "获取 Minecraft 版本列表失败")
                RunInUi(Sub() Hint("获取 Minecraft 版本列表失败：" & ex.Message, HintType.Red))
            Finally
                RunInUi(
                Sub()
                    ServerVersionsLoading = False
                    ComboServerVersion.IsEnabled = True
                    BtnServerVersionRefresh.IsEnabled = True
                    BtnServerVersionRefresh.Text = "刷新列表"
                End Sub)
            End Try
        End Sub)
    End Sub

    Private Sub BtnServerOutput_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerOutput.Click
        Dim Folder = Dialogs.SelectFolder("选择服务端输出目录", False).FirstOrDefault
        If Folder IsNot Nothing Then TxtServerOutput.Text = Folder
    End Sub

    Private Sub BtnServerPackOutput_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerPackOutput.Click
        Dim Folder = Dialogs.SelectFolder("选择服务端输出目录", False).FirstOrDefault
        If Folder IsNot Nothing Then TxtServerPackOutput.Text = Folder
    End Sub

    Private Sub BtnServerPackBrowse_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnServerPackBrowse.Click
        Dim File = Dialogs.SelectFile("选择整合包文件", False, filter:={({"zip", "mrpack"}, "整合包文件")}).FirstOrDefault
        If File IsNot Nothing Then TxtServerPack.Text = File
    End Sub

    Private Sub BtnGenVanilla_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnGenVanilla.Click
        Dim SelectedItem = TryCast(ComboServerVersion.SelectedItem, MyComboBoxItem)
        Dim VersionId As String = ""
        If SelectedItem IsNot Nothing Then VersionId = SelectedItem.Content.ToString
        StartServerGen(ServerGenMode.Vanilla, VersionId, "", TxtServerOutput.Text, TxtServerName.Text)
    End Sub

    Private Sub BtnGenModpack_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnGenModpack.Click
        StartServerGen(ServerGenMode.ModPack, "", TxtServerPack.Text, TxtServerPackOutput.Text, TxtServerPackName.Text)
    End Sub

#End Region

#Region "内存优化"

    Private MemoryOptimizing As Boolean = False
    Private IsLoad As Boolean = True

    Private Sub ComboMemoryMode_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboMemoryMode.SelectionChanged
        If IsLoad OrElse ComboMemoryMode.SelectedIndex < 0 Then Return
        WriteMode(CType(ComboMemoryMode.SelectedIndex, MemoryOptimizeMode))
    End Sub

    Private Sub BtnOpenMemoryAdvanced_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnOpenMemoryAdvanced.Click
        PanToolHome.Visibility = Visibility.Collapsed
        PanMemoryAdvanced.Visibility = Visibility.Visible
        RefreshMemoryProcesses()
    End Sub

    Private Sub BtnMemoryAdvancedBack_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnMemoryAdvancedBack.Click
        PanMemoryAdvanced.Visibility = Visibility.Collapsed
        PanToolHome.Visibility = Visibility.Visible
        RefreshMemoryInfo()
    End Sub

    Private Sub BtnMemoryOptimize_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnMemoryOptimize.Click
        If MemoryOptimizing Then
            Hint("内存优化正在进行中，请稍候……", HintType.Blue)
            Return
        End If
        '关闭进程会造成不可逆的内容丢失，仅在确实需要关闭进程时二次确认
        Dim KillNames As List(Of String) = Nothing
        If CheckMemoryKill.Checked Then
            KillNames = ReadKillList()
            If KillNames.Any Then
                Dim Result = MyMsgBox($"内存优化将关闭下列进程：{String.Join("、", KillNames)}。" & vbCrLf & vbCrLf &
                                      "这些软件中未保存的内容会丢失，且部分软件不会自动重启，确定继续吗？",
                                      "内存优化", "确定", "取消", IsWarn:=True, HighLight:=True)
                If Result <> 1 Then Return
            End If
        End If
        MemoryOptimizing = True
        BtnMemoryOptimize.IsEnabled = False
        RunInThread(
        Sub()
            Try
                MemoryOptimizeRun(True)
            Catch ex As Exception
                Logger.Error(ex, "内存优化失败")
                RunInUi(Sub() Hint("内存优化失败：" & ex.Message, HintType.Red))
            Finally
                RunInUi(
                Sub()
                    MemoryOptimizing = False
                    BtnMemoryOptimize.IsEnabled = True
                    RefreshMemoryInfo()
                End Sub)
            End Try
        End Sub)
    End Sub

    Private Sub CheckMemoryKill_Changed(sender As Object, user As Boolean) Handles CheckMemoryKill.Change
        Settings.Set("MemoryOptimizeKillEnabled", CheckMemoryKill.Checked)
    End Sub

    Private Sub BtnMemoryProcRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnMemoryProcRefresh.Click
        RefreshMemoryProcesses()
    End Sub

    ''' <summary>
    ''' 按当前设置重建可关闭的进程列表。
    ''' </summary>
    Private Sub RefreshMemoryProcesses()
        Dim Selected = ReadKillList()
        PanMemoryProcList.Children.Clear()
        Dim Entries As List(Of ModMemoryOptimize.MemoryProcessEntry)
        Try
            Entries = GetProcessList()
        Catch ex As Exception
            Logger.Error(ex, "获取进程列表失败")
            LabMemoryListTip.Text = "获取进程列表失败：" & ex.Message
            LabMemoryListTip.Visibility = Visibility.Visible
            Return
        End Try
        If Not Entries.Any Then
            LabMemoryListTip.Text = "没有发现可以关闭的程序。只有当前拥有窗口的程序才会出现在这里。"
            LabMemoryListTip.Visibility = Visibility.Visible
            Return
        End If
        LabMemoryListTip.Visibility = Visibility.Collapsed
        For Each Entry In Entries
            Dim Box As New MyCheckBox With {.Text = Entry.DisplayName(), .Height = 22, .Tag = Entry.Name}
            Box.Checked = Selected.Contains(Entry.Name, StringComparer.OrdinalIgnoreCase)
            AddHandler Box.Change, AddressOf MemoryProc_Changed
            PanMemoryProcList.Children.Add(Box)
        Next
    End Sub

    Private Sub MemoryProc_Changed(sender As Object, user As Boolean)
        Dim Names As New List(Of String)
        For Each ProcBox As MyCheckBox In PanMemoryProcList.Children.OfType(Of MyCheckBox)()
            If ProcBox.Checked AndAlso ProcBox.Tag IsNot Nothing Then Names.Add(ProcBox.Tag.ToString)
        Next
        WriteKillList(Names)
    End Sub

    Private Sub RefreshMemoryInfo()
        Try
            Dim Total = My.Computer.Info.TotalPhysicalMemory
            Dim Available = My.Computer.Info.AvailablePhysicalMemory
            LabMemoryInfo.Text = $"当前物理内存：共 {Total / 1024 / 1024 / 1024:F1} GB，可用 {Available / 1024 / 1024 / 1024:F1} GB（已使用 {(1 - Available / Total) * 100:F0}%）"
        Catch ex As Exception
            Logger.Warn(ex, "获取内存信息失败")
        End Try
    End Sub

#End Region

#Region "模组报错图形化检查"

    Private Shared ModGraphScanning As Boolean = False

    ''' <summary>
    ''' 按功能开关显示按钮或显示提示文字。
    ''' </summary>
    Private Sub RefreshModGraphEnable()
        If CheckModGraphEnable Is Nothing OrElse PanModGraphButtons Is Nothing Then Return
        If CheckModGraphEnable.Checked Then
            PanModGraphButtons.Visibility = Visibility.Visible
            LabModGraphDisabled.Visibility = Visibility.Collapsed
        Else
            PanModGraphButtons.Visibility = Visibility.Collapsed
            LabModGraphDisabled.Visibility = Visibility.Visible
        End If
    End Sub

    Private Sub CheckModGraphEnable_Change(sender As Object, user As Boolean) Handles CheckModGraphEnable.Change
        '设置项由 SettingService 自动写入，这里只需要刷新界面
        RefreshModGraphEnable()
    End Sub

    Private Sub BtnModGraphScan_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnModGraphScan.Click
        If Not CheckModGraphEnable.Checked Then
            Hint("模组图形化检查已关闭，请先勾选 启用模组图形化检查。", HintType.Red)
            Return
        End If
        If McInstanceSelected Is Nothing Then
            Hint("请先在 启动 页选择一个版本。", HintType.Red)
            Return
        End If
        RescanModGraph(McInstanceSelected, Nothing)
    End Sub

    Private Sub BtnModGraphCrash_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnModGraphCrash.Click
        If Not CheckModGraphEnable.Checked Then
            Hint("模组图形化检查已关闭，请先勾选 启用模组图形化检查。", HintType.Red)
            Return
        End If
        If McInstanceSelected Is Nothing Then
            Hint("请先在 启动 页选择一个版本。", HintType.Red)
            Return
        End If
        Dim FilePath = Dialogs.SelectFile("选择崩溃日志或错误报告", False, filter:={({"log", "txt", "zip"}, "崩溃日志与错误报告")}).FirstOrDefault
        If FilePath Is Nothing Then Return
        RescanModGraph(McInstanceSelected, FilePath)
    End Sub

    ''' <summary>
    ''' 扫描该版本的 Mod 并建立依赖图；CrashFilePath 不为空时，再叠加崩溃日志的分析结果。
    ''' </summary>
    Public Shared Sub RescanModGraph(Instance As McInstance, CrashFilePath As String)
        If Instance Is Nothing Then
            Hint("请先在 启动 页选择一个版本。", HintType.Red)
            Return
        End If
        If ModGraphScanning Then
            Hint("正在检查模组依赖，请稍候……", HintType.Blue)
            Return
        End If
        ModGraphScanning = True
        RunInNewThread(
        Sub()
            Try
                Hint($"正在检查 {Instance.Name} 的模组依赖……", HintType.Blue)
                Dim Nodes = ModGraphScan(Instance)
                Dim Links = ModGraphBuild(Nodes, GetModGraphVersionName(Instance))
                Dim IsCrashAnalyzed As Boolean = False
                If CrashFilePath IsNot Nothing Then
                    Try
                        Dim Analyzer As New CrashAnalyzer(Instance)
                        Analyzer.Import(CrashFilePath)
                        If Analyzer.Prepare() Then
                            Analyzer.Analyze()
                            ModGraphMergeCrash(Nodes, Analyzer.ModSuspectNames)
                            IsCrashAnalyzed = True
                        Else
                            Hint("所选文件中没有可以用于分析的内容，只显示模组之间的依赖关系。", HintType.Red)
                        End If
                    Catch ex As Exception
                        Logger.Error(ex, "模组图形化检查中分析崩溃日志失败")
                        Hint("分析崩溃日志失败：" & ex.Message, HintType.Red)
                    End Try
                End If
                Dim ProblemCount As Integer = 0
                Dim SuspectCount As Integer = 0
                For Each CheckNode In Nodes
                    If CheckNode.HasProblem Then ProblemCount += 1
                    If CheckNode.CrashSuspect Then SuspectCount += 1
                Next
                '分析过崩溃日志时，未勾选自动打开就先问一次；单纯检查依赖则直接打开窗口
                If IsCrashAnalyzed AndAlso Not Settings.Get(Of Boolean)("ToolModGraphAutoPopup") Then
                    If MyMsgBox($"已经读完了：共 {Nodes.Count} 个 Mod，其中 {ProblemCount} 个存在疑点，崩溃日志中提到 {SuspectCount} 个。" & vbCrLf & vbCrLf &
                                "要现在打开图形化检查窗口吗？", "模组报错图形化检查", "打开窗口", "关闭", HighLight:=True) <> 1 Then Return
                End If
                RunInUi(Sub() FrmModGraph.OpenGraph(Instance, Nodes, Links))
            Catch ex As Exception
                Logger.Error(ex, "模组依赖图检查失败", LogBehavior.Toast)
                Hint("模组依赖图检查失败：" & ex.Message, HintType.Red)
            Finally
                ModGraphScanning = False
            End Try
        End Sub, "模组依赖图检查")
    End Sub

    ''' <summary>
    ''' 该版本对应的 Minecraft 版本号，用于判断 Mod 声明的版本范围；无法获取时返回空字符串。
    ''' </summary>
    Private Shared Function GetModGraphVersionName(Instance As McInstance) As String
        Try
            If Instance.Version IsNot Nothing Then Return Instance.Version.VanillaName
        Catch ex As Exception
            Logger.Warn(ex, "获取当前 Minecraft 版本号失败")
        End Try
        Return ""
    End Function

#End Region

#Region "保留的公开接口"

    Public Shared Sub StartCustomDownload(Url As String, FileName As String, Optional Folder As String = Nothing)
        Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub Jrrp()
        Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub RubbishClear()
        Hint("为便于维护，开源内容中不包含百宝箱功能……")
    End Sub
    Public Shared Sub MemoryOptimize(ShowHint As Boolean)
        MemoryOptimizeRun(ShowHint)
    End Sub
    Public Shared Sub MemoryOptimizeInternal(ShowHint As Boolean)
        MemoryOptimizeRun(ShowHint)
    End Sub
    Public Shared Function GetRandomCave() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function
    Public Shared Function GetRandomHint() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function
    Public Shared Function GetRandomPresetHint() As String
        Return "为便于维护，开源内容中不包含百宝箱功能……"
    End Function

#End Region

End Class
