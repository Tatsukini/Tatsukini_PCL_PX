Public Class PageOtherLink

    ''' <summary>
    ''' 内核是否已经准备好，可以调用 HTTP API。
    ''' </summary>
    Private IsKernelReady As Boolean = False
    ''' <summary>
    ''' 是否正在轮询陶瓦的状态。离开页面时停止，回到页面时重新开始。
    ''' </summary>
    Private IsPolling As Boolean = False
    Private PollFailCount As Integer = 0
    ''' <summary>
    ''' 上一次读到的状态编号。陶瓦每次状态变化都会让它递增，未变化时不需要刷新 UI。
    ''' </summary>
    Private LastIndex As Long = -1
    Private CurrentState As TerraState = Nothing

#Region "内核准备"

    Private ReadOnly KernelLoader As New LoaderCombo(Of Integer)("陶瓦联机", {
        New LoaderTask(Of Integer, List(Of NetFile))("准备下载联机内核", AddressOf KernelPrepareDownload) With {.ProgressWeight = 2},
        New LoaderDownload("下载联机内核", New List(Of NetFile)) With {.ProgressWeight = 40},
        New LoaderTask(Of Integer, Integer)("解压联机内核", AddressOf KernelExtract) With {.ProgressWeight = 6},
        New LoaderTask(Of Integer, Integer)("启动联机内核", AddressOf KernelStart) With {.ProgressWeight = 12}
    })

    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanBack, Nothing, KernelLoader, AddressOf Kernel_OnFinish)
        LabCopyright.Text = TerraLicenseText
        TxtPlayerName.Text = Settings.Get(Of String)("LinkTerracottaPlayer")
        TxtPublicNodes.Text = Settings.Get(Of String)("LinkTerracottaNodes")
    End Sub

    Private Sub KernelPrepareDownload(Task As LoaderTask(Of Integer, List(Of NetFile)))
        Task.Output = TerraRequiredFiles()
    End Sub
    Private Sub KernelExtract(Task As LoaderTask(Of Integer, Integer))
        TerraExtractDownloaded()
    End Sub
    Private Sub KernelStart(Task As LoaderTask(Of Integer, Integer))
        TerraStart(Task)
    End Sub

    Private Sub Kernel_OnFinish(Task As LoaderCombo(Of Integer))
        IsKernelReady = True
        PollFailCount = 0
        UpdateKernelInfo()
        StartPolling()
    End Sub

    ''' <summary>
    ''' 尝试开始联机前的检查。内核没准备好时会自动开始准备流程。
    ''' </summary>
    Private Function EnsureKernel() As Boolean
        If IsKernelReady AndAlso TerraPort > 0 Then Return True
        Select Case KernelLoader.State
            Case LoadState.Loading, LoadState.Waiting
                Hint("正在准备联机内核，请稍候……", Log:=False)
            Case Else
                PageLoaderRestart(IsForceRestart:=True)
        End Select
        Return False
    End Function

    ''' <summary>
    ''' 陶瓦退出了。它随时可能被别的程序结束，所以要能自愈。
    ''' </summary>
    Private Sub KernelStopped(Message As String)
        IsKernelReady = False
        TerraPort = 0
        TerraMeta = Nothing
        CurrentState = Nothing
        LastIndex = -1
        CardRoom.Visibility = Visibility.Collapsed
        LabStatus.Text = Message
        UpdateKernelInfo()
    End Sub

    Private Sub UpdateKernelInfo()
        Dim Version As String = "未就绪"
        Dim EasyTier As String = ""
        If TerraMeta IsNot Nothing Then
            If TerraMeta("version") IsNot Nothing Then Version = TerraMeta("version").ToString()
            If TerraMeta("easytier_version") IsNot Nothing Then EasyTier = "，内置 EasyTier " & TerraMeta("easytier_version").ToString()
        End If
        LabKernelInfo.Text = $"内核 {TerraVersion}（{Version}{EasyTier}）{vbLf}程序位置：{TerraExePath()}{vbLf}" &
            If(TerraPort > 0, $"本地端口：{TerraPort}", "本地端口：未运行")
    End Sub

#End Region

#Region "状态轮询"

    Private Sub StartPolling()
        If IsPolling OrElse TerraPort = 0 Then Return
        IsPolling = True
        RunInThread(
        Sub()
            Do While IsPolling
                Dim State As TerraState = Nothing
                Try
                    State = TerraGetState()
                    PollFailCount = 0
                Catch ex As Exception
                    ex.ThrowIfCanceled()
                    PollFailCount += 1
                    Logger.Warn(ex, "读取陶瓦状态失败")
                    If PollFailCount >= 10 Then
                        '陶瓦的接口连续 5 次以上读不到就可以认为它已经退出了，这里多留一点余量
                        IsPolling = False
                        RunInUi(Sub() KernelStopped("陶瓦联机内核已退出。点击任意卡片即可重新启动。"))
                        Return
                    End If
                End Try
                If State IsNot Nothing AndAlso State.Index <> LastIndex Then
                    LastIndex = State.Index
                    CurrentState = State
                    RunInUi(Sub() UpdateRoomUI(State))
                End If
                Thread.Sleep(500)
            Loop
        End Sub)
    End Sub

    Private Sub UpdateRoomUI(State As TerraState)
        LabStatus.Text = "状态：" & State.Description()
        '网络难度
        Dim Difficulty = State.DifficultyDescription()
        If Difficulty Is Nothing Then
            LabStatusHint.Visibility = Visibility.Collapsed
        Else
            LabStatusHint.Text = "网络难度：" & Difficulty
            LabStatusHint.Visibility = Visibility.Visible
        End If
        If State.Name = "waiting" OrElse State.Name = "exception" Then
            CardRoom.Visibility = Visibility.Collapsed
            If State.Name = "exception" AndAlso MyMsgBox(State.Description() & vbCrLf & "点击【返回待机】可以重新尝试。", "联机出错", "返回待机", "关闭", IsWarn:=True) = 1 Then ResetRoom()
            Return
        End If
        CardRoom.Visibility = Visibility.Visible
        CardRoom.Title = If(State.IsHostSide, "我的房间", "已加入的房间")
        BtnRoomLeave.Text = If(State.IsHostSide, "关闭房间", "离开房间")
        '邀请码
        If State.Room Is Nothing Then
            PanRoomCode.Visibility = Visibility.Collapsed
        Else
            PanRoomCode.Visibility = Visibility.Visible
            LabRoomCode.Text = State.Room
            Settings.Set("LinkTerracottaRoom", State.Room)
        End If
        '直连地址
        If State.DirectUrl Is Nothing Then
            PanRoomDirect.Visibility = Visibility.Collapsed
        Else
            PanRoomDirect.Visibility = Visibility.Visible
            LabRoomDirect.Text = State.DirectUrl
        End If
        '成员
        PanMembers.Children.Clear()
        For Each Profile In State.Profiles
            Dim Kind As String
            Select Case Profile.Kind
                Case "HOST" : Kind = "房主"
                Case "LOCAL" : Kind = "本机"
                Case Else : Kind = "玩家"
            End Select
            Dim Item As New TextBlock With {
                .Text = $"· {If(Profile.Name, "未知玩家")}（{Kind}）",
                .Margin = New Thickness(0, 2, 0, 0),
                .TextWrapping = TextWrapping.Wrap}
            PanMembers.Children.Add(Item)
        Next
        LabMembersEmpty.Visibility = If(State.Profiles.Count > 1, Visibility.Collapsed, Visibility.Visible)
        '提示
        LabRoomTip.Text = If(State.IsHostSide,
            "把邀请码发给朋友，他们加入后就能在游戏里看到你。请保持游戏处于开启状态，也不要关掉陶瓦。",
            "现在可以启动游戏，在多人游戏列表里就能看到房主的房间。如果列表是空的，请稍等几秒再刷新。")
    End Sub

#End Region

#Region "创建与加入"

    Private Function PlayerName() As String
        Return TxtPlayerName.Text.Trim
    End Function

    Private Function PublicNodes() As List(Of String)
        Return TxtPublicNodes.Text.SplitLines(True).Select(Function(s) s.Trim).Where(Function(s) s <> "").ToList()
    End Function

    Private Sub CardTileHost_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles CardTileHost.MouseLeftButtonUp
        If Not EnsureKernel() Then Return
        If CurrentState IsNot Nothing AndAlso CurrentState.Name <> "waiting" Then
            If MyMsgBox("当前已经在房间中，需要先返回待机才能创建新房间。", "无法创建房间", "返回待机", "取消") = 1 Then ResetRoom()
            Return
        End If
        Try
            TerraHost(Nothing, PlayerName(), PublicNodes())
        Catch ex As Exception
            MyMsgBox(ex.ToString(), "创建房间失败", IsWarn:=True)
            Return
        End Try
        Logger.Info("正在创建陶瓦联机房间")
        Hint("正在创建房间……", Log:=False)
        StartPolling()
    End Sub

    Private Sub CardTileJoin_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles CardTileJoin.MouseLeftButtonUp
        If Not EnsureKernel() Then Return
        If CurrentState IsNot Nothing AndAlso CurrentState.Name <> "waiting" Then
            If MyMsgBox("当前已经在房间中，需要先返回待机才能加入别的房间。", "无法加入房间", "返回待机", "取消") = 1 Then ResetRoom()
            Return
        End If
        Dim Code = MyMsgBoxInput("加入房间", $"输入朋友给你的邀请码，形如 {TerraRoomHint}。",
            DefaultInput:=Settings.Get(Of String)("LinkTerracottaRoom"), HintText:=TerraRoomHint)
        If Code Is Nothing Then Return
        Try
            TerraJoin(Code, PlayerName(), PublicNodes())
        Catch ex As Exception
            MyMsgBox(ex.ToString(), "加入房间失败", IsWarn:=True)
            Return
        End Try
        Logger.Info($"正在加入陶瓦联机房间：{Code}")
        Hint("正在加入房间……", Log:=False)
        StartPolling()
    End Sub

    ''' <summary>
    ''' 回到待机状态，同时关闭当前房间。
    ''' </summary>
    Private Sub BtnRoomLeave_Click(sender As Object, e As EventArgs) Handles BtnRoomLeave.Click
        ResetRoom()
    End Sub
    Private Sub ResetRoom()
        Try
            TerraReset()
            Hint("已返回待机。", Log:=False)
        Catch ex As Exception
            MyMsgBox(ex.ToString(), "返回待机失败", IsWarn:=True)
        End Try
    End Sub

    Private Sub BtnRoomCopy_Click(sender As Object, e As EventArgs) Handles BtnRoomCopy.Click
        ClipboardSet(LabRoomCode.Text, False)
        Hint("已复制邀请码！", HintType.Green)
    End Sub
    Private Sub BtnDirectCopy_Click(sender As Object, e As EventArgs) Handles BtnDirectCopy.Click
        ClipboardSet(LabRoomDirect.Text, False)
        Hint("已复制服务器地址！", HintType.Green)
    End Sub

#End Region

#Region "设置"

    Private Sub TxtPlayerName_LostFocus(sender As Object, e As RoutedEventArgs) Handles TxtPlayerName.LostFocus
        Settings.Set("LinkTerracottaPlayer", TxtPlayerName.Text.Trim)
    End Sub
    Private Sub TxtPublicNodes_LostFocus(sender As Object, e As RoutedEventArgs) Handles TxtPublicNodes.LostFocus
        Settings.Set("LinkTerracottaNodes", TxtPublicNodes.Text)
    End Sub

    Private Sub BtnOpenPanel_Click(sender As Object, e As EventArgs) Handles BtnOpenPanel.Click
        Dim Url = TerraPanelUrl()
        If Url Is Nothing Then
            Hint("陶瓦联机内核没有在运行。", Log:=False)
        Else
            OpenWebsite(Url)
        End If
    End Sub

    Private Sub BtnTerraWebsite_Click(sender As Object, e As EventArgs) Handles BtnTerraWebsite.Click
        OpenWebsite(TerraWebsite)
    End Sub
    Private Sub BtnTerraLicense_Click(sender As Object, e As EventArgs) Handles BtnTerraLicense.Click
        MyMsgBox(TerraLicenseText & vbCrLf & vbCrLf & TerraLicenseDetail, "开源许可", "关闭", HighLight:=False)
    End Sub

    Private Sub BtnQuitKernel_Click(sender As Object, e As EventArgs) Handles BtnQuitKernel.Click
        If TerraPort = 0 Then
            Hint("陶瓦联机内核没有在运行。", Log:=False)
            Return
        End If
        If CurrentState IsNot Nothing AndAlso CurrentState.Name <> "waiting" AndAlso
            MyMsgBox("陶瓦正在联机中，现在退出会断开所有连接。", "确认退出", "仍然退出", "取消") = 2 Then Return
        IsPolling = False
        TerraQuit()
        KernelStopped("陶瓦联机内核已退出。点击任意卡片即可重新启动。")
        Hint("已退出陶瓦后台。", HintType.Green)
    End Sub

    Private Sub BtnReinstallKernel_Click(sender As Object, e As EventArgs) Handles BtnReinstallKernel.Click
        If MyMsgBox($"这会删除当前的陶瓦联机内核，然后重新下载 {TerraVersion} 版本。", "确认重新下载", "重新下载", "取消") = 2 Then Return
        IsPolling = False
        TerraQuit()
        TerraReinstall()
        KernelStopped("正在重新下载陶瓦联机内核……")
        PageLoaderRestart(IsForceRestart:=True)
    End Sub

#End Region

#Region "页面切换"

    Private Sub PageEnterHandler() Handles Me.PageEnter
        If TerraPort = 0 Then IsKernelReady = False
        If Not IsKernelReady Then
            If KernelLoader.State = LoadState.Finished OrElse KernelLoader.State = LoadState.Failed Then PageLoaderRestart(IsForceRestart:=True)
        Else
            StartPolling()
            UpdateKernelInfo()
            '重新进入页面时立刻刷新一次，避免显示过期的房间信息
            StartPollingOnce()
        End If
    End Sub
    Private Sub PageExitHandler() Handles Me.PageExit
        IsPolling = False
        Settings.Set("LinkTerracottaPlayer", TxtPlayerName.Text.Trim)
        Settings.Set("LinkTerracottaNodes", TxtPublicNodes.Text)
    End Sub

    ''' <summary>
    ''' 只读取一次状态，用于进入页面时的立即刷新。
    ''' </summary>
    Private Sub StartPollingOnce()
        RunInThread(
        Sub()
            Try
                Dim State = TerraGetState()
                If State.Index <> LastIndex Then
                    LastIndex = State.Index
                    CurrentState = State
                    RunInUi(Sub() UpdateRoomUI(State))
                End If
            Catch ex As Exception
                Logger.Warn(ex, "刷新陶瓦状态失败")
            End Try
        End Sub)
    End Sub

#End Region

End Class
