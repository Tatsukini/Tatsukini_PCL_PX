Public Class PageDownloadSkin

    Private Const LittleSkinApi As String = "https://littleskin.cn"
    Private Const SessionServerApi As String = "https://sessionserver.mojang.com/session/minecraft/profile/"

    Private IsSearching As Boolean = False
    ''' <summary>用于丢弃过期的查询结果。</summary>
    Private SearchTick As Integer = 0

    Private ResultName As String = ""
    Private SkinUrl As String = ""
    Private CapeUrl As String = ""

    Private Class SkinResult
        Public Name As String = ""
        Public Info As String = ""
        Public Skin As String = ""
        Public Cape As String = ""
    End Class

    ''' <summary>
    ''' 皮肤默认保存到的位置。
    ''' </summary>
    Public Shared Function DefaultFolder() As String
        Return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) & "\Minecraft 皮肤\"
    End Function

    Private Sub Page_Loaded() Handles Me.Loaded
        If TxtSkinFolder.Text = "" Then TxtSkinFolder.Text = DefaultFolder()
    End Sub

#Region "查询"

    Private Sub ComboSkinSource_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboSkinSource.SelectionChanged
        If TxtSkinPlayer Is Nothing Then Return
        If ItemType(ComboSkinSource) = "mojang" Then
            TxtSkinPlayer.HintText = "请输入正版玩家名或 UID"
        Else
            TxtSkinPlayer.HintText = "请输入 LittleSkin 玩家名"
        End If
    End Sub

    Private Sub BtnSkinSearch_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSkinSearch.Click
        StartSearch()
    End Sub

    ''' <summary>
    ''' 由边栏刷新按钮触发，重新查询当前输入。
    ''' </summary>
    Public Sub Reload()
        If IsSearching Then
            Hint("正在查询中，请稍候。", HintType.Blue, Log:=False)
        ElseIf TxtSkinPlayer.Text.Trim = "" Then
            Hint("请先输入玩家名或 UID，再刷新。", HintType.Red, Log:=False)
        Else
            StartSearch()
        End If
    End Sub

    Private Sub StartSearch()
        If IsSearching Then Return
        Dim Input = TxtSkinPlayer.Text.Trim
        If Input = "" Then
            Hint("请输入玩家名或 UID！", HintType.Red)
            Return
        End If
        IsSearching = True
        SearchTick += 1
        Dim Tick As Integer = SearchTick
        Dim IsMojang As Boolean = ItemType(ComboSkinSource) = "mojang"
        SkinUrl = ""
        CapeUrl = ""
        BtnSkinDownload.IsEnabled = False
        BtnCapeDownload.IsEnabled = False
        ImgSkin.Source = Nothing
        ImgCape.Source = Nothing
        LabSkinTip.Text = "正在查询……"
        RunInThread(
        Sub()
            Dim Result As New SkinResult With {.Name = Input}
            Try
                Result = If(IsMojang, SearchMojang(Input), SearchLittleSkin(Input))
            Catch ex As Exception
                Logger.Warn(ex, "查询皮肤失败")
                Result.Info = "查询失败：" & ex.Message
            End Try
            FinishSearch(Tick, Result)
        End Sub)
    End Sub

    ''' <summary>
    ''' 从 LittleSkin 的 CustomSkinAPI 查询皮肤与披风。
    ''' </summary>
    Private Function SearchLittleSkin(Name As String) As SkinResult
        Dim Result As New SkinResult With {.Name = Name}
        Dim Raw As String = NetRequestByClientRetry(LittleSkinApi & "/csl/" & StringUtils.UrlEscape(Name) & ".json", RequireJson:=True)
        Dim Json As JObject = Raw.DeserializeJson()
        Dim Display As String = GetTokenString(Json, "username")
        If Display <> "" Then Result.Name = Display
        '皮肤：优先 default，其次 slim，再退而取任意一项
        Dim SkinKey As String = ""
        Dim SkinValue As String = ""
        For Each Key In {"default", "slim"}
            Dim Value = GetTokenString(Json, "skins", Key)
            If Value <> "" Then
                SkinKey = Key
                SkinValue = Value
                Exit For
            End If
        Next
        If SkinValue = "" Then
            Dim Obj = TryCast(GetToken(Json, "skins"), JObject)
            If Obj IsNot Nothing AndAlso Obj.Properties.Any Then
                SkinKey = Obj.Properties.First.Name
                SkinValue = Obj.Properties.First.Value.ToString
            End If
        End If
        If SkinValue = "" Then
            Result.Info = $"在 LittleSkin 没有找到 {Result.Name} 的皮肤。" & vbCrLf &
                "该玩家可能从未在 LittleSkin 上传过皮肤，或者玩家名拼写有误。" & vbCrLf &
                $"（接口返回：{ShortText(Raw)}）"
            Return Result
        End If
        Result.Skin = LittleSkinTextureGet(SkinValue)
        Dim Lines As New List(Of String)
        Lines.Add("来源：LittleSkin")
        Lines.Add("玩家名：" & Result.Name)
        Lines.Add("模型：" & If(SkinKey = "slim", "细瘦（Alex）", "经典（Steve）"))
        '披风
        Dim CapeValue As String = ""
        If Json?("cape") IsNot Nothing Then CapeValue = LittleSkinValueGet(Json("cape"))
        If CapeValue <> "" Then
            Result.Cape = LittleSkinTextureGet(CapeValue)
            Lines.Add("披风：有")
        Else
            Lines.Add("披风：没有披风")
        End If
        Result.Info = Lines.Join(vbCrLf)
        Return Result
    End Function

    ''' <summary>
    ''' LittleSkin 返回的披风项可能是哈希字符串，也可能是包含哈希的对象。
    ''' </summary>
    Private Function LittleSkinValueGet(Token As JToken) As String
        If Token Is Nothing OrElse Token.Type = JTokenType.Null Then Return ""
        If TypeOf Token Is JValue Then Return CType(Token, JValue).ToString
        For Each Key In {"texture", "hash", "url", "default", "value"}
            Dim Value = GetTokenString(Token, Key)
            If Value <> "" Then Return Value
        Next
        Return ""
    End Function

    ''' <summary>
    ''' 由皮肤哈希或绝对地址得到纹理下载地址。
    ''' </summary>
    Private Function LittleSkinTextureGet(HashOrUrl As String) As String
        If HashOrUrl.StartsWithF("http") Then Return HashOrUrl.Replace("http://", "https://")
        Return LittleSkinApi & "/csl/textures/" & HashOrUrl
    End Function

    ''' <summary>
    ''' 从正版档案查询皮肤与披风。Input 可以是玩家名，也可以是 UID。
    ''' </summary>
    Private Function SearchMojang(Input As String) As SkinResult
        Dim Result As New SkinResult With {.Name = Input}
        '判断输入是 UID 还是玩家名
        Dim Uuid As String = UuidNormalize(Input)
        If Uuid = "" Then
            Uuid = MojangUuidGet(Input)
            If Uuid = "" Then
                Result.Info = $"在正版档案中没有找到 {Input}。" & vbCrLf &
                    "正版来源只支持 Minecraft 官方记录过的玩家名或 UID。" & vbCrLf &
                    "若该玩家使用的是 LittleSkin 账号，请把图片来源改为 LittleSkin。"
                Return Result
            End If
        End If
        Result.Name = Input
        '该接口对不存在的档案返回 204 空响应，因此不能要求 JSON
        Dim Raw As String = NetRequestByClientRetry(SessionServerApi & Uuid)
        If Raw.Trim = "" Then
            Result.Info = $"没有找到 UID {UuidWithDash(Uuid)} 的正版档案。" & vbCrLf & "该 UID 可能不存在，或者从未进入过游戏。"
            Return Result
        End If
        Dim Json As JObject = Raw.DeserializeJson()
        Dim Display As String = GetTokenString(Json, "name")
        If Display <> "" Then Result.Name = Display
        '取出 base64 编码的 textures 数据
        Dim ValueB64 As String = ""
        Dim Properties = TryCast(GetToken(Json, "properties"), JArray)
        If Properties IsNot Nothing Then
            For Each Prop In Properties.OfType(Of JObject)
                If GetTokenString(Prop, "name").Lower = "textures" Then
                    ValueB64 = GetTokenString(Prop, "value")
                    Exit For
                End If
            Next
        End If
        If ValueB64 = "" Then
            Result.Info = $"{Result.Name} 的正版档案中没有皮肤数据。" & vbCrLf & "该玩家可能还没有登录过游戏，或者没有设置自定义皮肤。"
            Return Result
        End If
        Dim TexJson As JObject
        Try
            TexJson = CType(Encoding.UTF8.GetString(Convert.FromBase64String(ValueB64)).DeserializeJson(), JObject)
        Catch ex As Exception
            Logger.Warn(ex, "解析正版皮肤数据失败")
            Result.Info = "解析正版皮肤数据失败：" & ex.Message
            Return Result
        End Try
        '皮肤
        Dim SkinToken = GetToken(TexJson, "textures", "SKIN")
        If SkinToken Is Nothing Then
            Result.Info = $"{Result.Name} 没有设置自定义皮肤，因此没有可下载的皮肤。"
            Return Result
        End If
        Result.Skin = MojangUrlFix(GetTokenString(SkinToken, "url"))
        '披风
        Dim CapeToken = GetToken(TexJson, "textures", "CAPE")
        If CapeToken IsNot Nothing Then Result.Cape = MojangUrlFix(GetTokenString(CapeToken, "url"))
        Dim Lines As New List(Of String)
        Lines.Add("来源：正版（Mojang）")
        Lines.Add("玩家名：" & Result.Name)
        Lines.Add("UID：" & UuidWithDash(Uuid))
        Lines.Add("模型：" & If(GetTokenString(SkinToken, "metadata", "model").Lower = "slim", "细瘦（Alex）", "经典（Steve）"))
        Lines.Add(If(Result.Cape = "", "披风：没有披风", "披风：有"))
        If Result.Skin = "" Then Lines.Add("警告：档案中的皮肤地址为空，可能无法下载。")
        Result.Info = Lines.Join(vbCrLf)
        Return Result
    End Function

    ''' <summary>
    ''' 由玩家名取得不带连字符的 UID；查不到时返回空字符串。
    ''' </summary>
    Private Function MojangUuidGet(Name As String) As String
        Try
            Dim Json As JObject = NetRequestByClientRetry("https://api.mojang.com/users/profiles/minecraft/" & StringUtils.UrlEscape(Name), RequireJson:=True).DeserializeJson()
            Dim Id = GetTokenString(Json, "id")
            If Id.Length = 32 Then Return Id.Lower
        Catch ex As Exception
            Logger.Warn(ex, $"获取正版 UID 失败（{Name}）")
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' textures.minecraft.net 的地址是 http 的，改用 https 以免被拦截。
    ''' </summary>
    Private Function MojangUrlFix(Url As String) As String
        If Url = "" Then Return ""
        If Url.Contains("minecraft.net/") Then Return Url.Replace("http://", "https://")
        Return Url
    End Function

    ''' <summary>
    ''' 若输入是合法的 UID，返回去掉连字符的小写形式；否则返回空字符串。
    ''' </summary>
    Private Function UuidNormalize(Input As String) As String
        Dim Value = Input.Replace("-", "").Trim.Lower
        If Value.Length <> 32 Then Return ""
        For Each Key In Value
            If Not "0123456789abcdef".Contains(Key.ToString) Then Return ""
        Next
        Return Value
    End Function

    Private Function UuidWithDash(Uuid As String) As String
        If Uuid.Length <> 32 Then Return Uuid
        Return Uuid.Substring(0, 8) & "-" & Uuid.Substring(8, 4) & "-" & Uuid.Substring(12, 4) & "-" & Uuid.Substring(16, 4) & "-" & Uuid.Substring(20)
    End Function

    Private Sub FinishSearch(Tick As Integer, Result As SkinResult)
        RunInUi(
        Sub()
            IsSearching = False
            If Tick <> SearchTick Then Return
            SkinUrl = Result.Skin
            CapeUrl = Result.Cape
            ResultName = Result.Name
            LabSkinTip.Text = Result.Info
            ImgSkin.Source = If(SkinUrl = "", Nothing, SkinUrl)
            ImgCape.Source = If(CapeUrl = "", Nothing, CapeUrl)
            BtnSkinDownload.IsEnabled = SkinUrl <> ""
            BtnCapeDownload.IsEnabled = CapeUrl <> ""
            If SkinUrl = "" Then Hint(Result.Info, HintType.Red, Log:=False)
        End Sub)
    End Sub

#End Region

#Region "下载"

    Private Sub BtnSkinFolder_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSkinFolder.Click
        Dim Folder = Dialogs.SelectFolder("选择皮肤保存目录", False, DefaultFolder()).FirstOrDefault
        If Folder IsNot Nothing Then TxtSkinFolder.Text = PathUtils.AddSlashSuffix(Folder)
    End Sub

    Private Sub BtnSkinDownload_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSkinDownload.Click
        SaveTexture(SkinUrl, "皮肤")
    End Sub

    Private Sub BtnCapeDownload_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnCapeDownload.Click
        SaveTexture(CapeUrl, "披风")
    End Sub

    Private Sub SaveTexture(Url As String, Kind As String)
        Try
            If Url = "" Then
                Hint($"还没有查询到{Kind}，请先查询。", HintType.Red)
                Return
            End If
            Dim Folder As String = PathUtils.AddSlashSuffix(TxtSkinFolder.Text.Trim.Replace("/", "\"))
            If Folder.Length < 3 Then
                Hint("请选择一个有效的保存目录！", HintType.Red)
                Return
            End If
            DirectoryUtils.Create(Folder)
            Dim BaseName As String = SafeFileName(If(ResultName = "", "texture", ResultName))
            Dim Target As String = GetFreePath(Folder, BaseName & If(Kind = "披风", "_披风", "") & ".png")
            '重复任务检查
            Dim LoaderName As String = "下载" & Kind & "：" & BaseName
            If LoaderTaskbar.Any(Function(Old) Old.Name = LoaderName) Then
                Hint($"该{Kind}正在下载中！", HintType.Red)
                Return
            End If
            '构建任务
            Dim Loaders As New List(Of LoaderBase) From {
                New LoaderDownload("下载" & Kind,
                    New List(Of NetFile) From {New NetFile(New List(Of String) From {Url}, Target, New FileChecker With {.MinSize = 64})}) With {.ProgressWeight = 5}}
            Dim Loader As New LoaderCombo(Of String)(LoaderName, Loaders) With {.OnStateChanged = AddressOf SkinDownloadState}
            Loader.Start(Folder)
            LoaderTaskbarAdd(Loader)
            FrmMain.BtnExtraDownload.ShowRefresh()
            FrmMain.BtnExtraDownload.Ribble()
        Catch ex As Exception
            Logger.Error(ex, "开始下载皮肤失败")
            Hint("开始下载皮肤失败：" & ex.Message, HintType.Red)
        End Try
    End Sub

    Private Sub SkinDownloadState(Loader As LoaderBase)
        Select Case Loader.State
            Case LoadState.Finished
                Hint(Loader.Name & "成功！", HintType.Green)
            Case LoadState.Failed
                Hint(Loader.Name & "失败：" & Loader.Error.GetDisplay(False), HintType.Red)
                Logger.Error(Loader.Error, Loader.Name & "失败")
            Case LoadState.Canceled
                Hint(Loader.Name & "已取消！", HintType.Blue)
        End Select
    End Sub

    ''' <summary>
    ''' 若同名文件已经存在，则在文件名后追加编号。
    ''' </summary>
    Private Function GetFreePath(Folder As String, Name As String) As String
        Dim Target As String = Folder & Name
        If Not FileUtils.Exists(Target) Then Return Target
        Dim Base = PathUtils.GetFileNameWithoutExtension(Name)
        Dim Ext = PathUtils.GetExtension(Name)
        For Each Index In Enumerable.Range(2, 200)
            Target = Folder & Base & " (" & Index & ")." & Ext
            If Not FileUtils.Exists(Target) Then Return Target
        Next
        Throw New Exception("同名文件过多，无法确定保存位置：" & Folder)
    End Function

    Private Function SafeFileName(Name As String) As String
        Dim Result = New String(Name.Where(Function(c) Not "<>:""/|?*".Contains(c.ToString) AndAlso c <> "\"c AndAlso c <> vbLf AndAlso c <> vbCr).ToArray).Trim
        If Result = "" Then Result = "texture"
        If Result.Length > 60 Then Result = Result.Substring(0, 60)
        Return Result
    End Function

#End Region

#Region "通用工具"

    ''' <summary>
    ''' 获取某个下拉框选中项的 Tag。
    ''' </summary>
    Private Function ItemType(Combo As MyComboBox) As String
        Dim Item = TryCast(Combo.SelectedItem, MyComboBoxItem)
        If Item Is Nothing OrElse Item.Tag Is Nothing Then Return ""
        Return Item.Tag.ToString
    End Function

    ''' <summary>
    ''' 忽略大小写地取出 Json 项，缺失时返回 Nothing。
    ''' </summary>
    Private Function GetToken(Token As JToken, ParamArray Names() As String) As JToken
        Try
            If Token Is Nothing Then Return Nothing
            For Each Key In Names
                Dim NextToken As JToken = Nothing
                Dim Obj = TryCast(Token, JObject)
                If Obj IsNot Nothing Then
                    For Each Prop In Obj.Properties
                        If String.Equals(Prop.Name, Key, StringComparison.OrdinalIgnoreCase) Then
                            NextToken = Prop.Value
                            Exit For
                        End If
                    Next
                Else
                    NextToken = Token(Key)
                End If
                If NextToken Is Nothing OrElse NextToken.Type = JTokenType.Null Then Return Nothing
                Token = NextToken
            Next
            Return Token
        Catch ex As Exception
            Logger.Warn(ex, "解析 Json 项失败")
            Return Nothing
        End Try
    End Function

    Private Function GetTokenString(Token As JToken, ParamArray Names() As String) As String
        Dim Result = GetToken(Token, Names)
        If Result Is Nothing Then Return ""
        If TypeOf Result Is JValue Then Return CType(Result, JValue).ToString
        Return Result.ToString
    End Function

    ''' <summary>
    ''' 截断过长的文本，避免提示框被塞满。
    ''' </summary>
    Private Function ShortText(Text As String) As String
        If Text Is Nothing Then Return ""
        Text = Text.ReplaceLineEndings(" ").Trim
        If Text.Length > 200 Then Return Text.Substring(0, 200) & "……"
        Return Text
    End Function

#End Region

End Class
