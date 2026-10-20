Public Module ModModGraph

    ''' <summary>
    ''' 节点在依赖图中的状态，用于决定描边配色。
    ''' </summary>
    Public Enum ModNodeState
        正常
        警告
        危险
    End Enum

    ''' <summary>
    ''' 连线种类。
    ''' </summary>
    Public Enum ModGraphLinkKind
        Dependency
        Conflict
        MissingDependencyTarget
    End Enum

    ''' <summary>
    ''' 依赖图中的一个 Mod 节点。
    ''' </summary>
    Public Class ModGraphNode

        ''' <summary>
        ''' 文件名。由其它 Mod 声明但未安装的前置节点没有文件，此时为 Nothing。
        ''' </summary>
        Public Property FileName As String = Nothing
        Public Property FilePath As String = Nothing
        Public Property DisplayName As String = ""
        Public Property ModId As String = ""
        Public Property VersionText As String = ""
        ''' <summary>
        ''' 声明支持的游戏版本范围，例如 &gt;=1.20 与 [1.19,2.0)。
        ''' </summary>
        Public Property GameVersions As New List(Of String)
        ''' <summary>
        ''' 声明会与哪些游戏版本冲突，例如 breaks 中的 minecraft。
        ''' </summary>
        Public Property BrokenGameVersions As New List(Of String)
        ''' <summary>
        ''' fabric、quilt、forge、neoforge、forge-legacy 或 unknown。
        ''' </summary>
        Public Property Platform As String = "unknown"
        Public Property Dependencies As New List(Of String)
        Public Property Conflicts As New List(Of String)
        ''' <summary>
        ''' 该 Mod 额外提供的 Mod ID，来自 fabric 的 provides。
        ''' </summary>
        Public Property Provides As New List(Of String)
        ''' <summary>
        ''' 声明的前置所要求的版本范围，键为小写的 Mod ID。
        ''' </summary>
        Public Property DependencyLabels As New Dictionary(Of String, String)
        Public Property IsEnabled As Boolean = True
        Public Property ProblemFlags As New List(Of String)
        ''' <summary>
        ''' 是否被崩溃日志点名。
        ''' </summary>
        Public Property CrashSuspect As Boolean = False

        ''' <summary>
        ''' 是否为缺失前置的占位节点。
        ''' </summary>
        Public ReadOnly Property IsMissing As Boolean
            Get
                Return FilePath Is Nothing
            End Get
        End Property

        ''' <summary>
        ''' 增加一个问题标记，重复的标记会被忽略。
        ''' </summary>
        Public Sub AddFlag(FlagText As String)
            If FlagText = "" OrElse ProblemFlags.Contains(FlagText) Then Return
            ProblemFlags.Add(FlagText)
        End Sub

        ''' <summary>
        ''' 是否存在任何问题。
        ''' </summary>
        Public ReadOnly Property HasProblem As Boolean
            Get
                Return ProblemFlags.Any()
            End Get
        End Property

        ''' <summary>
        ''' 该节点应当使用的配色档位。
        ''' </summary>
        Public ReadOnly Property NodeState As ModNodeState
            Get
                For Each FlagText In ProblemFlags
                    If FlagText = "缺少前置" OrElse FlagText = "与当前 MC 版本不符" Then Return ModNodeState.危险
                Next
                If ProblemFlags.Any() Then Return ModNodeState.警告
                Return ModNodeState.正常
            End Get
        End Property

    End Class

    ''' <summary>
    ''' 依赖图中的一条连线。
    ''' </summary>
    Public Class ModGraphLink

        Public Property FromNode As ModGraphNode
        Public Property ToNode As ModGraphNode
        Public Property Kind As ModGraphLinkKind
        ''' <summary>
        ''' 声明的版本范围，仅用于提示文本，可能为空。
        ''' </summary>
        Public Property Label As String = ""

    End Class

#Region "扫描 Mod 文件夹"

    ''' <summary>
    ''' 读取指定版本的 mods 文件夹，解析每个 Mod 自己声明的依赖与冲突。
    ''' 单个文件损坏不会中断整个扫描。
    ''' </summary>
    Public Function ModGraphScan(Instance As McInstance) As List(Of ModGraphNode)
        Dim Results As New List(Of ModGraphNode)
        Try
            If Instance Is Nothing Then Return Results
            Dim Folder = Instance.PathIndie & "mods\"
            If Not DirectoryUtils.Exists(Folder) Then
                Logger.Info($"模组依赖图扫描：Mod 文件夹不存在（{Folder}）")
                Return Results
            End If

            '获取文件列表
            Dim FileNames As New List(Of String)
            Try
                For Each TargetFile In DirectoryUtils.EnumerateFiles(Folder, False)
                    If IsModFileName(TargetFile) Then FileNames.Add(TargetFile)
                Next
            Catch ex As Exception
                Logger.Warn(ex, "模组依赖图扫描：枚举 Mod 文件失败")
            End Try
            FileNames = FileNames.OrderBy(Function(Item) PathUtils.GetLastPart(Item).Lower).ToList

            '逐个读取 jar 自带的元数据
            For Each FullPath In FileNames
                Dim ReadNodes As List(Of ModGraphNode) = Nothing
                Try
                    Using JarArchive = FileUtils.OpenZip(FullPath)
                        ReadNodes = ReadFabricMeta(JarArchive, FullPath)
                        If ReadNodes Is Nothing Then ReadNodes = ReadQuiltMeta(JarArchive, FullPath)
                        If ReadNodes Is Nothing Then ReadNodes = ReadForgeMeta(JarArchive, FullPath)
                        If ReadNodes Is Nothing Then ReadNodes = ReadMcmodMeta(JarArchive, FullPath)
                    End Using
                Catch ex As Exception
                    Logger.Warn(ex, $"读取 Mod 元数据失败（{PathUtils.GetLastPart(FullPath)}）")
                End Try
                If ReadNodes Is Nothing OrElse Not ReadNodes.Any() Then
                    Results.Add(ReadFileNameMeta(FullPath))
                Else
                    For Each ReadNode In ReadNodes
                        Results.Add(ReadNode)
                    Next
                End If
            Next
            Logger.Info($"模组依赖图扫描完成：{Folder}，共 {Results.Count} 个 Mod")
        Catch ex As Exception
            Logger.Warn(ex, "模组依赖图扫描失败")
        End Try
        Return Results
    End Function

    ''' <summary>
    ''' 判断是否为 Mod 文件。与 Mod 管理页面一致：去掉 .disabled 与 .old 后缀后仍是 jar、zip 或 litemod。
    ''' </summary>
    Private Function IsModFileName(FullName As String) As Boolean
        Dim ExtensionName = PathUtils.GetExtension(FullName)
        If ExtensionName = "disabled" OrElse ExtensionName = "old" Then
            Return IsModExtension(PathUtils.GetExtension(FullName.BeforeLast(".")))
        End If
        Return IsModExtension(ExtensionName)
    End Function
    Private Function IsModExtension(ExtensionName As String) As Boolean
        Return ExtensionName = "jar" OrElse ExtensionName = "zip" OrElse ExtensionName = "litemod"
    End Function

    ''' <summary>
    ''' 新建一个对应某个文件的节点。
    ''' </summary>
    Private Function NewGraphNode(FullPath As String, PlatformName As String) As ModGraphNode
        Dim Result As New ModGraphNode
        Result.FilePath = FullPath
        Result.FileName = PathUtils.GetLastPart(FullPath)
        Result.Platform = PlatformName
        Result.IsEnabled = Not Result.FileName.ContainsIgnoreCase(".disabled") AndAlso Not Result.FileName.EndsWithF(".old")
        Return Result
    End Function

#End Region

#Region "元数据解析"

    ''' <summary>
    ''' 读取压缩包中某个文本文件的内容。若不存在则返回 Nothing。
    ''' </summary>
    Private Function ZipEntryText(Archive As ZipArchive, EntryName As String) As String
        Dim Target As ZipArchiveEntry = Archive.GetEntry(EntryName)
        If Target Is Nothing Then Return Nothing
        Return Target.Open().ReadString(Encoding.UTF8)
    End Function
    Private Function ZipEntryJson(Archive As ZipArchive, EntryName As String) As JToken
        Dim Content = ZipEntryText(Archive, EntryName)
        If String.IsNullOrWhiteSpace(Content) Then Return Nothing
        Return Content.DeserializeJson()
    End Function

    ''' <summary>
    ''' 读取 Fabric 的 fabric.mod.json。
    ''' </summary>
    Private Function ReadFabricMeta(Archive As ZipArchive, FullPath As String) As List(Of ModGraphNode)
        Dim Root = ZipEntryJson(Archive, "fabric.mod.json")
        If TypeOf Root IsNot JObject Then Return Nothing
        Dim Json As JObject = DirectCast(Root, JObject)
        Dim Node = NewGraphNode(FullPath, "fabric")
        Node.ModId = TokenText(Json("id"))
        Node.VersionText = TokenText(Json("version"))
        Node.DisplayName = TokenText(Json("name"))
        'depends 既可能是 { modid = 范围 } 也可能是数组，breaks 与 conflicts 同理
        CollectGraphIds(Node, Json("depends"), True)
        CollectGraphIds(Node, Json("breaks"), False)
        CollectGraphIds(Node, Json("conflicts"), False)
        AddProvides(Node, Json("provides"))
        FinishGraphNode(Node)
        Return New List(Of ModGraphNode) From {Node}
    End Function

    ''' <summary>
    ''' 读取 Quilt 的 quilt.mod.json，字段位于 quilt_mod 之下。
    ''' </summary>
    Private Function ReadQuiltMeta(Archive As ZipArchive, FullPath As String) As List(Of ModGraphNode)
        Dim Root = ZipEntryJson(Archive, "quilt.mod.json")
        If TypeOf Root IsNot JObject Then Return Nothing
        Dim Body As JObject = DirectCast(Root, JObject)
        Dim BodyToken = Body("quilt_mod")
        If TypeOf BodyToken Is JObject Then Body = DirectCast(BodyToken, JObject)
        Dim Node = NewGraphNode(FullPath, "quilt")
        Node.ModId = TokenText(Body("id"))
        Node.VersionText = TokenText(Body("version"))
        Dim MetaToken = Body("metadata")
        If TypeOf MetaToken Is JObject Then Node.DisplayName = TokenText(DirectCast(MetaToken, JObject)("name"))
        '新版 schema 里 depends 可能是字符串数组、对象数组或对象，一律宽松读取
        CollectGraphIds(Node, Body("depends"), True)
        CollectGraphIds(Node, Body("breaks"), False)
        CollectGraphIds(Node, Body("conflicts"), False)
        AddProvides(Node, Body("provides"))
        FinishGraphNode(Node)
        Return New List(Of ModGraphNode) From {Node}
    End Function

    ''' <summary>
    ''' 读取 Forge 与 NeoForge 的 META-INF/mods.toml。
    ''' 项目里没有 TOML 库，所以只手写最小行解析：抓 [[mods]] 中的 modId、version、displayName，
    ''' 以及 [[dependencies.x]] 中的 modId、versionRange 与是否必需。无法识别的内容一律跳过，不抛异常。
    ''' </summary>
    Private Function ReadForgeMeta(Archive As ZipArchive, FullPath As String) As List(Of ModGraphNode)
        Dim Content = ZipEntryText(Archive, "META-INF/mods.toml")
        If Content Is Nothing Then Content = ZipEntryText(Archive, "META-INF/neoforge.mods.toml")
        If String.IsNullOrWhiteSpace(Content) Then Return Nothing

        Dim Results As New List(Of ModGraphNode)
        Dim Owner As ModGraphNode = Nothing
        Dim Section As String = ""
        Dim PendingId As String = Nothing
        Dim PendingRange As String = Nothing
        Dim PendingRequired As Boolean = True
        For Each RawLine In Content.SplitLines(True)
            Dim Line = RawLine.Trim
            If Line = "" OrElse Line.StartsWithF("#") Then Continue For
            '段落标题
            If Line.StartsWithF("[") Then
                If Section = "deps" AndAlso Owner IsNot Nothing AndAlso PendingRequired Then AddGraphId(Owner, PendingId, PendingRange, True)
                PendingId = Nothing
                PendingRange = Nothing
                PendingRequired = True
                Dim Header = Line.TrimStart("["c).TrimEnd("]"c).Lower
                If Header.StartsWithF("mods") OrElse Header = "mod" Then
                    Section = "mods"
                    Owner = NewGraphNode(FullPath, "forge")
                    Results.Add(Owner)
                ElseIf Header.StartsWithF("dependencies") OrElse Header.StartsWithF("deps") Then
                    '把依赖段落挂到它前面那个 Mod 上；对 [[deps.x]] 之类的变体同样接受
                    Section = "deps"
                    If Owner IsNot Nothing AndAlso Header.ContainsIgnoreCase("neoforge") Then Owner.Platform = "neoforge"
                Else
                    Section = "other"
                End If
                Continue For
            End If
            '键值对
            Dim Position = Line.IndexOfF("=")
            If Position < 0 OrElse Owner Is Nothing Then Continue For
            Dim Key = Line.Substring(0, Position).Trim.Trim(""""c).Lower
            Dim Value = TomlLineValue(Line.Substring(Position + 1))
            If Value = "" Then Continue For
            Select Case Section
                Case "mods"
                    Select Case Key
                        Case "modid" : Owner.ModId = Value
                        Case "version" : Owner.VersionText = Value
                        Case "displayname" : Owner.DisplayName = Value
                        Case "mcversion" : AddToList(Owner.GameVersions, Value)
                    End Select
                Case "deps"
                    Select Case Key
                        Case "modid" : PendingId = Value
                        Case "versionrange", "versions" : PendingRange = Value
                        Case "mandatory", "required" : PendingRequired = Value.StartsWithF("true")
                    End Select
            End Select
        Next
        If Section = "deps" AndAlso Owner IsNot Nothing AndAlso PendingRequired Then AddGraphId(Owner, PendingId, PendingRange, True)

        For Each Node In Results
            FinishGraphNode(Node)
        Next
        If Not Results.Any() Then Return Nothing
        Return Results
    End Function

    ''' <summary>
    ''' 取 toml 行值的文本部分，去掉引号与行尾注释。
    ''' </summary>
    Private Function TomlLineValue(RawValue As String) As String
        Dim Text = RawValue.Trim
        If Not Text.StartsWithF("""") AndAlso Not Text.StartsWithF("'") Then
            Dim Position = Text.IndexOfF("#")
            If Position >= 0 Then Text = Text.Substring(0, Position)
        End If
        Return Text.Trim(""""c, "'"c).Trim
    End Function

    ''' <summary>
    ''' 读取老 Forge 与 LiteLoader 使用的 mcmod.info。
    ''' </summary>
    Private Function ReadMcmodMeta(Archive As ZipArchive, FullPath As String) As List(Of ModGraphNode)
        Dim Root = ZipEntryJson(Archive, "mcmod.info")
        If Root Is Nothing Then Return Nothing
        Dim Items As New List(Of JToken)
        If TypeOf Root Is JArray Then
            Items.AddRange(DirectCast(Root, JArray))
        ElseIf TypeOf Root Is JObject Then
            Dim ListToken = DirectCast(Root, JObject)("modlist")
            If TypeOf ListToken Is JArray Then
                Items.AddRange(DirectCast(ListToken, JArray))
            Else
                Items.Add(Root)
            End If
        End If

        Dim Results As New List(Of ModGraphNode)
        For Each JsonItem In Items
            If TypeOf JsonItem IsNot JObject Then Continue For
            Dim Json As JObject = DirectCast(JsonItem, JObject)
            Dim Node = NewGraphNode(FullPath, "forge-legacy")
            Node.ModId = TokenText(Json("modid"))
            Node.VersionText = TokenText(Json("version"))
            Node.DisplayName = TokenText(Json("name"))
            AddToList(Node.GameVersions, TokenText(Json("mcversion")))
            'dependencies 通常是字符串数组，也有写成 "a,b" 的
            Dim Dependencies = Json("dependencies")
            If TypeOf Dependencies Is JArray Then
                For Each ItemToken As JToken In DirectCast(Dependencies, JArray)
                    AddGraphId(Node, TokenText(ItemToken), "", True)
                Next
            ElseIf TypeOf Dependencies Is JObject Then
                For Each Pair In DirectCast(Dependencies, JObject).Properties()
                    AddGraphId(Node, Pair.Name, "", True)
                Next
            ElseIf Dependencies IsNot Nothing Then
                For Each Part In Dependencies.ToString.Replace(";", ",").Split(","c, True)
                    AddGraphId(Node, Part, "", True)
                Next
            End If
            FinishGraphNode(Node)
            Results.Add(Node)
        Next
        If Not Results.Any() Then Return Nothing
        Return Results
    End Function

    ''' <summary>
    ''' 元数据完全读不到时，退化为按 &lt;名称&gt;-&lt;版本&gt;.jar 猜测。
    ''' </summary>
    Private Function ReadFileNameMeta(FullPath As String) As ModGraphNode
        Dim Node = NewGraphNode(FullPath, "unknown")
        Dim EnabledName = PathUtils.GetLastPart(FullPath).Replace(".disabled", "").Replace(".old", "")
        Dim BaseName = PathUtils.GetFileNameWithoutExtension(EnabledName)
        Node.DisplayName = BaseName
        Node.ModId = BaseName.Lower
        Node.VersionText = "未知"
        Dim Position = Math.Max(BaseName.LastIndexOfF("-"), BaseName.LastIndexOfF("_"))
        If Position > 0 AndAlso Position < BaseName.Length - 1 Then
            Dim Tail = BaseName.Substring(Position + 1)
            If Char.IsDigit(Tail(0)) Then
                Node.VersionText = Tail
                Node.DisplayName = BaseName.Substring(0, Position)
                Node.ModId = Node.DisplayName.Lower
            End If
        End If
        FinishGraphNode(Node)
        Return Node
    End Function

    ''' <summary>
    ''' 补全节点中缺失的显示信息。
    ''' </summary>
    Private Sub FinishGraphNode(Node As ModGraphNode)
        Node.ModId = CleanGraphId(Node.ModId)
        Node.Dependencies = Node.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        Node.Conflicts = Node.Conflicts.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        Node.Provides = Node.Provides.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        If Node.DisplayName = "" Then Node.DisplayName = If(Node.ModId <> "", Node.ModId, PathUtils.GetFileNameWithoutExtension(Node.FileName))
        If Node.VersionText = "" Then Node.VersionText = "未知"
    End Sub

    ''' <summary>
    ''' 把 JSON 中 object 或 array 形式的依赖声明收集到节点中。
    ''' </summary>
    Private Sub CollectGraphIds(Node As ModGraphNode, Token As JToken, IsDependency As Boolean)
        If Token Is Nothing Then Return
        If TypeOf Token Is JObject Then
            For Each Pair In DirectCast(Token, JObject).Properties()
                AddGraphId(Node, Pair.Name, TokenText(Pair.Value), IsDependency)
            Next
        ElseIf TypeOf Token Is JArray Then
            For Each ItemToken As JToken In DirectCast(Token, JArray)
                Dim DeclareId As String = Nothing, DeclareRange As String = Nothing
                If TypeOf ItemToken Is JObject Then
                    Dim DeclareItem As JObject = DirectCast(ItemToken, JObject)
                    DeclareId = TokenText(DeclareItem("id"))
                    If DeclareId = "" Then DeclareId = TokenText(DeclareItem("modId"))
                    DeclareRange = TokenText(DeclareItem("version"))
                ElseIf TypeOf ItemToken Is JArray Then
                    DeclareId = TokenText(DirectCast(ItemToken, JArray)(0))
                Else
                    '形如 "mod:minecraft@>=1.18" 的写法
                    Dim ItemText = ItemToken.ToString
                    If ItemText.Contains("@") Then
                        DeclareId = ItemText.BeforeFirst("@")
                        DeclareRange = ItemText.AfterFirst("@")
                    Else
                        DeclareId = ItemText
                    End If
                End If
                AddGraphId(Node, DeclareId, DeclareRange, IsDependency)
            Next
        End If
    End Sub
    ''' <summary>
    ''' 记录一条声明的依赖或冲突。minecraft 用来推断游戏版本，加载器一类的 ID 不参与建图。
    ''' </summary>
    Private Sub AddGraphId(Node As ModGraphNode, RawId As String, RawRange As String, IsDependency As Boolean)
        Dim DeclareId = CleanGraphId(RawId)
        If DeclareId = "" Then Return
        If DeclareId.Lower = "minecraft" Then
            '记录声明支持或破坏的游戏版本
            If IsDependency Then AddToList(Node.GameVersions, RawRange) Else AddToList(Node.BrokenGameVersions, RawRange)
            Return
        End If
        If IsLoaderId(DeclareId) Then Return
        If IsDependency Then
            AddToList(Node.Dependencies, DeclareId)
            Dim LabelKey = DeclareId.Lower
            If Not Node.DependencyLabels.ContainsKey(LabelKey) Then Node.DependencyLabels(LabelKey) = CutText(RawRange, 24)
        Else
            AddToList(Node.Conflicts, DeclareId)
        End If
    End Sub
    ''' <summary>
    ''' 记录 fabric 的 provides，即该 Mod 冒充的其它 Mod ID。
    ''' </summary>
    Private Sub AddProvides(Node As ModGraphNode, Token As JToken)
        If TypeOf Token IsNot JArray Then Return
        For Each ItemToken As JToken In DirectCast(Token, JArray)
            If TypeOf ItemToken Is JArray Then
                AddToList(Node.Provides, CleanGraphId(TokenText(DirectCast(ItemToken, JArray)(0))))
            Else
                AddToList(Node.Provides, CleanGraphId(TokenText(ItemToken)))
            End If
        Next
    End Sub

    ''' <summary>
    ''' 去掉 quilt 的 mod: 等命名空间前缀与 @ 之后的版本范围。
    ''' </summary>
    Private Function CleanGraphId(RawId As String) As String
        If RawId Is Nothing Then Return ""
        Dim Text = RawId.Trim
        If Text.Contains("@") Then Text = Text.BeforeFirst("@")
        For Each Prefix In {"mod:", "maven:", "java:", "javaagent:", "registry:", "curse:", "broken_curse:"}
            If Text.StartsWithF(Prefix) Then
                Text = Text.Substring(Prefix.Length)
                Exit For
            End If
        Next
        Return Text.Trim
    End Function
    ''' <summary>
    ''' 加载器、Java 与 Maven 坐标之类的前置并不是 mods 文件夹里的 jar，不参与建图。
    ''' </summary>
    Private Function IsLoaderId(DeclareId As String) As Boolean
        Select Case DeclareId.Lower
            Case "java", "jdk", "javaagent", "java_agent", "fabricloader", "fabric", "quilt", "quilt_loader",
                 "quiltloader", "forge", "neoforge", "fmlloader", "javafml", "maven", "curse", "curse.maven",
                 "broken_curse", "federate", "registry", "registryconstants"
                Return True
            Case Else
                Return False
        End Select
    End Function
    Private Function TokenText(Token As JToken) As String
        If Token Is Nothing OrElse TypeOf Token Is JObject OrElse TypeOf Token Is JArray Then Return ""
        Return Token.ToString.Trim
    End Function
    Private Sub AddToList(Target As List(Of String), Value As String)
        If Value = "" OrElse Target.Contains(Value) Then Return
        Target.Add(Value)
    End Sub
    ''' <summary>
    ''' 截断过长的文本，避免提示气泡里塞下整段 JSON。
    ''' </summary>
    Private Function CutText(Value As String, MaxLength As Integer) As String
        If Value Is Nothing Then Return ""
        If Value.Length <= MaxLength Then Return Value
        Return Value.Substring(0, MaxLength) & "…"
    End Function

#End Region

#Region "建立连线与问题标记"

    ''' <summary>
    ''' 把声明的 Mod ID 与实际安装的 Mod 对应起来，生成连线并标记问题。
    ''' 会向 Nodes 中追加缺失前置的占位节点。
    ''' </summary>
    Public Function ModGraphBuild(Nodes As List(Of ModGraphNode), McVersionName As String) As List(Of ModGraphLink)
        Dim Links As New List(Of ModGraphLink)
        Try
            If Nodes Is Nothing OrElse Not Nodes.Any() Then Return Links

            '建立 ID 索引，provides 提供的别名同样可以命中
            Dim Index As New Dictionary(Of String, ModGraphNode)
            For Each Node In Nodes
                If Node.IsMissing OrElse Not Node.IsEnabled OrElse Node.ModId = "" Then Continue For
                AddGraphIndex(Index, Node, Node.ModId)
                For Each ProvideId In Node.Provides
                    AddGraphIndex(Index, Node, ProvideId)
                Next
            Next

            '过程中会向 Nodes 追加占位节点，所以遍历快照
            Dim Snapshot = Nodes.ToList()
            For Each Node In Snapshot
                If Node.IsMissing OrElse Not Node.IsEnabled Then Continue For
                '前置
                For Each DeclareId In Node.Dependencies.ToList()
                    If DeclareId.Lower = Node.ModId.Lower Then Continue For
                    Dim Target As ModGraphNode = Nothing
                    If Index.TryGetValue(DeclareId.Lower, Target) AndAlso Target IsNot Node AndAlso Not Target.IsMissing Then
                        Links.Add(NewLink(Node, Target, ModGraphLinkKind.Dependency, GraphLabel(Node, DeclareId)))
                    Else
                        '没有安装的前置：也画进图里，这样一眼就能看出缺了谁
                        Dim Missing = GetMissingNode(Nodes, DeclareId)
                        Node.AddFlag("缺少前置")
                        Links.Add(NewLink(Node, Missing, ModGraphLinkKind.MissingDependencyTarget, GraphLabel(Node, DeclareId)))
                    End If
                Next
                '冲突
                For Each DeclareId In Node.Conflicts.ToList()
                    Dim Target As ModGraphNode = Nothing
                    If Not Index.TryGetValue(DeclareId.Lower, Target) Then Continue For
                    If Target Is Node OrElse Target.IsMissing OrElse Not Target.IsEnabled Then Continue For
                    '两个方向都声明冲突时只保留一条连线
                    If HasLink(Links, Node, Target) OrElse HasLink(Links, Target, Node) Then Continue For
                    Links.Add(NewLink(Node, Target, ModGraphLinkKind.Conflict, ""))
                    Node.AddFlag($"与 {Target.DisplayName} 冲突")
                    Target.AddFlag($"与 {Node.DisplayName} 冲突")
                Next
            Next

            '重复安装
            Dim DuplicateGroups = Nodes.Where(Function(SourceNode) Not SourceNode.IsMissing AndAlso SourceNode.IsEnabled AndAlso SourceNode.ModId <> "").
                                  GroupBy(Function(SourceNode) SourceNode.ModId.Lower).
                                  Where(Function(GroupItems) GroupItems.Count() > 1).
                                  ToList()
            For Each DuplicateGroup In DuplicateGroups
                For Each Node In DuplicateGroup
                    Node.AddFlag("重复安装")
                Next
            Next

            '游戏版本
            Dim McParts = VersionNumbers(McVersionName)
            If McParts.Any() Then
                For Each Node In Snapshot
                    If Node.IsMissing Then Continue For
                    If Node.GameVersions.Any() Then
                        Dim IsSupport = McInRanges(Node.GameVersions, McParts)
                        If IsSupport.HasValue AndAlso Not IsSupport.Value Then Node.AddFlag("与当前 MC 版本不符")
                    End If
                    If Node.BrokenGameVersions.Any() Then
                        Dim IsBroken = McInRanges(Node.BrokenGameVersions, McParts)
                        If IsBroken.HasValue AndAlso IsBroken.Value Then Node.AddFlag("与当前 MC 版本不符")
                    End If
                Next
            End If
            Logger.Info($"模组依赖图建立完成：{Nodes.Count} 个节点，{Links.Count} 条连线")
        Catch ex As Exception
            Logger.Warn(ex, "模组依赖图建立失败")
        End Try
        Return Links
    End Function

    Private Sub AddGraphIndex(Index As Dictionary(Of String, ModGraphNode), Node As ModGraphNode, DeclareId As String)
        Dim Key = CleanGraphId(DeclareId).Lower
        If Key = "" OrElse Index.ContainsKey(Key) Then Return
        Index.Add(Key, Node)
    End Sub
    ''' <summary>
    ''' 取得或新建某个缺失前置的占位节点。
    ''' </summary>
    Private Function GetMissingNode(Nodes As List(Of ModGraphNode), DeclareId As String) As ModGraphNode
        For Each Candidate In Nodes
            If Candidate.IsMissing AndAlso Candidate.ModId.Lower = DeclareId.Lower Then Return Candidate
        Next
        Dim Result As New ModGraphNode
        Result.ModId = DeclareId
        Result.DisplayName = DeclareId
        Result.VersionText = "未安装"
        Result.Platform = "unknown"
        Result.AddFlag("缺少前置")
        Nodes.Add(Result)
        Return Result
    End Function
    Private Function NewLink(FromNode As ModGraphNode, ToNode As ModGraphNode, LinkKind As ModGraphLinkKind, LinkLabel As String) As ModGraphLink
        Dim Result As New ModGraphLink
        Result.FromNode = FromNode
        Result.ToNode = ToNode
        Result.Kind = LinkKind
        Result.Label = LinkLabel
        Return Result
    End Function
    Private Function GraphLabel(Node As ModGraphNode, DeclareId As String) As String
        Dim Result As String = Nothing
        If Node.DependencyLabels.TryGetValue(DeclareId.Lower, Result) Then Return Result
        Return ""
    End Function
    Private Function HasLink(Links As List(Of ModGraphLink), FromNode As ModGraphNode, ToNode As ModGraphNode) As Boolean
        Return Links.Any(Function(Target) Target.Kind = ModGraphLinkKind.Conflict AndAlso Target.FromNode Is FromNode AndAlso Target.ToNode Is ToNode)
    End Function

#End Region

#Region "合并崩溃分析结果"

    ''' <summary>
    ''' 把崩溃分析点名到的 Mod 在图中标记出来。
    ''' </summary>
    Public Sub ModGraphMergeCrash(Nodes As List(Of ModGraphNode), SuspectNames As IEnumerable(Of String))
        Try
            If Nodes Is Nothing OrElse SuspectNames Is Nothing Then Return
            For Each SuspectText In SuspectNames
                Dim CleanText = If(SuspectText, "").Trim((vbCrLf & vbTab & " ").ToCharArray)
                '多行的、过长的信息并不是 Mod 名称，不做匹配
                If CleanText = "" OrElse CleanText.Contains(vbLf) OrElse CleanText.Length > 150 Then Continue For
                Dim Target As ModGraphNode = Nothing
                '先按文件名精确匹配
                For Each Node In Nodes
                    If Node.IsMissing OrElse Node.FileName Is Nothing Then Continue For
                    If Node.FileName.Lower = CleanText.Lower Then
                        Target = Node
                        Exit For
                    End If
                Next
                '再按忽略分隔符后的名称、Mod ID 互相包含匹配，取最像的一个
                If Target Is Nothing Then
                    Dim SuspectNormal = NormalizeGraphName(CleanText)
                    If SuspectNormal.Length >= 4 Then
                        Dim BestLength As Integer = 0
                        For Each Node In Nodes
                            If Node.IsMissing OrElse Node.FileName Is Nothing Then Continue For
                            For Each CandidateText In {Node.FileName, Node.ModId, Node.DisplayName}
                                Dim CandidateNormal = NormalizeGraphName(CandidateText)
                                If CandidateNormal.Length < 4 Then Continue For
                                If (SuspectNormal.Contains(CandidateNormal) OrElse CandidateNormal.Contains(SuspectNormal)) AndAlso CandidateNormal.Length > BestLength Then
                                    BestLength = CandidateNormal.Length
                                    Target = Node
                                End If
                            Next
                        Next
                    End If
                End If
                If Target Is Nothing Then Continue For
                Target.CrashSuspect = True
                Target.AddFlag("崩溃日志中提及")
            Next
        Catch ex As Exception
            Logger.Warn(ex, "合并崩溃分析结果失败")
        End Try
    End Sub
    ''' <summary>
    ''' 只保留字母与数字，用于忽略分隔符后比较 Mod 名称。
    ''' </summary>
    Private Function NormalizeGraphName(Value As String) As String
        If Value Is Nothing Then Return ""
        Return New String(Value.Lower.Where(Function(CharValue) Char.IsLetterOrDigit(CharValue)).ToArray())
    End Function

#End Region

#Region "游戏版本范围判断"

    '以下为启发式判断：无法识别的写法一律不认为是问题，避免误报。
    ''' <summary>
    ''' 判断当前游戏版本是否落在声明的若干个范围之一内。全部无法判断时返回 Nothing。
    ''' </summary>
    Private Function McInRanges(Ranges As List(Of String), McParts As List(Of Integer)) As Boolean?
        Dim Result As Boolean? = Nothing
        For Each TargetRange In Ranges
            Dim Judged = McInSingleRange(TargetRange, McParts)
            If Judged Is Nothing Then Continue For
            If Judged.Value Then Return True
            Result = False
        Next
        Return Result
    End Function
    Private Function McInSingleRange(TargetRange As String, McParts As List(Of Integer)) As Boolean?
        Dim RangeText = If(TargetRange, "").Trim
        If RangeText = "" OrElse RangeText = "*" OrElse RangeText.ContainsIgnoreCase("any") Then Return Nothing
        'Maven 区间，例如 [1.19,2.0) 与 [1.18.2,)
        If RangeText.StartsWithF("[") OrElse RangeText.StartsWithF("(") Then
            Dim IsClosedUpper = RangeText.EndsWithF("]")
            Dim Body = RangeText.TrimStart("["c, "("c).TrimEnd("]"c, ")"c)
            Dim Bounds = Body.Split(","c, True)
            If Not Bounds.Any() Then Return Nothing
            Dim Lower = VersionNumbers(Bounds(0))
            Dim Upper = If(Bounds.Length > 1, VersionNumbers(Bounds(1)), New List(Of Integer))
            Dim IsLowerOk As Boolean? = Nothing
            Dim IsUpperOk As Boolean? = Nothing
            If Lower.Any() Then IsLowerOk = If(RangeText.StartsWithF("("), VersionCompare(McParts, Lower) > 0, VersionCompare(McParts, Lower) >= 0)
            If Upper.Any() Then IsUpperOk = If(IsClosedUpper, VersionCompare(McParts, Upper) <= 0, VersionCompare(McParts, Upper) < 0)
            If IsLowerOk Is Nothing AndAlso IsUpperOk Is Nothing Then Return Nothing
            If IsLowerOk IsNot Nothing AndAlso Not IsLowerOk.Value Then Return False
            If IsUpperOk IsNot Nothing AndAlso Not IsUpperOk.Value Then Return False
            Return True
        End If
        'Fabric 风格：>=1.20、~1.20.1、1.19.x 等，按逗号与空格逐条判断
        Dim IsAnyJudged As Boolean = False
        For Each Piece In RangeText.Replace(",", " ").Split(" "c, True)
            Dim Declared = VersionNumbers(Piece)
            If Not Declared.Any() Then Continue For          '通配写法，无法判断
            IsAnyJudged = True
            Dim Versus = VersionCompare(McParts, Declared)
            Dim IsOk As Boolean
            If Piece.StartsWithF(">=") OrElse Piece.StartsWithF("~") OrElse Piece.StartsWithF("^") Then
                IsOk = Versus >= 0
            ElseIf Piece.StartsWithF(">") Then
                IsOk = Versus > 0
            ElseIf Piece.StartsWithF("<=") Then
                IsOk = Versus <= 0
            ElseIf Piece.StartsWithF("<") Then
                IsOk = Versus < 0
            Else
                '裸版本号：声明了几位就要相等几位，例如 1.20 适配 1.20.1，而 1.20.1 不适配 1.20.2
                IsOk = Versus >= 0 AndAlso HasVersionPrefix(McParts, Declared)
            End If
            If Not IsOk Then Return False
        Next
        If Not IsAnyJudged Then Return Nothing
        Return True
    End Function
    ''' <summary>
    ''' 取出文本中出现的所有数字段，例如 1.20.1 得到 { 1, 20, 1 }。
    ''' </summary>
    Private Function VersionNumbers(Value As String) As List(Of Integer)
        Dim Result As New List(Of Integer)
        If Value Is Nothing Then Return Result
        For Each NumberText In Value.RegexSearch("\d+")
            Result.Add(CInt(Val(NumberText)))
        Next
        Return Result
    End Function
    Private Function VersionCompare(Value As List(Of Integer), Other As List(Of Integer)) As Integer
        Dim Count = Math.Min(Value.Count, Other.Count)
        For i As Integer = 0 To Count - 1
            If Value(i) <> Other(i) Then Return If(Value(i) > Other(i), 1, -1)
        Next
        Return Math.Sign(Value.Count - Other.Count)
    End Function
    Private Function HasVersionPrefix(Value As List(Of Integer), Prefix As List(Of Integer)) As Boolean
        If Value.Count < Prefix.Count Then Return False
        For i As Integer = 0 To Prefix.Count - 1
            If Value(i) <> Prefix(i) Then Return False
        Next
        Return True
    End Function

#End Region

End Module
