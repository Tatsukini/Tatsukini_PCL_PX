Imports System.IO.Compression

''' <summary>
''' 升级整合包：备份、内核组件换算、模组兼容性检查与数据迁移。
''' 本模块中标注了“必须在工作线程执行”的函数会进行网络请求或大量文件操作。
''' </summary>
Public Module ModModpackUpgrade

#Region "数据结构"

    Public Enum ModpackUpgradeModStatus
        ''' <summary>已找到可用于目标版本的模组文件。</summary>
        UpdateAvaliable
        ''' <summary>目标版本下没有匹配的文件，可能无法下载。</summary>
        NoRelease
        ''' <summary>只找到被排除的发行类型，可能无法下载。</summary>
        ReleaseFiltered
        ''' <summary>找到的文件与本版本的 Mod 加载器不一致。</summary>
        LoaderMismatch
        ''' <summary>无法在 Modrinth 或 CurseForge 上识别该文件。</summary>
        Unrecognized
    End Enum

    Public Class ModpackUpgradeMod
        Public FileName As String
        Public DisplayName As String
        Public Status As ModpackUpgradeModStatus
        Public NewFile As ResourceVersion
        Public Note As String = ""
        Public IsSelected As Boolean
    End Class

    Public Class ModpackUpgradeCategory
        Public Key As String
        Public Title As String
        Public Tooltip As String
        ''' <summary>需要复制的相对路径列表，相对于版本的隔离目录。</summary>
        Public Items As New List(Of String)
        Public Compatible As Integer
        Public Incompatible As Integer
        Public UnknownCount As Integer
        Public IsAvailable As Boolean
        Public IsSelected As Boolean
    End Class

    Public Class ModpackUpgradeKernel
        Public Components As McComponents
        Public Request As McInstallRequest
        Public Notes As New List(Of String)
    End Class

#End Region

#Region "备份"

    ''' <summary>
    ''' 将整个版本文件夹备份到 versions\ 下，返回备份文件夹地址。
    ''' 备份文件夹写入了 .pclignore，因此不会出现在版本列表中。
    ''' </summary>
    Public Function ModpackUpgradeBackup(Instance As McInstance) As String
        Dim Target As String = $"{McFolderSelected}versions\{Instance.Name} 备份 {Date.Now.ToString("yyyyMMdd-HHmmss")}\"
        Dim Drive As New DriveInfo(Path.GetPathRoot(McFolderSelected))
        If Drive.AvailableFreeSpace < 1024 * 1024 * 1024 Then
            Throw New Exception($"磁盘空间不足，无法备份：{Drive.Name}仅剩余 {StringUtils.FormatByteSize(Drive.AvailableFreeSpace)}，请清理磁盘后再升级。")
        End If
        Logger.Info($"开始备份版本：{Instance.PathVersion} → {Target}")
        DirectoryUtils.Copy(Instance.PathVersion.TrimEnd("\"c), Target.TrimEnd("\"c))
        FileUtils.Write(Target & ".pclignore", "用于临时地在 PCL 的版本列表中屏蔽此版本。由升级整合包自动生成。")
        Logger.Info($"版本备份完成：{Target}")
        Return Target
    End Function

#End Region

#Region "内核组件"

    Private Function LoaderVersionOf(Versions As Dictionary(Of ModLoaders, String), Loader As ModLoaders) As String
        If Versions Is Nothing OrElse Not Versions.ContainsKey(Loader) Then Return Nothing
        Return Versions(Loader)
    End Function

    ''' <summary>
    ''' 读取版本当前安装的组件。若无安装清单，则从版本 json 中分析。
    ''' </summary>
    Public Function ModpackUpgradeComponentsNow(Instance As McInstance) As McComponents
        Dim Manifest = Configs.McInstallManifest.Get(Instance.Config)
        If Manifest IsNot Nothing AndAlso Manifest.Components IsNot Nothing Then Return Manifest.Components
        Dim VersionInfo = Instance.Version
        Dim OptiFineEntry As JObject = Nothing
        If VersionInfo.OptiFine IsNot Nothing Then
            OptiFineEntry = New JObject From {{"InstanceName", VersionInfo.VanillaName & "-OptiFine_" & VersionInfo.OptiFine}}
        End If
        Return New McComponents(VersionInfo.VanillaName, OptiFineEntry,
            LoaderVersionOf(VersionInfo.ModLoaderVersions, ModLoaders.Forge),
            LoaderVersionOf(VersionInfo.ModLoaderVersions, ModLoaders.NeoForge),
            LoaderVersionOf(VersionInfo.ModLoaderVersions, ModLoaders.Fabric),
            Nothing, Nothing,
            LoaderVersionOf(VersionInfo.ModLoaderVersions, ModLoaders.LiteLoader))
    End Function

    ''' <summary>
    ''' 为目标 Minecraft 版本换算可用的组件与下载信息。必须在工作线程执行。
    ''' 无法自动换算的组件会被丢弃，并在 Notes 中说明。
    ''' </summary>
    Public Function ModpackUpgradeKernelBuild(Instance As McInstance, TargetMc As String, NewName As String, IsInPlace As Boolean) As ModpackUpgradeKernel
        Dim Result As New ModpackUpgradeKernel
        Dim Current = ModpackUpgradeComponentsNow(Instance)
        Dim ForgeEntry As DlForgeVersionEntry = Nothing
        Dim NeoForgeEntry As DlNeoForgeListEntry = Nothing
        Dim LiteLoaderEntry As DlLiteLoaderListEntry = Nothing
        Dim FabricApiEntry As ResourceVersion = Nothing
        Dim OptiFabricEntry As ResourceVersion = Nothing
        Dim OptiFineJson As JObject = Nothing
        Dim ForgeVersion As String = Nothing, NeoForgeVersion As String = Nothing, FabricVersion As String = Nothing
        Dim LiteLoaderVersion As String = Nothing, FabricApiVersion As String = Nothing, OptiFabricVersion As String = Nothing

        'Mod 加载器
        If Current.ModLoaderVersions.ContainsKey(ModLoaders.Fabric) Then
            FabricVersion = Current.ModLoaderVersions(ModLoaders.Fabric) 'Fabric Loader 的版本号与 MC 版本无关，可以直接沿用
            Try
                DlFabricListLoader.WaitForExit()
                If Not DlFabricListLoader.Output.Value("game").Any(Function(G) G("version").ToString = TargetMc) Then
                    Result.Notes.Add($"{TargetMc} 尚未出现在 Fabric Loader 的支持列表中，启动前请自行确认。")
                End If
            Catch ex As Exception
                Logger.Warn(ex, "获取 Fabric 版本列表失败")
                Result.Notes.Add("无法获取 Fabric 版本列表，已沿用当前的 Fabric Loader。")
            End Try
            'Fabric API
            If Current.FabricApiVersion IsNot Nothing Then
                Try
                    DlFabricApiLoader.WaitForExit()
                    Dim ApiEntry = DlFabricApiLoader.Output.Where(Function(V) V.GameVersions IsNot Nothing AndAlso V.GameVersions.Contains(TargetMc)).
                        OrderByDescending(Function(V) V.ReleaseDate).FirstOrDefault()
                    If ApiEntry Is Nothing Then
                        Result.Notes.Add($"未能找到适配 {TargetMc} 的 Fabric API，升级后需要自行下载。")
                    Else
                        FabricApiEntry = ApiEntry
                        FabricApiVersion = If(ApiEntry.Version, If(ApiEntry.Id, ApiEntry.Display))
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "获取 Fabric API 版本列表失败")
                    Result.Notes.Add("无法获取 Fabric API 版本列表，升级后需要自行下载。")
                End Try
            End If
        End If
        If Current.ModLoaderVersions.ContainsKey(ModLoaders.Forge) Then
            Try
                Dim ForgeLoader = New LoaderTask(Of String, List(Of DlForgeVersionEntry))("ModpackUpgradeForge " & TargetMc, AddressOf DlForgeVersionMain)
                ForgeLoader.WaitForExit(TargetMc)
                Dim Entries = ForgeLoader.Output.Where(Function(V) V.Category <> "universal" AndAlso V.Category <> "client").OrderByDescending(Function(V) V).ToList()
                If Not Entries.Any Then
                    Result.Notes.Add($"未能找到适配 {TargetMc} 的 Forge，升级后需要自行安装。")
                Else
                    ForgeEntry = Entries.FirstOrDefault(Function(V) V.IsRecommended)
                    If ForgeEntry Is Nothing Then ForgeEntry = Entries(0)
                    ForgeVersion = ForgeEntry.VersionName
                End If
            Catch ex As Exception
                Logger.Warn(ex, "获取 Forge 版本列表失败")
                Result.Notes.Add($"无法获取 {TargetMc} 的 Forge 版本列表，升级后需要自行安装。")
            End Try
        End If
        If Current.ModLoaderVersions.ContainsKey(ModLoaders.NeoForge) Then
            Try
                DlNeoForgeListLoader.WaitForExit()
                Dim Entries = DlNeoForgeListLoader.Output.Value.Where(Function(V) V.Inherit = TargetMc).ToList()
                If Not Entries.Any Then
                    Result.Notes.Add($"未能找到适配 {TargetMc} 的 NeoForge，升级后需要自行安装。")
                Else
                    NeoForgeEntry = Entries(0)
                    NeoForgeVersion = NeoForgeEntry.VersionName
                End If
            Catch ex As Exception
                Logger.Warn(ex, "获取 NeoForge 版本列表失败")
                Result.Notes.Add($"无法获取 {TargetMc} 的 NeoForge 版本列表，升级后需要自行安装。")
            End Try
        End If
        If Current.ModLoaderVersions.ContainsKey(ModLoaders.LiteLoader) Then
            Try
                DlLiteLoaderListLoader.WaitForExit()
                Dim Entries = DlLiteLoaderListLoader.Output.Value.Where(Function(V) V.Inherit = TargetMc).ToList()
                If Not Entries.Any Then
                    Result.Notes.Add($"未能找到适配 {TargetMc} 的 LiteLoader，升级后需要自行安装。")
                Else
                    LiteLoaderEntry = Entries(0)
                    LiteLoaderVersion = LiteLoaderEntry.JsonToken?("version").ToString()
                End If
            Catch ex As Exception
                Logger.Warn(ex, "获取 LiteLoader 版本列表失败")
            End Try
        End If

        'OptiFine
        If Current.OptiFineEntry IsNot Nothing Then
            Try
                DlOptiFineListLoader.WaitForExit()
                Dim Entry = DlOptiFineListLoader.Output.Value.FirstOrDefault(Function(V) V.DisplayName.StartsWithF(TargetMc & " "))
                If Entry Is Nothing Then
                    Result.Notes.Add($"未能找到适配 {TargetMc} 的 OptiFine，升级后需要自行安装。")
                Else
                    OptiFineJson = JObject.FromObject(Entry)
                    'OptiFabric
                    If FabricVersion IsNot Nothing Then
                        DlOptiFabricLoader.WaitForExit()
                        Dim FabricEntry = DlOptiFabricLoader.Output.Where(Function(V) V.GameVersions IsNot Nothing AndAlso V.GameVersions.Contains(TargetMc)).
                            OrderByDescending(Function(V) V.ReleaseDate).FirstOrDefault()
                        If FabricEntry Is Nothing Then
                            Result.Notes.Add($"未能找到适配 {TargetMc} 的 OptiFabric，升级后需要自行下载。")
                        Else
                            OptiFabricEntry = FabricEntry
                            OptiFabricVersion = If(FabricEntry.Version, If(FabricEntry.Id, FabricEntry.Display))
                        End If
                    End If
                End If
            Catch ex As Exception
                Logger.Warn(ex, "获取 OptiFine 版本列表失败")
                Result.Notes.Add($"无法获取 {TargetMc} 的 OptiFine 版本列表，升级后需要自行安装。")
            End Try
        End If

        '构建安装请求
        Result.Components = New McComponents(TargetMc, OptiFineJson, ForgeVersion, NeoForgeVersion, FabricVersion, FabricApiVersion, OptiFabricVersion, LiteLoaderVersion)
        Dim JsonUrl As String = Nothing
        Try
            DlClientListLoader.WaitForExit()
            For Each Version As JObject In DlClientListLoader.Output.Value("versions")
                If Version("id").ToString = TargetMc Then JsonUrl = Version("url").ToString
            Next
        Catch ex As Exception
            Logger.Warn(ex, "获取 Minecraft 版本列表失败")
        End Try
        If JsonUrl Is Nothing Then Throw New Exception($"无法在版本列表中找到 {TargetMc}，请换一个版本号再试。")
        Result.Request = New McInstallRequest With {
            .NewInstanceName = NewName,
            .InstanceFolder = If(IsInPlace, Instance.PathVersion, $"{McFolderSelected}versions\{NewName}\"),
            .Components = Result.Components,
            .MinecraftJson = JsonUrl,
            .ForgeEntry = ForgeEntry,
            .NeoForgeEntry = NeoForgeEntry,
            .FabricApi = FabricApiEntry,
            .OptiFabric = OptiFabricEntry,
            .LiteLoaderEntry = LiteLoaderEntry}
        Return Result
    End Function

#End Region

#Region "模组兼容性检查"

    ''' <summary>
    ''' 检查 Mods 文件夹下的模组在目标版本是否可用。必须在工作线程执行。
    ''' </summary>
    Public Function ModpackUpgradeModsCheck(PathMods As String, TargetMc As String,
                                            AllowRelease As Boolean, AllowBeta As Boolean, AllowAlpha As Boolean) As List(Of ModpackUpgradeMod)
        Dim Results As New List(Of ModpackUpgradeMod)
        Dim Entries As New List(Of LocalResourceFile)
        If DirectoryUtils.Exists(PathMods) Then
            For Each File In DirectoryUtils.EnumerateFiles(PathMods, False)
                If Not {"jar", "zip", "litemod"}.Contains(PathUtils.GetExtension(File).Lower) Then Continue For '已禁用的模组与被解压的文件夹不参与升级                Entries.Add(New LocalResourceFile(File))
            Next
        End If
        For Each Entry In Entries
            Results.Add(New ModpackUpgradeMod With {.FileName = Entry.File.Name, .DisplayName = Entry.Display, .Status = ModpackUpgradeModStatus.Unrecognized})
        Next
        If Not Entries.Any Then Return Results

        'Modrinth：先按 Hash 找到模组，再按目标版本找到最新文件
        Dim ModrinthMatched As New List(Of Integer) '在 Modrinth 上识别到的条目序号
        Try
            Dim Hashes = Entries.Select(Function(E) E.ModrinthHash).ToList()
            Dim OldFiles As JObject = DlModRequest("https://api.modrinth.com/v2/version_files", HttpMethod.Post,
                $"{{""hashes"": [""{Hashes.Join(""",""")}""], ""algorithm"": ""sha1""}}", "application/json")
            Dim Requests As New List(Of String)
            Dim Indexes As New List(Of Integer)
            Dim OldLoaders As New List(Of List(Of String))
            For Index = 0 To Entries.Count - 1
                Dim Hash = Entries(Index).ModrinthHash
                If Not OldFiles.ContainsKey(Hash) Then Continue For
                ModrinthMatched.Add(Index)
                Dim Loaders = OldFiles(Hash)("loaders").Select(Function(L) L.ToString.Lower).OrderBy(Function(L) L).ToList()
                Requests.Add($"{{""hash"": ""{Hash}"", ""loaders"": [""{Loaders.Join(""",""")}""], ""game_versions"": [""{TargetMc}""]}}")
                Indexes.Add(Index)
                OldLoaders.Add(Loaders)
            Next
            If Requests.Any Then
                Dim Updates As JObject = DlModRequest("https://api.modrinth.com/v2/version_files/update_individual", HttpMethod.Post,
                    $"{{""algorithm"": ""sha1"", ""hashes"": [{Requests.Join(",")}]}}", "application/json")
                For PairIndex = 0 To Indexes.Count - 1
                    Dim Result As ModpackUpgradeMod = Results(Indexes(PairIndex))
                    Dim Hash = Entries(Indexes(PairIndex)).ModrinthHash
                    If Not Updates.ContainsKey(Hash) Then
                        Result.Status = ModpackUpgradeModStatus.NoRelease
                        Result.Note = $"{TargetMc} 没有可用的更新文件"
                        Continue For
                    End If
                    Dim NewLoaders = Updates(Hash)("loaders").Select(Function(L) L.ToString.Lower).OrderBy(Function(L) L).ToList()
                    UpdateFrom(Result, ResourceVersion.FromPlatformJson(Updates(Hash), ResourceTypes.Mod), OldLoaders(PairIndex), NewLoaders,
                        AllowRelease, AllowBeta, AllowAlpha, TargetMc)
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "在 Modrinth 上检查模组更新失败")
            For Each Index In ModrinthMatched
                If Results(Index).Note = "" Then Results(Index).Note = "Modrinth 检查失败"
            Next
        End Try

        'CurseForge：处理 Modrinth 未识别的模组
        Dim Pending = Enumerable.Range(0, Entries.Count).Where(Function(I) Not ModrinthMatched.Contains(I)).ToList()
        If Pending.Any Then
            Try
                Dim HashToIndexes As New Dictionary(Of UInteger, List(Of Integer))
                For Each Index In Pending
                    Dim Hash = Entries(Index).CurseForgeHash
                    If Not HashToIndexes.ContainsKey(Hash) Then HashToIndexes(Hash) = New List(Of Integer)
                    HashToIndexes(Hash).Add(Index)
                Next
                Dim Matches As JContainer = DlModRequest("https://api.curseforge.com/v1/fingerprints/432", HttpMethod.Post,
                    $"{{""fingerprints"": [{String.Join(",", HashToIndexes.Keys)}]}}", "application/json")("data")("exactMatches")
                Dim ProjectToIndexes As New Dictionary(Of Integer, List(Of Integer))
                For Each Match In Matches
                    Dim ProjectId = Match("id").ToObject(Of Integer)
                    Dim Hash = Match("file")("fileFingerprint").ToObject(Of UInteger)
                    If Not HashToIndexes.ContainsKey(Hash) Then Continue For
                    If Not ProjectToIndexes.ContainsKey(ProjectId) Then ProjectToIndexes(ProjectId) = New List(Of Integer)
                    For Each Index In HashToIndexes(Hash)
                        If Not ProjectToIndexes(ProjectId).Contains(Index) Then ProjectToIndexes(ProjectId).Add(Index)
                    Next
                Next
                If Not ProjectToIndexes.Any Then Return SortResults(Results)
                Dim FileIdToIndexes As New Dictionary(Of Integer, List(Of Integer))
                Dim Projects As JContainer = DlModRequest("https://api.curseforge.com/v1/mods", HttpMethod.Post,
                    $"{{""modIds"": [{String.Join(",", ProjectToIndexes.Keys)}]}}", "application/json")("data")
                For Each Project In Projects
                    If Project("isAvailable") IsNot Nothing AndAlso Not Project("isAvailable").ToObject(Of Boolean) Then Continue For
                    Dim ProjectId = Project("id").ToObject(Of Integer)
                    If Not ProjectToIndexes.ContainsKey(ProjectId) Then Continue For
                    For Each Index In ProjectToIndexes(ProjectId)
                        Results(Index).Status = ModpackUpgradeModStatus.NoRelease
                        Results(Index).Note = $"{TargetMc} 没有可用的更新文件"
                    Next
                    For Each FileIndex In Project("latestFilesIndexes")
                        If FileIndex("gameVersion") Is Nothing OrElse FileIndex("gameVersion").ToString <> TargetMc Then Continue For
                        Dim FileId = FileIndex("fileId").ToObject(Of Integer)
                        If Not FileIdToIndexes.ContainsKey(FileId) Then FileIdToIndexes(FileId) = New List(Of Integer)
                        For Each Index In ProjectToIndexes(ProjectId)
                            If Not FileIdToIndexes(FileId).Contains(Index) Then FileIdToIndexes(FileId).Add(Index)
                        Next
                    Next
                Next
                If Not FileIdToIndexes.Any Then Return SortResults(Results)
                Dim NewFiles As JContainer = DlModRequest("https://api.curseforge.com/v1/mods/files", HttpMethod.Post,
                    $"{{""fileIds"": [{String.Join(",", FileIdToIndexes.Keys)}]}}", "application/json")("data")
                For Each Token In NewFiles
                    Dim NewFile = ResourceVersion.FromPlatformJson(Token, ResourceTypes.Mod)
                    If Not NewFile.DownloadAvailable Then Continue For
                    For Each Index In FileIdToIndexes(Token("id").ToObject(Of Integer))
                        If Results(Index).NewFile IsNot Nothing AndAlso Results(Index).NewFile.ReleaseDate >= NewFile.ReleaseDate Then Continue For
                        UpdateFrom(Results(Index), NewFile, Nothing, Nothing, AllowRelease, AllowBeta, AllowAlpha, TargetMc)
                    Next
                Next
            Catch ex As Exception
                Logger.Warn(ex, "在 CurseForge 上检查模组更新失败")
            End Try
        End If
        Return SortResults(Results)
    End Function

    Private Function SortResults(Results As List(Of ModpackUpgradeMod)) As List(Of ModpackUpgradeMod)
        Return Results.OrderBy(Function(R) If(R.Status = ModpackUpgradeModStatus.UpdateAvaliable, 0, 1)).ThenBy(Function(R) R.FileName).ToList()
    End Function

    ''' <summary>
    ''' 依据发行类型与加载器设置检查结果。OldLoaders 为 Nothing 时不检查加载器。
    ''' </summary>
    Private Sub UpdateFrom(Result As ModpackUpgradeMod, NewFile As ResourceVersion, OldLoaders As List(Of String), NewLoaders As List(Of String),
                          AllowRelease As Boolean, AllowBeta As Boolean, AllowAlpha As Boolean, TargetMc As String)
        If NewFile Is Nothing OrElse Not NewFile.DownloadAvailable Then
            Result.Status = ModpackUpgradeModStatus.NoRelease
            Result.Note = $"{TargetMc} 没有可下载的文件"
            Return
        End If
        Result.NewFile = NewFile
        If OldLoaders IsNot Nothing AndAlso Not NewLoaders.SequenceEqual(OldLoaders) Then
            Result.Status = ModpackUpgradeModStatus.LoaderMismatch
            Result.Note = "最新文件的 Mod 加载器与本地模组不同"
            Return
        End If
        Dim IsAllowed As Boolean = (NewFile.ReleaseType = ResourceVersion.ReleaseTypes.Release AndAlso AllowRelease) OrElse
            (NewFile.ReleaseType = ResourceVersion.ReleaseTypes.Beta AndAlso AllowBeta) OrElse
            (NewFile.ReleaseType = ResourceVersion.ReleaseTypes.Alpha AndAlso AllowAlpha)
        If Not IsAllowed Then
            Result.Status = ModpackUpgradeModStatus.ReleaseFiltered
            Result.Note = $"最新文件为 {NewFile.ReleaseTypeDisplay}，该类型未被选中"
            Return
        End If
        Result.Status = ModpackUpgradeModStatus.UpdateAvaliable
        Result.IsSelected = True
        Result.Note = NewFile.ReleaseTypeDisplay & " · " & NewFile.FileName
    End Sub

#End Region

#Region "数据迁移"

    ''' <summary>
    ''' 检查版本可以迁移的数据，并逐项判断与目标版本的兼容性。
    ''' </summary>
    Public Function ModpackUpgradeDataScan(Instance As McInstance, TargetMc As String) As List(Of ModpackUpgradeCategory)
        Dim Result As New List(Of ModpackUpgradeCategory)
        Dim Indie = PathUtils.AddSlashSuffix(Instance.PathIndie)
        Result.Add(NewPackCategory("resourcepacks", "材质包", Indie & "resourcepacks\", TargetMc, "材质包与数据包会声明自己支持的 Minecraft 版本范围，未声明的无法判断。"))
        Result.Add(NewPackCategory("shaderpacks", "光影包", Indie & "shaderpacks\", TargetMc, "光影包不声明支持的版本，需要你自行确认。"))
        '世界数据包
        Dim DataPacks As New ModpackUpgradeCategory With {
            .Key = "datapacks", .Title = "世界数据包", .Tooltip = "只迁移各个存档文件夹里的 datapacks，不迁移存档本体。就地升级时无需迁移。"}
        If DirectoryUtils.Exists(Indie & "saves\") Then
            For Each World In DirectoryUtils.EnumerateDirectories(Indie & "saves\", False)
                Dim Folder = PathUtils.AddSlashSuffix(World)
                If Not DirectoryUtils.Exists(Folder & "datapacks\") Then Continue For
                For Each Pack In EnumerateEntries(Folder & "datapacks\")
                    DataPacks.Items.Add($"{PathUtils.GetLastPart(Folder)}\datapacks\{PathUtils.GetLastPart(Pack)}")
                    Select Case PackFormatCheck(Pack, TargetMc)
                        Case 1 : DataPacks.Compatible += 1
                        Case 0 : DataPacks.Incompatible += 1
                        Case Else : DataPacks.UnknownCount += 1
                    End Select
                Next
            Next
        End If
        DataPacks.IsAvailable = DataPacks.Items.Any
        DataPacks.IsSelected = DataPacks.IsAvailable AndAlso DataPacks.Incompatible = 0
        Result.Add(DataPacks)
        '重要数据
        Dim Important As New ModpackUpgradeCategory With {
            .Key = "important", .Title = "重要数据", .Tooltip = "按键与视频设置（options.txt）、光影设置（optionsshaders.txt）、服务器列表（servers.dat）。这些文件通常可以跨版本使用。"}
        For Each FileName In {"options.txt", "optionsshaders.txt", "servers.dat"}
            If FileUtils.Exists(Indie & FileName) Then Important.Items.Add(FileName)
        Next
        Important.IsAvailable = Important.Items.Any
        Important.UnknownCount = Important.Items.Count
        Important.IsSelected = Important.IsAvailable
        Result.Add(Important)
        '配置文件
        Dim Config As New ModpackUpgradeCategory With {
            .Key = "config", .Title = "模组配置文件", .Tooltip = "config 文件夹。跨 Minecraft 版本使用旧配置容易让模组崩溃，默认不迁移。"}
        If DirectoryUtils.Exists(Indie & "config\") Then
            For Each Item In EnumerateEntries(Indie & "config\")
                Config.Items.Add($"config\{PathUtils.GetLastPart(Item)}")
            Next
        End If
        Config.IsAvailable = Config.Items.Any
        Config.UnknownCount = Config.Items.Count
        Result.Add(Config)
        Return Result
    End Function

    ''' <summary>
    ''' 列出文件夹下的文件与子文件夹。MeloongCore 没有提供对应的接口。
    ''' </summary>
    Private Function EnumerateEntries(Folder As String) As List(Of String)
        Dim Result As New List(Of String)
        If Not DirectoryUtils.Exists(Folder) Then Return Result
        Result.AddRange(DirectoryUtils.EnumerateFiles(Folder, False))
        Result.AddRange(DirectoryUtils.EnumerateDirectories(Folder, False))
        Result.Sort()
        Return Result
    End Function

    Private Function NewPackCategory(Key As String, Title As String, Folder As String, TargetMc As String, Tooltip As String) As ModpackUpgradeCategory
        Dim Category As New ModpackUpgradeCategory With {.Key = Key, .Title = Title, .Tooltip = Tooltip}
        Dim FolderName = PathUtils.GetLastPart(Folder.TrimEnd("\"c))
        For Each Item In EnumerateEntries(Folder)
            Category.Items.Add($"{FolderName}\{PathUtils.GetLastPart(Item)}")
            Select Case PackFormatCheck(Item, TargetMc)
                Case 1 : Category.Compatible += 1
                Case 0 : Category.Incompatible += 1
                Case Else : Category.UnknownCount += 1
            End Select
        Next
        Category.IsAvailable = Category.Items.Any
        Category.IsSelected = Category.IsAvailable AndAlso Category.Incompatible = 0
        Return Category
    End Function

    ''' <summary>
    ''' 依据 pack.mcmeta 判断资源包与目标 Minecraft 版本是否兼容。
    ''' 返回 1 兼容，0 不兼容，-1 无法判断。
    ''' </summary>
    Public Function PackFormatCheck(PackPath As String, TargetMc As String) As Integer
        Dim Meta As String = Nothing
        Try
            If DirectoryUtils.Exists(PackPath) Then
                Dim MetaPath = PathUtils.AddSlashSuffix(PackPath) & "pack.mcmeta"
                If FileUtils.Exists(MetaPath) Then Meta = FileUtils.TryReadAsString(MetaPath)
            ElseIf PathUtils.GetExtension(PackPath).Lower = "zip" Then
                Using Archive As ZipArchive = FileUtils.OpenZip(PackPath)
                    Dim Entry = Archive.Entries.FirstOrDefault(Function(E) E.FullName.TrimStart("/"c).Replace("\"c, "/") = "pack.mcmeta")
                    If Entry Is Nothing Then Entry = Archive.Entries.FirstOrDefault(Function(E) E.FullName.EndsWithF("/pack.mcmeta") AndAlso E.FullName.Count(Function(C) C = "/"c) = 1)
                    If Entry IsNot Nothing Then
                        Using Stream = Entry.Open()
                            Using Reader As New StreamReader(Stream, Text.Encoding.UTF8)
                                Meta = Reader.ReadToEnd()
                            End Using
                        End Using
                    End If
                End Using
            End If
        Catch ex As Exception
            Logger.Warn(ex, $"读取资源包信息失败：{PackPath}", LogBehavior.None)
            Return -1
        End Try
        If Meta Is Nothing OrElse Not Meta.TrimStart().StartsWithF("{") Then Return -1
        Try
            Dim Pack = Meta.DeserializeJson()("pack")
            If Pack Is Nothing Then Return -1
            '只有明确声明了版本范围时才做判断，pack_format 数字与新版本的换算不可靠
            Dim Supported = Pack("supported_formats")
            If Supported Is Nothing Then Return -1
            If Supported.Type = JTokenType.Array Then
                For Each Item In Supported
                    If Item("min_version") Is Nothing OrElse Item("max_version") Is Nothing Then Continue For
                    If McUtils.CompareVersion(TargetMc, Item("min_version").ToString) >= 0 AndAlso
                       McUtils.CompareVersion(TargetMc, Item("max_version").ToString) <= 0 Then Return 1
                Next
                Return 0
            End If
            Dim Min = Supported("min_version")?.ToString()
            Dim Max = Supported("max_version")?.ToString()
            If Min Is Nothing OrElse Max Is Nothing Then Return -1
            If McUtils.CompareVersion(Min, TargetMc) <= 0 AndAlso McUtils.CompareVersion(TargetMc, Max) <= 0 Then Return 1
            Return 0
        Catch ex As Exception
            Logger.Warn(ex, $"解析 pack.mcmeta 失败：{PackPath}", LogBehavior.None)
            Return -1
        End Try
    End Function

    ''' <summary>
    ''' 将选中的类别复制到目标版本的隔离目录，返回成功迁移的项数。必须在工作线程执行。
    ''' </summary>
    Public Function ModpackUpgradeDataMigrate(Source As McInstance, TargetFolder As String, Categories As IEnumerable(Of ModpackUpgradeCategory)) As Integer
        Dim SourceFolder = PathUtils.AddSlashSuffix(Source.PathIndie)
        Dim Target = PathUtils.AddSlashSuffix(TargetFolder)
        If PathUtils.IsSame(SourceFolder, Target) Then
            Logger.Info("就地升级，数据已经位于目标版本文件夹，无需迁移")
            Return 0
        End If
        Dim Finished As Integer = 0
        For Each Category In Categories.Where(Function(C) C.IsSelected)
            For Each Relative In Category.Items
                Dim SourcePath = SourceFolder & Relative
                Dim TargetPath = Target & Relative
                Try
                    If DirectoryUtils.Exists(SourcePath) Then
                        DirectoryUtils.Copy(SourcePath.TrimEnd("\"c), TargetPath.TrimEnd("\"c))
                    ElseIf FileUtils.Exists(SourcePath) Then
                        FileUtils.Copy(SourcePath, TargetPath)
                    Else
                        Logger.Warn($"待迁移的文件已不存在，跳过：{SourcePath}")
                        Continue For
                    End If
                    Finished += 1
                Catch ex As Exception
                    Logger.Warn(ex, $"迁移 {Category.Title} 失败：{Relative}")
                End Try
            Next
        Next
        Return Finished
    End Function

#End Region

#Region "执行升级"

    ''' <summary>
    ''' 构建并启动升级任务：下载新内核、下载模组、迁移数据。返回是否已经开始执行。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Public Function ModpackUpgradeStart(Instance As McInstance, Kernel As ModpackUpgradeKernel, Mods As IEnumerable(Of ModpackUpgradeMod),
                                        Categories As IEnumerable(Of ModpackUpgradeCategory), IsInPlace As Boolean, DisableUnmatched As Boolean) As Boolean
        Dim Request = Kernel.Request
        Dim TargetMc = Kernel.Components.MinecraftVersion
        Dim VersionFolder = If(IsInPlace, Instance.PathVersion, PathUtils.AddSlashSuffix($"{McFolderSelected}versions\{Request.NewInstanceName}\"))
        Dim Loaders As New List(Of LoaderBase)
        '为了避开 LoaderCombo 的输入类型限制，先下载到临时文件夹，再复制到新版本
        Dim TempMods As String = PathTemp & "mods_upgrade\"
        Dim UpdateList = Mods.Where(Function(M) M.IsSelected AndAlso M.NewFile IsNot Nothing AndAlso M.NewFile.DownloadAvailable).ToList()
        Dim FileList As New List(Of NetFile)
        For Each ModEntry In UpdateList
            FileList.Add(ModEntry.NewFile.ToNetFile(TempMods & ModEntry.NewFile.FileName, ResourceVersion.DownloadReason.Update, TargetMc))
        Next
        '1. 新内核
        If IsInPlace Then
            '先在临时文件夹中构建新版本，再覆盖到当前版本文件夹
            Dim BuildFolder = RequestTaskTempFolder(True) & Instance.Name & "\"
            Dim BuildRequest As New McInstallRequest With {
                .NewInstanceName = Instance.Name,
                .InstanceFolder = BuildFolder,
                .Components = Request.Components,
                .MinecraftJson = Request.MinecraftJson,
                .ForgeEntry = Request.ForgeEntry,
                .NeoForgeEntry = Request.NeoForgeEntry,
                .FabricApi = Request.FabricApi,
                .OptiFabric = Request.OptiFabric,
                .LiteLoaderEntry = Request.LiteLoaderEntry,
                .IsIntermediateOutput = True}
            Loaders.AddRange(McInstallLoadersGet(BuildRequest))
            Loaders.Add(New LoaderTask(Of Integer, Integer)("覆盖到当前版本",
            Sub()
                Dim Manifest = Configs.McInstallManifest.Get(New McInstance(BuildFolder).Config)
                If Manifest Is Nothing Then Throw New Exception("未能读取到新内核的安装清单！")
                McInstallCommit(Instance, Manifest, BuildFolder, PathUtils.AddSlashSuffix(Instance.PathIndie))
                RunInUiWait(
                Sub()
                    WriteIni(McFolderSelected & "PCL.ini", "InstanceCache", "")
                    If PageInstanceLeft.Instance IsNot Nothing AndAlso PathUtils.IsSame(PageInstanceLeft.Instance.PathVersion, Instance.PathVersion) Then
                        PageInstanceLeft.Instance = New McInstance(Instance.PathVersion).Load()
                    End If
                End Sub)
            End Sub) With {.ProgressWeight = 1, .Block = True})
        Else
            Loaders.AddRange(McInstallLoadersGet(Request))
        End If
        '2. 模组
        If FileList.Any Then
            Loaders.Add(New LoaderDownload("下载新版模组", FileList) With {.ProgressWeight = UpdateList.Count * 1.5})
        End If
        Loaders.Add(New LoaderTask(Of Integer, Integer)("安装模组与迁移数据",
        Sub()
            Dim Indie = PathUtils.AddSlashSuffix(New McInstance(VersionFolder).PathIndie)
            Dim ModsFolder = Indie & "mods\"
            '复制下载好的模组，并把被替换的旧模组重命名为 .disabled
            For Each ModEntry In UpdateList
                Dim Source = TempMods & ModEntry.NewFile.FileName
                Dim Target = ModsFolder & ModEntry.NewFile.FileName
                If Not FileUtils.Exists(Source) Then
                    Logger.Warn($"模组下载结果已丢失，跳过：{Source}")
                    Continue For
                End If
                FileUtils.Copy(Source, Target)
                Dim OldFile = ModsFolder & ModEntry.FileName
                If IsInPlace AndAlso Not PathUtils.IsSame(OldFile, Target) AndAlso FileUtils.Exists(OldFile) Then
                    FileUtils.Move(OldFile, OldFile & ".disabled")
                End If
            Next
            '禁用未通过检查的模组
            If DisableUnmatched Then
                For Each ModEntry In Mods
                    If ModEntry.Status = ModpackUpgradeModStatus.UpdateAvaliable AndAlso ModEntry.IsSelected Then Continue For
                    Dim Target = ModsFolder & ModEntry.FileName
                    If Not FileUtils.Exists(Target) Then Continue For
                    Try
                        FileUtils.Move(Target, Target & ".disabled")
                    Catch ex As Exception
                        Logger.Warn(ex, $"禁用模组失败：{ModEntry.FileName}")
                    End Try
                Next
            End If
            '迁移数据
            Dim Migrated = ModpackUpgradeDataMigrate(Instance, Indie, Categories)
            Logger.Info($"升级整合包结束，已安装 {UpdateList.Count} 个模组，迁移 {Migrated} 项数据")
        End Sub) With {.ProgressWeight = 2})
        '3. 启动
        Dim Loader As New LoaderCombo(Of String)("升级整合包：" & Instance.Name, Loaders) With {
            .OnStateChanged =
            Sub(CurrentLoader)
                Select Case CurrentLoader.State
                    Case LoadState.Finished
                        Hint(CurrentLoader.Name & "成功！", HintType.Green)
                    Case LoadState.Failed
                        MyMsgBox(CurrentLoader.Error.GetDisplay(True), CurrentLoader.Name & "失败", IsWarn:=True)
                    Case LoadState.Canceled
                        Hint(CurrentLoader.Name & "已取消！", HintType.Blue)
                End Select
                '失败或取消时不清理版本文件夹，避免误删数据，需要玩家自行处理
                WriteIni(McFolderSelected & "PCL.ini", "InstanceCache", "")
                LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
            End Sub}
        If IsInPlace Then
            '占用版本，防止与启动、修改同时进行
            Dim Worker As New RedoableWorker(Sub() Loader.WaitForExit(Instance.PathVersion))
            If Not McInstanceLock.TryAcquire(Worker, Instance.PathVersion) Then
                Logger.Error("版本正在被其他任务修改，升级整合包失败")
                Return False
            End If
            AddHandler Worker.Stopped, Sub() RunInUi(Sub() LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\"))
            Worker.Start()
            LoaderFolderRun(McInstanceListLoader, McFolderSelected, LoaderFolderRunType.ForceRun, MaxDepth:=1, ExtraPath:="versions\")
        Else
            Loader.Start(McFolderSelected & "versions\" & Request.NewInstanceName & "\")
        End If
        LoaderTaskbarAdd(Loader)
        FrmMain.BtnExtraDownload.ShowRefresh()
        FrmMain.BtnExtraDownload.Ribble()
        Return True
    End Function

#End Region

End Module
