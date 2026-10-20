Public Class PageSetupUI

    Private Sub PageSetupUI_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        '重复加载部分
        PanBack.ScrollToHome()
        ThemeCheckAll(True)
        If ThemeDontClick <> 0 Then
            Dim NewText As String = Nothing
            Select Case ThemeDontClick
                Case 1
                    NewText = "眼瞎白"
                Case 2
                    NewText = "真·滑稽彩"
            End Select
            For Each Control In PanLauncherTheme.Children
                If (TypeOf Control Is MyRadioBox) AndAlso CType(Control, MyRadioBox).IsEnabled Then CType(Control, MyRadioBox).Text = NewText
            Next
        End If

        AniControlEnabled += 1
        Refresh() '#4826
        AniControlEnabled -= 1

        '非重复加载部分
        Static Reloaded As Boolean = False
        If Reloaded Then Return
        Reloaded = True

        SliderLoad()

    End Sub
    Public Sub Refresh()
        Try
            SettingService.RefreshSettings(Me)
            BackgroundRefresh(False, False)
            VideoRefreshUI()
            ThemeCustomRefresh()

            '标题栏
            CheckLogoLeft.Visibility = If(RadioLogoType0.Checked, Visibility.Visible, Visibility.Collapsed)
            PanLogoText.Visibility = If(RadioLogoType2.Checked, Visibility.Visible, Visibility.Collapsed)
            PanLogoChange.Visibility = If(RadioLogoType3.Checked, Visibility.Visible, Visibility.Collapsed)

            '背景音乐
            MusicRefreshUI()

            '主页
            OnMainPageTypeChanged()
        Catch ex As NullReferenceException
            Logger.Error(ex, "个性化设置项存在异常，已被自动重置", LogBehavior.Alert)
            Reset()
        Catch ex As Exception
            Logger.Error(ex, "重载个性化设置时出错")
        End Try
    End Sub
    Public Sub Reset()
        Try
            SettingService.ResetSettings(Me)
            Logger.Info("已初始化个性化设置！")
            Hint("已初始化个性化设置", HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "初始化个性化设置失败", LogBehavior.Alert)
        End Try
        Refresh()
    End Sub

    '背景图片
    Private PictureCount As Integer = 0
    Private Sub BtnUIBgOpen_Click(sender As Object, e As EventArgs) Handles BtnBackgroundOpen.Click
        OpenExplorer(Paths.Base & "PCL\Pictures\")
    End Sub
    Private Sub BtnBackgroundRefresh_Click(sender As Object, e As EventArgs) Handles BtnBackgroundRefresh.Click
        BackgroundRefresh(True, True)
    End Sub
    Public Sub BackgroundRefreshUI(Show As Boolean, Count As Integer)
        If IsNothing(PanBackgroundOpacity) Then Return
        PictureCount = Count
        '动态背景同样使用不透明度与模糊设置，因此有视频时也要显示这两行
        Dim SharedVisible = Show OrElse VideoBackgroundPath() IsNot Nothing
        If SharedVisible Then
            PanBackgroundOpacity.Visibility = Visibility.Visible
            PanBackgroundBlur.Visibility = Visibility.Visible
        Else
            PanBackgroundOpacity.Visibility = Visibility.Collapsed
            PanBackgroundBlur.Visibility = Visibility.Collapsed
        End If
        If Show Then
            PanBackgroundSuit.Visibility = Visibility.Visible
            BtnBackgroundClear.Visibility = Visibility.Visible
            CardBackground.Title = "背景图片（" & Count & " 张）"
        Else
            PanBackgroundSuit.Visibility = Visibility.Collapsed
            BtnBackgroundClear.Visibility = Visibility.Collapsed
            CardBackground.Title = "背景图片"
        End If
        CardBackground.TriggerForceResize()
    End Sub
    Private Sub BtnBackgroundClear_Click(sender As Object, e As EventArgs) Handles BtnBackgroundClear.Click
        If MyMsgBox("即将删除背景图片文件夹中的所有文件。" & vbCrLf & "此操作不可撤销，是否确定？", "警告",, "取消", IsWarn:=True) = 1 Then
            DirectoryUtils.Delete(Paths.Base & "PCL\Pictures")
            BackgroundRefresh(False, True)
            Hint("背景图片已清空！", HintType.Green)
        End If
    End Sub
    ''' <summary>
    ''' 刷新背景图片及设置页 UI。
    ''' </summary>
    ''' <param name="IsHint">是否显示刷新提示。</param>
    ''' <param name="Refresh">是否刷新图片显示。</param>
    Public Shared Sub BackgroundRefresh(IsHint As Boolean, Refresh As Boolean)
        Try

            '获取可用的图片文件
            DirectoryUtils.Create(Paths.Base & "PCL\Pictures\")
            Dim Pic As New List(Of String)
            For Each File In DirectoryUtils.EnumerateFiles(Paths.Base & "PCL\Pictures\", True)
                Dim Extension As String = PathUtils.GetExtension(File)
                If Extension <> "ini" AndAlso Extension <> "db" Then Pic.Add(File) '文件夹可能会被加入 .ini 和 thumbs.db
            Next
            '加载
            If Not Pic.Any() Then
                If Refresh Then
                    If FrmMain.ImgBack.Visibility = Visibility.Collapsed Then
                        If IsHint Then Hint("未检测到可用背景图片！", HintType.Red)
                    Else
                        FrmMain.ImgBack.Visibility = Visibility.Collapsed
                        If IsHint Then Hint("背景图片已清除！", HintType.Green)
                    End If
                End If
                If Not IsNothing(FrmSetupUI) Then FrmSetupUI.BackgroundRefreshUI(False, 0)
            Else
                If Refresh AndAlso Not VideoBackgroundActive() Then
                    Dim Address As String = RandomOne(Pic)
                    Try
                        Logger.Info($"加载背景图片：{Address}")
                        FrmMain.ImgBack.Background = New MyBitmap(Address)
                        FrmMain.ImgBack.Visibility = Visibility.Visible
                        FrmMain.UpdateBackgroundAndTitleBar()
                        If IsHint Then Hint("背景图片已刷新：" & PathUtils.GetLastPart(Address), HintType.Green, False)
                    Catch ex As Exception
                        If ex.Message.Contains("参数无效") Then
                            Logger.Error($"刷新背景图片失败，该图片文件可能并非标准格式。{vbCrLf}你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。{vbCrLf}文件：{Address}", LogBehavior.Alert)
                        Else
                            Logger.Error(ex, $"刷新背景图片失败（{Address}）", LogBehavior.Alert)
                        End If
                    End Try
                End If
                If Not IsNothing(FrmSetupUI) Then FrmSetupUI.BackgroundRefreshUI(True, Pic.Count)
            End If

        Catch ex As Exception
            Logger.Error(ex, "刷新背景图片时出现未知错误")
        End Try
    End Sub

    '动态背景
    Private Sub BtnVideoImport_Click(sender As Object, e As EventArgs) Handles BtnVideoImport.Click
        Dim FileName As String = Dialogs.SelectFile("选择视频", False, filter:={({"mp4", "wmv", "mkv", "avi", "mov", "webm"}, "常用视频文件")}).FirstOrDefault()
        If String.IsNullOrEmpty(FileName) Then Return
        '复制到 PCL 文件夹下，以避免源文件被移动或重命名；磁盘不足等情况则直接使用原文件
        Dim Address As String = FileName
        Try
            If Not PathUtils.IsSame(Path.GetDirectoryName(FileName), Paths.Base & "PCL\Videos") Then
                Dim Target As String = Paths.Base & "PCL\Videos\" & PathUtils.GetLastPart(FileName)
                DirectoryUtils.Create(Paths.Base & "PCL\Videos\")
                FileUtils.Copy(FileName, Target)
                Address = Target
            End If
        Catch ex As Exception
            Logger.Error(ex, "导入动态背景视频失败，将直接使用原文件位置", LogBehavior.Alert)
            Address = FileName
        End Try
        Settings.Set("UiBackgroundVideo", Address)
        Hint("动态背景已设置：" & PathUtils.GetLastPart(Address), HintType.Green, False)
    End Sub
    Private Sub BtnVideoFolder_Click(sender As Object, e As EventArgs) Handles BtnVideoFolder.Click
        Dim Address = VideoBackgroundPath()
        If Address Is Nothing Then
            OpenExplorer(Paths.Base & "PCL\Videos\")
        Else
            OpenExplorer(Path.GetDirectoryName(Address) & "\")
        End If
    End Sub
    Private Sub BtnVideoClear_Click(sender As Object, e As EventArgs) Handles BtnVideoClear.Click
        If MyMsgBox("即将停止使用动态背景，不会删除视频文件。是否确定？", "清空动态背景", "确定", "取消") = 2 Then Return
        Settings.Set("UiBackgroundVideo", "")
        Hint("动态背景已清空！", HintType.Green)
    End Sub
    ''' <summary>
    ''' 按当前动态背景设置刷新本页 UI。
    ''' </summary>
    Public Sub VideoRefreshUI()
        If IsNothing(CardVideo) Then Return
        Dim Address = VideoBackgroundPath()
        If Address Is Nothing Then
            PanVideoDetail.Visibility = Visibility.Collapsed
            BtnVideoFolder.Visibility = Visibility.Collapsed
            BtnVideoClear.Visibility = Visibility.Collapsed
            CardVideo.Title = "动态背景"
        Else
            PanVideoDetail.Visibility = Visibility.Visible
            BtnVideoFolder.Visibility = Visibility.Visible
            BtnVideoClear.Visibility = Visibility.Visible
            CardVideo.Title = "动态背景（" & PathUtils.GetLastPart(Address) & "）"
        End If
        CardVideo.TriggerForceResize()
        BackgroundRefreshUI(PictureCount > 0, PictureCount)
    End Sub

    '顶部栏
    Private Sub BtnLogoChange_Click(sender As Object, e As EventArgs) Handles BtnLogoChange.Click
        Dim FileName As String = Dialogs.SelectFile("选择图片", False, filter:={({"png", "jpg", "jpeg", "gif", "webp"}, "常用图片文件")}).FirstOrDefault()
        If String.IsNullOrEmpty(FileName) Then Return
        Dim TargetPath As String = Paths.Base & "PCL\Logo.png"
        Try
            '复制文件
            FileUtils.Copy(FileName, TargetPath)
            '设置当前显示
            FrmMain.ImageTitleLogo.Source = Nothing '防止因为 Source 属性前后的值相同而不更新 (#5628)
            FrmMain.ImageTitleLogo.Source = TargetPath
        Catch ex As Exception
            If ex.Message.Contains("参数无效") Then
                Logger.Error($"改变标题栏图片失败，该图片文件可能并非标准格式。{vbCrLf}你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。{vbCrLf}文件：{TargetPath}", LogBehavior.Alert)
            Else
                Logger.Error(ex, "设置标题栏图片失败", LogBehavior.Alert)
            End If
            FrmMain.ImageTitleLogo.Source = Nothing
        End Try
    End Sub
    Private Sub RadioLogoType3_Check(sender As Object, e As RouteEventArgs) Handles RadioLogoType3.PreviewCheck
        If Not (AniControlEnabled = 0 AndAlso e.RaiseByMouse) Then Return
Refresh:
        '已有图片则不再选择
        Dim TargetPath As String = Paths.Base & "PCL\Logo.png"
        If FileUtils.Exists(TargetPath) Then
            Try
                FrmMain.ImageTitleLogo.Source = Nothing '防止因为 Source 属性前后的值相同而不更新 (#5628)
                FrmMain.ImageTitleLogo.Source = TargetPath
            Catch ex As Exception
                If ex.Message.Contains("参数无效") Then
                    Logger.Error($"改变标题栏图片失败，该图片文件可能并非标准格式。{vbCrLf}你可以尝试使用画图打开该文件并重新保存，这会让图片变为标准格式。{vbCrLf}文件：{TargetPath}", LogBehavior.Alert)
                Else
                    Logger.Error(ex, "调整标题栏图片失败", LogBehavior.Alert)
                End If
                FrmMain.ImageTitleLogo.Source = Nothing
                e.Handled = True
                Try
                    FileUtils.Delete(TargetPath)
                Catch exx As Exception
                    Logger.Error(exx, "清理错误的标题栏图片失败", LogBehavior.Alert)
                End Try
            End Try
            Return
        End If
        '没有图片则要求选择
        Dim FileName As String = Dialogs.SelectFile("选择图片", False, filter:={({"png", "jpeg", "jpg", "gif", "webp"}, "常用图片文件")}).FirstOrDefault()
        If String.IsNullOrEmpty(FileName) Then
            FrmMain.ImageTitleLogo.Source = Nothing
            e.Handled = True
        Else
            Try
                FileUtils.Copy(FileName, TargetPath)
                GoTo Refresh
            Catch ex As Exception
                Logger.Error(ex, "复制标题栏图片失败", LogBehavior.Alert)
            End Try
        End If
    End Sub
    Private Sub BtnLogoDelete_Click(sender As Object, e As EventArgs) Handles BtnLogoDelete.Click
        Try
            FileUtils.Delete(Paths.Base & "PCL\Logo.png")
            RadioLogoType1.SetChecked(True, True)
            Hint("标题栏图片已清空！", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "清空标题栏图片失败", LogBehavior.Alert)
        End Try
    End Sub

    '背景音乐
    Private Sub BtnMusicOpen_Click(sender As Object, e As EventArgs) Handles BtnMusicOpen.Click
        OpenExplorer(Paths.Base & "PCL\Musics\")
    End Sub
    Private Sub BtnMusicRefresh_Click(sender As Object, e As EventArgs) Handles BtnMusicRefresh.Click
        MusicRefreshPlay(True)
    End Sub
    Public Sub MusicRefreshUI()
        If PanBackgroundOpacity Is Nothing Then Return
        If MusicAllList.Any Then
            PanMusicVolume.Visibility = Visibility.Visible
            PanMusicDetail.Visibility = Visibility.Visible
            BtnMusicClear.Visibility = Visibility.Visible
            CardMusic.Title = "背景音乐（" &
                DirectoryUtils.EnumerateFiles(Paths.Base & "PCL\Musics\", True).Count(Function(f) Not {"ini", "jpg", "txt", "cfg", "lrc", "db", "png"}.Contains(PathUtils.GetExtension(f))) &
                " 首）"
        Else
            PanMusicVolume.Visibility = Visibility.Collapsed
            PanMusicDetail.Visibility = Visibility.Collapsed
            BtnMusicClear.Visibility = Visibility.Collapsed
            CardMusic.Title = "背景音乐"
        End If
        CardMusic.TriggerForceResize()
    End Sub
    Private Sub BtnMusicClear_Click(sender As Object, e As EventArgs) Handles BtnMusicClear.Click
        If MyMsgBox("即将删除背景音乐文件夹中的所有文件。" & vbCrLf & "此操作不可撤销，是否确定？", "警告",, "取消", IsWarn:=True) = 1 Then
            RunInThread(
            Sub()
                Hint("正在删除背景音乐……")
                '停止播放音乐
                MusicNAudio = Nothing
                MusicWaitingList = New List(Of String)
                MusicAllList = New List(Of String)
                Thread.Sleep(200)
                '删除文件
                Try
                    DirectoryUtils.Delete(Paths.Base & "PCL\Musics")
                    Hint("背景音乐已删除！", HintType.Green)
                Catch ex As Exception
                    Logger.Error(ex, "删除背景音乐失败", LogBehavior.Alert)
                End Try
                Try
                    DirectoryUtils.Create(Paths.Base & "PCL\Musics\")
                    RunInUi(Sub() MusicRefreshPlay(False))
                Catch ex As Exception
                    Logger.Error(ex, "重建背景音乐文件夹失败", LogBehavior.Alert)
                End Try
            End Sub)
        End If
    End Sub
    Private Sub CheckMusicStart_Change() Handles CheckMusicStart.Change
        If AniControlEnabled <> 0 Then Return
        If CheckMusicStart.Checked Then CheckMusicStop.Checked = False
    End Sub
    Private Sub CheckMusicStop_Change() Handles CheckMusicStop.Change
        If AniControlEnabled <> 0 Then Return
        If CheckMusicStop.Checked Then CheckMusicStart.Checked = False
    End Sub

    '主页
    Private Sub BtnCustomFile_Click(sender As Object, e As EventArgs) Handles BtnCustomFile.Click
        Try
            If FileUtils.Exists(Paths.Base & "PCL\Custom.xaml") Then
                If MyMsgBox("当前已存在布局文件，继续生成教学文件将会覆盖现有布局文件！", "覆盖确认", "继续", "取消", IsWarn:=True) = 2 Then Return
            End If
            ExtractResources(Paths.Base & "PCL\Custom.xaml", "Custom")
            Hint("教学文件已生成！", HintType.Green)
            OpenExplorer(Paths.Base & "PCL\Custom.xaml")
        Catch ex As Exception
            Logger.Error(ex, "生成教学文件失败")
        End Try
    End Sub
    Private Sub BtnCustomRefresh_Click() Handles BtnCustomRefresh.Click
        FrmLaunchRight.ForceRefresh()
        Hint("已刷新主页！", HintType.Green)
    End Sub
    Private Sub BtnCustomTutorial_Click(sender As Object, e As EventArgs) Handles BtnCustomTutorial.Click
        MyMsgBox("1. 点击 生成教学文件 按钮，这会在 PCL 文件夹下生成 Custom.xaml 布局文件。" & vbCrLf &
                 "2. 使用记事本等工具打开这个文件并进行修改，修改完记得保存。" & vbCrLf &
                 "3. 点击 刷新主页 按钮，查看主页现在长啥样了。" & vbCrLf &
                 vbCrLf &
                 "你可以在生成教学文件后直接刷新主页，对照着进行修改，更有助于理解。" & vbCrLf &
                 "直接将主页文件拖进 PCL 窗口也可以快捷加载。", "主页自定义教程")
    End Sub
    Public Shared Sub OnMainPageTypeChanged()
        If FrmSetupUI Is Nothing Then Return
        Select Case CInt(Settings.Get(Of Integer)("UiCustomType"))
            Case 0 '无
                FrmSetupUI.PanCustomPreset.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomLocal.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomNet.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustom.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustomWarn.Visibility = Visibility.Collapsed
            Case 1 '本地
                FrmSetupUI.PanCustomPreset.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomLocal.Visibility = Visibility.Visible
                FrmSetupUI.PanCustomNet.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustom.Visibility = Visibility.Visible
                FrmSetupUI.HintCustomWarn.Visibility = If(Settings.Get(Of Boolean)("HintCustomWarn"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupUI.HintCustom.Text = $"从 PCL 文件夹下的 Custom.xaml 读取主页内容。{vbCrLf}你可以手动编辑该文件，向主页添加文本、图片、常用网站、快捷启动等功能。"
                CustomEventService.SetEventType(FrmSetupUI.HintCustom, CustomEvent.EventType.None)
            Case 2 '联网
                FrmSetupUI.PanCustomPreset.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomLocal.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomNet.Visibility = Visibility.Visible
                FrmSetupUI.HintCustom.Visibility = Visibility.Visible
                FrmSetupUI.HintCustomWarn.Visibility = If(Settings.Get(Of Boolean)("HintCustomWarn"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupUI.HintCustom.Text = $"从指定网址联网获取主页内容。服主也可以用于动态更新服务器公告。{vbCrLf}如果你制作了稳定运行的联网主页，可以点击这条提示投稿，若合格即可加入预设！"
                CustomEventService.SetEventType(FrmSetupUI.HintCustom, CustomEvent.EventType.打开网页)
                CustomEventService.SetEventData(FrmSetupUI.HintCustom, "https://github.com/Meloong-Git/PCL/discussions/2528")
            Case 3 '预设
                FrmSetupUI.PanCustomPreset.Visibility = Visibility.Visible
                FrmSetupUI.PanCustomLocal.Visibility = Visibility.Collapsed
                FrmSetupUI.PanCustomNet.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustom.Visibility = Visibility.Collapsed
                FrmSetupUI.HintCustomWarn.Visibility = Visibility.Collapsed
        End Select
        FrmSetupUI.CardCustom.TriggerForceResize()
    End Sub
    Private Sub BtnCustomFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCustomFolder.Click
        Dim CustomPath As String = Paths.Base & "PCL\Custom.xaml"
        If FileUtils.Exists(CustomPath) Then
            OpenExplorer(CustomPath)
        Else
            OpenExplorer(Paths.Base & "PCL\")
        End If
    End Sub

    '主题自定义
    Private Sub RadioLauncherTheme14_Change(sender As Object, e As RouteEventArgs) Handles RadioLauncherTheme14.Changed
        ThemeCustomRefresh()
    End Sub
    ''' <summary>
    ''' 按是否选中自定义主题，显示或隐藏色调等设置项。
    ''' </summary>
    Public Sub ThemeCustomRefresh()
        If IsNothing(LabLauncherHue) Then Return
        '未赞助解锁时不允许选中自定义主题，并在下方给出赞助入口
        Dim Unlocked = ThemeCustomUnlocked()
        RadioLauncherTheme14.IsEnabled = Unlocked
        PanThemeCustomLock.Visibility = If(Unlocked, Visibility.Collapsed, Visibility.Visible)
        Dim Value = If(RadioLauncherTheme14.Checked, Visibility.Visible, Visibility.Collapsed)
        LabLauncherHue.Visibility = Value
        SliderLauncherHue.Visibility = Value
        LabLauncherSat.Visibility = Value
        SliderLauncherSat.Visibility = Value
        LabLauncherDelta.Visibility = Value
        SliderLauncherDelta.Visibility = Value
        LabLauncherLight.Visibility = Value
        SliderLauncherLight.Visibility = Value
        PanThemeCustom.Visibility = Value
        CardLauncher.TriggerForceResize()
    End Sub
    Private Sub BtnThemePickColor_Click(sender As Object, e As EventArgs) Handles BtnThemePickColor.Click
        Dim FileName As String = Dialogs.SelectFile("选择图片", False, filter:={({"png", "jpg", "jpeg", "gif", "webp", "bmp"}, "常用图片文件")}).FirstOrDefault()
        If String.IsNullOrEmpty(FileName) Then Return
        Try
            Dim MainColor = GetImageMainColor(FileName)
            Settings.Set("UiLauncherHue", MainColor(0))
            Settings.Set("UiLauncherSat", MainColor(1))
            AniControlEnabled += 1
            SliderLauncherHue.Value = MainColor(0)
            SliderLauncherSat.Value = MainColor(1)
            AniControlEnabled -= 1
            RadioLauncherTheme14.Checked = True
            Hint("已从图片取出主题色：" & MainColor(0) & "°，" & MainColor(1) & "%", HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "从图片取主色失败", LogBehavior.Alert)
        End Try
    End Sub
    Private Sub BtnThemeResetCustom_Click(sender As Object, e As EventArgs) Handles BtnThemeResetCustom.Click
        Dim Hue = CInt(Val(Settings.GetDefault("UiLauncherHue"))), Sat = CInt(Val(Settings.GetDefault("UiLauncherSat")))
        Dim Light = CInt(Val(Settings.GetDefault("UiLauncherLight"))), Delta = CInt(Val(Settings.GetDefault("UiLauncherDelta")))
        Settings.Set("UiLauncherHue", Hue)
        Settings.Set("UiLauncherSat", Sat)
        Settings.Set("UiLauncherLight", Light)
        Settings.Set("UiLauncherDelta", Delta)
        AniControlEnabled += 1
        SliderLauncherHue.Value = Hue
        SliderLauncherSat.Value = Sat
        SliderLauncherLight.Value = Light
        SliderLauncherDelta.Value = Delta
        AniControlEnabled -= 1
        ThemeRefresh()
        Hint("自定义主题色已重置！", HintType.Green)
    End Sub
    Private Sub BtnThemeCustomSponsor_Click(sender As Object, e As EventArgs) Handles BtnThemeCustomSponsor.Click
        OpenWebsite("https://meloong.com/afd/a/LTCat")
    End Sub
    Private Sub BtnThemeCustomUnlock_Click(sender As Object, e As EventArgs) Handles BtnThemeCustomUnlock.Click
        If ThemeCustomUnlockRequest() = 1 Then ThemeCustomRefresh()
    End Sub
    ''' <summary>
    ''' 统计图片中出现最多的彩色像素，返回 {色调 0-360, 饱和度 0-100}。
    ''' </summary>
    Private Shared Function GetImageMainColor(FileName As String) As Integer()
        Dim Big As System.Drawing.Bitmap = New MyBitmap(FileName)
        If Big Is Nothing OrElse Big.Width < 1 OrElse Big.Height < 1 Then Throw New Exception("无法读取该图片")
        Dim Longest = Math.Max(1, Math.Max(Big.Width, Big.Height))
        Using Image As System.Drawing.Bitmap = If(Longest > 64, New System.Drawing.Bitmap(Big, New System.Drawing.Size(Math.Max(1, Big.Width * 64 \ Longest), Math.Max(1, Big.Height * 64 \ Longest))), Big)
            Dim Counts(35) As Integer, Sats(35) As Long
            For X = 0 To Image.Width - 1
                For Y = 0 To Image.Height - 1
                    Dim Pixel = Image.GetPixel(X, Y)
                    If Pixel.A < 128 Then Continue For
                    Dim R = CDbl(Pixel.R), G = CDbl(Pixel.G), B = CDbl(Pixel.B)
                    Dim Max = Math.Max(R, Math.Max(G, B)), Min = Math.Min(R, Math.Min(G, B))
                    If Max = Min OrElse Max < 1 Then Continue For
                    Dim Saturation = (Max - Min) / Max * 100.0
                    If Saturation < 20 Then Continue For
                    Dim Hue As Double
                    If Max = R Then
                        Hue = 60.0 * (G - B) / (Max - Min)
                    ElseIf Max = G Then
                        Hue = 60.0 * (B - R) / (Max - Min) + 120
                    Else
                        Hue = 60.0 * (R - G) / (Max - Min) + 240
                    End If
                    Dim Bucket = CInt(((Hue + 360) Mod 360) / 10) Mod 36
                    Counts(Bucket) += 1
                    Sats(Bucket) += CLng(Saturation)
                Next
            Next
            Dim Best As Integer = 0
            For Index = 1 To 35
                If Counts(Index) > Counts(Best) Then Best = Index
            Next
            If Counts(Best) = 0 Then Return New Integer() {210, 85}
            Return New Integer() {Best * 10 + 5, CInt(Math.Min(100, Sats(Best) / Counts(Best)))}
        End Using
    End Function
    Private Sub HSL_Change() Handles SliderLauncherHue.Change, SliderLauncherLight.Change, SliderLauncherSat.Change, SliderLauncherDelta.Change
        If AniControlEnabled <> 0 OrElse SliderLauncherSat Is Nothing OrElse Not SliderLauncherSat.IsLoaded Then Return
        ThemeRefresh()
    End Sub

#Region "功能隐藏"

    Private Shared _HiddenForceShow As Boolean = False
    ''' <summary>
    ''' 是否强制显示被禁用的功能。
    ''' </summary>
    Public Shared Property HiddenForceShow As Boolean
        Get
            Return _HiddenForceShow
        End Get
        Set(value As Boolean)
            _HiddenForceShow = value
            HiddenRefresh()
        End Set
    End Property

    ''' <summary>
    ''' 更新功能隐藏带来的显示变化。
    ''' </summary>
    Public Shared Sub HiddenRefresh() Handles Me.Loaded
        If FrmMain.PanTitleSelect Is Nothing OrElse Not FrmMain.PanTitleSelect.IsLoaded Then Return
        Try
            '顶部栏
            If Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageDownload") AndAlso Settings.Get(Of Boolean)("UiHiddenPageLink") AndAlso Settings.Get(Of Boolean)("UiHiddenPageSetup") AndAlso Settings.Get(Of Boolean)("UiHiddenPageOther") Then
                '顶部栏已被全部隐藏
                FrmMain.PanTitleSelect.Visibility = Visibility.Collapsed
            Else
                '顶部栏未被全部隐藏
                FrmMain.PanTitleSelect.Visibility = Visibility.Visible
                FrmMain.BtnTitleSelect1.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageDownload"), Visibility.Collapsed, Visibility.Visible)
                FrmMain.BtnTitleSelect2.Visibility = Visibility.Collapsed 'If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageLink"), Visibility.Collapsed, Visibility.Visible)
                FrmMain.BtnTitleSelect3.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageSetup"), Visibility.Collapsed, Visibility.Visible)
                FrmMain.BtnTitleSelect4.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenPageOther"), Visibility.Collapsed, Visibility.Visible)
            End If
            '功能
            FrmLaunchLeft.RefreshButtonsUI()
            If FrmSetupUI IsNot Nothing Then
                FrmSetupUI.CardSwitch.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenFunctionHidden"), Visibility.Collapsed, Visibility.Visible)
            End If
            '版本设置子页面
            If FrmInstanceLeft IsNot Nothing Then
                FrmInstanceLeft.ItemModify.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenFunctionInstanceModify"), Visibility.Collapsed, Visibility.Visible)
            End If
            '设置子页面
            If FrmSetupLeft IsNot Nothing Then
                FrmSetupLeft.ItemLaunch.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenSetupLaunch"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupLeft.ItemUI.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenSetupUi"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupLeft.ItemLink.Visibility = Visibility.Collapsed 'If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenSetupLink"), Visibility.Collapsed, Visibility.Visible)
                FrmSetupLeft.ItemSystem.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenSetupSystem"), Visibility.Collapsed, Visibility.Visible)
                '隐藏左边选择卡
                Dim AvaliableCount As Integer = 0
                If Not Settings.Get(Of Boolean)("UiHiddenSetupLaunch") Then AvaliableCount += 1
                If Not Settings.Get(Of Boolean)("UiHiddenSetupUi") Then AvaliableCount += 1
                'If Not Settings.Get(Of Boolean)("UiHiddenSetupLink") Then AvaliableCount += 1
                If Not Settings.Get(Of Boolean)("UiHiddenSetupSystem") Then AvaliableCount += 1
                FrmSetupLeft.PanItem.Visibility = If(AvaliableCount < 2 AndAlso Not HiddenForceShow, Visibility.Collapsed, Visibility.Visible)
            End If
            '更多子页面
            Dim OtherAvaliableCount As Integer = 0
            If Not Settings.Get(Of Boolean)("UiHiddenOtherHelp") Then OtherAvaliableCount += 1
            If Not Settings.Get(Of Boolean)("UiHiddenOtherAbout") Then OtherAvaliableCount += 1
            If Not Settings.Get(Of Boolean)("UiHiddenOtherTest") Then OtherAvaliableCount += 1
            If Not Settings.Get(Of Boolean)("UiHiddenOtherFeedback") Then OtherAvaliableCount += 1
            If Not Settings.Get(Of Boolean)("UiHiddenOtherVote") Then OtherAvaliableCount += 1
            If Not Settings.Get(Of Boolean)("UiHiddenOtherLink") Then OtherAvaliableCount += 1
            If FrmOtherLeft IsNot Nothing Then
                FrmOtherLeft.ItemHelp.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherHelp"), Visibility.Collapsed, Visibility.Visible)
                FrmOtherLeft.ItemFeedback.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherFeedback"), Visibility.Collapsed, Visibility.Visible)
                FrmOtherLeft.ItemVote.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherVote"), Visibility.Collapsed, Visibility.Visible)
                FrmOtherLeft.ItemAbout.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherAbout"), Visibility.Collapsed, Visibility.Visible)
                FrmOtherLeft.ItemTest.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherTest"), Visibility.Collapsed, Visibility.Visible)
                FrmOtherLeft.ItemLink.Visibility = If(Not HiddenForceShow AndAlso Settings.Get(Of Boolean)("UiHiddenOtherLink"), Visibility.Collapsed, Visibility.Visible)
                '隐藏左边选择卡
                FrmOtherLeft.PanItem.Visibility = If(OtherAvaliableCount < 2 AndAlso Not HiddenForceShow, Visibility.Collapsed, Visibility.Visible)
            End If
            If OtherAvaliableCount = 1 AndAlso Not HiddenForceShow Then
                If Not Settings.Get(Of Boolean)("UiHiddenOtherHelp") Then
                    FrmMain.BtnTitleSelect4.Text = "帮助"
                ElseIf Not Settings.Get(Of Boolean)("UiHiddenOtherAbout") Then
                    FrmMain.BtnTitleSelect4.Text = "关于"
                ElseIf Not Settings.Get(Of Boolean)("UiHiddenOtherTest") Then
                    FrmMain.BtnTitleSelect4.Text = "小工具"
                ElseIf Not Settings.Get(Of Boolean)("UiHiddenOtherLink") Then
                    FrmMain.BtnTitleSelect4.Text = "联机"
                Else
                    FrmMain.BtnTitleSelect4.Text = "更多"
                End If
            Else
                FrmMain.BtnTitleSelect4.Text = "更多"
            End If
            '各个页面的入口
            If FrmMain.PageCurrent = FormMain.PageType.InstanceSelect Then FrmSelectRight.BtnEmptyDownload_Loaded()
            If FrmMain.PageCurrent = FormMain.PageType.Launch Then FrmLaunchLeft.RefreshButtonsUI()
            If FrmMain.PageCurrent = FormMain.PageType.InstanceSetup AndAlso FrmInstanceModDisabled IsNot Nothing Then FrmInstanceModDisabled.BtnDownload_Loaded()
            '备注
            If FrmSetupUI IsNot Nothing Then FrmSetupUI.CardSwitch.Title = If(HiddenForceShow, "功能隐藏（已暂时关闭，按 F12 以重新启用）", "功能隐藏")
        Catch ex As Exception
            Logger.Error(ex, "刷新功能隐藏项目失败")
        End Try
    End Sub

    'UI 协同改变
    Private Sub HiddenSetupMain() Handles CheckHiddenPageSetup.Change
        '设置主页面
        If CheckHiddenPageSetup.Checked Then
            '开启
            CheckHiddenSetupLaunch.Checked = True
            CheckHiddenSetupSystem.Checked = True
            CheckHiddenSetupLink.Checked = True
            CheckHiddenSetupUI.Checked = True
        Else
            '关闭
            If Settings.Get(Of Boolean)("UiHiddenSetupLaunch") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupUi") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupSystem") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupLink") Then
                CheckHiddenSetupLaunch.Checked = False
                CheckHiddenSetupSystem.Checked = False
                CheckHiddenSetupLink.Checked = False
                CheckHiddenSetupUI.Checked = False
            End If
        End If
    End Sub
    Private Sub HiddenSetupSub() Handles CheckHiddenSetupLaunch.Change, CheckHiddenSetupSystem.Change, CheckHiddenSetupLink.Change, CheckHiddenSetupUI.Change
        '设置子页面
        If Settings.Get(Of Boolean)("UiHiddenSetupLaunch") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupUi") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupSystem") AndAlso Settings.Get(Of Boolean)("UiHiddenSetupLink") Then
            '已被全部隐藏
            CheckHiddenPageSetup.Checked = True
        Else
            '未被全部隐藏
            CheckHiddenPageSetup.Checked = False
        End If
    End Sub
    Private Sub HiddenOtherMain() Handles CheckHiddenPageOther.Change
        '更多主页面
        If CheckHiddenPageOther.Checked Then
            '开启
            CheckHiddenOtherAbout.Checked = True
            CheckHiddenOtherTest.Checked = True
            CheckHiddenOtherLink.Checked = True
            CheckHiddenOtherFeedback.Checked = True
            CheckHiddenOtherVote.Checked = True
            CheckHiddenOtherHelp.Checked = True
        Else
            '关闭
            If Settings.Get(Of Boolean)("UiHiddenOtherHelp") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherAbout") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherTest") AndAlso
                Settings.Get(Of Boolean)("UiHiddenOtherLink") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherVote") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherFeedback") Then
                CheckHiddenOtherAbout.Checked = False
                CheckHiddenOtherTest.Checked = False
                CheckHiddenOtherLink.Checked = False
                CheckHiddenOtherFeedback.Checked = False
                CheckHiddenOtherVote.Checked = False
                CheckHiddenOtherHelp.Checked = False
            End If
        End If
    End Sub
    Private Sub HiddenOtherSub(sender As Object, user As Boolean) Handles CheckHiddenOtherHelp.Change, CheckHiddenOtherAbout.Change, CheckHiddenOtherTest.Change, CheckHiddenOtherLink.Change
        '更多子页面（有具体内容的）
        If IsOtherSubAllHidden() Then
            '已被全部隐藏
            CheckHiddenPageOther.Checked = True
        Else
            '未被全部隐藏
            CheckHiddenPageOther.Checked = False
        End If
        '修改无具体内容的项
        If Not user Then Return
        If IsOtherSubAllHidden() Then
            CheckHiddenOtherFeedback.Checked = True
            CheckHiddenOtherVote.Checked = True
        End If
    End Sub
    Private Function IsOtherSubAllHidden() As Boolean
        Return Settings.Get(Of Boolean)("UiHiddenOtherHelp") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherAbout") AndAlso
            Settings.Get(Of Boolean)("UiHiddenOtherTest") AndAlso Settings.Get(Of Boolean)("UiHiddenOtherLink")
    End Function
    Private Sub HiddenOtherNet(sender As Object, user As Boolean) Handles CheckHiddenOtherFeedback.Change, CheckHiddenOtherVote.Change
        '更多子页面（无具体内容的）
        If Not user Then Return
        If IsOtherSubAllHidden() AndAlso (Not Settings.Get(Of Boolean)("UiHiddenOtherFeedback") OrElse Not Settings.Get(Of Boolean)("UiHiddenOtherVote")) Then
            CheckHiddenOtherAbout.Checked = False
            CheckHiddenOtherTest.Checked = False
            CheckHiddenOtherLink.Checked = False
            CheckHiddenOtherHelp.Checked = False
        End If
    End Sub

    '警告提示
    Private Sub HiddenHint(sender As Object, user As Boolean) Handles CheckHiddenFunctionHidden.Change, CheckHiddenPageSetup.Change, CheckHiddenSetupUI.Change
        If AniControlEnabled = 0 AndAlso sender.Checked Then Hint("按 F12 即可暂时关闭功能隐藏设置。千万别忘了，要不然设置就改不回来了……")
    End Sub

#End Region

    '滑动条
    Private Sub SliderLoad()
        SliderMusicVolume.GetHintText = Function(v) Math.Ceiling(v * 0.1) & "%"
        SliderLauncherTransparent.GetHintText = Function(v) Math.Round(40 + v * 0.1) & "%"
        SliderLauncherHue.GetHintText = Function(v) v & "°"
        SliderLauncherSat.GetHintText = Function(v) v & "%"
        SliderLauncherDelta.GetHintText =
        Function(Value As Integer) As String
            If Value > 90 Then
                Return "+" & (Value - 90)
            ElseIf Value = 90 Then
                Return 0
            Else
                Return Value - 90
            End If
        End Function
        SliderLauncherLight.GetHintText =
        Function(Value As Integer) As String
            If Value > 20 Then
                Return "+" & (Value - 20)
            ElseIf Value = 20 Then
                Return 0
            Else
                Return Value - 20
            End If
        End Function
        SliderBackgroundOpacity.GetHintText = Function(v) Math.Round(v * 0.1) & "%"
        SliderBackgroundBlur.GetHintText = Function(v) v & " 像素"
        SliderVideoSpeed.GetHintText = Function(v) (v / 100.0).ToString("F1") & " 倍速"
        SliderVideoVolume.GetHintText = Function(v) Math.Round(v * 0.1) & "%"
    End Sub

End Class
