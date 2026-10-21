'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "OpenSource"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLDebug"
    '用于微软登录的 ClientId
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "")
    'CurseForge API Key：优先使用 设置 → 其他 → 社区资源 中填写的值，其次才回退到环境变量
    '（环境变量只在进程启动时被继承一次，从启动器、旧资源管理器等处再次启动 PCL 时可能读不到）
    Public ReadOnly Property CurseForgeAPIKey As String
        Get
            Try
                Dim Local = Settings.Get(Of String)("ToolCurseForgeAPIKey")
                If Local IsNot Nothing AndAlso Local.Trim() <> "" Then Return Local.Trim()
            Catch ex As Exception
                Logger.Warn(ex, "读取 CurseForge API Key 设置失败")
            End Try
            Return If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "").Trim()
        End Get
    End Property
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

    '本分支已恢复 MC 下载功能，不再屏蔽
    Public Const RestrictMcDownload As Boolean = False
    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If (Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net")) AndAlso CurseForgeAPIKey <> "" Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme
            ThemeApplyHsl()

            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    ''' <summary>
    ''' 按当前主题 id 写入 HSL 参数，供 ThemeRefresh 与 ThemeRefreshMain 使用。
    ''' </summary>
    Private Sub ThemeApplyHsl()
        Select Case ThemeNow
            Case 1 '甜柠青
                ColorHue = 168 : ColorSat = 78 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            Case 2 '小草绿
                ColorHue = 122 : ColorSat = 56 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            Case 3 '菠萝黄
                ColorHue = 46 : ColorSat = 92 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            Case 4 '橡木棕
                ColorHue = 26 : ColorSat = 52 : ColorLightAdjust = -3 : ColorHueTopbarDelta = 0
            Case 5 '玄素黑
                ColorHue = 210 : ColorSat = 0 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            Case 6 '铁杆粉
                ColorHue = 330 : ColorSat = 76 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            Case 7 '神秘紫
                ColorHue = 272 : ColorSat = 72 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            Case 8 '秋仪金
                ColorHue = 42 : ColorSat = 72 : ColorLightAdjust = -2 : ColorHueTopbarDelta = 0
            Case 9 '活跃橙
                ColorHue = 22 : ColorSat = 94 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            Case 10 '跳票红
                ColorHue = 356 : ColorSat = 88 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
            Case 11 '极客蓝
                ColorHue = 226 : ColorSat = 92 : ColorLightAdjust = -6 : ColorHueTopbarDelta = 0
            Case 12 '滑稽彩，由主时钟不断调用以流转颜色
                ColorHue = (ColorHue + 4) Mod 360 : ColorSat = 90 : ColorLightAdjust = 0
                ColorHueTopbarDelta = New Integer() {-70, 0, 70}
            Case 13 '欧皇彩
                ColorHue = 45 : ColorSat = 96 : ColorLightAdjust = 3
                ColorHueTopbarDelta = New Integer() {-45, 0, 45}
            Case 14 '自定义
                ColorHue = Settings.Get(Of Integer)("UiLauncherHue")
                ColorSat = Settings.Get(Of Integer)("UiLauncherSat")
                ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight") - 20
                ColorHueTopbarDelta = Settings.Get(Of Integer)("UiLauncherDelta") - 90
            Case Else '龙猫蓝
                ColorHue = 210 : ColorSat = 85 : ColorLightAdjust = 0 : ColorHueTopbarDelta = 0
        End Select
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        Dim Theme As Integer = Settings.Get(Of Integer)("UiLauncherTheme")
        If Theme < 0 OrElse Theme > 14 Then Settings.Set("UiLauncherTheme", 0)
        '未解锁时不允许停留在自定义主题上（可能从别的机器导入了设置）
        If Theme = 14 AndAlso Not ThemeCustomUnlocked() Then Settings.Set("UiLauncherTheme", 0)
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return True
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        '除自定义主题色外，其余主题在上游开源分支中本就无需解锁
        Return False
    End Function

    ''' <summary>
    ''' 自定义主题色（主题 14）是否需要赞助解锁。分发版必须保持 True，自用编译改为 False 即可直接使用。
    ''' </summary>
    Public Const ThemeCustomRequireSponsor As Boolean = True
    ''' <summary>
    ''' 自定义主题色是否已解锁。
    ''' </summary>
    Public Function ThemeCustomUnlocked() As Boolean
        Return Not ThemeCustomRequireSponsor OrElse Settings.Get(Of Boolean)("UiLauncherThemeCustomUnlock")
    End Function
    ''' <summary>
    ''' 弹出赞助说明并请求解锁。返回 1 已解锁，2 打开了赞助页面，3 取消。
    ''' </summary>
    Public Function ThemeCustomUnlockRequest() As Integer
        Dim UnlockResult As Integer = MyMsgBox(
            "自定义主题色是需要赞助解锁的纪念性功能，与上游 PCL 的赞助主题保持一致。" & vbCrLf &
            "赞助链接指向 龙腾猫跃（https://meloong.com/afd/a/LTCat），本修改版不收取任何费用。" & vbCrLf & vbCrLf &
            "赞助之后回到这里点击「我已赞助，解锁」即可使用。",
            "解锁自定义主题色", "我已赞助，解锁", "打开赞助页面", "取消")
        Select Case UnlockResult
            Case 1
                Settings.Set("UiLauncherThemeCustomUnlock", True)
                Logger.Info("已解锁自定义主题色")
                Hint("已解锁自定义主题色，感谢你对 PCL 的支持！", HintType.Green, False)
            Case 2
                OpenWebsite("https://meloong.com/afd/a/LTCat")
        End Select
        Return UnlockResult
    End Function

#End Region

#Region "字体"

    ''' <summary>
    ''' XAML 里声明的默认字体，启动时抓取一次，供恢复默认使用。
    ''' 必须由 XAML 解析出来的对象承担，自己 new 会丢掉内嵌字体的相对地址。
    ''' </summary>
    Public FontFamilyDefault As FontFamily = Nothing
    ''' <summary>当前生效的字体，供需要预览的地方直接复用。</summary>
    Public FontFamilyNow As FontFamily = Nothing
    ''' <summary>代码里拼回退链时只能用绝对地址，默认的相对写法脱离 XAML 上下文会解析不到内嵌字体。</summary>
    Private Const FontFamilyFallback As String = "pack://application:,,,/Resources/#PCL English, Microsoft YaHei UI"

    Public Sub FontFamilyDefaultCapture()
        If FontFamilyDefault IsNot Nothing Then Return
        FontFamilyDefault = TryCast(Application.Current.Resources("FontFamilyGlobal"), FontFamily)
        If FontFamilyNow Is Nothing Then FontFamilyNow = FontFamilyDefault
    End Sub

    ''' <summary>
    ''' 按 UiFontFamily 设置替换全局字体。自定义字体缺字时会自动回退到默认字体。
    ''' </summary>
    Public Sub FontRefresh()
        Try
            FontFamilyDefaultCapture()
            If FontFamilyDefault Is Nothing Then Return
            Dim Name = Settings.Get(Of String)("UiFontFamily")
            Dim Target = If(String.IsNullOrEmpty(Name), FontFamilyDefault, New FontFamily(Name & ", " & FontFamilyFallback))
            Application.Current.Resources("FontFamilyGlobal") = Target
        Catch ex As Exception
            Logger.Error(ex, "刷新字体失败", LogBehavior.Toast)
        End Try
    End Sub

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        Hint("该版本中不包含更新功能……")
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
