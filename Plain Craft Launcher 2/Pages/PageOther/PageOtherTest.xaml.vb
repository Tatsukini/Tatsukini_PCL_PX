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
        BuildWebsiteList()
        BuildSourceStatus()
        CheckSourceAuto.Checked = Settings.Get(Of Boolean)("ToolSourceStatusAuto")
        TxtDeviceServer.Text = Settings.Get(Of String)("ToolDeviceServer")
        TxtDeviceClientId.Text = Settings.Get(Of String)("ToolDeviceClientId")
        TxtDeviceScope.Text = Settings.Get(Of String)("ToolDeviceScope")
        DeviceRefreshSession()
        AddHandler SourceTimer.Tick, AddressOf SourceTimer_Tick
        SourceTimer.Start()
        IsLoad = False
        RefreshMemoryInfo()
        RefreshAllSources()
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

#Region "实用网站"

    Private Sub BuildWebsiteList()
        PanWebsiteList.Children.Clear()
        PanWebsiteList.RowDefinitions.Clear()
        AddWebsite("Planet Minecraft", "https://www.planetminecraft.com/", "玩家创作社区，可以下载地图、材质、光影与建筑作品。")
        AddWebsite("Chunkbase", "https://www.chunkbase.com/", "种子查询工具，输入种子就能预览地形、结构与矿物分布。")
        AddWebsite("MC 百科", "https://www.mcmod.cn/", "中文 Mod 百科，查 Mod 的介绍、教程与汉化下载。")
        AddWebsite("OBJ 转建筑方案", "https://objtoschematic.com/", "把 OBJ 模型转换成游戏内可用的建筑方案文件。")
        AddWebsite("CurseForge", "https://www.curseforge.com/minecraft", "Mod、整合包与插件的托管站，资源最全但需要科学上网。")
        AddWebsite("LittleSkin", "https://littleskin.cn/?lang=zh_CN", "第三方皮肤站，可以上传自己的皮肤并离线使用。")
        AddWebsite("Modrinth", "https://modrinth.com/", "开源的 Mod 与整合包下载站，页面干净、国内直连较快。")
        AddWebsite("PCL PX 源码仓库", "https://github.com/Tatsukini/Tatsukini_PCL_PX", "本启动器修改版的源码仓库，记录了全部改动说明。")
    End Sub

    Private Sub AddWebsite(SiteName As String, SiteUrl As String, SiteUsage As String)
        Dim RowIndex = PanWebsiteList.RowDefinitions.Count
        PanWebsiteList.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        PanWebsiteList.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})

        Dim SiteButton As New MyButton With {.Text = SiteName, .Height = 32, .MinWidth = 140, .HorizontalAlignment = HorizontalAlignment.Left}
        SiteButton.Tag = SiteUrl
        Grid.SetRow(SiteButton, RowIndex)
        Grid.SetColumn(SiteButton, 0)
        PanWebsiteList.Children.Add(SiteButton)
        AddHandler SiteButton.Click, AddressOf WebsiteButton_Click

        Dim LabSplit As New TextBlock With {.Text = "|", .Margin = New Thickness(8, 0, 8, 0), .VerticalAlignment = VerticalAlignment.Center, .Opacity = 0.3}
        Grid.SetRow(LabSplit, RowIndex)
        Grid.SetColumn(LabSplit, 1)
        PanWebsiteList.Children.Add(LabSplit)

        Dim LabDomain As New TextBlock With {.Text = GetWebsiteDomain(SiteUrl), .FontSize = 13, .VerticalAlignment = VerticalAlignment.Center, .TextWrapping = TextWrapping.Wrap, .Opacity = 0.7}
        Grid.SetRow(LabDomain, RowIndex)
        Grid.SetColumn(LabDomain, 2)
        PanWebsiteList.Children.Add(LabDomain)

        Dim LabUsage As New TextBlock With {.Text = SiteUsage, .FontSize = 12, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 4, 0, 15), .Opacity = 0.6}
        Grid.SetRow(LabUsage, RowIndex + 1)
        Grid.SetColumn(LabUsage, 0)
        Grid.SetColumnSpan(LabUsage, 3)
        PanWebsiteList.Children.Add(LabUsage)
    End Sub

    Private Sub WebsiteButton_Click(sender As Object, e As MouseButtonEventArgs)
        Dim SiteButton = TryCast(sender, MyButton)
        If SiteButton Is Nothing Then Return
        Dim SiteUrl = TryCast(SiteButton.Tag, String)
        If String.IsNullOrEmpty(SiteUrl) Then Return
        OpenWebsite(SiteUrl)
        e.Handled = True
    End Sub

    Private Shared Function GetWebsiteDomain(Url As String) As String
        Try
            Return New Uri(Url).Host
        Catch ex As Exception
            Return Url
        End Try
    End Function

#End Region

#Region "下载源状态"

    Private Class SourceProbe
        Public Title As String
        Public Url As String
        Public Solution As String
        Public History As New List(Of Integer) '最近若干次延迟，-1 表示失败
        Public LastOk As Boolean = False
        Public LastMs As Integer = 0
        Public LastCode As Integer = 0
        Public LastError As String = ""
        Public Pending As SourceProbeResult
        Public PanBars As Canvas
        Public TagPanel As StackPanel
        Public LabStatus As TextBlock
        Public LabPing As TextBlock
        Public LabError As TextBlock
        Public LabSolution As TextBlock
    End Class

    Private Class SourceProbeResult
        Public Ok As Boolean
        Public Ms As Integer
        Public Code As Integer
        Public ErrorMsg As String = ""
    End Class

    Private Const SourceHistoryMax As Integer = 40
    Private Const SourceProbeTimeout As Integer = 5000
    Private Const SourceBarHeight As Double = 32
    Private Const SourceBarPitch As Double = 9
    Private ReadOnly SourceProbes As New List(Of SourceProbe)
    Private SourceProbing As Boolean = False
    Private ReadOnly SourceTimer As New System.Windows.Threading.DispatcherTimer With {.Interval = New TimeSpan(0, 0, 0, 5)}
    Private Shared ProbeClient As HttpClient
    Private Shared ReadOnly ProbeClientLock As New Object
    Private Shared ReadOnly SourceBlueBrush As SolidColorBrush = MakeSourceBrush(45, 108, 223)
    Private Shared ReadOnly SourceGreenBrush As SolidColorBrush = MakeSourceBrush(39, 164, 76)
    Private Shared ReadOnly SourceYellowBrush As SolidColorBrush = MakeSourceBrush(224, 150, 0)
    Private Shared ReadOnly SourceRedBrush As SolidColorBrush = MakeSourceBrush(217, 59, 59)
    Private Shared ReadOnly SourceGrayBrush As SolidColorBrush = MakeSourceBrush(140, 140, 140)

    Private Shared Function MakeSourceBrush(R As Integer, G As Integer, B As Integer) As SolidColorBrush
        Dim Result = New SolidColorBrush(Color.FromRgb(CByte(R), CByte(G), CByte(B)))
        Result.Freeze()
        Return Result
    End Function

    ''' <summary>延迟分档：蓝 ≤150ms，绿 ≤300ms，黄 ≤600ms，红为更慢或超时。</summary>
    Private Shared Function GetLatencyBrush(Ms As Integer) As SolidColorBrush
        If Ms <= 150 Then Return SourceBlueBrush
        If Ms <= 300 Then Return SourceGreenBrush
        If Ms <= 600 Then Return SourceYellowBrush
        Return SourceRedBrush
    End Function

    Private Sub BuildSourceStatus()
        SourceProbes.Clear()
        AddSource("Minecraft 核心（官方）", "https://launchermeta.mojang.com/mc/game/version_manifest.json", "官方源在国内不稳定，可在【设置 → 其他 → 下载 → 文件下载源】选择【尽量使用镜像源】。")
        AddSource("Minecraft 核心（BMCLAPI）", "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json", "镜像站偶发限流，稍后重试，或在【设置 → 其他 → 下载】改用官方源。")
        AddSource("CurseForge 搜索（官方）", "https://api.curseforge.com/v1/mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize=1&classId=6&query=jei", "403 表示 Key 缺失或无效，请在【设置 → 其他 → 社区资源 → CurseForge Key】重新填写；其余错误请检查网络，或把【来源】改为镜像。")
        AddSource("CurseForge 搜索（MCIM 镜像）", "https://mod.mcimirror.top/curseforge/v1/mods/search?gameId=432&sortField=2&sortOrder=desc&pageSize=1&classId=6&query=jei", "该镜像会临时停服（503），可在【设置 → 其他 → 社区资源 → 来源】改为【尽量使用官方源】。")
        AddSource("Modrinth 搜索（官方）", "https://api.modrinth.com/v2/search?limit=1&index=relevance&query=jei", "Modrinth 通常可直连，持续失败时把【来源】改为镜像源。")
        AddSource("Modrinth 搜索（MCIM 镜像）", "https://mod.mcimirror.top/modrinth/v2/search?limit=1&index=relevance&query=jei", "该镜像会临时停服（503），可把【来源】改为官方源。")
        AddSource("Fabric 加载器", "https://meta.fabricmc.net/v2/versions", "连不上时把【设置 → 其他 → 下载 → 文件下载源】改为【尽量使用镜像源】。")
        AddSource("Quilt 加载器", "https://meta.quiltmc.org/v3/versions", "连不上时把【设置 → 其他 → 下载 → 文件下载源】改为【尽量使用镜像源】。")
        AddSource("Forge 加载器", "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", "连不上时把【设置 → 其他 → 下载 → 文件下载源】改为【尽量使用镜像源】。")
        AddSource("NeoForge 加载器（镜像）", "https://neoforged.forgecdn.net/api/maven/versions/releases/net/neoforged/neoforge", "maven.neoforged.net 在国内被墙，本启动器默认走这个镜像；它失败时改用 BMCLAPI 的 maven 路径。")
        AddSource("OptiFine", "https://optifine.net/downloads", "OptiFine 站点对国内网络不稳定，可稍后重试或改用 Fabric / Forge 系的替代 Mod。")
        AddSource("Java（Adoptium 官方）", "https://api.adoptium.net/v3/info/available_releases", "失败时改用【下载 → Java】里的清华镜像。")
        AddSource("Java（清华镜像）", "https://mirrors.tuna.tsinghua.edu.cn/Adoptium/", "清华镜像一般可直连，失败时改用【下载 → Java】的官方源。")
        AddSource("皮肤站 LittleSkin", "https://littleskin.cn/csl/Steve.json", "皮肤站偶发 5xx，稍后重试即可。")
        AddSource("正版会话（Mojang）", "https://sessionserver.mojang.com/session/minecraft/profile/069a79f444e9429dcbbb77caa0bb75a0", "用于皮肤与正版校验，偶发超时属正常波动。")
        AddSource("联机节点（EasyTier）", "https://uptime.easytier.cn/api/nodes?page=1&per_page=5", "只影响联机页的节点列表，不影响其他功能。")
        PanSourceList.Children.Clear()
        For Each Probe In SourceProbes
            PanSourceList.Children.Add(BuildSourceRow(Probe))
        Next
    End Sub

    Private Sub AddSource(Title As String, Url As String, Solution As String)
        SourceProbes.Add(New SourceProbe With {.Title = Title, .Url = Url, .Solution = Solution})
    End Sub

    Private Function BuildSourceRow(Probe As SourceProbe) As Grid
        Dim Root As New Grid With {.Margin = New Thickness(0, 0, 0, 15)}
        Root.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
        Root.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})

        Dim Top As New Grid()
        Top.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(250)})
        Top.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Top.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})

        Dim PanName As New StackPanel With {.VerticalAlignment = VerticalAlignment.Center}
        Dim LabName As New TextBlock With {.Text = Probe.Title, .FontSize = 14, .FontWeight = FontWeights.Bold, .TextWrapping = TextWrapping.Wrap}
        Dim LabHost As New TextBlock With {.Text = GetWebsiteDomain(Probe.Url), .FontSize = 12, .Opacity = 0.55, .Margin = New Thickness(0, 2, 12, 0), .TextWrapping = TextWrapping.Wrap}
        PanName.Children.Add(LabName)
        PanName.Children.Add(LabHost)
        Grid.SetColumn(PanName, 0)
        Top.Children.Add(PanName)

        '竖条画在固定宽度的 Canvas 上，套一层会裁剪的容器，避免窗口变窄时压到右侧文字
        Probe.PanBars = New Canvas With {.Width = SourceHistoryMax * SourceBarPitch, .Height = SourceBarHeight, .HorizontalAlignment = HorizontalAlignment.Left, .VerticalAlignment = VerticalAlignment.Center}
        Dim PanBarsClip As New Grid With {.ClipToBounds = True, .VerticalAlignment = VerticalAlignment.Center, .Margin = New Thickness(4, 0, 12, 0)}
        PanBarsClip.Children.Add(Probe.PanBars)
        Grid.SetColumn(PanBarsClip, 1)
        Top.Children.Add(PanBarsClip)

        Dim PanRight As New StackPanel With {.Orientation = Orientation.Horizontal, .VerticalAlignment = VerticalAlignment.Center}
        Probe.LabStatus = New TextBlock With {.Text = "未探测", .FontSize = 13, .FontWeight = FontWeights.Bold, .Foreground = SourceGrayBrush, .Margin = New Thickness(0, 0, 10, 0), .VerticalAlignment = VerticalAlignment.Center}
        Probe.LabPing = New TextBlock With {.Text = "ping:----ms", .FontSize = 12, .Foreground = SourceGrayBrush, .VerticalAlignment = VerticalAlignment.Center}
        PanRight.Children.Add(Probe.LabStatus)
        PanRight.Children.Add(Probe.LabPing)
        Grid.SetColumn(PanRight, 2)
        Top.Children.Add(PanRight)

        Grid.SetRow(Top, 0)
        Root.Children.Add(Top)

        Dim PanError As New StackPanel With {.Margin = New Thickness(0, 4, 0, 0), .Visibility = Visibility.Collapsed}
        Probe.LabError = New TextBlock With {.Text = "", .FontSize = 13, .FontWeight = FontWeights.Bold, .Foreground = SourceRedBrush}
        Probe.LabSolution = New TextBlock With {.Text = "", .FontSize = 12, .Opacity = 0.7, .TextWrapping = TextWrapping.Wrap, .Margin = New Thickness(0, 2, 0, 0)}
        PanError.Children.Add(Probe.LabError)
        PanError.Children.Add(Probe.LabSolution)
        Probe.TagPanel = PanError
        Grid.SetRow(PanError, 1)
        Root.Children.Add(PanError)
        Return Root
    End Function

    Private Sub DrawSourceBars(Probe As SourceProbe)
        Probe.PanBars.Children.Clear()
        Dim Count = Math.Min(Probe.History.Count, SourceHistoryMax)
        Dim StartIndex = Probe.History.Count - Count
        For I = 0 To Count - 1
            Dim Ms = Probe.History(StartIndex + I)
            Dim Bar As New Border With {.Width = 4, .Height = SourceBarHeight, .CornerRadius = New CornerRadius(1)}
            Bar.Background = If(Ms < 0, SourceRedBrush, GetLatencyBrush(Ms))
            Canvas.SetLeft(Bar, I * SourceBarPitch)
            Probe.PanBars.Children.Add(Bar)
        Next
    End Sub

    Private Sub UpdateSourceItem(Probe As SourceProbe)
        If Probe.LabStatus Is Nothing Then Return
        If Probe.LastOk Then
            Probe.LabStatus.Text = "Enabled"
            Probe.LabStatus.Foreground = SourceGreenBrush
            Probe.LabPing.Text = "ping:" & Probe.LastMs.ToString("0000") & "ms"
            Probe.LabPing.Foreground = GetLatencyBrush(Probe.LastMs)
            Probe.TagPanel.Visibility = Visibility.Collapsed
        Else
            Probe.LabStatus.Text = "Disabled"
            Probe.LabStatus.Foreground = SourceRedBrush
            If Probe.LastCode > 0 Then
                Probe.LabPing.Text = "ping:" & Probe.LastMs.ToString("0000") & "ms"
                Probe.LabError.Text = "✗ ERROR Code:" & CStr(Probe.LastCode)
            Else
                Probe.LabPing.Text = "ping:连接失败"
                Probe.LabError.Text = "✗ ERROR " & Probe.LastError
            End If
            Probe.LabPing.Foreground = SourceRedBrush
            Probe.LabSolution.Text = "解决方案：" & Probe.Solution
            Probe.TagPanel.Visibility = Visibility.Visible
        End If
        DrawSourceBars(Probe)
    End Sub

    Private Sub RefreshAllSources()
        If SourceProbing Then Return
        SourceProbing = True
        LabSourceTip.Text = "正在探测 " & SourceProbes.Count & " 个源……"
        RunInNewThread(
        Sub()
            Try
                Dim Options As New ParallelOptions With {.MaxDegreeOfParallelism = 8}
                Parallel.For(0, SourceProbes.Count, Options,
                Sub(Index)
                    SourceProbes(Index).Pending = ProbeSource(SourceProbes(Index).Url)
                End Sub)
            Catch ex As Exception
                Logger.Warn(ex, "下载源状态探测失败")
            End Try
            RunInUi(
            Sub()
                For Each Probe In SourceProbes
                    If Probe.Pending Is Nothing Then Continue For
                    Probe.LastOk = Probe.Pending.Ok
                    Probe.LastMs = Math.Max(0, Probe.Pending.Ms)
                    Probe.LastCode = Probe.Pending.Code
                    Probe.LastError = Probe.Pending.ErrorMsg
                    Probe.History.Add(If(Probe.Pending.Ok, Probe.LastMs, -1))
                    While Probe.History.Count > SourceHistoryMax
                        Probe.History.RemoveAt(0)
                    End While
                    Probe.Pending = Nothing
                    UpdateSourceItem(Probe)
                Next
                SourceProbing = False
                RefreshSourceTip()
            End Sub)
        End Sub, "下载源状态探测")
    End Sub

    Private Sub RefreshSourceTip()
        Dim OkCount = 0
        For Each Probe In SourceProbes
            If Probe.LastOk Then OkCount += 1
        Next
        Dim NowText = DateTime.Now.ToString("HH:mm:ss")
        LabSourceTip.Text = $"共 {SourceProbes.Count} 个源，{OkCount} 个可用　上次探测：{NowText}"
    End Sub

    Private Shared Function GetProbeClient() As HttpClient
        SyncLock ProbeClientLock
            If ProbeClient Is Nothing Then
                ProbeClient = New HttpClient(New HttpClientHandler With {
                    .UseCookies = False,
                    .AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
                }) With {.Timeout = New TimeSpan(0, 0, 0, 0, SourceProbeTimeout + 1000)}
            End If
            Return ProbeClient
        End SyncLock
    End Function

    ''' <summary>只取响应头就丢弃响应体，因此不会真的下载文件；CurseForge 的 Key 由 SecretHeadersSign 自动带上。</summary>
    Private Shared Function ProbeSource(Url As String) As SourceProbeResult
        Dim Result As New SourceProbeResult With {.Ms = -1}
        Dim StartTime As Long = GetTimeMs()
        Try
            Dim Req As New HttpRequestMessage(HttpMethod.Get, Url)
            SecretHeadersSign(Url, Req)
            Using Resp = GetProbeClient().SendAsync(Req, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()
                Result.Ms = CInt(GetTimeMs() - StartTime)
                Result.Code = CInt(Resp.StatusCode)
                Result.Ok = Resp.IsSuccessStatusCode
                If Not Result.Ok Then Result.ErrorMsg = "HTTP " & Result.Code
            End Using
        Catch ex As Exception
            Result.Ok = False
            If TypeOf ex Is TaskCanceledException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is TimeoutException Then
                Result.Ms = SourceProbeTimeout
                Result.ErrorMsg = "超时"
            Else
                Result.ErrorMsg = If(String.IsNullOrEmpty(ex.Message), "无法连接", ex.Message.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).First)
            End If
        End Try
        Return Result
    End Function

    Private Sub BtnSourceRefresh_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSourceRefresh.Click
        RefreshAllSources()
    End Sub

    Private Sub BtnSourceClear_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSourceClear.Click
        For Each Probe In SourceProbes
            Probe.History.Clear()
            Probe.LastOk = False
            Probe.LastCode = 0
            Probe.LastMs = 0
            Probe.LastError = ""
            Probe.LabStatus.Text = "未探测"
            Probe.LabStatus.Foreground = SourceGrayBrush
            Probe.LabPing.Text = "ping:----ms"
            Probe.LabPing.Foreground = SourceGrayBrush
            Probe.TagPanel.Visibility = Visibility.Collapsed
            DrawSourceBars(Probe)
        Next
        LabSourceTip.Text = "记录已清空，点【立即探测】重新开始。"
    End Sub

    Private Sub SourceTimer_Tick(sender As Object, e As EventArgs)
        '只在勾选自动刷新、并且这张卡片确实看得见时才探测，折叠卡片或离开本页都会停下来
        If Not CheckSourceAuto.Checked Then Return
        If Not PanSourceList.IsVisible Then Return
        RefreshAllSources()
    End Sub

#End Region

#Region "设备码登录"

    Private DeviceEndpointsNow As DeviceEndpoints = Nothing
    Private DeviceCodeNow As DeviceCodeInfo = Nothing
    Private DeviceBusy As Boolean = False
    Private DevicePollCancel As Boolean = False

    ''' <summary>后台线程也能直接调，把一行结果追加到日志框。</summary>
    Private Sub DeviceAppendLine(Line As String)
        Dim Text = Date.Now.ToString("HH:mm:ss") & "  " & Line & vbCrLf
        RunInUi(
        Sub()
            If TextDeviceLog Is Nothing Then Return
            If TextDeviceLog.Text.Length > 40000 Then TextDeviceLog.Text = ""
            TextDeviceLog.Text &= Text
            TextDeviceLog.ScrollToEnd()
        End Sub)
    End Sub

    Private Sub DeviceRefreshButtons()
        BtnDeviceProbe.IsEnabled = Not DeviceBusy
        BtnDeviceRequest.IsEnabled = Not DeviceBusy
        BtnDeviceOpen.IsEnabled = DeviceCodeNow IsNot Nothing
        BtnDeviceCopy.IsEnabled = DeviceCodeNow IsNot Nothing
        BtnDeviceStop.IsEnabled = DeviceBusy AndAlso DeviceCodeNow IsNot Nothing
    End Sub

    Private Sub DeviceSaveForm()
        Settings.Set("ToolDeviceServer", NormalizeDeviceServer(TxtDeviceServer.Text))
        Settings.Set("ToolDeviceClientId", TxtDeviceClientId.Text.Trim)
        Settings.Set("ToolDeviceScope", TxtDeviceScope.Text.Trim)
    End Sub

    Private Class DeviceFormValues
        Public Server As String = ""
        Public ClientId As String = ""
        Public Scope As String = ""
        Public DeviceUri As String = ""
        Public TokenUri As String = ""
    End Class

    ''' <summary>读一张卡片上的表单，必须在 UI 线程调用。</summary>
    Private Function DeviceReadForm() As DeviceFormValues
        Return New DeviceFormValues With {
            .Server = NormalizeDeviceServer(TxtDeviceServer.Text),
            .ClientId = TxtDeviceClientId.Text.Trim,
            .Scope = TxtDeviceScope.Text.Trim,
            .DeviceUri = TxtDeviceAuthUri.Text.Trim,
            .TokenUri = TxtDeviceTokenUri.Text.Trim}
    End Function

    Private Sub DeviceFail(Line As String)
        DeviceAppendLine(Line)
        DeviceBusy = False
        RunInUi(AddressOf DeviceRefreshButtons)
    End Sub

    Private Sub BtnDeviceProbe_Click() Handles BtnDeviceProbe.Click
        Dim Form = DeviceReadForm()
        DeviceSaveForm()
        If Form.Server = "" Then
            Hint("先填写服务器地址。", HintType.Red)
            Return
        End If
        DeviceBusy = True
        DeviceRefreshButtons()
        RunInNewThread(
        Sub()
            Try
                Dim Endpoints = DiscoverDeviceEndpoints(Form.Server)
                DeviceEndpointsNow = Endpoints
                If Endpoints.DiscoveryOk Then
                    DeviceAppendLine("已读到 OAuth 元数据：" & Endpoints.DiscoveryUrl)
                    DeviceAppendLine("· authorization_endpoint：" & Endpoints.AuthorizationEndpoint)
                    DeviceAppendLine("· token_endpoint：" & Endpoints.TokenEndpoint)
                    DeviceAppendLine("· device_authorization_endpoint：" & Endpoints.DeviceAuthorizationEndpoint)
                    DeviceAppendLine("· grant_types_supported：" & String.Join("、", Endpoints.GrantTypes))
                    DeviceAppendLine(If(Endpoints.SupportsDeviceFlow(), "结论：这个站点声明了设备码流程。", "结论：元数据里没有看到设备码流程的迹象。"))
                    If Endpoints.Note <> "" Then DeviceAppendLine("· " & Endpoints.Note)
                Else
                    DeviceAppendLine(Endpoints.Note)
                    DeviceAppendLine("开始逐个实测候选路径，只看状态码，不下载内容……")
                    For Each Item In ProbeDeviceEndpoints(Form.Server, Form.DeviceUri, Form.TokenUri)
                        DeviceAppendLine(DeviceProbeLine(Item))
                    Next
                    DeviceAppendLine("说明：200/400/401/405/302 都说明这个路由存在，404 基本可以认为没有。")
                End If
                RunInUi(
                Sub()
                    If Endpoints.DeviceAuthorizationEndpoint <> "" AndAlso TxtDeviceAuthUri.Text = "" Then TxtDeviceAuthUri.Text = Endpoints.DeviceAuthorizationEndpoint
                    If Endpoints.TokenEndpoint <> "" AndAlso TxtDeviceTokenUri.Text = "" Then TxtDeviceTokenUri.Text = Endpoints.TokenEndpoint
                    DeviceBusy = False
                    DeviceRefreshButtons()
                End Sub)
            Catch ex As Exception
                DeviceFail("探测失败：" & ex.GetDisplay(False))
            End Try
        End Sub, "设备码端点探测")
    End Sub

    Private Function DeviceProbeLine(Item As DeviceProbeItem) As String
        If Item.Status = 0 Then Return "· " & Item.Url & "　无法连接：" & Item.ErrorMsg
        Return "· " & Item.Url & "　HTTP " & Item.Status & If(Item.Exists(), "　（路由存在）", "")
    End Function

    Private Sub BtnDeviceRequest_Click() Handles BtnDeviceRequest.Click
        If DeviceBusy Then Return
        Dim Form = DeviceReadForm()
        DeviceSaveForm()
        Dim Endpoints = If(DeviceEndpointsNow, New DeviceEndpoints())
        If Endpoints.Server = "" Then Endpoints.Server = Form.Server
        If Form.DeviceUri <> "" Then Endpoints.DeviceAuthorizationEndpoint = Form.DeviceUri
        If Form.TokenUri <> "" Then Endpoints.TokenEndpoint = Form.TokenUri
        DeviceEndpointsNow = Endpoints
        DeviceBusy = True
        DeviceCodeNow = Nothing
        DeviceRefreshButtons()
        RunInNewThread(
        Sub()
            Try
                Dim Info = RequestDeviceCode(Endpoints, Form.ClientId, Form.Scope)
                DeviceCodeNow = Info
                DeviceAppendLine("设备码已申请，用户码：" & Info.UserCode)
                DeviceAppendLine("· 验证页：" & Info.OpenUrl())
                DeviceAppendLine("· 有效期 " & Info.ExpiresIn & " 秒，轮询间隔 " & Info.Interval & " 秒")
                DeviceAppendLine("请在手机或另一台设备的浏览器里打开验证页并输入用户码，这里会自动等待结果。")
                RunInUi(
                Sub()
                    LabDeviceUser.Text = "用户码：" & Info.UserCode & "　（已复制到剪贴板）"
                    LabDeviceUser.Visibility = Visibility.Visible
                    DeviceRefreshButtons()
                End Sub)
                ClipboardSet(Info.UserCode, False)
                DevicePollLoop(Info)
            Catch ex As Exception
                DeviceFail("请求设备码失败：" & ex.GetDisplay(False))
            End Try
        End Sub, "设备码登录")
    End Sub

    ''' <summary>按 RFC 8628 的要求轮询：先用站点给的间隔，被要求放慢时再加 5 秒。</summary>
    Private Sub DevicePollLoop(Info As DeviceCodeInfo)
        DevicePollCancel = False
        Dim IntervalMs = Math.Max(1000, Info.Interval * 1000)
        Dim Deadline = GetTimeMs() + CLng(Info.ExpiresIn) * 1000
        Dim Times = 0
        Dim Canceled = False
        While Not DevicePollCancel AndAlso GetTimeMs() < Deadline
            Thread.Sleep(IntervalMs)
            If DevicePollCancel Then
                Canceled = True
                Exit While
            End If
            Times += 1
            Dim Result = PollDeviceToken(Info)
            Select Case Result.Status
                Case DeviceTokenStatus.Ok
                    DeviceAppendLine("授权成功。" & vbCrLf & DescribeDeviceToken(Result.Json))
                    SaveDeviceSession(Info, Result.Json)
                    DeviceAppendLine("令牌已保存在本机加密设置中，后续可以直接接进登录流程。")
                    Hint("设备码流程已经跑通。", HintType.Green, False)
                    DeviceFinish()
                    Return
                Case DeviceTokenStatus.Pending
                    If Times = 1 OrElse Times Mod 12 = 0 Then
                        DeviceAppendLine($"仍在等待浏览器端确认（第 {Times} 次轮询）")
                    End If
                Case DeviceTokenStatus.SlowDown
                    IntervalMs += 5000
                    DeviceAppendLine("站点要求放慢轮询，间隔改为 " & (IntervalMs \ 1000) & " 秒")
                Case DeviceTokenStatus.Expired
                    DeviceFailWith("设备码已过期，请重新请求。")
                    Return
                Case DeviceTokenStatus.Denied
                    DeviceFailWith("浏览器端拒绝了这次授权。")
                    Return
                Case Else
                    DeviceFailWith("轮询失败：" & Result.Message)
                    Return
            End Select
        End While
        DeviceFailWith(If(Canceled, "已停止轮询。", "等待超时，设备码可能已经过期。"))
    End Sub

    Private Sub DeviceFailWith(Line As String)
        DeviceAppendLine(Line)
        DeviceFinish()
    End Sub

    Private Sub DeviceFinish()
        DeviceBusy = False
        DeviceCodeNow = Nothing
        RunInUi(
        Sub()
            LabDeviceUser.Visibility = Visibility.Collapsed
            DeviceRefreshButtons()
        End Sub)
    End Sub

    Private Sub BtnDeviceOpen_Click() Handles BtnDeviceOpen.Click
        If DeviceCodeNow Is Nothing Then Return
        OpenWebsite(DeviceCodeNow.OpenUrl())
        DeviceAppendLine("已用浏览器打开验证页：" & DeviceCodeNow.OpenUrl())
    End Sub

    Private Sub BtnDeviceCopy_Click() Handles BtnDeviceCopy.Click
        If DeviceCodeNow Is Nothing Then Return
        ClipboardSet(DeviceCodeNow.UserCode)
    End Sub

    Private Sub BtnDeviceStop_Click() Handles BtnDeviceStop.Click
        DevicePollCancel = True
        DeviceAppendLine("正在停止轮询……")
    End Sub

    Private Sub BtnDeviceClear_Click() Handles BtnDeviceClear.Click
        ClearDeviceSession()
        DeviceAppendLine("已清除本机保存的设备码令牌。")
        Hint("已清除本机令牌。", HintType.Green, False)
    End Sub

    ''' <summary>把已经保存的会话摘要显示出来，令牌一律脱敏。</summary>
    Private Sub DeviceRefreshSession()
        Dim Session = ReadDeviceSession()
        If Session Is Nothing Then
            TextDeviceLog.Text = "本机还没有保存的设备码令牌。" & vbCrLf
            Return
        End If
        Dim ServerText = If(Session("server")?.ToString(), "")
        Dim SavedText = If(Session("savedAt")?.ToString(), "")
        Dim AccessToken = If(Session("accessToken")?.ToString(), "")
        Dim RefreshToken = If(Session("refreshToken")?.ToString(), "")
        TextDeviceLog.Text = "本机已保存会话（" & ServerText & "，" & SavedText & "）" & vbCrLf &
            "· access_token：" & MaskToken(AccessToken) & vbCrLf &
            "· refresh_token：" & MaskToken(RefreshToken) & vbCrLf
    End Sub

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
