Public Class FrmModpackUpgrade

    Private Instance As McInstance
    Private StepIndex As Integer = 1
    Private IsBusy As Boolean = False
    Private IsInPlaceMode As Boolean = True
    Private Kernel As ModpackUpgradeKernel = Nothing
    Private Mods As List(Of ModpackUpgradeMod) = Nothing
    Private Categories As List(Of ModpackUpgradeCategory) = Nothing

    ''' <summary>
    ''' 打开升级向导。必须在 UI 线程调用。
    ''' </summary>
    Public Sub Init(Source As McInstance)
        Instance = Source
        IsInPlaceMode = True
        LabCurrent.Text = $"当前版本：{Source.Name}（Minecraft {Source.Version.VanillaName}，{LoaderText(Source.Version.ModLoaders)}）" & vbCrLf &
                          $"版本文件夹：{Source.PathVersion}"
        CheckRelease.Checked = Settings.Get(Of Boolean)("ModpackUpgradeRelease")
        CheckBeta.Checked = Settings.Get(Of Boolean)("ModpackUpgradeBeta")
        CheckAlpha.Checked = Settings.Get(Of Boolean)("ModpackUpgradeAlpha")
        FillVersions()
        ChangeStep(1)
        Show()
    End Sub

    Private Function LoaderText(Loaders As ModLoaders) As String
        If Loaders = ModLoaders.None Then Return "无 Mod 加载器"
        Dim Result As New List(Of String)
        If Loaders.HasFlagF(ModLoaders.Forge) Then Result.Add("Forge")
        If Loaders.HasFlagF(ModLoaders.NeoForge) Then Result.Add("NeoForge")
        If Loaders.HasFlagF(ModLoaders.Fabric) Then Result.Add("Fabric")
        If Loaders.HasFlagF(ModLoaders.LiteLoader) Then Result.Add("LiteLoader")
        Return String.Join(" + ", Result)
    End Function

    Private Sub FillVersions()
        If DlClientListLoader.State = LoadState.Finished Then
            FillVersionsUi()
        ElseIf DlClientListLoader.State = LoadState.Loading Then
            RunInNewThread(
            Sub()
                Try
                    DlClientListLoader.WaitForExit()
                Catch ex As Exception
                    Logger.Warn(ex, "获取 Minecraft 版本列表失败")
                End Try
                RunInUi(AddressOf FillVersionsUi)
            End Sub, "升级整合包版本列表")
        Else
            RunInNewThread(
            Sub()
                Try
                    DlClientListLoader.WaitForExit(Nothing, IsForceRestart:=True)
                Catch ex As Exception
                    Logger.Warn(ex, "获取 Minecraft 版本列表失败")
                End Try
                RunInUi(AddressOf FillVersionsUi)
            End Sub, "升级整合包版本列表")
        End If
    End Sub
    Private Sub FillVersionsUi()
        Try
            ComboTarget.Items.Clear()
            For Each Version As JObject In DlClientListLoader.Output.Value("versions")
                Dim VersionType = Version("type").ToString
                If VersionType = "old_beta" OrElse VersionType = "old_alpha" Then Continue For
                Dim Id = Version("id").ToString
                ComboTarget.Items.Add(New MyComboBoxItem With {.Content = If(VersionType = "release", Id, Id & "（快照）"), .Tag = Id})
            Next
            For Each Item As MyComboBoxItem In ComboTarget.Items
                If Item.Tag = Instance.Version.VanillaName Then
                    ComboTarget.SelectedItem = Item
                    Exit For
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, "填充 Minecraft 版本列表失败")
        End Try
    End Sub

    Private Function TargetVersion() As String
        Dim Manual = TextTarget.Text.Trim
        If Manual <> "" Then Return Manual
        If ComboTarget.SelectedItem IsNot Nothing Then Return CType(ComboTarget.SelectedItem, MyComboBoxItem).Tag.ToString
        Return ""
    End Function

#Region "步骤切换"

    Private Sub ChangeStep(NewStep As Integer)
        StepIndex = NewStep
        PanStep1.Visibility = If(NewStep = 1, Visibility.Visible, Visibility.Collapsed)
        PanStep2.Visibility = If(NewStep = 2, Visibility.Visible, Visibility.Collapsed)
        PanStep3.Visibility = If(NewStep = 3, Visibility.Visible, Visibility.Collapsed)
        Select Case NewStep
            Case 1
                BtnNext.Text = "下一步"
            Case 2
                BtnNext.Text = "下一步"
            Case Else
                BtnNext.Text = "开始升级"
                RenderSummary()
        End Select
        RefreshStepText()
        BtnPrev.IsEnabled = NewStep > 1 AndAlso Not IsBusy
        PanScroll.ScrollToHome()
    End Sub
    Private Sub RefreshStepText()
        If IsBusy Then Return
        Select Case StepIndex
            Case 1 : LabStep.Text = "第 1 步，共 3 步：选择目标版本"
            Case 2 : LabStep.Text = "第 2 步，共 3 步：确认模组与数据"
            Case Else : LabStep.Text = "第 3 步，共 3 步：确认无误后开始下载"
        End Select
    End Sub

    Private Sub SetBusyState(Busy As Boolean, Text As String)
        IsBusy = Busy
        BtnNext.IsEnabled = Not Busy
        BtnPrev.IsEnabled = Not Busy AndAlso StepIndex > 1
        BtnClose.IsEnabled = Not Busy
        If Busy Then
            LabStep.Text = Text
        Else
            RefreshStepText()
            LabProgress.Text = ""
        End If
    End Sub

    Private Sub BtnNext_Click() Handles BtnNext.Click
        If IsBusy Then Return
        Select Case StepIndex
            Case 1 : StartCheck()
            Case 2 : ChangeStep(3)
            Case 3 : StartUpgrade()
        End Select
    End Sub
    Private Sub BtnPrev_Click() Handles BtnPrev.Click
        If IsBusy Then Return
        ChangeStep(Math.Max(1, StepIndex - 1))
    End Sub
    Private Sub BtnClose_Click() Handles BtnClose.Click
        Close()
    End Sub
    Private Sub CheckNewVersion_Changed(sender As Object, user As Boolean) Handles CheckNewVersion.Change
        If Not user Then Return
        TextNewName.Visibility = If(CheckNewVersion.Checked, Visibility.Visible, Visibility.Collapsed)
    End Sub

#End Region

#Region "第 1 步：检查"

    Private Sub StartCheck()
        Dim Target = TargetVersion()
        If Target = "" Then
            Hint("请先选择或输入目标 Minecraft 版本！", HintType.Red)
            Return
        End If
        If Not CheckRelease.Checked AndAlso Not CheckBeta.Checked AndAlso Not CheckAlpha.Checked Then
            Hint("请至少选择一种 Mod 发行类型！", HintType.Red)
            Return
        End If
        Settings.Set("ModpackUpgradeRelease", CheckRelease.Checked)
        Settings.Set("ModpackUpgradeBeta", CheckBeta.Checked)
        Settings.Set("ModpackUpgradeAlpha", CheckAlpha.Checked)
        '确认升级方式
        IsInPlaceMode = Not CheckNewVersion.Checked
        Dim NewName = TextNewName.Text.Trim
        If Not IsInPlaceMode Then
            If NewName = "" Then
                Hint("请输入新版本的名称！", HintType.Red)
                Return
            End If
            If NewName.IndexOfAny(IO.Path.GetInvalidFileNameChars()) >= 0 Then
                Hint("新版本名称中含有无法作为文件夹名的字符！", HintType.Red)
                Return
            End If
            If NewName.Contains("!") OrElse NewName.Contains(";") Then
                Hint("新版本名称中不能含有感叹号或分号！", HintType.Red)
                Return
            End If
            If DirectoryUtils.Exists(McFolderSelected & "versions\" & NewName & "\") OrElse FileUtils.Exists(McFolderSelected & "versions\" & NewName & ".json") Then
                Hint($"已存在名为 {NewName} 的版本，换一个名称！", HintType.Red)
                Return
            End If
        End If
        If McInstanceLock.IsLocked(Instance) Then
            Hint("该版本正在被修改，请稍后再试！", HintType.Red)
            Return
        End If
        SetBusyState(True, "正在检查兼容性，这可能需要几十秒……")
        RunInNewThread(
        Sub()
            Try
                Kernel = ModpackUpgradeKernelBuild(Instance, Target, If(IsInPlaceMode, Instance.Name, NewName), IsInPlaceMode)
                Mods = ModpackUpgradeModsCheck(PathUtils.AddSlashSuffix(Instance.PathIndie) & "mods\", Target,
                                               CheckRelease.Checked, CheckBeta.Checked, CheckAlpha.Checked)
                Categories = ModpackUpgradeDataScan(Instance, Target)
                RunInUi(Sub()
                            SetBusyState(False, "")
                            RenderStep2(Target)
                        End Sub)
            Catch ex As Exception
                ex.ThrowIfCanceled()
                Logger.Warn(ex, "升级整合包兼容性检查失败")
                RunInUi(Sub()
                            SetBusyState(False, "")
                            Hint("检查失败：" & ex.GetDisplay(False), HintType.Red)
                        End Sub)
            End Try
        End Sub, "升级整合包检查")
    End Sub

#End Region

#Region "第 2 步：显示结果"

    Private Sub RenderStep2(Target As String)
        '内核
        Dim Components = Kernel.Components
        Dim Lines As New List(Of String) From {"Minecraft " & Components.MinecraftVersion}
        For Each Entry In Components.ModLoaderVersions
            Lines.Add(Entry.Key & " " & Entry.Value)
        Next
        If Components.OptiFineEntry IsNot Nothing Then Lines.Add("OptiFine " & Components.OptiFineEntry("InstanceName").ToString)
        If Components.FabricApiVersion IsNot Nothing Then Lines.Add("Fabric API " & Components.FabricApiVersion)
        If Components.OptiFabricVersion IsNot Nothing Then Lines.Add("OptiFabric " & Components.OptiFabricVersion)
        LabKernel.Text = "将下载新内核：" & String.Join("，", Lines)
        If Kernel.Notes.Any Then LabKernel.Text &= vbCrLf & vbCrLf & "以下组件无法自动匹配，升级后需要自行安装：" & vbCrLf & "· " & Kernel.Notes.Join(vbCrLf & "· ")
        '模组
        PanMods.Children.Clear()
        Dim AvailableCount = Mods.Where(Function(M) M.Status = ModpackUpgradeModStatus.UpdateAvaliable).Count
        LabModSummary.Text = $"共 {Mods.Count} 个模组，其中 {AvailableCount} 个已找到 {Target} 的可用文件；其余 {Mods.Count - AvailableCount} 个可能无法下载。"
        For Each ModEntry In Mods
            If ModEntry.Status = ModpackUpgradeModStatus.UpdateAvaliable Then
                Dim Item As New MyCheckBox With {
                    .Text = $"{ModEntry.FileName} → {ModEntry.NewFile.FileName}（{ModEntry.Note}）",
                    .Height = 26, .Margin = New Thickness(0, 2, 0, 2), .Tag = ModEntry, .Checked = ModEntry.IsSelected}
                AddHandler Item.Change, AddressOf ModItem_Changed
                PanMods.Children.Add(Item)
            Else
                PanMods.Children.Add(New TextBlock With {
                    .Text = $"{ModEntry.FileName}：{StatusText(ModEntry.Status)}" & If(ModEntry.Note = "", "", $"（{ModEntry.Note}）"),
                    .TextWrapping = TextWrapping.Wrap, .FontSize = 12, .Margin = New Thickness(0, 3, 0, 3),
                    .Foreground = CType(FindResource("ColorBrushGray3"), Brush)})
            End If
        Next
        '数据
        PanData.Children.Clear()
        If IsInPlaceMode Then
            LabDataSummary.Text = "就地升级时，存档、材质包等数据已经位于当前版本文件夹，无需迁移。"
        Else
            LabDataSummary.Text = "勾选的类别会被复制到新版本文件夹中。"
        End If
        For Each Category In Categories
            If Not Category.IsAvailable Then
                PanData.Children.Add(New TextBlock With {.Text = $"{Category.Title}：没有可迁移的文件", .FontSize = 12, .Margin = New Thickness(0, 3, 0, 3),
                                                          .Foreground = CType(FindResource("ColorBrushGray3"), Brush)})
                Continue For
            End If
            Dim Item As New MyCheckBox With {
                .Text = $"{Category.Title}（{Category.Items.Count} 项，{Category.Compatible} 项兼容，{Category.Incompatible} 项不兼容，{Category.UnknownCount} 项无法判断）",
                .Height = 26, .Margin = New Thickness(0, 2, 0, 2), .Tag = Category, .Checked = Category.IsSelected, .ToolTip = Category.Tooltip}
            AddHandler Item.Change, AddressOf DataItem_Changed
            PanData.Children.Add(Item)
        Next
        '询问迁移方式
        If Not IsInPlaceMode AndAlso Categories.Any(Function(C) C.Incompatible > 0) AndAlso Categories.Any(Function(C) C.IsAvailable) Then
            Dim IncompatibleText = Categories.Where(Function(C) C.Incompatible > 0).Select(Function(C) $"{C.Title}：{C.Incompatible} 项不兼容").ToList()
            If MyMsgBox("以下数据类别中存在与目标版本不兼容的文件：" & vbCrLf & "· " & IncompatibleText.Join(vbCrLf & "· ") & vbCrLf & vbCrLf &
                        "是否直接把没有不兼容项的数据复制到新版本？你也可以在下一步之前自行勾选。", "数据迁移", "直接迁移兼容数据", "我自己选择") = 1 Then
                For Each Category In Categories
                    Category.IsSelected = Category.IsAvailable AndAlso Category.Incompatible = 0
                Next
                RenderStep2DataOnly()
            End If
        End If
        ChangeStep(2)
    End Sub

    ''' <summary>
    ''' 仅重新绘制数据迁移的勾选状态。
    ''' </summary>
    Private Sub RenderStep2DataOnly()
        PanData.Children.Clear()
        For Each Category In Categories
            If Not Category.IsAvailable Then
                PanData.Children.Add(New TextBlock With {.Text = $"{Category.Title}：没有可迁移的文件", .FontSize = 12, .Margin = New Thickness(0, 3, 0, 3),
                                                          .Foreground = CType(FindResource("ColorBrushGray3"), Brush)})
                Continue For
            End If
            Dim Item As New MyCheckBox With {
                .Text = $"{Category.Title}（{Category.Items.Count} 项，{Category.Compatible} 项兼容，{Category.Incompatible} 项不兼容，{Category.UnknownCount} 项无法判断）",
                .Height = 26, .Margin = New Thickness(0, 2, 0, 2), .Tag = Category, .Checked = Category.IsSelected, .ToolTip = Category.Tooltip}
            AddHandler Item.Change, AddressOf DataItem_Changed
            PanData.Children.Add(Item)
        Next
    End Sub

    Private Function StatusText(Status As ModpackUpgradeModStatus) As String
        Select Case Status
            Case ModpackUpgradeModStatus.NoRelease
                Return "可能无法下载：没有匹配的版本"
            Case ModpackUpgradeModStatus.ReleaseFiltered
                Return "可能无法下载：发行类型未被选中"
            Case ModpackUpgradeModStatus.LoaderMismatch
                Return "加载器不一致"
            Case Else
                Return "可能无法下载：未识别的模组"
        End Select
    End Function

    Private Sub ModItem_Changed(sender As Object, user As Boolean)
        CType(CType(sender, MyCheckBox).Tag, ModpackUpgradeMod).IsSelected = CType(sender, MyCheckBox).Checked
    End Sub
    Private Sub DataItem_Changed(sender As Object, user As Boolean)
        CType(CType(sender, MyCheckBox).Tag, ModpackUpgradeCategory).IsSelected = CType(sender, MyCheckBox).Checked
    End Sub

#End Region

#Region "第 3 步：执行"

    Private Sub RenderSummary()
        Dim Lines As New List(Of String)
        Lines.Add($"当前版本：{Instance.Name}（Minecraft {Instance.Version.VanillaName}）")
        Lines.Add(If(IsInPlaceMode, "升级方式：就地升级，当前版本的内核文件会被覆盖（旧文件删除到回收站）",
                              $"升级方式：另存为新版本 {Kernel.Request.NewInstanceName}，当前版本保持不变"))
        Lines.Add($"目标版本：Minecraft {Kernel.Components.MinecraftVersion}，组件 {String.Join("，", Kernel.Components.ModLoaderVersions.Select(Function(E) E.Key & " " & E.Value))}")
        Dim UpdateCount = Mods.Where(Function(M) M.IsSelected AndAlso M.NewFile IsNot Nothing).Count
        Lines.Add(If(UpdateCount = 0, "模组：不下载任何模组", $"模组：下载 {UpdateCount} 个模组的新版本文件"))
        If CheckDisable.Checked Then Lines.Add("模组：把未通过检查的模组重命名为 .disabled")
        If IsInPlaceMode Then
            Lines.Add("数据：已经位于当前版本文件夹，无需迁移")
        Else
            Dim Selected = Categories.Where(Function(C) C.IsSelected).Select(Function(C) C.Title).ToList()
            Lines.Add(If(Selected.Any, "数据迁移：" & String.Join("，", Selected), "数据：不迁移任何数据"))
        End If
        LabSummary.Text = String.Join(vbCrLf, Lines)
    End Sub

    Private Sub StartUpgrade()
        Try
            If Not ModpackUpgradeStart(Instance, Kernel, Mods, Categories, IsInPlaceMode, CheckDisable.Checked) Then
                Hint("该版本正在被修改，请稍后再试！", HintType.Red)
                Return
            End If
        Catch ex As Exception
            Logger.Error(ex, $"启动升级整合包任务失败（{Instance.Name}）", LogBehavior.Alert)
            SetBusyState(False, "")
            Return
        End Try
        Hint("升级任务已开始，进度显示在窗口右下角的下载列表中。", HintType.Blue)
        Close()
    End Sub

#End Region

End Class
