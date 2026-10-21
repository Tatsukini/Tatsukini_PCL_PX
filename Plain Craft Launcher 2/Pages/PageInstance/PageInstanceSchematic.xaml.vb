Imports System.Windows.Media.Media3D
Imports System.Windows.Shapes

Public Class PageInstanceSchematic
    Implements IRefreshable

    Private Model As SchematicModel = Nothing
    Private SceneScale As Double = 1
    Private ModelCenter As New Point3D(0, 0, 0)
    Private LoadToken As Integer = 0
    '清单最多渲染这么多行，剩下的靠搜索过滤，避免几百种方块时把界面拖垮
    Private Const MaxListRows As Integer = 400
    Private Infos As List(Of SchematicStateInfo) = Nothing
    Private Rows As New Dictionary(Of SchematicStateInfo, Border)
    Private SelectedInfo As SchematicStateInfo = Nothing

    '场景容器
    Private SceneRoot As ModelVisual3D
    Private ReadOnly BlockGroup As New Model3DGroup
    Private ReadOnly SelectGroup As New Model3DGroup
    Private SelectionGeometry As MeshGeometry3D
    Private SelectionMaterial As DiffuseMaterial

    '相机状态，视角操作方式与 Blender 一致
    Private Const CamFov As Double = 45
    Private CamYaw As Double = -32
    Private CamPitch As Double = 18
    Private CamDist As Double = 300
    Private CamTarget As New Point3D(0, 0, 0)
    Private IsOrtho As Boolean = False
    Private CamPerspective As PerspectiveCamera
    Private CamOrthographic As OrthographicCamera
    Private ReadOnly WorldUp As New Vector3D(0, 1, 0)

    Private Dragging As Boolean = False
    Private LastMouse As Point
    Private LastViewKey As Integer = -1
    Private LastViewTime As Date = Date.MinValue
    Private ReadOnly WarnBrush As Brush = New SolidColorBrush(Color.FromRgb(210, 120, 40))
    Private ReadOnly RowHighlightBrush As Brush = New SolidColorBrush(Color.FromArgb(45, 255, 205, 90))
    Private LastExportExt As String = ".txt"

    Public Sub New()
        InitializeComponent()
        CamPerspective = New PerspectiveCamera With {.FieldOfView = CamFov, .NearPlaneDistance = 0.02, .FarPlaneDistance = 200000}
        CamOrthographic = New OrthographicCamera With {.NearPlaneDistance = 0.02, .FarPlaneDistance = 200000}
        '灯光：一盏环境光保证暗面不死黑，两盏方向光提供立体感
        Dim Lights As New Model3DGroup
        Lights.Children.Add(New AmbientLight(Color.FromRgb(126, 126, 134)))
        Lights.Children.Add(New DirectionalLight(Color.FromRgb(228, 228, 228), New Vector3D(-0.45, -1, -0.6)))
        Lights.Children.Add(New DirectionalLight(Color.FromRgb(70, 70, 84), New Vector3D(0.7, 0.35, 0.5)))
        PanView.Children.Add(New ModelVisual3D With {.Content = Lights})
        Dim BlocksVisual As New ModelVisual3D With {.Content = BlockGroup}
        Dim SelectVisual As New ModelVisual3D With {.Content = SelectGroup}
        SceneRoot = New ModelVisual3D
        SceneRoot.Children.Add(BlocksVisual)
        SceneRoot.Children.Add(SelectVisual)
        PanView.Children.Add(SceneRoot)
        BuildSelectionMesh()
        '输入
        AddHandler PanViewBorder.MouseDown, AddressOf View_MouseDown
        AddHandler PanViewBorder.MouseMove, AddressOf View_MouseMove
        AddHandler PanViewBorder.MouseUp, AddressOf View_MouseUp
        AddHandler PanViewBorder.MouseWheel, AddressOf View_MouseWheel
        AddHandler PanViewBorder.SizeChanged, Sub() UpdateCamera()
        UpdateCamera()
        LabTip.Text = "点击「选择原理图」载入 .schematic、.schem 或 .litematic 文件" & vbCrLf & "点击「载入示例」可以先试一下视角操作"
        LabTip.Visibility = Visibility.Visible
    End Sub

    Public Sub Refresh() Implements IRefreshable.Refresh
        UpdateCamera()
    End Sub

#Region "载入"

    Private Function NextToken() As Integer
        LoadToken += 1
        Return LoadToken
    End Function

    Private Sub BtnOpen_Click() Handles BtnOpen.Click
        Dim Directory As String = Nothing
        If PageInstanceLeft.Instance IsNot Nothing Then
            Try
                Directory = PageInstanceLeft.Instance.PathIndie & "schematics\"
            Catch ex As Exception
                Logger.Error(ex, "获取版本原理图文件夹失败")
            End Try
        End If
        Dim Selected = Dialogs.SelectFile("选择原理图文件", False, Directory, {({"schem", "schematic", "litematic"}, "原理图文件")}).FirstOrDefault()
        If String.IsNullOrEmpty(Selected) Then Return
        LoadSchematic(Selected)
    End Sub

    Private Sub BtnSample_Click() Handles BtnSample.Click
        Dim NewModel = BuildSampleSchematic()
        ApplyModel(NewModel, BuildSchematicRender(NewModel), CountSchematicStates(NewModel), NextToken())
    End Sub

    Private Sub BtnFit_Click() Handles BtnFit.Click
        If Model Is Nothing Then Return
        CamYaw = -32
        CamPitch = 18
        FitView()
    End Sub

    Private Sub LoadSchematic(Path As String)
        Dim Token = NextToken()
        LabTip.Text = "正在解析 " & IO.Path.GetFileName(Path) & " …"
        LabTip.Visibility = Visibility.Visible
        LabSelect.Text = ""
        RunInNewThread(
        Sub()
            Dim NewModel As SchematicModel = Nothing
            Dim Render As SchematicRender = Nothing
            Dim NewInfos As List(Of SchematicStateInfo) = Nothing
            Dim ErrMsg As String = Nothing
            Try
                NewModel = ReadSchematic(Path)
                Render = BuildSchematicRender(NewModel)
                NewInfos = CountSchematicStates(NewModel)
            Catch ex As Exception
                Logger.Error(ex, "解析原理图失败：" & Path)
                ErrMsg = ex.Message
            End Try
            If ErrMsg IsNot Nothing Then
                RunInUi(
                Sub()
                    If Token <> LoadToken Then Return
                    LabTip.Text = "解析失败"
                    LabInfo.Text = "尚未载入原理图"
                    LabStatus.Text = "解析失败：" & ErrMsg
                    LabStatus.Foreground = WarnBrush
                    LabStatus.Opacity = 1
                End Sub)
            Else
                RunInUi(Sub() ApplyModel(NewModel, Render, NewInfos, Token))
            End If
        End Sub)
    End Sub

    Private Sub ApplyModel(NewModel As SchematicModel, Render As SchematicRender, NewInfos As List(Of SchematicStateInfo), Token As Integer)
        If Token <> LoadToken Then Return
        Model = NewModel
        Infos = NewInfos
        SelectedInfo = Nothing
        BlockGroup.Children.Clear()
        For Each Mesh In Render.Models
            BlockGroup.Children.Add(Mesh)
        Next
        SelectGroup.Children.Clear()
        Dim MaxDim = Math.Max(NewModel.Width, Math.Max(NewModel.Height, NewModel.Depth))
        ModelCenter = New Point3D(NewModel.Width / 2.0, NewModel.Height / 2.0, NewModel.Depth / 2.0)
        SceneScale = 200 / Math.Max(1, MaxDim)
        Dim Transform As New Transform3DGroup
        Transform.Children.Add(New TranslateTransform3D(-ModelCenter.X, -ModelCenter.Y, -ModelCenter.Z))
        Transform.Children.Add(New ScaleTransform3D(SceneScale, SceneScale, SceneScale))
        SceneRoot.Transform = Transform
        LabInfo.Text = NewModel.FileName & "　" & NewModel.Format & vbCrLf &
                       NewModel.Width & " × " & NewModel.Height & " × " & NewModel.Depth & "　" & NewModel.SolidCount() & " 个方块"
        LabStatus.Opacity = 0.6
        LabStatus.Foreground = Nothing
        If Render.Truncated Then
            LabStatus.Text = $"方块面数已达上限 {Render.Faces}，只显示了靠前的部分，请改用更小的原理图。"
            LabStatus.Foreground = WarnBrush
            LabStatus.Opacity = 1
        Else
            LabStatus.Text = "共渲染 " & Render.Faces & " 个面，合并为 " & Render.Models.Count & " 组网格。"
        End If
        LabTip.Visibility = Visibility.Collapsed
        CardBlocks.Visibility = Visibility.Visible
        RebuildList()
        FitView()
    End Sub

    Private Sub FitView()
        If Model Is Nothing Then Return
        CamTarget = New Point3D(0, 0, 0)
        Dim Radius = 0.5 * Math.Sqrt(Model.Width ^ 2 + Model.Height ^ 2 + Model.Depth ^ 2) * SceneScale
        CamDist = Math.Max(20, Radius / Math.Tan(CamFov / 2.0 * Math.PI / 180) * 1.1)
        UpdateCamera()
    End Sub

#End Region

#Region "相机"

    Private Structure CameraAxes
        Public Dir As Vector3D   '由目标指向相机
        Public Look As Vector3D  '相机朝向
        Public Right As Vector3D
        Public Up As Vector3D
        Public Position As Point3D
    End Structure

    Private Function GetAxes() As CameraAxes
        Dim Yaw = CamYaw * Math.PI / 180
        Dim Pitch = Math.Max(-89.5, Math.Min(89.5, CamPitch)) * Math.PI / 180
        Dim Result As New CameraAxes
        Result.Dir = New Vector3D(Math.Sin(Yaw) * Math.Cos(Pitch), Math.Sin(Pitch), Math.Cos(Yaw) * Math.Cos(Pitch))
        Result.Position = CamTarget + Result.Dir * CamDist
        Result.Look = -Result.Dir
        Result.Right = Vector3D.CrossProduct(Result.Look, WorldUp)
        If Result.Right.LengthSquared < 0.000001 Then
            Result.Right = New Vector3D(1, 0, 0)
        Else
            Result.Right.Normalize()
        End If
        Result.Up = Vector3D.CrossProduct(Result.Right, Result.Look)
        Result.Up.Normalize()
        Return Result
    End Function

    Private Sub UpdateCamera()
        Dim Axes = GetAxes()
        If IsOrtho Then
            CamOrthographic.Position = Axes.Position
            CamOrthographic.LookDirection = Axes.Look
            CamOrthographic.UpDirection = Axes.Up
            Dim Aspect = If(PanView.ActualHeight > 0, PanView.ActualWidth / PanView.ActualHeight, 1.6)
            CamOrthographic.Width = Math.Max(1, 2 * CamDist * Math.Tan(CamFov / 2.0 * Math.PI / 180) * Aspect)
            PanView.Camera = CamOrthographic
        Else
            CamPerspective.Position = Axes.Position
            CamPerspective.LookDirection = Axes.Look
            CamPerspective.UpDirection = Axes.Up
            PanView.Camera = CamPerspective
        End If
    End Sub

#End Region

#Region "视角操作"

    Private Sub View_MouseDown(sender As Object, e As MouseButtonEventArgs)
        If Not PanView.IsFocused Then PanView.Focus()
        Select Case e.ChangedButton
            Case MouseButton.Middle
                Dragging = True
                LastMouse = e.GetPosition(PanView)
                PanViewBorder.CaptureMouse()
                e.Handled = True
            Case MouseButton.Right
                If Model IsNot Nothing Then SelectAt(e.GetPosition(PanView))
                e.Handled = True
        End Select
    End Sub

    Private Sub View_MouseMove(sender As Object, e As MouseEventArgs)
        If Not Dragging OrElse Model Is Nothing Then Return
        Dim Current = e.GetPosition(PanView)
        Dim Dx = Current.X - LastMouse.X
        Dim Dy = Current.Y - LastMouse.Y
        LastMouse = Current
        If Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) OrElse Keyboard.Modifiers.HasFlag(ModifierKeys.Control) Then
            '平移：与 Blender 一样，画面随拖动方向移动
            Dim Axes = GetAxes()
            Dim PanStep = CamDist * 0.0016
            CamTarget -= Axes.Right * (Dx * PanStep)
            CamTarget += Axes.Up * (Dy * PanStep)
        Else
            CamYaw -= Dx * 0.35
            CamPitch -= Dy * 0.35
            CamPitch = Math.Max(-89.5, Math.Min(89.5, CamPitch))
        End If
        UpdateCamera()
    End Sub

    Private Sub View_MouseUp(sender As Object, e As MouseButtonEventArgs)
        If e.ChangedButton = MouseButton.Middle AndAlso Dragging Then
            Dragging = False
            PanViewBorder.ReleaseMouseCapture()
            e.Handled = True
        End If
    End Sub

    Private Sub View_MouseWheel(sender As Object, e As MouseWheelEventArgs)
        If Model Is Nothing Then Return
        ZoomAt(If(e.Delta > 0, 0.85, 1 / 0.85), e.GetPosition(PanView))
        e.Handled = True
    End Sub

    ''' <summary>Blender 的滚轮是以光标为中心缩放的，键盘的 +/- 则以画面中心为准。</summary>
    Private Sub ZoomAt(Factor As Double, p As Point)
        If Model Is Nothing Then Return
        Dim Hit = RayHit(p)
        If Hit IsNot Nothing Then CamTarget = Hit.Value + (CamTarget - Hit.Value) * Factor
        CamDist = Math.Max(2, Math.Min(100000, CamDist * Factor))
        UpdateCamera()
    End Sub

    Private Sub Page_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        '搜索框里打字时不要触发快捷键
        If TextSearch.IsKeyboardFocusWithin Then Return
        If Model Is Nothing Then Return
        Dim Ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
        Select Case e.Key
            Case Key.F
                If Ctrl Then
                    TextSearch.Focus()
                    TextSearch.SelectAll()
                Else
                    BtnFit_Click()
                End If
                e.Handled = True
                Return
            Case Key.Add, Key.OemPlus, Key.Subtract, Key.OemMinus
                ZoomAt(If(e.Key = Key.Add OrElse e.Key = Key.OemPlus, 0.85, 1 / 0.85), New Point(PanView.ActualWidth / 2, PanView.ActualHeight / 2))
                e.Handled = True
                Return
            Case Key.L
                BtnList_Click()
                e.Handled = True
                Return
            Case Key.E
                If Not Ctrl Then Return
                BtnExport_Click()
                e.Handled = True
                Return
            Case Key.Escape
                ClearSelection()
                e.Handled = True
                Return
        End Select
        Dim View As Integer = -1
        Select Case e.Key
            Case Key.NumPad1
                View = 0 '前
            Case Key.NumPad3
                View = 1 '右
            Case Key.NumPad7
                View = 2 '顶
            Case Key.NumPad5
                IsOrtho = Not IsOrtho
                UpdateCamera()
                e.Handled = True
                Return
            Case Key.NumPad0
                IsOrtho = False
                UpdateCamera()
                e.Handled = True
                Return
            Case Key.Decimal
                BtnFit_Click()
                e.Handled = True
                Return
            Case Else
                Return
        End Select
        '连按两次同一数字键看对面，Ctrl + 数字键直接看对面（与 Blender 相同）
        Dim Flipped = View = LastViewKey AndAlso Date.Now.Subtract(LastViewTime).TotalSeconds < 1.5
        If Keyboard.Modifiers.HasFlag(ModifierKeys.Control) Then Flipped = Not Flipped
        LastViewKey = View
        LastViewTime = Date.Now
        Select Case View
            Case 0
                CamYaw = If(Flipped, 180, 0)
                CamPitch = 0
            Case 1
                CamYaw = If(Flipped, -90, 90)
                CamPitch = 0
            Case Else
                CamPitch = If(Flipped, -89.5, 89.5)
        End Select
        UpdateCamera()
        e.Handled = True
    End Sub

#End Region

#Region "方块清单"

    ''' <summary>列表里显示的名字：去掉 minecraft: 前缀，但保留 [属性]，因为台阶朝向之类对建造用料是有区别的。</summary>
    Private Function DisplayName(State As String) As String
        Dim Split = State.IndexOf("["c)
        Dim Name = If(Split >= 0, State.Substring(0, Split), State)
        If Name.StartsWithF("minecraft:") Then Name = Name.Substring(10)
        Return Name & If(Split >= 0, State.Substring(Split), "")
    End Function

    Private Sub RebuildList()
        PanBlockList.Children.Clear()
        Rows.Clear()
        If Infos Is Nothing OrElse Model Is Nothing Then
            LabListSummary.Text = "—"
            Return
        End If
        Dim Keyword = TextSearch.Text.Trim.ToLower
        Dim Shown As Integer = 0
        Dim Matched As Integer = 0
        For Each Info In Infos
            If Keyword <> "" AndAlso Not Info.State.ToLower.Contains(Keyword) Then Continue For
            Matched += 1
            If Shown >= MaxListRows Then Continue For
            Shown += 1
            Dim Row = BuildRow(Info)
            Rows(Info) = Row
            PanBlockList.Children.Add(Row)
        Next
        LabListSummary.Text = $"共 {Infos.Count} 种 · {Model.SolidCount()} 个方块" &
            If(Keyword = "", "", $"　· 匹配 {Matched} 种") & If(Shown < Matched, $"　· 只显示前 {MaxListRows} 项", "")
        HighlightRow(SelectedInfo)
    End Sub

    Private Function BuildRow(Info As SchematicStateInfo) As Border
        Dim Panel As New Grid
        Panel.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Panel.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
        Panel.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
        Dim Swatch As New Rectangle With {.Width = 11, .Height = 11, .RadiusX = 2, .RadiusY = 2, .Margin = New Thickness(0, 0, 7, 0), .VerticalAlignment = VerticalAlignment.Center}
        Dim Argb = GetStateColor(Info.State)
        Swatch.Fill = New SolidColorBrush(Color.FromArgb(CByte((Argb >> 24) And &HFF), CByte((Argb >> 16) And &HFF), CByte((Argb >> 8) And &HFF), CByte(Argb And &HFF)))
        Dim LabName As New TextBlock With {.Text = DisplayName(Info.State), .TextTrimming = TextTrimming.CharacterEllipsis, .ToolTip = Info.State, .Margin = New Thickness(0, 0, 8, 0), .VerticalAlignment = VerticalAlignment.Center}
        Dim LabCount As New TextBlock With {.Text = Info.Count.ToString, .Opacity = 0.75, .FontWeight = FontWeights.SemiBold, .VerticalAlignment = VerticalAlignment.Center}
        Grid.SetColumn(Swatch, 0)
        Grid.SetColumn(LabName, 1)
        Grid.SetColumn(LabCount, 2)
        Panel.Children.Add(Swatch)
        Panel.Children.Add(LabName)
        Panel.Children.Add(LabCount)
        Dim Row As New Border With {.Child = Panel, .Background = Brushes.Transparent, .Padding = New Thickness(5, 4, 5, 4), .Cursor = Cursors.Hand, .CornerRadius = New CornerRadius(3)}
        AddHandler Row.MouseLeftButtonUp, Sub() Locate(Info)
        Return Row
    End Function

    Private Sub HighlightRow(Info As SchematicStateInfo)
        SelectedInfo = Info
        For Each Pair In Rows
            Pair.Value.Background = If(Pair.Key Is Info, RowHighlightBrush, CType(Brushes.Transparent, Brush))
        Next
    End Sub

    ''' <summary>把相机移到该种方块的第一个实例并描边，用来确认清单里的这一项到底长什么样。</summary>
    Private Sub Locate(Info As SchematicStateInfo)
        If Model Is Nothing OrElse Info Is Nothing Then Return
        Dim i = Info.FirstIndex
        Dim X = i Mod Model.Width
        Dim Z = (i \ Model.Width) Mod Model.Depth
        Dim Y = i \ (Model.Width * Model.Depth)
        SelectVoxel(New VoxelHit With {.X = X, .Y = Y, .Z = Z, .State = Info.State, .IsValid = True})
        CamTarget = New Point3D((X + 0.5 - ModelCenter.X) * SceneScale, (Y + 0.5 - ModelCenter.Y) * SceneScale, (Z + 0.5 - ModelCenter.Z) * SceneScale)
        UpdateCamera()
        HighlightRow(Info)
    End Sub

    Private Function FirstMatch() As SchematicStateInfo
        If Infos Is Nothing Then Return Nothing
        Dim Keyword = TextSearch.Text.Trim.ToLower
        If Keyword = "" Then Return Infos.FirstOrDefault()
        Return Infos.FirstOrDefault(Function(v) v.State.ToLower.Contains(Keyword))
    End Function

    Private Sub BtnList_Click() Handles BtnList.Click
        CardBlocks.Visibility = If(CardBlocks.Visibility = Visibility.Visible, Visibility.Collapsed, Visibility.Visible)
        If CardBlocks.Visibility = Visibility.Visible Then RebuildList()
    End Sub

    Private Sub TextSearch_TextChanged(sender As Object, e As TextChangedEventArgs) Handles TextSearch.TextChanged
        RebuildList()
    End Sub

    Private Sub TextSearch_PreviewKeyDown(sender As Object, e As KeyEventArgs) Handles TextSearch.PreviewKeyDown
        Select Case e.Key
            Case Key.Return
                Dim First = FirstMatch()
                If First IsNot Nothing Then Locate(First)
                e.Handled = True
            Case Key.Escape
                TextSearch.Text = ""
                TextSearch.Focus()
                e.Handled = True
        End Select
    End Sub

    Private Sub BtnExport_Click() Handles BtnExport.Click
        If Model Is Nothing OrElse Infos Is Nothing OrElse Infos.Count = 0 Then
            LabStatus.Text = "还没有载入原理图，无法导出清单。"
            LabStatus.Foreground = WarnBrush
            LabStatus.Opacity = 1
            Return
        End If
        Dim Base = Model.FileName
        For Each Bad In IO.Path.GetInvalidFileNameChars
            Base = Base.Replace(Bad, " "c)
        Next
        Dim Address = Dialogs.SaveFile("导出方块清单", Base & LastExportExt, filter:={("txt", "文本文件"), ("csv", "CSV 表格")})
        If String.IsNullOrEmpty(Address) Then Return
        Dim Csv = Address.ToLower.EndsWithF(".csv")
        RunInNewThread(
        Sub()
            Try
                IO.File.WriteAllText(Address, BuildSchematicListText(Model, Infos, Csv), New System.Text.UTF8Encoding(True))
                LastExportExt = If(Csv, ".csv", ".txt")
                RunInUi(Sub() Hint("已导出 " & IO.Path.GetFileName(Address), HintType.Green))
            Catch ex As Exception
                Logger.Error(ex, "导出原理图方块清单失败：" & Address)
                RunInUi(Sub() Hint("导出失败：" & ex.Message, HintType.Red))
            End Try
        End Sub)
    End Sub

#End Region

#Region "选中方块"

    Private Structure VoxelHit
        Public X As Integer
        Public Y As Integer
        Public Z As Integer
        Public State As String
        Public IsValid As Boolean
    End Structure

    Private Function RayHit(p As Point) As Point3D?
        Dim Result As Point3D? = Nothing
        Dim Callback As New HitTestResultCallback(
        Function(r As HitTestResult) As HitTestResultBehavior
            Dim Ray = TryCast(r, RayMeshGeometry3DHitTestResult)
            If Ray IsNot Nothing AndAlso Ray.ModelHit IsNot Nothing AndAlso Ray.MeshHit IsNot SelectionGeometry Then
                Result = Ray.PointHit
                Return HitTestResultBehavior.Stop
            End If
            Return HitTestResultBehavior.Continue
        End Function)
        VisualTreeHelper.HitTest(PanView, Nothing, Callback, New PointHitTestParameters(p))
        Return Result
    End Function

    ''' <summary>把世界坐标换算回方块序号。交点正好落在方块表面上，因此需要向内外各试一次。</summary>
    Private Function WorldToVoxel(World As Point3D) As VoxelHit
        Dim Axes = GetAxes()
        For Each Probe In {0.03, -0.03}
            Dim mx = (World.X + Axes.Look.X * Probe * SceneScale) / SceneScale + ModelCenter.X
            Dim my = (World.Y + Axes.Look.Y * Probe * SceneScale) / SceneScale + ModelCenter.Y
            Dim mz = (World.Z + Axes.Look.Z * Probe * SceneScale) / SceneScale + ModelCenter.Z
            Dim X = Math.Floor(mx), Y = Math.Floor(my), Z = Math.Floor(mz)
            If X < 0 OrElse Y < 0 OrElse Z < 0 OrElse X >= Model.Width OrElse Y >= Model.Height OrElse Z >= Model.Depth Then Continue For
            Dim State = Model.States((CLng(Y) * Model.Depth + CLng(Z)) * Model.Width + CLng(X))
            If State IsNot Nothing Then
                Return New VoxelHit With {.X = CInt(X), .Y = CInt(Y), .Z = CInt(Z), .State = State, .IsValid = True}
            End If
        Next
        Return New VoxelHit
    End Function

    Private Sub SelectAt(p As Point)
        Dim Hit = RayHit(p)
        If Hit Is Nothing Then
            ClearSelection()
            Return
        End If
        Dim Voxel = WorldToVoxel(Hit.Value)
        If Not Voxel.IsValid Then Return
        SelectVoxel(Voxel)
        If Infos IsNot Nothing Then
            For Each Info In Infos
                If Info.State = Voxel.State Then
                    HighlightRow(Info)
                    Exit For
                End If
            Next
        End If
    End Sub

    Private Sub SelectVoxel(Voxel As VoxelHit)
        SelectGroup.Children.Clear()
        Dim Mesh As New GeometryModel3D(SelectionGeometry, SelectionMaterial) With {
            .BackMaterial = SelectionMaterial,
            .Transform = New TranslateTransform3D(Voxel.X, Voxel.Y, Voxel.Z)}
        SelectGroup.Children.Add(Mesh)
        LabSelect.Text = Voxel.State & vbCrLf & "坐标 X " & Voxel.X & " · Y " & Voxel.Y & " · Z " & Voxel.Z
    End Sub

    Private Sub ClearSelection()
        SelectGroup.Children.Clear()
        LabSelect.Text = ""
        HighlightRow(Nothing)
    End Sub

    Private Sub BuildSelectionMesh()
        Dim t = 0.055
        Dim Boxes As New List(Of Double())
        For Axis = 0 To 2
            For Each Edge0 In {0, 1}
                For Each Edge1 In {0, 1}
                    Dim Lo(2) As Double, Hi(2) As Double
                    For k = 0 To 2
                        If k = Axis Then
                            Lo(k) = -t
                            Hi(k) = 1 + t
                        Else
                            Dim v = If(k = (Axis + 1) Mod 3, Edge0, Edge1)
                            Lo(k) = v - t
                            Hi(k) = v + t
                        End If
                    Next
                    Boxes.Add(New Double() {Lo(0), Lo(1), Lo(2), Hi(0), Hi(1), Hi(2)})
                Next
            Next
        Next
        SelectionGeometry = BuildBoxesGeometry(Boxes)
        Dim Brush As New SolidColorBrush(Color.FromArgb(235, 255, 205, 90))
        Brush.Freeze()
        SelectionMaterial = New DiffuseMaterial(Brush)
        SelectionMaterial.Freeze()
    End Sub

#End Region

End Class
