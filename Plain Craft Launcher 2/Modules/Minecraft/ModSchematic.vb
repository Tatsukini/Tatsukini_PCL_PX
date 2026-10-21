Imports System.IO.Compression
Imports System.Windows.Media.Media3D

''' <summary>
''' 原理图文件（旧版 .schematic、新版 .schem、Litematica .litematic）的 NBT 解析与 3D 网格生成。
''' </summary>
Module ModSchematic

    '单个原理图最多解析的方块数，超出则拒绝，避免内存爆炸
    Public Const MaxSchematicVolume As Integer = 12000000
    '面剔除后最多生成的四边形数
    Public Const MaxSchematicFaces As Integer = 1500000

    ''' <summary>解析结果。方块序号统一为 (y * Depth + z) * Width + x，Nothing 表示空气。</summary>
    Public Class SchematicModel
        Public Format As String
        Public FileName As String
        Public Width As Integer   'X
        Public Height As Integer  'Y
        Public Depth As Integer   'Z（旧版格式中称为 Length）
        Public States As String()
        Public Function Volume() As Integer
            Return Width * Height * Depth
        End Function
        Public Function SolidCount() As Integer
            Dim Result As Integer = 0
            For Each State In States
                If State IsNot Nothing Then Result += 1
            Next
            Return Result
        End Function
        Public Function StateAt(X As Integer, Y As Integer, Z As Integer) As String
            If X < 0 OrElse Y < 0 OrElse Z < 0 OrElse X >= Width OrElse Y >= Height OrElse Z >= Depth Then Return Nothing
            Return States((Y * Depth + Z) * Width + X)
        End Function
    End Class

    ''' <summary>网格生成结果，每种方块颜色一个 GeometryModel3D。</summary>
    Public Class SchematicRender
        Public Models As New List(Of GeometryModel3D)
        Public Faces As Integer
        Public Truncated As Boolean
    End Class

#Region "NBT 读取"

    Friend Class NbtNode
        Public Type As Byte
        Public Name As String = ""
        '标量 / String / Byte() / Integer() / Long() / List(Of NbtNode) / Dictionary(Of String, NbtNode)
        Public Payload As Object
        Public Function Children As Dictionary(Of String, NbtNode)
            Return TryCast(Payload, Dictionary(Of String, NbtNode))
        End Function
        Public Function Items As List(Of NbtNode)
            Return TryCast(Payload, List(Of NbtNode))
        End Function
        Public Function Find(Key As String) As NbtNode
            Dim All = Children
            If All Is Nothing Then Return Nothing
            Dim Result As NbtNode = Nothing
            Return If(All.TryGetValue(Key, Result), Result, Nothing)
        End Function
        ''' <summary>把任意数值标签读成 Integer，标签不存在时返回 Default。</summary>
        Public Function IntOf(Key As String, Optional [Default] As Integer = 0) As Integer
            Dim Node = Find(Key)
            If Node Is Nothing OrElse TypeOf Node.Payload Is String Then Return [Default]
            Return CInt(CLng(Node.Payload))
        End Function
    End Class

    Public Function ReadNbt(Bytes As Byte()) As NbtNode
        Dim Data = Bytes
        If Bytes.Length >= 2 AndAlso Bytes(0) = &H1F AndAlso Bytes(1) = &H8B Then 'gzip 封装
            Using Input As New GZipStream(New MemoryStream(Bytes), CompressionMode.Decompress)
                Using Output As New MemoryStream
                    Input.CopyTo(Output)
                    Data = Output.ToArray()
                End Using
            End Using
        End If
        Using Stream As New MemoryStream(Data)
            Dim Root = ReadNbtNode(Stream)
            If Root.Type <> 10 Then Throw New Exception("原理图根节点不是一个 Compound，文件可能已损坏。")
            Return Root
        End Using
    End Function

    Private Function ReadNbtNode(s As Stream) As NbtNode
        Dim Type As Integer = s.ReadByte()
        If Type < 0 Then Throw New Exception("原理图不是合法的 NBT 数据。")
        Dim Result As New NbtNode With {.Type = CByte(Type), .Name = ReadNbtString(s)}
        Result.Payload = ReadNbtPayload(s, CByte(Type))
        Return Result
    End Function

    ''' <summary>读取 Count 个大端字节并就地反转为本机字节序，供 BitConverter 使用。</summary>
    Private Sub ReadNbtExact(s As Stream, Buffer As Byte(), Count As Integer)
        If Count = 0 Then Return
        Dim Read As Integer
        While Read < Count
            Dim n = s.Read(Buffer, Read, Count - Read)
            If n <= 0 Then Throw New Exception("原理图数据不完整，文件可能已损坏。")
            Read += n
        End While
        If Count >= 2 Then Array.Reverse(Buffer, 0, Count)
    End Sub

    Private Function ReadNbtShort(s As Stream) As Short
        Dim Buffer(1) As Byte
        ReadNbtExact(s, Buffer, 2)
        Return BitConverter.ToInt16(Buffer, 0)
    End Function

    Private Function ReadNbtInt(s As Stream) As Integer
        Dim Buffer(3) As Byte
        ReadNbtExact(s, Buffer, 4)
        Return BitConverter.ToInt32(Buffer, 0)
    End Function

    Private Function ReadNbtString(s As Stream) As String
        Dim Len = ReadNbtShort(s) And &HFFFF
        If Len = 0 Then Return ""
        If Len > 1000000 Then Throw New Exception("原理图数据损坏：字符串过长")
        Return Encoding.UTF8.GetString(ReadNbtBytes(s, Len))
    End Function

    Private Function ReadNbtRaw(s As Stream, Count As Integer) As Byte()
        If Count = 0 Then Return Array.Empty(Of Byte)()
        Dim Buffer(Count - 1) As Byte
        ReadNbtExact(s, Buffer, Count)
        Return Buffer
    End Function

    ''' <summary>读取原始字节但不反转：数组型标签的元素顺序必须保持文件中的先后。</summary>
    Private Function ReadNbtBytes(s As Stream, Count As Integer) As Byte()
        If Count = 0 Then Return Array.Empty(Of Byte)()
        Dim Buffer(Count - 1) As Byte
        Dim Read As Integer
        While Read < Count
            Dim n = s.Read(Buffer, Read, Count - Read)
            If n <= 0 Then Throw New Exception("原理图数据不完整，文件可能已损坏。")
            Read += n
        End While
        Return Buffer
    End Function

    Private Function ReadNbtPayload(s As Stream, Type As Byte) As Object
        Select Case Type
            Case 0 'End
                Return Nothing
            Case 1 'Byte
                Return ReadNbtRaw(s, 1)(0)
            Case 2 'Short
                Return ReadNbtShort(s)
            Case 3 'Int
                Return ReadNbtInt(s)
            Case 4 'Long
                Return BitConverter.ToInt64(ReadNbtRaw(s, 8), 0)
            Case 5 'Float
                Return BitConverter.ToSingle(ReadNbtRaw(s, 4), 0)
            Case 6 'Double
                Return BitConverter.ToDouble(ReadNbtRaw(s, 8), 0)
            Case 7 'ByteArray
                Dim Len = ReadNbtInt(s)
                If Len < 0 OrElse Len > MaxSchematicVolume Then Throw New Exception($"原理图数据损坏：字节数组过长（{Len}）")
                Return ReadNbtBytes(s, Len)
            Case 8 'String
                Return ReadNbtString(s)
            Case 9 'List
                Dim ItemType = s.ReadByte()
                If ItemType < 0 Then Throw New Exception("原理图数据不完整，文件可能已损坏。")
                Dim Count = ReadNbtInt(s)
                If Count < 0 OrElse Count > 2000000 Then Throw New Exception($"原理图数据损坏：列表过长（{Count}）")
                Dim Result As New List(Of NbtNode)
                For i As Integer = 0 To Count - 1
                    Result.Add(New NbtNode With {.Type = CByte(ItemType), .Payload = ReadNbtPayload(s, CByte(ItemType))})
                Next
                Return Result
            Case 10 'Compound
                Dim Result As New Dictionary(Of String, NbtNode)
                While True
                    Dim ItemType = s.ReadByte()
                    If ItemType < 0 Then Throw New Exception("原理图数据不完整，文件可能已损坏。")
                    If ItemType = 0 Then Exit While
                    Dim Name = ReadNbtString(s)
                    Result(Name) = New NbtNode With {.Type = CByte(ItemType), .Name = Name, .Payload = ReadNbtPayload(s, CByte(ItemType))}
                End While
                Return Result
            Case 11 'IntArray
                Dim Len = ReadNbtInt(s)
                If Len < 0 OrElse Len > MaxSchematicVolume Then Throw New Exception($"原理图数据损坏：整型数组过长（{Len}）")
                Dim Buffer = ReadNbtBytes(s, Len * 4)
                Dim Values(Len - 1) As Integer
                For i = 0 To Len - 1
                    Array.Reverse(Buffer, i * 4, 4)
                    Values(i) = BitConverter.ToInt32(Buffer, i * 4)
                Next
                Return Values
            Case 12 'LongArray
                Dim Len = ReadNbtInt(s)
                If Len < 0 OrElse Len > MaxSchematicVolume \ 4 Then Throw New Exception($"原理图数据损坏：长整型数组过长（{Len}）")
                Dim Buffer = ReadNbtBytes(s, Len * 8)
                Dim Values(Len - 1) As Long
                For i = 0 To Len - 1
                    Array.Reverse(Buffer, i * 8, 8)
                    Values(i) = BitConverter.ToInt64(Buffer, i * 8)
                Next
                Return Values
            Case Else
                Throw New Exception($"原理图包含不支持的 NBT 标签类型：{Type}")
        End Select
    End Function

    ''' <summary>取得数组型标签的字节视图：Sponge 的 Blocks 既可能是 IntArray 也可能是 ByteArray，每个元素其实是一个无符号字节。</summary>
    Private Function NbtByteView(Node As NbtNode) As Integer()
        If Node Is Nothing Then Return Nothing
        Dim Buffer = TryCast(Node.Payload, Byte())
        If Buffer IsNot Nothing Then Return Buffer.Select(Function(b) b And &HFF).ToArray()
        Dim Ints = TryCast(Node.Payload, Integer())
        If Ints IsNot Nothing Then Return Ints.Select(Function(v) v And &HFF).ToArray()
        Return Nothing
    End Function

    Private Function NbtLongs(Node As NbtNode) As Long()
        If Node Is Nothing Then Return Nothing
        Return TryCast(Node.Payload, Long())
    End Function

    ''' <summary>把 varint 字节流解码为整数序列（Sponge v2/v3 的 Blocks）。</summary>
    Private Function DecodeVarints(Source As Integer(), Expected As Integer) As Integer()
        Dim Result As New List(Of Integer)(Math.Max(Expected, 1))
        Dim Value As Integer = 0
        Dim Shift As Integer = 0
        For Each ByteValue In Source
            Dim Piece = ByteValue And &HFF
            If Shift >= 28 AndAlso (Piece And &H80) <> 0 Then Throw New Exception("原理图数据损坏：varint 超出范围")
            Value = Value Or ((Piece And &H7F) << Shift)
            If (Piece And &H80) = 0 Then
                Result.Add(Value)
                Value = 0
                Shift = 0
                If Result.Count >= Expected Then Exit For
            Else
                Shift += 7
            End If
        Next
        Return Result.ToArray()
    End Function

    ''' <summary>按 bitWidth 从 Long 流中还原索引（Litematica 的 BlockData），位流在每个 Long 内从低位开始连续排列。</summary>
    Private Function UnpackBits(Source As Long(), BitWidth As Integer, Count As Integer) As Integer()
        Dim Result(Count - 1) As Integer
        If BitWidth < 1 OrElse BitWidth > 32 OrElse Source.Length = 0 Then Return Result
        Dim Mask As Long = (1L << BitWidth) - 1
        For i As Integer = 0 To Count - 1
            Dim BitPos As Long = CLng(i) * BitWidth
            Dim LongIndex As Integer = CInt(BitPos \ 64)
            If LongIndex >= Source.Length Then Exit For
            Dim Offset As Integer = CInt(BitPos Mod 64)
            Dim Value As Long
            If Offset + BitWidth <= 64 Then
                Value = Source(LongIndex) >> Offset
            ElseIf LongIndex + 1 < Source.Length Then
                'VB 的 >> 会保留符号，所以低位部分要单独抹掉高位，再拼上下一个 Long 的低几位
                Dim Keep = 64 - Offset
                Dim Rest = BitWidth - Keep
                Value = ((Source(LongIndex) >> Offset) And ((1L << Keep) - 1)) Or ((Source(LongIndex + 1) And ((1L << Rest) - 1)) << Keep)
            Else
                Exit For
            End If
            Result(i) = CInt(Value And Mask)
        Next
        Return Result
    End Function

#End Region

#Region "解析"

    Private ReadOnly SchematicAir As New HashSet(Of String) From {
        "minecraft:air", "minecraft:cave_air", "minecraft:void_air", "minecraft:structure_void"}

    ''' <summary>读取原理图文件，自动识别格式。</summary>
    Public Function ReadSchematic(Path As String) As SchematicModel
        Dim Root = ReadNbt(File.ReadAllBytes(Path))
        Dim Result As SchematicModel
        Dim Region = FirstRegionNode(Root.Find("Regions"))
        If Region IsNot Nothing AndAlso (Region.Find("BlockData") IsNot Nothing OrElse Region.Find("BlockStates") IsNot Nothing OrElse Region.Find("BlockStatePalette") IsNot Nothing) Then
            Result = ParseLitematic(Root)
        ElseIf Root.Find("Palette") IsNot Nothing OrElse Region IsNot Nothing Then
            Result = ParseSponge(Root) 'v2 把 Palette 放在根节点，v3 放进 Regions
        Else
            Result = ParseAlpha(Root)
        End If
        Result.FileName = System.IO.Path.GetFileName(Path)
        Return Result
    End Function

    Private Sub CheckSize(Model As SchematicModel)
        If Model.Width <= 0 OrElse Model.Height <= 0 OrElse Model.Depth <= 0 Then
            Throw New Exception($"原理图尺寸异常（{Model.Width} × {Model.Height} × {Model.Depth}）。")
        End If
        If Model.Volume() > MaxSchematicVolume Then
            Throw New Exception($"原理图过大（{Model.Volume()} 个方块），最多支持 {MaxSchematicVolume} 个。")
        End If
        Model.States = New String(Model.Volume() - 1) {}
    End Sub

    ''' <summary>新版 v3 与 Litematica 都把尺寸或方块放进 Regions，这里统一取第一个区域。</summary>
    Private Function FirstRegionNode(Node As NbtNode) As NbtNode
        If Node Is Nothing Then Return Nothing
        Dim List = Node.Items
        If List IsNot Nothing AndAlso List.Count > 0 Then Return List(0)
        Dim Map = Node.Children
        If Map IsNot Nothing AndAlso Map.Count > 0 Then Return Map.OrderBy(Function(kv) Val(kv.Key)).First().Value
        Return Nothing
    End Function

    Private Function FirstItemNode(Node As NbtNode) As NbtNode
        Dim List = If(Node Is Nothing, Nothing, Node.Items)
        If List IsNot Nothing AndAlso List.Count > 0 Then Return List(0)
        Return Nothing
    End Function

    ''' <summary>读取形如 [X, Y, Z] 的三个数值，标签可能是 List、IntArray，也可能是 Compound{x, y, z}。</summary>
    Private Function Triples(Node As NbtNode) As Integer()
        If Node Is Nothing Then Return Nothing
        Dim List = Node.Items
        If List IsNot Nothing Then
            If List.Count < 3 Then Return Nothing
            Return {Math.Abs(PaletteInt(List(0))), Math.Abs(PaletteInt(List(1))), Math.Abs(PaletteInt(List(2)))}
        End If
        Dim Ints = TryCast(Node.Payload, Integer())
        If Ints IsNot Nothing AndAlso Ints.Length >= 3 Then Return {Math.Abs(Ints(0)), Math.Abs(Ints(1)), Math.Abs(Ints(2))}
        Dim Map = Node.Children
        If Map IsNot Nothing Then
            Dim AX = AxisOf(Map, "x"), AY = AxisOf(Map, "y"), AZ = AxisOf(Map, "z")
            If AX >= 0 AndAlso AY >= 0 AndAlso AZ >= 0 Then Return {AX, AY, AZ}
        End If
        Return Nothing
    End Function

    ''' <summary>Litematica 新版把尺寸与位置写成 Compound{x, y, z}，键名大小写不一律，两种都认，负数按绝对值取尺寸。</summary>
    Private Function AxisOf(Map As Dictionary(Of String, NbtNode), Axis As String) As Integer
        For Each Key In {Axis, Axis.ToUpper()}
            Dim Node As NbtNode = Nothing
            If Map.TryGetValue(Key, Node) AndAlso Node IsNot Nothing AndAlso Not (TypeOf Node.Payload Is String) Then
                Try
                    Return Math.Abs(CInt(CLng(Node.Payload)))
                Catch ex As InvalidCastException
                Catch ex As OverflowException
                End Try
            End If
        Next
        Return -1
    End Function

    ''' <summary>旧版 .schematic（MCEdit / Alpha）：Blocks 是数字 ID，Data 是低 4 位的数据值，Y 变化最快。</summary>
    Private Function ParseAlpha(Root As NbtNode) As SchematicModel
        Dim Blocks = NbtByteView(Root.Find("Blocks"))
        If Blocks Is Nothing Then Throw New Exception("这是一个旧版 .schematic，但没有找到 Blocks 标签。")
        Dim Data = NbtByteView(Root.Find("Data"))
        Dim Model As New SchematicModel With {
            .Format = "旧版 .schematic（Alpha）",
            .Width = Root.IntOf("Width"),
            .Height = Root.IntOf("Height"),
            .Depth = Root.IntOf("Length")}
        CheckSize(Model)
        For X = 0 To Model.Width - 1
            For Z = 0 To Model.Depth - 1
                Dim Native = (X * Model.Depth + Z) * Model.Height
                For Y = 0 To Model.Height - 1
                    Dim i = Native + Y
                    If i >= Blocks.Length Then Continue For
                    Dim Meta = If(Data IsNot Nothing AndAlso i < Data.Length, Data(i) And &HF, 0)
                    Dim State = LegacyState(Blocks(i) And &HFF, Meta)
                    If State IsNot Nothing Then Model.States((Y * Model.Depth + Z) * Model.Width + X) = State
                Next
            Next
        Next
        Return Model
    End Function

    ''' <summary>新版 .schem（Sponge v2/v3）：Blocks 是指向 Palette 的 varint 流。</summary>
    Private Function ParseSponge(Root As NbtNode) As SchematicModel
        Dim Version = Root.IntOf("Version", 3)
        Dim Model As New SchematicModel With {
            .Format = "新版 .schem（Sponge v" & Version & "）",
            .Width = Root.IntOf("Width"),
            .Height = Root.IntOf("Height"),
            .Depth = Root.IntOf("Length")}
        Dim BlocksNode = Root.Find("Blocks")
        Dim PaletteNode = Root.Find("Palette")
        If BlocksNode Is Nothing OrElse PaletteNode Is Nothing OrElse Model.Width <= 0 OrElse Model.Height <= 0 OrElse Model.Depth <= 0 Then
            'v3 把尺寸与方块数据放在 Regions 里
            Dim Region = FirstRegionNode(Root.Find("Regions"))
            If Region Is Nothing Then Throw New Exception("新版原理图缺少 Blocks 与 Palette 标签。")
            Dim Size = Triples(Region.Find("Size"))
            If Size Is Nothing Then Throw New Exception("新版原理图缺少区域尺寸。")
            Model.Width = Size(0)
            Model.Height = Size(1)
            Model.Depth = Size(2)
            BlocksNode = Region.Find("Blocks")
            PaletteNode = Region.Find("Palette")
            If BlocksNode Is Nothing OrElse PaletteNode Is Nothing Then
                Dim Tile = FirstItemNode(Region.Find("Tiles"))
                If Tile IsNot Nothing Then
                    BlocksNode = Tile.Find("Blocks")
                    PaletteNode = Tile.Find("Palette")
                End If
            End If
        End If
        If BlocksNode Is Nothing OrElse PaletteNode Is Nothing Then Throw New Exception("新版原理图缺少 Blocks 与 Palette 标签。")
        CheckSize(Model)
        Dim View = NbtByteView(BlocksNode)
        If View Is Nothing Then Throw New Exception("新版原理图缺少 Blocks 标签。")
        FillByIndices(Model, DecodeVarints(View, Model.Volume()), PaletteNames(PaletteNode))
        Return Model
    End Function

    ''' <summary>Litematica 的 .litematic：方块数据存放在 Regions 中，且使用位压缩而非 varint。</summary>
    Private Function ParseLitematic(Root As NbtNode) As SchematicModel
        Dim Region = FirstRegionNode(Root.Find("Regions"))
        If Region Is Nothing Then Throw New Exception("Litematica 原理图中没有可用的区域。")
        Dim Size = Triples(Region.Find("Size"))
        If Size Is Nothing Then Throw New Exception("Litematica 原理图缺少区域尺寸。")
        Dim Model As New SchematicModel With {
            .Format = "Litematica .litematic（v" & Root.IntOf("Version", 0) & "）",
            .Width = Size(0),
            .Height = Size(1),
            .Depth = Size(2)}
        CheckSize(Model)
        Dim Names = LitematicaPalette(Region)
        Dim Data = NbtLongs(If(Region.Find("BlockData"), Region.Find("BlockStates")))
        If Data Is Nothing Then Throw New Exception("Litematica 原理图缺少 BlockData / BlockStates 标签。")
        FillByIndices(Model, UnpackLitematic(Data, Names.Count, Model.Volume()), Names)
        Return Model
    End Function

    ''' <summary>老版把调色板写成 Palette{状态名: ID}，1.20.5 之后写成 BlockStatePalette[{Name, Properties}]，按列表顺序取用。</summary>
    Private Function LitematicaPalette(Region As NbtNode) As List(Of String)
        Dim Old = Region.Find("Palette")
        If Old IsNot Nothing AndAlso Old.Children IsNot Nothing AndAlso Old.Children.Count > 0 Then Return PaletteNames(Old)
        Dim NewListNode = Region.Find("BlockStatePalette")
        Dim Items = If(NewListNode Is Nothing, Nothing, NewListNode.Items)
        If Items Is Nothing OrElse Items.Count = 0 Then Throw New Exception("Litematica 原理图缺少调色板。")
        Dim Result As New List(Of String)
        For Each Item In Items
            Dim NameNode = Item.Find("Name")
            Dim Entry = If(NameNode Is Nothing, Nothing, TryCast(NameNode.Payload, String))
            If String.IsNullOrEmpty(Entry) Then Entry = "minecraft:stone"
            Dim PropNode = Item.Find("Properties")
            Dim Props = If(PropNode Is Nothing, Nothing, PropNode.Children)
            If Props IsNot Nothing AndAlso Props.Count > 0 Then
                Dim Parts = Props.OrderBy(Function(kv) kv.Key).Select(Function(kv) kv.Key & "=" & If(TryCast(kv.Value.Payload, String), "")).ToList()
                Entry &= "[" & String.Join(",", Parts) & "]"
            End If
            Result.Add(Entry)
        Next
        Return Result
    End Function

    ''' <summary>
    ''' 还原 Litematica 的位压缩索引：每个索引占几位要先按调色板大小取下限，
    ''' 再用实际 Long 数量和「索引必须落在调色板内」两条约束试出来，因为不同版本会各自强制最小位宽。
    ''' </summary>
    Private Function UnpackLitematic(Data As Long(), PaletteCount As Integer, Volume As Integer) As Integer()
        Dim Minimum = 1
        While (1L << Minimum) - 1 < PaletteCount - 1 AndAlso Minimum < 32
            Minimum += 1
        End While
        Dim Best As Integer() = Nothing
        For BitWidth = Minimum To 32
            If ((CLng(Volume) * BitWidth + 63) \ 64) <> Data.Length Then Continue For
            Dim Indices = UnpackBits(Data, BitWidth, Volume)
            If Indices.All(Function(v) v >= 0 AndAlso v < PaletteCount) Then Return Indices
            If Best Is Nothing Then Best = Indices
        Next
        Return If(Best, UnpackBits(Data, Minimum, Volume))
    End Function

    ''' <summary>Palette 的键是方块状态名，值就是 Blocks 中记录的 ID，按 ID 还原成下标数组。</summary>
    Private Function PaletteNames(Node As NbtNode) As List(Of String)
        Dim Map = Node.Children
        If Map Is Nothing OrElse Map.Count = 0 Then Throw New Exception("原理图的 Palette 为空。")
        Dim Ids = Map.Select(Function(kv) PaletteInt(kv.Value)).ToList()
        If Ids.Any(Function(v) v < 0) OrElse Ids.Distinct().Count <> Ids.Count OrElse Ids.Max() > 1000000 Then
            Return Map.Keys.ToList() 'ID 不正常时按文件出现顺序，至少不会崩溃
        End If
        Dim Result(Ids.Max()) As String
        For Each Pair In Map
            Result(PaletteInt(Pair.Value)) = Pair.Key
        Next
        Return Result.ToList()
    End Function

    Private Function PaletteInt(Node As NbtNode) As Integer
        If Node Is Nothing OrElse TypeOf Node.Payload Is String Then Return -1
        Try
            Return CInt(CLng(Node.Payload))
        Catch
            Return -1
        End Try
    End Function

    Private Sub FillByIndices(Model As SchematicModel, Indices As Integer(), Names As List(Of String))
        For i = 0 To Indices.Length - 1
            If i >= Model.States.Length Then Exit For
            Dim Index = Indices(i)
            If Index < 0 OrElse Index >= Names.Count Then Continue For
            Dim Name = Names(Index)
            If Name Is Nothing Then Continue For
            Dim Split = Name.IndexOf("["c)
            If SchematicAir.Contains(If(Split >= 0, Name.Substring(0, Split), Name).ToLower) Then Continue For
            Model.States(i) = Name
        Next
    End Sub

#End Region

#Region "方块清单"

    ''' <summary>一种方块状态的用量统计。FirstIndex 是它第一次出现的下标，供搜索定位使用。</summary>
    Public Class SchematicStateInfo
        Public State As String
        Public Count As Integer
        Public FirstIndex As Integer
    End Class

    Public Function CountSchematicStates(Model As SchematicModel) As List(Of SchematicStateInfo)
        Dim Result As New List(Of SchematicStateInfo)
        If Model Is Nothing OrElse Model.States Is Nothing Then Return Result
        Dim Map As New Dictionary(Of String, SchematicStateInfo)
        For i = 0 To Model.States.Length - 1
            Dim State = Model.States(i)
            If State Is Nothing Then Continue For
            Dim Info As SchematicStateInfo = Nothing
            If Map.TryGetValue(State, Info) Then
                Info.Count += 1
            Else
                Info = New SchematicStateInfo With {.State = State, .Count = 1, .FirstIndex = i}
                Map(State) = Info
                Result.Add(Info)
            End If
        Next
        Return Result.OrderBy(Function(v) -v.Count).ThenBy(Function(v) v.State, StringComparer.Ordinal).ToList()
    End Function

    ''' <summary>把方块清单拼成可导出的文本。方块状态名里本身带逗号，所以 CSV 必须整格加引号。</summary>
    Public Function BuildSchematicListText(Model As SchematicModel, Infos As List(Of SchematicStateInfo), Csv As Boolean) As String
        Dim Builder As New StringBuilder
        If Csv Then
            Builder.AppendLine("方块,数量")
            For Each Info In Infos
                Builder.AppendLine(CsvCell(Info.State, True) & "," & Info.Count)
            Next
        Else
            Builder.AppendLine(Model.FileName)
            Builder.AppendLine("格式：" & Model.Format)
            Builder.AppendLine($"尺寸：{Model.Width} × {Model.Height} × {Model.Depth}")
            Builder.AppendLine($"方块总数：{Model.SolidCount()}　种类：{Infos.Count}")
            Builder.AppendLine()
            For Each Info In Infos
                Builder.AppendLine(Info.State & vbTab & Info.Count)
            Next
        End If
        Return Builder.ToString
    End Function

    Private Function CsvCell(Text As String, Force As Boolean) As String
        If Not Force AndAlso Not (Text.Contains(","c) OrElse Text.Contains(""""c) OrElse Text.Contains(vbLf)) Then Return Text
        Return """" & Text.Replace("""", """""") & """"
    End Function

#End Region

#Region "方块名与颜色"

    Private ReadOnly SchematicWoods As String() = {"oak", "spruce", "birch", "jungle", "acacia", "dark_oak"}
    Private ReadOnly SchematicDyes As String() = {"white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"}

    ''' <summary>把旧版的数字 ID + 数据值尽量拼成现代名字，以便复用同一套配色。</summary>
    Private Function LegacyState(Id As Integer, Meta As Integer) As String
        If Id = 0 Then Return Nothing
        Dim Wood = Function(v As Integer) As String
                       Return SchematicWoods(v Mod 6)
                   End Function
        Dim Dye = Function(v As Integer) As String
                      Return SchematicDyes(v Mod 16)
                  End Function
        Select Case Id
            Case 1 : Return "minecraft:stone"
            Case 2 : Return "minecraft:grass_block"
            Case 3 : Return "minecraft:dirt"
            Case 4 : Return "minecraft:cobblestone"
            Case 5 : Return "minecraft:" & Wood(Meta) & "_planks"
            Case 6 : Return "minecraft:" & Wood(Meta) & "_sapling"
            Case 7 : Return "minecraft:bedrock"
            Case 8, 9 : Return "minecraft:water"
            Case 10, 11 : Return "minecraft:lava"
            Case 12 : Return "minecraft:sand"
            Case 13 : Return "minecraft:gravel"
            Case 14 : Return "minecraft:gold_ore"
            Case 15 : Return "minecraft:iron_ore"
            Case 16 : Return "minecraft:coal_ore"
            Case 17 : Return "minecraft:" & Wood(Meta And 3) & "_log"
            Case 18 : Return "minecraft:" & Wood(Meta And 3) & "_leaves"
            Case 19 : Return "minecraft:sponge"
            Case 20 : Return "minecraft:glass"
            Case 21 : Return "minecraft:lapis_ore"
            Case 22 : Return "minecraft:lapis_block"
            Case 23 : Return "minecraft:dispenser"
            Case 24 : Return "minecraft:sandstone"
            Case 25 : Return "minecraft:note_block"
            Case 26 : Return "minecraft:red_bed"
            Case 27 : Return "minecraft:powered_rail"
            Case 28 : Return "minecraft:detector_rail"
            Case 29 : Return "minecraft:sticky_piston"
            Case 30 : Return "minecraft:cobweb"
            Case 31 : Return "minecraft:short_grass"
            Case 32 : Return "minecraft:dead_bush"
            Case 33 : Return "minecraft:piston"
            Case 34 : Return "minecraft:piston_head"
            Case 35 : Return "minecraft:" & Dye(Meta And &HF) & "_wool"
            Case 37 : Return "minecraft:dandelion"
            Case 38 : Return "minecraft:poppy"
            Case 39 : Return "minecraft:brown_mushroom"
            Case 40 : Return "minecraft:red_mushroom"
            Case 41 : Return "minecraft:gold_block"
            Case 42 : Return "minecraft:iron_block"
            Case 43, 44 : Return "minecraft:stone"
            Case 45 : Return "minecraft:bricks"
            Case 46 : Return "minecraft:tnt"
            Case 47 : Return "minecraft:bookshelf"
            Case 48 : Return "minecraft:mossy_cobblestone"
            Case 49 : Return "minecraft:obsidian"
            Case 50 : Return "minecraft:torch"
            Case 51 : Return "minecraft:fire"
            Case 52 : Return "minecraft:spawner"
            Case 53 : Return "minecraft:oak_stairs"
            Case 54 : Return "minecraft:chest"
            Case 55 : Return "minecraft:redstone_wire"
            Case 56 : Return "minecraft:diamond_ore"
            Case 57 : Return "minecraft:diamond_block"
            Case 58 : Return "minecraft:crafting_table"
            Case 59 : Return "minecraft:wheat"
            Case 60 : Return "minecraft:farmland"
            Case 61, 62 : Return "minecraft:furnace"
            Case 63 : Return "minecraft:oak_sign"
            Case 64 : Return "minecraft:oak_door"
            Case 65 : Return "minecraft:ladder"
            Case 66 : Return "minecraft:rail"
            Case 67 : Return "minecraft:cobblestone_wall"
            Case 68 : Return "minecraft:lever"
            Case 69 : Return "minecraft:stone_pressure_plate"
            Case 70 : Return "minecraft:oak_pressure_plate"
            Case 71 : Return "minecraft:stone_button"
            Case 72 : Return "minecraft:snow"
            Case 73, 74 : Return "minecraft:redstone_ore"
            Case 79 : Return "minecraft:ice"
            Case 81 : Return "minecraft:clay"
            Case 82 : Return "minecraft:sugar_cane"
            Case 85 : Return "minecraft:jukebox"
            Case 86 : Return "minecraft:oak_fence"
            Case 87 : Return "minecraft:pumpkin"
            Case 88 : Return "minecraft:netherrack"
            Case 89 : Return "minecraft:soul_sand"
            Case 90 : Return "minecraft:glowstone"
            Case 91 : Return "minecraft:nether_portal"
            Case 92 : Return "minecraft:jack_o_lantern"
            Case 93, 94 : Return "minecraft:repeater"
            Case 95 : Return "minecraft:" & Dye(Meta And &HF) & "_stained_glass"
            Case 96 : Return "minecraft:oak_trapdoor"
            Case 98 : Return "minecraft:stone_bricks"
            Case 103 : Return "minecraft:melon"
            Case 106 : Return "minecraft:vine"
            Case 107 : Return "minecraft:oak_fence_gate"
            Case 110 : Return "minecraft:mycelium"
            Case 111 : Return "minecraft:waterlily"
            Case 112 : Return "minecraft:nether_bricks"
            Case 113 : Return "minecraft:nether_brick_fence"
            Case 114 : Return "minecraft:nether_brick_stairs"
            Case 116 : Return "minecraft:enchanting_table"
            Case 121 : Return "minecraft:end_stone"
            Case 122 : Return "minecraft:dragon_egg"
            Case 123, 124 : Return "minecraft:redstone_lamp"
            Case 129 : Return "minecraft:emerald_ore"
            Case 130 : Return "minecraft:ender_chest"
            Case 133 : Return "minecraft:emerald_block"
            Case 134 : Return "minecraft:spruce_stairs"
            Case 135 : Return "minecraft:birch_stairs"
            Case 136 : Return "minecraft:jungle_stairs"
            Case 137 : Return "minecraft:command_block"
            Case 138 : Return "minecraft:beacon"
            Case 155 : Return "minecraft:quartz_block"
            Case 156 : Return "minecraft:quartz_stairs"
            Case 159 : Return "minecraft:" & Dye(Meta And &HF) & "_terracotta"
            Case 160 : Return "minecraft:glass_pane"
            Case 162 : Return "minecraft:acacia_log"
            Case 163 : Return "minecraft:acacia_stairs"
            Case 164 : Return "minecraft:dark_oak_stairs"
            Case 165 : Return "minecraft:slime_block"
            Case 166 : Return "minecraft:barrier"
            Case 173 : Return "minecraft:coal_block"
            Case 174 : Return "minecraft:packed_ice"
            Case 251 : Return "minecraft:" & Dye(Meta And &HF) & "_concrete"
            Case 252 : Return "minecraft:" & Dye(Meta And &HF) & "_concrete_powder"
            Case 255 : Return "minecraft:structure_block"
            Case Else : Return "minecraft:unknown_" & Id
        End Select
    End Function

    Private ReadOnly SchematicColorCache As New Dictionary(Of String, Integer)
    Private ReadOnly SchematicColorLock As New Object

    ''' <summary>取方块状态颜色（ARGB）。找不到准确配色时按材质、染料、哈希依次回退。</summary>
    Public Function GetStateColor(State As String) As Integer
        SyncLock SchematicColorLock
            Dim Cached As Integer
            If SchematicColorCache.TryGetValue(State, Cached) Then Return Cached
            Dim Result = ComputeStateColor(State)
            SchematicColorCache(State) = Result
            Return Result
        End SyncLock
    End Function

    Private Function ColorOf(Name As String) As Integer?
        Dim Value As Integer
        If SchematicColors.TryGetValue(Name, Value) Then Return Value
        Return Nothing
    End Function

    Private Function ComputeStateColor(State As String) As Integer
        Dim Lower = State.ToLower
        Dim Split = Lower.IndexOf("["c)
        Dim Name = If(Split >= 0, Lower.Substring(0, Split), Lower)
        Dim Properties = If(Split >= 0, Lower.Substring(Split + 1).TrimEnd("]"c), "")
        If SchematicAir.Contains(Name) Then Return 0
        '1. 状态属性中的颜色优先，例如 minecraft:carpet[color=red]
        Dim Dye = DyeOfProperty(Properties)
        If Dye IsNot Nothing Then Return DyeColor(Dye)
        '2. 精确配色
        Dim Direct = ColorOf(Name)
        If Direct IsNot Nothing Then Return Direct.Value
        '3. 去掉 mossy / polished 等修饰前缀后再试
        For Each Prefix In {"mossy_", "cracked_", "chiseled_", "cut_", "smooth_", "polished_", "infested_", "lit_", "glowing_"}
            If Name.StartsWithF("minecraft:" & Prefix) Then
                Direct = ColorOf("minecraft:" & Name.Substring(10 + Prefix.Length))
                If Direct IsNot Nothing Then Return Direct.Value
            End If
        Next
        '4. 楼梯、台阶、栅栏之类回退到同材质的板材
        For Each Suffix In {"_stairs", "_slab", "_fence", "_fence_gate", "_door", "_trapdoor", "_pressure_plate", "_button", "_sign", "_wall", "_carpet", "_sapling", "_hanging_sign"}
            If Name.EndsWithF(Suffix) Then
                Dim Base = Name.Substring(0, Name.Length - Suffix.Length)
                For Each Candidate In {Base & "_planks", Base, Base & "_wool", Base & "_log"}
                    Direct = ColorOf(Candidate)
                    If Direct IsNot Nothing Then Return Direct.Value
                Next
                Exit For
            End If
        Next
        '5. 名字里出现的染料词，例如 minecraft:light_blue_concrete
        Dye = DyeOfToken(Name)
        If Dye IsNot Nothing Then Return DyeColor(Dye)
        '6. 最后按名字哈希出稳定颜色，保证同类方块至少看起来一致
        Dim Hash = Name.GetHashCode() And &H7FFFFFFF
        Return Rgb(70 + Hash Mod 130, 70 + (Hash \ 131) Mod 130, 70 + (Hash \ 17161) Mod 130, AlphaOf(State))
    End Function

    ''' <summary>半透明方块的透明度，其余为不透明。</summary>
    Private Function AlphaOf(State As String) As Integer
        Dim Lower = State.ToLower
        If Lower.Contains("glass_pane") OrElse Lower.Contains("barrier") Then Return 90
        If Lower.Contains("glass") OrElse Lower.Contains("ice") Then Return 120
        If Lower.Contains("water") OrElse Lower.Contains("portal") Then Return 160
        If Lower.Contains("slime") OrElse Lower.Contains("honey") OrElse Lower.Contains("crystal") Then Return 180
        Return 255
    End Function

    Private Function DyeOfProperty(Properties As String) As String
        For Each Piece In Properties.Split(","c)
            Dim Pair = Piece.Trim().Split("="c)
            If Pair.Length = 2 AndAlso Pair(0) = "color" Then
                Dim Value = Pair(1).Trim("""", " "c)
                If Value.Contains("_") Then
                    If Value = "light_blue" OrElse Value = "light_gray" Then Return Value
                    Value = Value.Replace("silver", "light_gray")
                End If
                Return Value
            End If
        Next
        Return Nothing
    End Function

    Private Function DyeOfToken(Name As String) As String
        Dim Tokens = Name.Replace("minecraft:", "").Split("_"c).ToList()
        For i = 0 To Tokens.Count - 1
            If Tokens(i) = "light" AndAlso i + 1 < Tokens.Count AndAlso (Tokens(i + 1) = "blue" OrElse Tokens(i + 1) = "gray") Then
                Return "light_" & Tokens(i + 1)
            End If
            Dim Index = Array.IndexOf(SchematicDyes, Tokens(i))
            If Index >= 0 Then Return SchematicDyes(Index)
        Next
        Return Nothing
    End Function

    Private Function DyeColor(Name As String) As Integer
        Select Case Name
            Case "white" : Return Rgb(235, 235, 235)
            Case "orange" : Return Rgb(240, 118, 20)
            Case "magenta" : Return Rgb(189, 68, 179)
            Case "light_blue" : Return Rgb(58, 175, 216)
            Case "yellow" : Return Rgb(248, 197, 39)
            Case "lime" : Return Rgb(112, 174, 59)
            Case "pink" : Return Rgb(190, 102, 115)
            Case "gray" : Return Rgb(64, 64, 64)
            Case "light_gray" : Return Rgb(154, 161, 161)
            Case "cyan" : Return Rgb(71, 107, 136)
            Case "purple" : Return Rgb(125, 59, 182)
            Case "blue" : Return Rgb(47, 69, 191)
            Case "brown" : Return Rgb(132, 77, 25)
            Case "green" : Return Rgb(94, 128, 3)
            Case "red" : Return Rgb(161, 38, 37)
            Case "black" : Return Rgb(30, 30, 34)
            Case Else : Return Rgb(180, 180, 180)
        End Select
    End Function

    Private Function Rgb(R As Integer, G As Integer, B As Integer, Optional A As Integer = 255) As Integer
        Return (A << 24) Or (R << 16) Or (G << 8) Or B
    End Function

    ''' <summary>常见方块配色，未列出的方块走材质回退或稳定哈希色。</summary>
    Private ReadOnly SchematicColors As New Dictionary(Of String, Integer) From {
        {"minecraft:stone", Rgb(125, 125, 125)},
        {"minecraft:granite", Rgb(157, 99, 93)},
        {"minecraft:diorite", Rgb(188, 188, 195)},
        {"minecraft:andesite", Rgb(137, 137, 137)},
        {"minecraft:tuff", Rgb(108, 109, 102)},
        {"minecraft:calcite", Rgb(223, 224, 220)},
        {"minecraft:deepslate", Rgb(74, 74, 76)},
        {"minecraft:cobbled_deepslate", Rgb(78, 78, 82)},
        {"minecraft:deepslate_bricks", Rgb(70, 70, 74)},
        {"minecraft:reinforced_deepslate", Rgb(68, 68, 72)},
        {"minecraft:grass_block", Rgb(116, 160, 82)},
        {"minecraft:dirt", Rgb(134, 96, 67)},
        {"minecraft:coarse_dirt", Rgb(118, 84, 55)},
        {"minecraft:rooted_dirt", Rgb(120, 86, 60)},
        {"minecraft:podzol", Rgb(101, 72, 30)},
        {"minecraft:mycelium", Rgb(110, 98, 109)},
        {"minecraft:moss_block", Rgb(90, 110, 50)},
        {"minecraft:mud", Rgb(60, 68, 71)},
        {"minecraft:farmland", Rgb(96, 62, 40)},
        {"minecraft:sand", Rgb(219, 216, 157)},
        {"minecraft:red_sand", Rgb(190, 102, 33)},
        {"minecraft:suspicious_sand", Rgb(214, 209, 151)},
        {"minecraft:gravel", Rgb(136, 126, 125)},
        {"minecraft:cobblestone", Rgb(127, 127, 127)},
        {"minecraft:mossy_cobblestone", Rgb(110, 118, 99)},
        {"minecraft:bedrock", Rgb(85, 85, 85)},
        {"minecraft:sandstone", Rgb(218, 210, 158)},
        {"minecraft:red_sandstone", Rgb(181, 80, 37)},
        {"minecraft:bricks", Rgb(150, 92, 75)},
        {"minecraft:stone_bricks", Rgb(122, 121, 119)},
        {"minecraft:mossy_stone_bricks", Rgb(110, 120, 100)},
        {"minecraft:bookshelf", Rgb(140, 110, 70)},
        {"minecraft:chest", Rgb(134, 96, 30)},
        {"minecraft:ender_chest", Rgb(40, 70, 70)},
        {"minecraft:barrel", Rgb(110, 84, 50)},
        {"minecraft:crafting_table", Rgb(120, 100, 70)},
        {"minecraft:smithing_table", Rgb(80, 60, 60)},
        {"minecraft:fletching_table", Rgb(150, 130, 90)},
        {"minecraft:cartography_table", Rgb(90, 70, 60)},
        {"minecraft:furnace", Rgb(70, 70, 70)},
        {"minecraft:blast_furnace", Rgb(60, 58, 60)},
        {"minecraft:smoker", Rgb(72, 62, 52)},
        {"minecraft:obsidian", Rgb(18, 12, 26)},
        {"minecraft:crying_obsidian", Rgb(42, 20, 54)},
        {"minecraft:tnt", Rgb(200, 60, 50)},
        {"minecraft:netherrack", Rgb(110, 54, 54)},
        {"minecraft:soul_sand", Rgb(81, 62, 50)},
        {"minecraft:soul_soil", Rgb(74, 56, 46)},
        {"minecraft:nether_bricks", Rgb(44, 22, 33)},
        {"minecraft:red_nether_bricks", Rgb(70, 20, 22)},
        {"minecraft:nether_wart_block", Rgb(90, 20, 25)},
        {"minecraft:warped_wart_block", Rgb(20, 80, 75)},
        {"minecraft:glowstone", Rgb(250, 210, 120)},
        {"minecraft:shroomlight", Rgb(240, 146, 58)},
        {"minecraft:sea_lantern", Rgb(160, 230, 220)},
        {"minecraft:ochre_froglight", Rgb(240, 230, 170)},
        {"minecraft:end_stone", Rgb(221, 223, 146)},
        {"minecraft:end_stone_bricks", Rgb(205, 206, 133)},
        {"minecraft:purpur_block", Rgb(169, 125, 169)},
        {"minecraft:prismarine", Rgb(100, 160, 145)},
        {"minecraft:dark_prismarine", Rgb(50, 95, 86)},
        {"minecraft:terracotta", Rgb(152, 94, 66)},
        {"minecraft:clay", Rgb(162, 162, 172)},
        {"minecraft:pumpkin", Rgb(212, 152, 27)},
        {"minecraft:carved_pumpkin", Rgb(212, 152, 27)},
        {"minecraft:jack_o_lantern", Rgb(230, 165, 25)},
        {"minecraft:melon", Rgb(110, 160, 60)},
        {"minecraft:sugar_cane", Rgb(110, 180, 90)},
        {"minecraft:cactus", Rgb(80, 140, 60)},
        {"minecraft:vine", Rgb(80, 120, 40)},
        {"minecraft:glow_lichen", Rgb(70, 130, 110)},
        {"minecraft:short_grass", Rgb(100, 150, 70)},
        {"minecraft:grass", Rgb(100, 150, 70)},
        {"minecraft:fern", Rgb(90, 140, 60)},
        {"minecraft:dead_bush", Rgb(140, 110, 60)},
        {"minecraft:wheat", Rgb(150, 190, 80)},
        {"minecraft:torch", Rgb(230, 190, 100)},
        {"minecraft:soul_torch", Rgb(120, 210, 200)},
        {"minecraft:lantern", Rgb(240, 200, 90)},
        {"minecraft:soul_lantern", Rgb(140, 210, 200)},
        {"minecraft:fire", Rgb(240, 120, 30)},
        {"minecraft:soul_fire", Rgb(90, 200, 190)},
        {"minecraft:redstone_wire", Rgb(150, 40, 40)},
        {"minecraft:redstone_lamp", Rgb(230, 190, 120)},
        {"minecraft:redstone_block", Rgb(180, 30, 30)},
        {"minecraft:repeater", Rgb(170, 60, 60)},
        {"minecraft:comparator", Rgb(150, 90, 90)},
        {"minecraft:observer", Rgb(110, 110, 115)},
        {"minecraft:hopper", Rgb(80, 80, 86)},
        {"minecraft:ladder", Rgb(150, 120, 70)},
        {"minecraft:rail", Rgb(130, 110, 90)},
        {"minecraft:powered_rail", Rgb(200, 170, 60)},
        {"minecraft:detector_rail", Rgb(140, 130, 120)},
        {"minecraft:activator_rail", Rgb(110, 130, 110)},
        {"minecraft:lever", Rgb(120, 100, 70)},
        {"minecraft:iron_bars", Rgb(100, 100, 105)},
        {"minecraft:chain", Rgb(60, 60, 64)},
        {"minecraft:anvil", Rgb(62, 66, 71)},
        {"minecraft:enchanting_table", Rgb(60, 50, 100)},
        {"minecraft:brewing_stand", Rgb(140, 120, 60)},
        {"minecraft:cauldron", Rgb(45, 45, 48)},
        {"minecraft:spawner", Rgb(45, 60, 62)},
        {"minecraft:command_block", Rgb(120, 80, 140)},
        {"minecraft:structure_block", Rgb(120, 120, 130)},
        {"minecraft:jigsaw", Rgb(150, 130, 180)},
        {"minecraft:beacon", Rgb(160, 230, 230, 150)},
        {"minecraft:end_portal_frame", Rgb(60, 80, 70)},
        {"minecraft:dragon_egg", Rgb(40, 20, 60)},
        {"minecraft:end_gateway", Rgb(20, 60, 40, 200)},
        {"minecraft:coal_ore", Rgb(110, 110, 112)},
        {"minecraft:iron_ore", Rgb(142, 130, 122)},
        {"minecraft:copper_ore", Rgb(130, 122, 100)},
        {"minecraft:gold_ore", Rgb(145, 140, 100)},
        {"minecraft:redstone_ore", Rgb(140, 100, 100)},
        {"minecraft:lapis_ore", Rgb(110, 120, 160)},
        {"minecraft:emerald_ore", Rgb(110, 145, 120)},
        {"minecraft:diamond_ore", Rgb(110, 150, 160)},
        {"minecraft:nether_quartz_ore", Rgb(120, 70, 70)},
        {"minecraft:ancient_debris", Rgb(66, 52, 52)},
        {"minecraft:coal_block", Rgb(22, 22, 24)},
        {"minecraft:iron_block", Rgb(220, 220, 220)},
        {"minecraft:copper_block", Rgb(192, 105, 77)},
        {"minecraft:gold_block", Rgb(250, 215, 80)},
        {"minecraft:lapis_block", Rgb(30, 60, 150)},
        {"minecraft:emerald_block", Rgb(100, 220, 130)},
        {"minecraft:diamond_block", Rgb(160, 245, 240)},
        {"minecraft:netherite_block", Rgb(66, 61, 64)},
        {"minecraft:quartz_block", Rgb(235, 230, 225)},
        {"minecraft:hay_block", Rgb(200, 170, 60)},
        {"minecraft:packed_ice", Rgb(130, 175, 230)},
        {"minecraft:blue_ice", Rgb(110, 150, 220)},
        {"minecraft:snow", Rgb(240, 245, 250)},
        {"minecraft:snow_block", Rgb(240, 245, 250)},
        {"minecraft:powder_snow", Rgb(230, 240, 245, 200)},
        {"minecraft:ice", Rgb(160, 200, 240, 120)},
        {"minecraft:water", Rgb(60, 110, 190, 150)},
        {"minecraft:lava", Rgb(228, 118, 24)},
        {"minecraft:glass", Rgb(230, 240, 245, 90)},
        {"minecraft:slime_block", Rgb(110, 190, 110, 160)},
        {"minecraft:honey_block", Rgb(240, 180, 40, 190)},
        {"minecraft:barrier", Rgb(230, 120, 60, 90)},
        {"minecraft:amethyst_block", Rgb(133, 97, 193)},
        {"minecraft:budding_amethyst", Rgb(150, 110, 200)},
        {"minecraft:dripstone_block", Rgb(134, 106, 95)},
        {"minecraft:pointed_dripstone", Rgb(160, 130, 120, 220)},
        {"minecraft:sculk", Rgb(16, 30, 35)},
        {"minecraft:sculk_catalyst", Rgb(20, 45, 50)},
        {"minecraft:mushroom_stem", Rgb(200, 195, 180)},
        {"minecraft:brown_mushroom", Rgb(150, 130, 100)},
        {"minecraft:red_mushroom", Rgb(200, 80, 70)},
        {"minecraft:brain_coral_block", Rgb(200, 100, 120)},
        {"minecraft:tube_coral_block", Rgb(40, 80, 160)},
        {"minecraft:dandelion", Rgb(240, 220, 60)},
        {"minecraft:poppy", Rgb(200, 60, 50)},
        {"minecraft:waterlily", Rgb(80, 140, 70, 220)},
        {"minecraft:lily_pad", Rgb(80, 140, 70, 220)},
        {"minecraft:nether_portal", Rgb(140, 60, 190, 160)},
        {"minecraft:oak_planks", Rgb(162, 130, 78)},
        {"minecraft:spruce_planks", Rgb(117, 89, 65)},
        {"minecraft:birch_planks", Rgb(192, 175, 121)},
        {"minecraft:jungle_planks", Rgb(160, 115, 80)},
        {"minecraft:acacia_planks", Rgb(168, 86, 48)},
        {"minecraft:dark_oak_planks", Rgb(66, 43, 19)},
        {"minecraft:mangrove_planks", Rgb(118, 71, 71)},
        {"minecraft:cherry_planks", Rgb(201, 141, 141)},
        {"minecraft:bamboo_planks", Rgb(192, 174, 92)},
        {"minecraft:crimson_planks", Rgb(107, 54, 63)},
        {"minecraft:warped_planks", Rgb(43, 104, 99)},
        {"minecraft:oak_log", Rgb(109, 85, 54)},
        {"minecraft:birch_log", Rgb(220, 220, 210)},
        {"minecraft:spruce_log", Rgb(74, 52, 30)},
        {"minecraft:jungle_log", Rgb(82, 62, 40)},
        {"minecraft:acacia_log", Rgb(86, 63, 46)},
        {"minecraft:dark_oak_log", Rgb(58, 38, 18)},
        {"minecraft:oak_leaves", Rgb(60, 120, 40)},
        {"minecraft:spruce_leaves", Rgb(40, 90, 40)},
        {"minecraft:birch_leaves", Rgb(80, 140, 50)},
        {"minecraft:jungle_leaves", Rgb(50, 130, 30)},
        {"minecraft:azalea_leaves", Rgb(70, 130, 60)},
        {"minecraft:air", 0},
        {"minecraft:cave_air", 0},
        {"minecraft:void_air", 0},
        {"minecraft:structure_void", 0},
        {"minecraft:unknown_", Rgb(180, 180, 180)}
    }

#End Region

#Region "网格生成"

    Private Structure SchematicFace
        Public Offset As Integer()
        Public Normal As Integer()
        Public Corners As Integer()
    End Structure

    Private SchematicCornersCache As Double()() = Nothing
    Private SchematicFacesCache As SchematicFace() = Nothing

    Private Function SchematicCubeCorners() As Double()()
        If SchematicCornersCache Is Nothing Then
            SchematicCornersCache = New Double()() {
                New Double() {0, 0, 0}, New Double() {1, 0, 0}, New Double() {0, 1, 0}, New Double() {1, 1, 0},
                New Double() {0, 0, 1}, New Double() {1, 0, 1}, New Double() {0, 1, 1}, New Double() {1, 1, 1}}
        End If
        Return SchematicCornersCache
    End Function

    Private Function SchematicFaceDefs() As SchematicFace()
        If SchematicFacesCache Is Nothing Then
            Dim Defs(5) As SchematicFace
            Defs(0).Offset = New Integer() {-1, 0, 0}
            Defs(0).Normal = New Integer() {-1, 0, 0}
            Defs(0).Corners = New Integer() {0, 4, 6, 2}
            Defs(1).Offset = New Integer() {1, 0, 0}
            Defs(1).Normal = New Integer() {1, 0, 0}
            Defs(1).Corners = New Integer() {1, 3, 7, 5}
            Defs(2).Offset = New Integer() {0, -1, 0}
            Defs(2).Normal = New Integer() {0, -1, 0}
            Defs(2).Corners = New Integer() {0, 1, 5, 4}
            Defs(3).Offset = New Integer() {0, 1, 0}
            Defs(3).Normal = New Integer() {0, 1, 0}
            Defs(3).Corners = New Integer() {2, 3, 7, 6}
            Defs(4).Offset = New Integer() {0, 0, -1}
            Defs(4).Normal = New Integer() {0, 0, -1}
            Defs(4).Corners = New Integer() {0, 1, 3, 2}
            Defs(5).Offset = New Integer() {0, 0, 1}
            Defs(5).Normal = New Integer() {0, 0, 1}
            Defs(5).Corners = New Integer() {4, 5, 7, 6}
            SchematicFacesCache = Defs
        End If
        Return SchematicFacesCache
    End Function

    Private Class SchematicMeshGroup
        Public Positions As New List(Of Point3D)
        Public Normals As New List(Of Vector3D)
        Public Indices As New List(Of Integer)
    End Class

    ''' <summary>面剔除后按颜色合并成少量网格。坐标为模型空间，1 个方块 = 1 个单位。</summary>
    Public Function BuildSchematicRender(Model As SchematicModel) As SchematicRender
        Dim Result As New SchematicRender
        Dim Corners = SchematicCubeCorners()
        Dim Faces = SchematicFaceDefs()
        Dim Groups As New Dictionary(Of Integer, SchematicMeshGroup)
        For Y = 0 To Model.Height - 1
            For Z = 0 To Model.Depth - 1
                Dim RowBase = (Y * Model.Depth + Z) * Model.Width
                For X = 0 To Model.Width - 1
                    Dim State = Model.States(RowBase + X)
                    If State Is Nothing Then Continue For
                    Dim Color = GetStateColor(State)
                    Dim SelfOpaque = ((Color >> 24) And &HFF) = 255
                    For Each Face In Faces
                        Dim Neighbor = Model.StateAt(X + Face.Offset(0), Y + Face.Offset(1), Z + Face.Offset(2))
                        If Neighbor IsNot Nothing Then
                            Dim NeighborColor = GetStateColor(Neighbor)
                            If NeighborColor = Color Then Continue For
                            If SelfOpaque AndAlso ((NeighborColor >> 24) And &HFF) = 255 Then Continue For
                        End If
                        Dim Group As SchematicMeshGroup = Nothing
                        If Not Groups.TryGetValue(Color, Group) Then
                            Group = New SchematicMeshGroup
                            Groups(Color) = Group
                        End If
                        Dim Start = Group.Positions.Count
                        Dim Normal As New Vector3D(Face.Normal(0), Face.Normal(1), Face.Normal(2))
                        For Each Corner In Face.Corners
                            Dim c = Corners(Corner)
                            Group.Positions.Add(New Point3D(X + c(0), Y + c(1), Z + c(2)))
                            Group.Normals.Add(Normal)
                        Next
                        Group.Indices.Add(Start)
                        Group.Indices.Add(Start + 1)
                        Group.Indices.Add(Start + 2)
                        Group.Indices.Add(Start)
                        Group.Indices.Add(Start + 2)
                        Group.Indices.Add(Start + 3)
                        Result.Faces += 1
                        If Result.Faces >= MaxSchematicFaces Then GoTo BuildFinished
                    Next
                Next
            Next
        Next
BuildFinished:
        For Each Group In Groups
            Dim Geometry As New MeshGeometry3D
            Geometry.Positions = New Point3DCollection(Group.Value.Positions)
            Geometry.Normals = New Vector3DCollection(Group.Value.Normals)
            Geometry.TriangleIndices = New Int32Collection(Group.Value.Indices)
            Geometry.Freeze()
            Dim Brush As New SolidColorBrush(Color.FromArgb((Group.Key >> 24) And &HFF, (Group.Key >> 16) And &HFF, (Group.Key >> 8) And &HFF, Group.Key And &HFF))
            Brush.Freeze()
            Dim Material As New DiffuseMaterial(Brush)
            Material.Freeze()
            Dim Mesh As New GeometryModel3D(Geometry, Material) With {.BackMaterial = Material}
            Mesh.Freeze()
            Result.Models.Add(Mesh)
        Next
        Return Result
    End Function

    ''' <summary>构造一组合并后的长方体网格，用于选中方块的描边。每个 Box 为 {x0, y0, z0, x1, y1, z1}。</summary>
    Public Function BuildBoxesGeometry(Boxes As List(Of Double())) As MeshGeometry3D
        Dim Positions As New List(Of Point3D)
        Dim Normals As New List(Of Vector3D)
        Dim Indices As New List(Of Integer)
        Dim Corners = SchematicCubeCorners()
        For Each Box In Boxes
            For Each Face In SchematicFaceDefs()
                Dim Start = Positions.Count
                Dim Normal As New Vector3D(Face.Normal(0), Face.Normal(1), Face.Normal(2))
                For Each Corner In Face.Corners
                    Dim c = Corners(Corner)
                    Positions.Add(New Point3D(If(c(0) = 0, Box(0), Box(3)), If(c(1) = 0, Box(1), Box(4)), If(c(2) = 0, Box(2), Box(5))))
                    Normals.Add(Normal)
                Next
                Indices.Add(Start)
                Indices.Add(Start + 1)
                Indices.Add(Start + 2)
                Indices.Add(Start)
                Indices.Add(Start + 2)
                Indices.Add(Start + 3)
            Next
        Next
        Dim Geometry As New MeshGeometry3D
        Geometry.Positions = New Point3DCollection(Positions)
        Geometry.Normals = New Vector3DCollection(Normals)
        Geometry.TriangleIndices = New Int32Collection(Indices)
        Geometry.Freeze()
        Return Geometry
    End Function

    ''' <summary>生成一个用于试用的小原理图：地基、墙体、羊毛立柱、楼梯、玻璃窗与水。</summary>
    Public Function BuildSampleSchematic() As SchematicModel
        Dim Model As New SchematicModel With {
            .Format = "示例原理图",
            .Width = 25, .Height = 14, .Depth = 19}
        Model.States = New String(Model.Volume() - 1) {}
        For X = 0 To Model.Width - 1
            For Z = 0 To Model.Depth - 1
                SetSchematicState(Model, X, 0, Z, "minecraft:stone_bricks")
                If X = 0 OrElse X = Model.Width - 1 OrElse Z = 0 OrElse Z = Model.Depth - 1 Then
                    For Y = 1 To 5
                        SetSchematicState(Model, X, Y, Z, "minecraft:oak_planks")
                    Next
                    If X Mod 6 = 2 AndAlso Z Mod 6 = 2 Then SetSchematicState(Model, X, 6, Z, "minecraft:glowstone")
                End If
                If (X = 0 OrElse X = Model.Width - 1) AndAlso (Z = 0 OrElse Z = Model.Depth - 1) Then
                    For Y = 1 To 8
                        SetSchematicState(Model, X, Y, Z, "minecraft:" & SchematicDyes((X + Z) Mod 16) & "_wool")
                    Next
                End If
            Next
        Next
        For X = 0 To Model.Width - 1
            For Z = 0 To Model.Depth - 1
                If X > 3 AndAlso X < Model.Width - 4 AndAlso Z > 3 AndAlso Z < Model.Depth - 4 Then Continue For
                SetSchematicState(Model, X, 6, Z, "minecraft:spruce_planks")
            Next
        Next
        For K = 0 To 6
            SetSchematicState(Model, 2 + K, K + 1, 2, "minecraft:stone")
        Next
        For X = 4 To 8
            SetSchematicState(Model, X, 3, 0, "minecraft:glass")
            SetSchematicState(Model, X, 4, 0, "minecraft:glass")
        Next
        For X = 8 To 12
            For Z = 8 To 11
                SetSchematicState(Model, X, 1, Z, "minecraft:water")
            Next
        Next
        Model.FileName = "示例原理图"
        Return Model
    End Function

    Public Sub SetSchematicState(Model As SchematicModel, X As Integer, Y As Integer, Z As Integer, State As String)
        Model.States((Y * Model.Depth + Z) * Model.Width + X) = State
    End Sub

#End Region

End Module
