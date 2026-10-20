Imports System.Windows.Media

''' <summary>
''' 动态背景：把视频绘制到主窗口背景上，覆盖背景图片。
''' </summary>
Public Module ModVideoBg

    Private Player As MediaPlayer = Nothing
    Private VideoDraw As VideoDrawing = Nothing
    Private VideoBrush As DrawingBrush = Nothing
    Private NowAddress As String = Nothing

    ''' <summary>
    ''' 设置中记录的视频文件位置，文件不存在时返回 Nothing。
    ''' </summary>
    Public Function VideoBackgroundPath() As String
        Dim Address = Settings.Get(Of String)("UiBackgroundVideo")
        If String.IsNullOrEmpty(Address) OrElse Not FileUtils.Exists(Address) Then Return Nothing
        Return Address
    End Function

    Public Function VideoBackgroundActive() As Boolean
        Return Player IsNot Nothing AndAlso NowAddress IsNot Nothing
    End Function

    ''' <summary>
    ''' 按当前设置加载或清除动态背景。必须在 UI 线程调用。
    ''' </summary>
    Public Sub VideoBackgroundApply()
        Try
            If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
            Dim Address = VideoBackgroundPath()

            If Address Is Nothing Then
                '没有可用的视频，恢复为背景图片
                Dim WasVideo = VideoBrush IsNot Nothing AndAlso ReferenceEquals(FrmMain.ImgBack.Background, VideoBrush)
                VideoBackgroundStop()
                If WasVideo Then
                    FrmMain.ImgBack.Background = Nothing
                    PageSetupUI.BackgroundRefresh(False, True)
                End If
                RefreshVideoUI()
                Return
            End If

            If Address = NowAddress AndAlso Player IsNot Nothing Then
                VideoBackgroundRefreshEffects()
                RefreshVideoUI()
                Return
            End If

            VideoBackgroundStop()
            Player = New MediaPlayer()
            AddHandler Player.MediaOpened, AddressOf Player_MediaOpened
            AddHandler Player.MediaEnded, AddressOf Player_MediaEnded
            AddHandler Player.MediaFailed, AddressOf Player_MediaFailed
            VideoDraw = New VideoDrawing With {.Player = Player, .Rect = New Rect(0, 0, 16, 9)}
            VideoBrush = BuildBrush()
            FrmMain.ImgBack.Background = VideoBrush
            FrmMain.ImgBack.Visibility = Visibility.Visible
            FrmMain.UpdateBackgroundAndTitleBar()
            Player.Open(New Uri(Address, UriKind.Absolute))
            Player.Play()
            NowAddress = Address
            Logger.Info($"加载动态背景：{Address}")
            RefreshVideoUI()
        Catch ex As Exception
            Logger.Error(ex, "加载动态背景失败", LogBehavior.Alert)
            VideoBackgroundStop()
            RefreshVideoUI()
        End Try
    End Sub

    ''' <summary>
    ''' 停止播放并释放播放器。
    ''' </summary>
    Public Sub VideoBackgroundClose()
        VideoBackgroundStop()
    End Sub

    Private Sub VideoBackgroundStop()
        NowAddress = Nothing
        If Player IsNot Nothing Then
            Try
                Player.Pause()
                Player.[Stop]()
                Player.Close()
            Catch ex As Exception
                Logger.Warn(ex, "释放动态背景播放器失败")
            End Try
            Player = Nothing
        End If
        VideoDraw = Nothing
        VideoBrush = Nothing
    End Sub

    ''' <summary>
    ''' 按设置更新播放速度、音量、显示方式。必须在 UI 线程调用。
    ''' </summary>
    Public Sub VideoBackgroundRefreshEffects()
        If Player Is Nothing OrElse VideoDraw Is Nothing Then Return
        Try
            Player.SpeedRatio = Math.Max(0.1, Settings.Get(Of Integer)("UiBackgroundVideoSpeed") / 100.0)
            Player.Volume = Math.Max(0, Math.Min(1, Settings.Get(Of Integer)("UiBackgroundVideoVolume") / 1000.0))
            VideoBrush = BuildBrush()
            If FrmMain IsNot Nothing AndAlso FrmMain.IsLoaded Then
                FrmMain.ImgBack.Background = VideoBrush
                FrmMain.ImgBack.Visibility = Visibility.Visible
            End If
        Catch ex As Exception
            Logger.Error(ex, "更新动态背景效果失败")
        End Try
    End Sub

    Private Function BuildBrush() As DrawingBrush
        Dim Suit As Stretch = Stretch.UniformToFill
        Select Case Settings.Get(Of Integer)("UiBackgroundVideoSuit")
            Case 1
                Suit = Stretch.Fill
            Case 2
                Suit = Stretch.None
        End Select
        Return New DrawingBrush(VideoDraw) With {.Stretch = Suit}
    End Function

    Private Sub Player_MediaOpened(sender As Object, e As RoutedEventArgs)
        Try
            If Player Is Nothing OrElse VideoDraw Is Nothing Then Return
            Dim Width As Integer = Player.NaturalVideoWidth, Height As Integer = Player.NaturalVideoHeight
            If Width < 1 OrElse Height < 1 Then Return
            VideoDraw.Rect = New Rect(0, 0, Width, Height)
            VideoBrush = BuildBrush()
            If FrmMain IsNot Nothing AndAlso FrmMain.IsLoaded Then
                FrmMain.ImgBack.Background = VideoBrush
                FrmMain.UpdateBackgroundAndTitleBar()
            End If
            VideoBackgroundRefreshEffects()
            Logger.Info($"动态背景已就绪：{Width} x {Height}")
        Catch ex As Exception
            Logger.Error(ex, "显示动态背景失败", LogBehavior.Alert)
        End Try
    End Sub

    Private Sub Player_MediaEnded(sender As Object, e As RoutedEventArgs)
        If Player Is Nothing OrElse Not Settings.Get(Of Boolean)("UiBackgroundVideoLoop") Then Return
        Try
            Player.Position = TimeSpan.Zero
            Player.Play()
        Catch ex As Exception
            Logger.Warn(ex, "循环播放动态背景失败")
        End Try
    End Sub

    Private Sub Player_MediaFailed(sender As Object, e As ExceptionEventArgs)
        Logger.Error(e.ErrorException, $"动态背景视频无法播放：{NowAddress}", LogBehavior.Alert)
        RunInUi(
        Sub()
            Dim WasVideo = VideoBrush IsNot Nothing AndAlso FrmMain IsNot Nothing AndAlso FrmMain.IsLoaded AndAlso ReferenceEquals(FrmMain.ImgBack.Background, VideoBrush)
            VideoBackgroundStop()
            If WasVideo Then
                FrmMain.ImgBack.Background = Nothing
                PageSetupUI.BackgroundRefresh(False, True)
            End If
            RefreshVideoUI()
        End Sub)
    End Sub

    Private Sub RefreshVideoUI()
        If FrmSetupUI IsNot Nothing Then FrmSetupUI.VideoRefreshUI()
    End Sub

End Module
