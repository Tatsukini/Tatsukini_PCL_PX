Public Class PageLoginAuth
    Private IsFirstLoad As Boolean = True
    ''' <summary>
    ''' 该页面同时服务于 Authlib-Injector 与 LittleSkin 登录，返回当前所使用的标识符。
    ''' </summary>
    Public Shared Function CurrentToken() As String
        Return If(Settings.Get(Of McLoginType)("LoginType") = McLoginType.LittleSkin, "LittleSkin", "Auth")
    End Function
    ''' <summary>
    ''' 刷新页面显示的所有信息。
    ''' </summary>
    Public Sub Reload(KeepInput As Boolean)
        Dim Token As String = CurrentToken()
        Dim IsLittleSkin As Boolean = Token = "LittleSkin"
        LabEmail.Text = If(IsLittleSkin, "账号", "邮箱")
        '记住密码
        CheckRemember.Checked = Settings.Get(Of Boolean)("LoginRemember")
        Dim Emails As String = Settings.Get(Of String)("Login" & Token & "Email")
        If KeepInput AndAlso Not IsFirstLoad Then '避免第一次就以 KeepInput 的方式加载，导致文本框里没东西
            '保留输入，只刷新下拉框列表
            Dim Input As String = ComboName.Text
            ComboName.ItemsSource = If(Emails = "", Nothing, Emails.Split("¨"))
            ComboName.Text = Input
        Else
            '不保留输入，刷新列表后自动选择第一项
            If Emails = "" Then
                ComboName.ItemsSource = Nothing
            Else
                ComboName.ItemsSource = Emails.Split("¨")
                ComboName.Text = Emails.BeforeFirst("¨")
                If Settings.Get(Of Boolean)("LoginRemember") Then TextPass.Password = Settings.Get(Of String)("Login" & Token & "Pass").BeforeFirst("¨").Trim
            End If
        End If
        IsFirstLoad = False
        ReloadRegisterButton()
    End Sub
    ''' <summary>
    ''' 获取当前页面的登录信息。
    ''' </summary>
    Public Shared Function GetLoginData() As McLoginServer
        Dim Token As String = CurrentToken()
        Dim IsLittleSkin As Boolean = Token = "LittleSkin"
        Dim Type As McLoginType = If(IsLittleSkin, McLoginType.LittleSkin, McLoginType.Auth)
        Dim Server As String = If(IsLittleSkin, LittleSkinAuthUrl(),
            If(IsNothing(McInstanceSelected), Settings.Get(Of String)("CacheAuthServerServer"), Settings.Get(Of String)("VersionServerAuthServer", Instance:=McInstanceSelected)) & "/authserver")
        Dim Description As String = If(IsLittleSkin, "LittleSkin", "Authlib-Injector")
        If FrmLoginAuth Is Nothing Then
            Return New McLoginServer(Type) With {.Token = Token, .BaseUrl = Server, .UserName = "", .Password = "", .Description = Description, .Type = Type}
        Else
            Return New McLoginServer(Type) With {.Token = Token, .BaseUrl = Server, .UserName = FrmLoginAuth.ComboName.Text.Replace("¨", "").Trim, .Password = FrmLoginAuth.TextPass.Password.Replace("¨", "").Trim, .Description = Description, .Type = Type}
        End If
    End Function
    ''' <summary>
    ''' 当前页面的登录信息是否有效。
    ''' </summary>
    Public Shared Function IsVaild(LoginData As McLoginServer) As String
        If LoginData.UserName = "" Then Return "账号不能为空！"
        If LoginData.Password = "" Then Return "密码不能为空！"
        Return ""
    End Function
    Public Function IsVaild() As String
        Return IsVaild(GetLoginData())
    End Function

    '保存输入信息
    Private Sub ComboName_TextChanged(sender As Object, e As TextChangedEventArgs) Handles ComboName.TextChanged
        If sender.Text = "" Then TextPass.Password = ""
        If AniControlEnabled = 0 Then Settings.Set("Cache" & CurrentToken() & "Access", "")  '迫使其不进行 Validate
    End Sub
    Private Sub TextPass_PasswordChanged(sender As Object, e As RoutedEventArgs) Handles TextPass.PasswordChanged
        If AniControlEnabled = 0 Then Settings.Set("Cache" & CurrentToken() & "Access", "")
    End Sub
    Private Sub ComboName_SelectionChanged(sender As MyComboBox, e As SelectionChangedEventArgs) Handles ComboName.SelectionChanged
        If sender.SelectedIndex = -1 OrElse Not Settings.Get(Of Boolean)("LoginRemember") Then
            TextPass.Password = ""
        Else
            TextPass.Password = Settings.Get(Of String)("Login" & CurrentToken() & "Pass").ToString.Split("¨")(sender.SelectedIndex).Trim
        End If
    End Sub
    Private Sub CheckBoxChange(sender As MyCheckBox, e As Object) Handles CheckRemember.Change
        If AniControlEnabled = 0 Then Settings.Set(sender.Tag, sender.Checked)
    End Sub

    '链接处理
    Private Sub ComboName_TextChanged() Handles ComboName.TextChanged
        BtnLink.Content = If(ComboName.Text = "", "注册账号", "找回密码")
    End Sub
    ''' <summary>
    ''' 当前登录方式对应的注册页面地址。
    ''' </summary>
    Public Shared Function RegisterUrl() As String
        If CurrentToken() = "LittleSkin" Then Return LittleSkinRegisterUrl()
        Return If(McInstanceSelected IsNot Nothing, Settings.Get(Of String)("VersionServerAuthRegister", Instance:=McInstanceSelected), Settings.Get(Of String)("CacheAuthServerRegister"))
    End Function
    Private Sub Btn_Click(sender As Object, e As EventArgs) Handles BtnLink.Click
        Dim Website As String = RegisterUrl()
        If BtnLink.Content = "注册账号" Then
            OpenWebsite(Website)
        Else
            OpenWebsite(Website.Replace("/auth/register", "/auth/forgot").Replace("/user/register", "/user/forgot"))
        End If
    End Sub
    '切换注册按钮可见性
    Private Sub ReloadRegisterButton() Handles Me.Loaded
        BtnLink.Visibility = If(String.IsNullOrEmpty(New ValidateHttp().Validate(RegisterUrl())), Visibility.Visible, Visibility.Collapsed)
    End Sub

End Class
