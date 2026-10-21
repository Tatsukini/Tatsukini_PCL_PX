''' <summary>
''' OAuth 2.0 设备码流程（RFC 8628）的通用客户端，用于给第三方登录站（LittleSkin 等）做前置准备。
''' 这类站点是否真的开放设备码流程，光读启动器代码判断不了：端点先走 well-known 发现，
''' 发现失败时列出常见路径供玩家实测，并把每个候选的状态码原样展示，不猜结论。
''' 令牌只在必要时保留在加密设置里，日志与界面一律脱敏。
''' </summary>
Public Module ModLoginDevice

    Public Const DeviceGrantType As String = "urn:ietf:params:oauth:grant-type:device_code"

    Public Enum DeviceTokenStatus
        Ok          ' 已拿到令牌
        Pending     ' 玩家还没在浏览器里确认
        SlowDown    ' 轮询过快，需要延长间隔
        Expired     ' 设备码已过期
        Denied      ' 玩家拒绝授权
        Failed      ' 其它错误
    End Enum

    Public Class DeviceEndpoints
        Public Server As String = ""
        Public AuthorizationEndpoint As String = ""
        Public TokenEndpoint As String = ""
        Public DeviceAuthorizationEndpoint As String = ""
        Public DiscoveryUrl As String = ""
        Public DiscoveryOk As Boolean = False
        Public GrantTypes As New List(Of String)
        Public Note As String = ""
        Public Function SupportsDeviceFlow() As Boolean
            If DeviceAuthorizationEndpoint <> "" Then Return True
            Return GrantTypes.Any(Function(g) String.Equals(g, DeviceGrantType, StringComparison.OrdinalIgnoreCase))
        End Function
    End Class

    Public Class DeviceCodeInfo
        Public Endpoints As DeviceEndpoints
        Public ClientId As String = ""
        Public DeviceCode As String = "" ' 等价于一次性凭据，不写日志、不进界面
        Public UserCode As String = ""
        Public VerificationUri As String = ""
        Public VerificationUriComplete As String = ""
        Public ExpiresIn As Integer = 600
        Public Interval As Integer = 5
        Public Sub New()
        End Sub
        Public Sub New(Endpoints As DeviceEndpoints, ClientId As String)
            Me.Endpoints = Endpoints
            Me.ClientId = ClientId
        End Sub
        ''' <summary>verification_uri_complete 自带用户码，能少一步输入；没有就退回普通地址。</summary>
        Public Function OpenUrl() As String
            If VerificationUriComplete <> "" Then Return VerificationUriComplete
            Return VerificationUri
        End Function
    End Class

    Public Class DeviceTokenResult
        Public Status As DeviceTokenStatus = DeviceTokenStatus.Failed
        Public Json As JObject = Nothing
        Public Message As String = ""
    End Class

    Public Class DeviceProbeItem
        Public Url As String = ""
        Public Status As Integer = 0      ' 0 表示网络层就失败了
        Public ErrorMsg As String = ""
        ''' <summary>404 之外的响应都说明这个路由真实存在（405、400、401 都是常见答案）。</summary>
        Public Function Exists() As Boolean
            Return Status <> 0 AndAlso Status <> 404
        End Function
    End Class

    ' 发现失败时供玩家逐个实测的常见路径，只列 RFC 8628 与各家实现的惯用写法
    Private ReadOnly DeviceCandidatePaths As String() = {
        "/.well-known/oauth-authorization-server", "/.well-known/openid-configuration",
        "/oauth2/device_authorization", "/oauth/device/code", "/device_authorization", "/api/oauth2/device_authorization",
        "/oauth2/token", "/oauth/token", "/api/oauth2/token", "/api/token"}

    ''' <summary>补协议、去尾斜杠。玩家一般只填域名。</summary>
    Public Function NormalizeDeviceServer(Server As String) As String
        Dim Result = If(Server, "").Trim()
        If Result = "" Then Return ""
        If Not Result.Contains("://") Then Result = "https://" & Result
        Return Result.TrimEnd("/"c)
    End Function

    ''' <summary>读取元数据文档。失败返回 Nothing，由调用方继续尝试下一个候选。</summary>
    Public Function DiscoverDeviceEndpoints(Server As String, Optional MakeLog As Boolean = True) As DeviceEndpoints
        Dim Result As New DeviceEndpoints With {.Server = NormalizeDeviceServer(Server)}
        If Result.Server = "" Then
            Result.Note = "需要先填写服务器地址。"
            Return Result
        End If
        Dim Tried As New List(Of String)
        For Each CandidatePath In {"/.well-known/oauth-authorization-server", "/.well-known/openid-configuration"}
            Dim Url = Result.Server & CandidatePath
            Tried.Add(Url)
            Dim Json = GetJsonQuiet(Url, MakeLog)
            If Json Is Nothing Then Continue For
            Result.DiscoveryOk = True
            Result.DiscoveryUrl = Url
            Result.AuthorizationEndpoint = JsonString(Json, "authorization_endpoint")
            Result.TokenEndpoint = JsonString(Json, "token_endpoint")
            Result.DeviceAuthorizationEndpoint =
                If(JsonString(Json, "device_authorization_endpoint") <> "",
                JsonString(Json, "device_authorization_endpoint"), JsonString(Json, "device_authorization_url"))
            If Json("grant_types_supported") IsNot Nothing Then
                For Each Token In Json("grant_types_supported")
                    Result.GrantTypes.Add(Token.ToString)
                Next
            End If
            If Result.DeviceAuthorizationEndpoint = "" AndAlso Result.TokenEndpoint <> "" Then
                ' 少数实现把设备码端点挂在 token 同一目录下，按惯用写法补一个，让玩家可以立即试
                Result.DeviceAuthorizationEndpoint = Result.TokenEndpoint.TrimEnd("/"c) & "/device"
                Result.Note = "元数据里没有 device_authorization_endpoint，已按 token 端点补了一个猜测地址：" & Result.DeviceAuthorizationEndpoint
            End If
            If Not Result.SupportsDeviceFlow() Then
                Result.Note = "元数据已读到，但 grant_types_supported 中没有设备码类型：" & If(String.Join("、", Result.GrantTypes), "空")
            End If
            Return Result
        Next
        Result.Note = "未发现 OAuth 元数据（" & String.Join("、", Tried) & " 都没有返回 JSON）。" &
            "这可能说明站点不提供 OAuth，也可能只是路径不一样，可用下面的候选路径逐个实测。"
        Return Result
    End Function

    ''' <summary>只发请求看状态码，不下载内容，用来判断某个端点是否真实存在。</summary>
    Public Function ProbeDeviceEndpoints(Server As String, DeviceEndpoint As String, TokenEndpoint As String) As List(Of DeviceProbeItem)
        Dim Result As New List(Of DeviceProbeItem)
        Dim Base = NormalizeDeviceServer(Server)
        If Base = "" Then Return Result
        For Each CandidatePath In DeviceCandidatePaths
            Result.Add(ProbeUrl(Base & CandidatePath))
        Next
        For Each Url In {DeviceEndpoint, TokenEndpoint}
            If Url <> "" AndAlso Not Result.Any(Function(r) String.Equals(r.Url, Url, StringComparison.OrdinalIgnoreCase)) Then
                Result.Add(ProbeUrl(Url))
            End If
        Next
        Return Result
    End Function

    Private Function ProbeUrl(Url As String) As DeviceProbeItem
        Dim Item As New DeviceProbeItem With {.Url = Url}
        Try
            NetRequestByClient(Url, HttpMethod.Get, Timeout:=12000, RequireJson:=False, MakeLog:=False)
            Item.Status = 200
        Catch ex As HttpRequestCodeException
            Item.Status = CInt(ex.StatusCode)
        Catch ex As Exception
            Item.Status = 0
            Item.ErrorMsg = ex.GetDisplay(False)
        End Try
        Return Item
    End Function

    ''' <summary>请求设备码。失败抛出带中文说明的异常，交给界面显示。</summary>
    Public Function RequestDeviceCode(Endpoints As DeviceEndpoints, ClientId As String, Scope As String, Optional MakeLog As Boolean = True) As DeviceCodeInfo
        Dim Endpoint = Endpoints.DeviceAuthorizationEndpoint
        If Endpoint = "" Then Throw New Exception("$该站点没有设备码端点，请先探测端点或手动填写。")
        If ClientId = "" Then Throw New Exception("$需要填写 client_id（OAuth 应用 ID）。")
        Dim Form = "client_id=" & StringUtils.FormUrlEscape(ClientId)
        If Scope <> "" Then Form &= "&scope=" & StringUtils.FormUrlEscape(Scope)
        Dim Json = PostFormJson(Endpoint, Form, MakeLog, "设备码请求")
        Dim Info = New DeviceCodeInfo(Endpoints, ClientId) With {
            .DeviceCode = JsonString(Json, "device_code"),
            .UserCode = JsonString(Json, "user_code"),
            .VerificationUri = JsonString(Json, "verification_uri"),
            .VerificationUriComplete = JsonString(Json, "verification_uri_complete"),
            .ExpiresIn = JsonInt(Json, "expires_in", 600),
            .Interval = Math.Max(1, JsonInt(Json, "interval", 5))}
        If Info.DeviceCode = "" OrElse Info.UserCode = "" Then
            Throw New Exception("$返回结果缺少 device_code 或 user_code。服务器说：" & DescribeError(Json))
        End If
        If Info.VerificationUri = "" AndAlso Info.VerificationUriComplete = "" Then
            Throw New Exception("$返回结果里没有验证页地址，无法继续。")
        End If
        ' 间隔过短会被站点直接拒掉，RFC 建议至少 5 秒
        If Info.Interval < 5 Then Info.Interval = 5
        Return Info
    End Function

    ''' <summary>轮询一次令牌。设备码流程的中间状态都用 400 返回，因此要读响应体而不是只看状态码。</summary>
    Public Function PollDeviceToken(Info As DeviceCodeInfo) As DeviceTokenResult
        Dim Result As New DeviceTokenResult
        If Info.Endpoints Is Nothing OrElse Info.Endpoints.TokenEndpoint = "" Then
            Result.Message = "缺少 token 端点地址。"
            Return Result
        End If
        Dim Form = "client_id=" & StringUtils.FormUrlEscape(Info.ClientId) &
            "&grant_type=" & StringUtils.FormUrlEscape(DeviceGrantType) &
            "&device_code=" & StringUtils.FormUrlEscape(Info.DeviceCode)
        Try
            Dim Text = NetRequestByClient(Info.Endpoints.TokenEndpoint, HttpMethod.Post,
                Content:=Form, ContentType:="application/x-www-form-urlencoded", Timeout:=15000, MakeLog:=False)
            Dim Json = Text.DeserializeJson()
            If JsonString(Json, "access_token") = "" Then
                Result.Status = DeviceTokenStatus.Failed
                Result.Message = "响应里没有 access_token：" & DescribeError(Json)
                Return Result
            End If
            Result.Status = DeviceTokenStatus.Ok
            Result.Json = Json
            Return Result
        Catch ex As HttpRequestCodeException
            Return ParseDeviceTokenError(ex.Response, CInt(ex.StatusCode))
        Catch ex As FormatException
            Result.Message = "响应不是 JSON：" & Shorten(ex.Message, 300)
            Return Result
        Catch ex As Exception
            If ex.IsCanceled() Then Throw
            Result.Message = ex.GetDisplay(False)
            Return Result
        End Try
    End Function

    Public Function ParseDeviceTokenError(Response As String, StatusCode As Integer) As DeviceTokenResult
        Dim Result As New DeviceTokenResult
        Dim ErrorCode = ""
        If Not String.IsNullOrEmpty(Response) Then
            Try
                Dim Json = Response.DeserializeJson()
                ErrorCode = JsonString(Json, "error")
                Result.Message = If(ErrorCode <> "", ErrorCode, Shorten(Response, 300))
                If ErrorCode = "authorization_pending" Then
                    Result.Status = DeviceTokenStatus.Pending
                ElseIf ErrorCode = "slow_down" Then
                    Result.Status = DeviceTokenStatus.SlowDown
                ElseIf ErrorCode = "expired_token" OrElse ErrorCode = "expired_code" Then
                    Result.Status = DeviceTokenStatus.Expired
                ElseIf ErrorCode = "access_denied" Then
                    Result.Status = DeviceTokenStatus.Denied
                Else
                    Result.Status = DeviceTokenStatus.Failed
                End If
                Return Result
            Catch ex As Exception
                Result.Message = Shorten(Response, 300)
            End Try
        End If
        Result.Status = If(StatusCode = 429, DeviceTokenStatus.SlowDown, DeviceTokenStatus.Failed)
        Result.Message = "HTTP " & StatusCode & If(String.IsNullOrEmpty(Response), "（无响应体）", "：" & Result.Message)
        Return Result
    End Function

    ''' <summary>令牌脱敏。任何地方都不要显示完整令牌。</summary>
    Public Function MaskToken(Value As String) As String
        If String.IsNullOrEmpty(Value) Then Return ""
        If Value.Length <= 12 Then Return New String("∙"c, Value.Length)
        Return Value.Substring(0, 6) & "…" & Value.Substring(Value.Length - 4) & "（共 " & Value.Length & " 位）"
    End Function

    ''' <summary>把成功响应整理成人能看的几行，只输出脱敏后的令牌。</summary>
    Public Function DescribeDeviceToken(Json As JObject) As String
        If Json Is Nothing Then Return "没有可显示的令牌响应。"
        Dim Lines As New List(Of String)
        For Each TokenName In {"access_token", "refresh_token", "id_token"}
            Dim Value = JsonString(Json, TokenName)
            If Value <> "" Then Lines.Add("· " & TokenName & "：" & MaskToken(Value))
        Next
        Dim TokenType = JsonString(Json, "token_type")
        If TokenType <> "" Then Lines.Add("· token_type：" & TokenType)
        Dim Expires = JsonInt(Json, "expires_in", 0)
        If Expires > 0 Then Lines.Add("· expires_in：" & Expires & " 秒")
        Dim Scope = JsonString(Json, "scope")
        If Scope <> "" Then Lines.Add("· scope：" & Scope)
        If Not Lines.Any Then Lines.Add("· 响应里只有：" & String.Join("、", Json.Properties.Select(Function(p) p.Name)))
        Return String.Join(vbCrLf, Lines)
    End Function

    ''' <summary>保存会话，供后续把设备码接入真正的登录流程。只存加密设置。</summary>
    Public Sub SaveDeviceSession(Info As DeviceCodeInfo, Json As JObject)
        Dim Data = New JObject(
            New JProperty("server", If(Info.Endpoints Is Nothing, "", Info.Endpoints.Server)),
            New JProperty("clientId", Info.ClientId),
            New JProperty("tokenType", JsonString(Json, "token_type")),
            New JProperty("scope", JsonString(Json, "scope")),
            New JProperty("accessToken", JsonString(Json, "access_token")),
            New JProperty("refreshToken", JsonString(Json, "refresh_token")),
            New JProperty("idToken", JsonString(Json, "id_token")),
            New JProperty("savedAt", Date.Now.ToString("yyyy-MM-dd HH:mm:ss")))
        Settings.Set("LoginDeviceTokenJson", Data.ToString(Newtonsoft.Json.Formatting.None))
    End Sub

    ''' <summary>读取已保存的会话；没有或解析失败时返回 Nothing。</summary>
    Public Function ReadDeviceSession() As JObject
        Dim Text = Settings.Get(Of String)("LoginDeviceTokenJson")
        If String.IsNullOrEmpty(Text) Then Return Nothing
        Try
            Return Text.DeserializeJson()
        Catch ex As Exception
            Logger.Warn(ex, "读取设备码登录会话失败")
            Return Nothing
        End Try
    End Function

    Public Sub ClearDeviceSession()
        Settings.Set("LoginDeviceTokenJson", "")
    End Sub

    ' ---- 工具 ----

    Private Function GetJsonQuiet(Url As String, MakeLog As Boolean) As JObject
        Try
            Dim Text = NetRequestByClient(Url, HttpMethod.Get, Timeout:=12000, RequireJson:=False, MakeLog:=MakeLog)
            Return Text.DeserializeJson()
        Catch ex As Exception
            If MakeLog Then Logger.Info("读取 " & Url & " 失败：" & ex.GetDisplay(False))
            Return Nothing
        End Try
    End Function

    Private Function PostFormJson(Url As String, Form As String, MakeLog As Boolean, Title As String) As JObject
        Try
            Dim Text = NetRequestByClient(Url, HttpMethod.Post,
                Content:=Form, ContentType:="application/x-www-form-urlencoded", Timeout:=20000, MakeLog:=MakeLog)
            Return Text.DeserializeJson()
        Catch ex As HttpRequestCodeException
            Throw New Exception($"${Title}失败（HTTP {ex.StatusCode}）：" & DescribeErrorBody(ex.Response))
        Catch ex As FormatException
            Throw New Exception($"${Title}的响应不是 JSON：" & Shorten(ex.Message, 300))
        End Try
    End Function

    Private Function DescribeErrorBody(Response As String) As String
        If String.IsNullOrEmpty(Response) Then Return "服务器没有返回内容"
        Try
            Return DescribeError(Response.DeserializeJson())
        Catch ex As Exception
            Return Shorten(Response, 300)
        End Try
    End Function

    Private Function DescribeError(Json As JToken) As String
        If Json Is Nothing Then Return "服务器没有返回内容"
        Dim ErrorCode = JsonString(Json, "error")
        Dim Description = If(JsonString(Json, "error_description"), JsonString(Json, "errorMessage"))
        If ErrorCode = "" AndAlso Description = "" Then Return Shorten(Json.ToString, 300)
        Return String.Join("　", (New List(Of String) From {ErrorCode, Description}).Where(Function(s) s <> ""))
    End Function

    ''' <summary>响应体可能很长，展示时截断。</summary>
    Private Function Shorten(Text As String, MaxLength As Integer) As String
        If Text Is Nothing Then Return ""
        If Text.Length <= MaxLength Then Return Text
        Return Text.Substring(0, MaxLength) & "…（共 " & Text.Length & " 字符）"
    End Function

    Private Function JsonString(Root As JToken, FieldName As String) As String
        If Root Is Nothing Then Return ""
        Dim Token = Root(FieldName)
        If Token Is Nothing OrElse Token.Type = JTokenType.Null Then Return ""
        Return Token.ToString
    End Function

    Private Function JsonInt(Root As JToken, FieldName As String, DefaultValue As Integer) As Integer
        Dim Text = JsonString(Root, FieldName)
        Dim Result As Integer
        If Integer.TryParse(Text, Result) Then Return Result
        Return DefaultValue
    End Function

End Module
