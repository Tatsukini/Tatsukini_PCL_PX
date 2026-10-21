Public Class PageLoginAuthSkin

    Public Sub New()
        InitializeComponent()
        Skin.Loader = PageLaunchLeft.SkinAuth
    End Sub
    Private Sub PageLoginLegacy_Loaded() Handles Me.Loaded
        Skin.Loader.Start()
    End Sub

    ''' <summary>
    ''' 刷新页面显示的所有信息。
    ''' </summary>
    Public Sub Reload(KeepInput As Boolean)
        Dim Token As String = PageLoginAuth.CurrentToken()
        TextName.Text = Settings.Get(Of String)("Cache" & Token & "Name")
        TextEmail.Text = Settings.Get(Of String)("Cache" & Token & "Username")
        TextEmail.Visibility = If(Settings.Get(Of Boolean)("UiLauncherEmail"), Visibility.Collapsed, Visibility.Visible)
        Skin.Loader = If(Token = "LittleSkin", PageLaunchLeft.SkinLittleSkin, PageLaunchLeft.SkinAuth)
        PageLoginLegacy_Loaded()
    End Sub
    ''' <summary>
    ''' 获取当前页面的登录信息。
    ''' </summary>
    Public Shared Function GetLoginData() As McLoginServer
        Dim Token As String = PageLoginAuth.CurrentToken()
        Dim IsLittleSkin As Boolean = Token = "LittleSkin"
        Dim Type As McLoginType = If(IsLittleSkin, McLoginType.LittleSkin, McLoginType.Auth)
        Dim Server As String = If(IsLittleSkin, LittleSkinAuthUrl(),
            If(IsNothing(McInstanceSelected), Settings.Get(Of String)("CacheAuthServerServer"), Settings.Get(Of String)("VersionServerAuthServer", Instance:=McInstanceSelected)) & "/authserver")
        Return New McLoginServer(Type) With {.Token = Token, .BaseUrl = Server, .UserName = Settings.Get(Of String)("Cache" & Token & "Username"), .Password = Settings.Get(Of String)("Cache" & Token & "Pass"), .Description = If(IsLittleSkin, "LittleSkin", "Authlib-Injector"), .Type = Type}
    End Function

    Private Sub PageLoginAuthSkin_MouseEnter(sender As Object, e As MouseEventArgs) Handles PanData.MouseEnter
        AniStart({
                 AaOpacity(BtnEdit, 1 - BtnEdit.Opacity, 80),
                 AaHeight(BtnEdit, 25.5 - BtnEdit.Height, 140,, New AniEaseOutFluent),
                 AaHeight(BtnEdit, -1.5, 50, 140, New AniEaseInFluent),
                 AaOpacity(BtnExit, 1 - BtnExit.Opacity, 80),
                 AaHeight(BtnExit, 25.5 - BtnExit.Height, 140,, New AniEaseOutFluent),
                 AaHeight(BtnExit, -1.5, 50, 140, New AniEaseInFluent)
        }, "PageLoginAuthSkin Button")
    End Sub
    Private Sub PageLoginAuthSkin_MouseLeave(sender As Object, e As MouseEventArgs) Handles PanData.MouseLeave
        AniStart({
                 AaOpacity(BtnEdit, -BtnEdit.Opacity, 120,, New AniEaseOutFluent),
                 AaHeight(BtnEdit, 14 - BtnEdit.Height, 120,, New AniEaseInFluent),
                 AaOpacity(BtnExit, -BtnExit.Opacity, 120,, New AniEaseOutFluent),
                 AaHeight(BtnExit, 14 - BtnExit.Height, 120,, New AniEaseInFluent)
        }, "PageLoginAuthSkin Button")
    End Sub

    Private Sub BtnEdit_Click(sender As Object, e As EventArgs) Handles BtnEdit.Click
        If McLoginLoader.State = LoadState.Loading Then
            Logger.Warn("要求更换角色，但登录加载器繁忙")
            If TypeOf McLoginLoader.Input Is McLoginServer AndAlso CType(McLoginLoader.Input, McLoginServer).ForceReselectProfile Then
                Hint("正在尝试更换，请稍候！")
            Else
                Hint("正在登录中，请稍后再更换角色！", HintType.Red)
            End If
            Return
        End If
        Hint("正在尝试更换，请稍候！")
        Dim Token As String = PageLoginAuth.CurrentToken()
        Settings.Set("Cache" & Token & "Uuid", "") '清空选择缓存
        Settings.Set("Cache" & Token & "Name", "")
        RunInThread(
        Sub()
            Try
                Dim Data As McLoginServer = GetLoginData()
                Data.ForceReselectProfile = True
                McLoginLoader.WaitForExit(Data, IsForceRestart:=True)
                RunInUi(Sub() Reload(True))
            Catch ex As Exception
                Logger.Error(ex, "更换角色失败", LogBehavior.Toast)
            End Try
        End Sub)
    End Sub
    Public Shared Sub ExitLogin() Handles BtnExit.Click
        Dim Token As String = PageLoginAuth.CurrentToken()
        Settings.Set("Cache" & Token & "Access", "")
        Settings.Set("Cache" & Token & "Uuid", "")
        Settings.Set("Cache" & Token & "Name", "")
        If Token = "LittleSkin" Then
            McLoginLittleSkinLoader.Input = Nothing '防止因为输入的用户名密码相同，直接使用了上次登录的加载器结果
        Else
            McLoginAuthLoader.Input = Nothing
        End If
        FrmLaunchLeft.RefreshPage(False, True)
    End Sub

    Private Sub Skin_Click(sender As Object, e As MouseButtonEventArgs) Handles Skin.Click
        Dim Address As String = PageLoginAuth.RegisterUrl()
        If Not String.IsNullOrEmpty(New ValidateHttp().Validate(Address)) Then Return
        OpenWebsite(Address.Replace("/auth/register", "/user/closet").Replace("/user/register", "/user/closet"))
    End Sub

End Class
