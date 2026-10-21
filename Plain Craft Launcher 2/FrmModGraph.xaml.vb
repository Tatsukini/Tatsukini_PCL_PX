Public Class FrmModGraph

    ''' <summary>
    ''' 当前已经打开的窗口。再次进入时只刷新并重新显示它，不重复打开第二个窗口。
    ''' </summary>
    Public Shared Current As FrmModGraph = Nothing

    '节点尺寸与间隔
    Private Const NodeWidth As Double = 190
    Private Const NodeHeight As Double = 52
    Private Const NodeGapX As Double = 90
    Private Const NodeGapY As Double = 26
    '按下后移动超过该距离才算拖动，否则算单击选中
    Private Const DragThreshold As Double = 3
    '选中节点后，往外高亮几层邻居
    Private Const HighlightDepth As Integer = 2

    Private TargetInstance As McInstance = Nothing
    Private GraphNodes As List(Of ModGraphNode) = Nothing
    Private GraphLinks As List(Of ModGraphLink) = Nothing

    '当前布局。玩家拖动过的位置会覆盖自动布局的结果，重画时保持不变
    Private Positions As New Dictionary(Of ModGraphNode, Point)
    Private ManualPositions As New Dictionary(Of ModGraphNode, Point)
    Private NodeCards As New Dictionary(Of ModGraphNode, Border)
    Private NodeLines As New Dictionary(Of ModGraphNode, List(Of System.Windows.Shapes.Path))
    Private AllLines As New List(Of System.Windows.Shapes.Path)
    Private SelectedNode As ModGraphNode = Nothing

    '拖动状态
    Private DragNode As ModGraphNode = Nothing
    Private DragStart As Point
    Private DragNodeStart As Point
    Private IsDragging As Boolean = False
    '本次按下锁定的节点，抬起时没有被拖动就当作单击
    Private ClickCandidate As ModGraphNode = Nothing

    Public Sub New()
        Try
            InitializeComponent()
        Catch ex As Exception
            Logger.Error(ex, "初始化模组依赖图窗口失败", LogBehavior.Toast)
        End Try
    End Sub

    ''' <summary>
    ''' 显示依赖图。必须在 UI 线程上调用。
    ''' </summary>
    Public Shared Sub OpenGraph(Instance As McInstance, Nodes As List(Of ModGraphNode), Links As List(Of ModGraphLink))
        Try
            If Current Is Nothing Then
                Current = New FrmModGraph
                If FrmMain IsNot Nothing Then Current.Owner = FrmMain
            End If
            Current.TargetInstance = Instance
            Current.GraphNodes = Nodes
            Current.GraphLinks = Links
            '重新扫描得到的是另一批节点对象，旧的拖动位置与选中项都不再有效
            Current.ManualPositions.Clear()
            Current.SelectedNode = Nothing
            Current.Show()
            If Current.WindowState = WindowState.Minimized Then Current.WindowState = WindowState.Normal
            Current.Activate()
            Current.DrawGraph()
        Catch ex As Exception
            Logger.Error(ex, "显示模组依赖图失败", LogBehavior.Toast)
        End Try
    End Sub

    ''' <summary>
    ''' 按依赖深度分层，重新绘制整个图。必须在 UI 线程上调用。
    ''' </summary>
    Private Sub DrawGraph()
        Try
            PanGraph.Children.Clear()
            Positions.Clear()
            NodeCards.Clear()
            NodeLines.Clear()
            AllLines.Clear()

            '统计信息
            Dim NodeCount = If(GraphNodes Is Nothing, 0, GraphNodes.Count)
            Dim ProblemCount As Integer = 0
            If GraphNodes IsNot Nothing Then
                For Each Node In GraphNodes
                    If Node.HasProblem Then ProblemCount += 1
                Next
            End If
            Dim VersionText As String = ""
            Try
                If TargetInstance IsNot Nothing Then VersionText = "（" & TargetInstance.Name & " · " & TargetInstance.Version.VanillaName & "）"
            Catch ex As Exception
                Logger.Warn(ex, "获取当前版本号失败")
            End Try
            LabSummary.Text = $"共 {NodeCount} 个 Mod，{ProblemCount} 个问题{VersionText}"
            HintEmpty.Visibility = If(NodeCount = 0, Visibility.Visible, Visibility.Collapsed)
            If GraphLinks Is Nothing Then GraphLinks = New List(Of ModGraphLink)

            '决定要显示哪些节点
            Dim Visible As New List(Of ModGraphNode)
            If GraphNodes IsNot Nothing Then
                For Each Node In GraphNodes
                    If CheckOnlyProblem.Checked AndAlso Not Node.HasProblem Then Continue For
                    Visible.Add(Node)
                Next
            End If
            If Not Visible.Any() Then
                PanGraph.Width = 0
                PanGraph.Height = 0
                Return
            End If

            '收集依赖出边
            Dim Outgoings As New Dictionary(Of ModGraphNode, List(Of ModGraphNode))
            For Each Link In GraphLinks
                If Link.Kind = ModGraphLinkKind.Conflict Then Continue For
                If Link.FromNode Is Nothing OrElse Link.ToNode Is Nothing Then Continue For
                If Not Outgoings.ContainsKey(Link.FromNode) Then Outgoings(Link.FromNode) = New List(Of ModGraphNode)
                If Not Outgoings(Link.FromNode).Contains(Link.ToNode) Then Outgoings(Link.FromNode).Add(Link.ToNode)
            Next

            '分层：没有被任何 Mod 依赖的留在第一层，被依赖的依次向右移动
            Dim Layers As New Dictionary(Of ModGraphNode, Integer)
            For Each Node In Visible
                Layers(Node) = 0
            Next
            For Round As Integer = 1 To Visible.Count
                Dim IsChanged As Boolean = False
                For Each Node In Visible
                    If Not Outgoings.ContainsKey(Node) Then Continue For
                    For Each Target In Outgoings(Node)
                        If Not Layers.ContainsKey(Target) Then Continue For
                        If Layers(Target) < Layers(Node) + 1 Then
                            Layers(Target) = Layers(Node) + 1
                            IsChanged = True
                        End If
                    Next
                Next
                If Not IsChanged Then Exit For
            Next

            '算出每个节点的位置
            Dim MaxLayer As Integer = 0
            For Each Node In Visible
                If Layers(Node) > MaxLayer Then MaxLayer = Layers(Node)
            Next
            Dim AutoPositions As New Dictionary(Of ModGraphNode, Point)
            Dim MaxSlot As Integer = 0
            For TargetLayer As Integer = 0 To MaxLayer
                Dim Column As New List(Of ModGraphNode)
                For Each Node In Visible
                    If Layers(Node) = TargetLayer Then Column.Add(Node)
                Next
                '有问题的排在上边
                Column = Column.OrderBy(Function(SourceNode) If(SourceNode.HasProblem, 0, 1)).
                         ThenBy(Function(SourceNode) SourceNode.DisplayName.Lower).ToList()
                For Slot As Integer = 0 To Column.Count - 1
                    AutoPositions(Column(Slot)) = New Point(TargetLayer * (NodeWidth + NodeGapX), Slot * (NodeHeight + NodeGapY))
                Next
                If Column.Count > MaxSlot Then MaxSlot = Column.Count
            Next
            For Each Node In Visible
                Dim ManualPos As Point = New Point(-1, -1)
                If ManualPositions.TryGetValue(Node, ManualPos) Then
                    Positions(Node) = ManualPos
                Else
                    Positions(Node) = AutoPositions(Node)
                End If
            Next
            '画布要装得下所有节点，包括被拖到远处的
            Dim RightEdge As Double = 0, BottomEdge As Double = 0
            For Each Node In Visible
                If Positions(Node).X + NodeWidth > RightEdge Then RightEdge = Positions(Node).X + NodeWidth
                If Positions(Node).Y + NodeHeight > BottomEdge Then BottomEdge = Positions(Node).Y + NodeHeight
            Next
            PanGraph.Width = RightEdge + 20
            PanGraph.Height = BottomEdge + 20

            '先画连线再画节点，让节点压在线上面
            For Each Link In GraphLinks
                DrawLink(Link)
            Next
            For Each Node In Visible
                Dim Card = BuildNodeCard(Node)
                Canvas.SetLeft(Card, Positions(Node).X)
                Canvas.SetTop(Card, Positions(Node).Y)
                PanGraph.Children.Add(Card)
                NodeCards(Node) = Card
            Next
            ApplyHighlight()
        Catch ex As Exception
            Logger.Warn(ex, "绘制模组依赖图失败")
        End Try
    End Sub

#Region "连线"

    ''' <summary>
    ''' 画一条连线。Path 与 System.IO.Path 同名，必须写全命名空间。
    ''' </summary>
    Private Sub DrawLink(Link As ModGraphLink)
        If Link.FromNode Is Nothing OrElse Link.ToNode Is Nothing Then Return
        If Not Positions.ContainsKey(Link.FromNode) OrElse Not Positions.ContainsKey(Link.ToNode) Then Return
        Dim LinkLine As New System.Windows.Shapes.Path With {.Tag = Link}
        If Link.Kind = ModGraphLinkKind.Dependency Then
            LinkLine.Stroke = New SolidColorBrush(Color.FromRgb(159, 179, 200))
        Else
            LinkLine.Stroke = New SolidColorBrush(Color.FromRgb(217, 59, 59))
            LinkLine.StrokeDashArray = New DoubleCollection From {4, 3}
        End If
        LinkLine.StrokeThickness = 1.2
        LinkLine.ToolTip = $"{Link.FromNode.DisplayName} → {Link.ToNode.DisplayName}　{Link.Label}"
        UpdateLinkGeometry(LinkLine)
        PanGraph.Children.Add(LinkLine)
        AllLines.Add(LinkLine)
        '记下每个节点关联的连线，拖动时只需要重算这些线
        Dim EndNodes() As ModGraphNode = {Link.FromNode, Link.ToNode}
        For Each Node In EndNodes
            If Node Is Nothing Then Continue For
            If Not NodeLines.ContainsKey(Node) Then NodeLines(Node) = New List(Of System.Windows.Shapes.Path)
            NodeLines(Node).Add(LinkLine)
        Next
    End Sub
    ''' <summary>
    ''' 按两个端点的当前位置重算一条连线的形状。
    ''' </summary>
    Private Sub UpdateLinkGeometry(Line As System.Windows.Shapes.Path)
        Dim Link = TryCast(Line.Tag, ModGraphLink)
        If Link Is Nothing Then Return
        Dim FromPos = Positions(Link.FromNode)
        Dim ToPos = Positions(Link.ToNode)
        Dim StartX = FromPos.X + NodeWidth
        Dim EndX = ToPos.X
        '目标在左边时改为从左侧出线，避免出现别扭的反向连线
        If ToPos.X < FromPos.X Then
            StartX = FromPos.X
            EndX = ToPos.X + NodeWidth
        End If
        Dim StartPoint As New Point(StartX, FromPos.Y + NodeHeight / 2)
        Dim EndPoint As New Point(EndX, ToPos.Y + NodeHeight / 2)
        If Link.Kind = ModGraphLinkKind.Dependency Then
            Line.Data = New LineGeometry(StartPoint, EndPoint)
        Else
            '冲突与缺少前置画成横向外扩的贝塞尔曲线
            Dim Offset = Math.Max(30, Math.Abs(EndPoint.X - StartPoint.X) / 3)
            Dim Figure As New PathFigure With {.StartPoint = StartPoint}
            Dim Segment As New BezierSegment With {.Point1 = New Point(StartPoint.X + Offset, StartPoint.Y), .Point2 = New Point(EndPoint.X - Offset, EndPoint.Y), .Point3 = EndPoint}
            Figure.Segments.Add(Segment)
            Dim LinkGeometry As New PathGeometry
            LinkGeometry.Figures.Add(Figure)
            Line.Data = LinkGeometry
        End If
    End Sub
    Private Sub RefreshNodeLines(Node As ModGraphNode)
        Dim Lines As List(Of System.Windows.Shapes.Path) = Nothing
        If Not NodeLines.TryGetValue(Node, Lines) Then Return
        For Each Line In Lines
            UpdateLinkGeometry(Line)
        Next
    End Sub

#End Region

#Region "节点卡片"

    ''' <summary>
    ''' 生成一个节点卡片。
    ''' </summary>
    Private Function BuildNodeCard(Node As ModGraphNode) As Border
        Dim TitleLine As New TextBlock With {
            .Text = If(Node.HasProblem, "！ ", "") & Node.DisplayName,
            .FontSize = 13,
            .FontWeight = FontWeights.SemiBold,
            .TextTrimming = TextTrimming.CharacterEllipsis}
        Dim DetailText = Node.ModId & " · " & Node.VersionText
        If Not Node.IsEnabled Then DetailText &= " · 已禁用"
        If Node.HasProblem Then DetailText &= " · " & Node.ProblemFlags.Join("；")
        Dim DetailLine As New TextBlock With {
            .Text = DetailText,
            .FontSize = 11,
            .Opacity = 0.6,
            .Margin = New Thickness(0, 3, 0, 0),
            .TextTrimming = TextTrimming.CharacterEllipsis}

        Dim CardPanel As New StackPanel With {.Margin = New Thickness(8, 5, 8, 5)}
        CardPanel.Children.Add(TitleLine)
        CardPanel.Children.Add(DetailLine)

        Dim Result As New Border With {
            .Width = NodeWidth,
            .Height = NodeHeight,
            .CornerRadius = New CornerRadius(4),
            .Background = StateFill(Node.NodeState),
            .BorderBrush = StateStroke(Node.NodeState),
            .Child = CardPanel,
            .Tag = Node,
            .Cursor = System.Windows.Input.Cursors.SizeAll}
        '崩溃日志点名的 Mod 加粗边框
        Result.BorderThickness = New Thickness(If(Node.CrashSuspect, 3, 2))
        Result.ToolTip = BuildNodeTip(Node)
        AddHandler Result.PreviewMouseLeftButtonDown, AddressOf Node_MouseDown
        AddHandler Result.PreviewMouseMove, AddressOf Node_MouseMove
        AddHandler Result.PreviewMouseLeftButtonUp, AddressOf Node_MouseUp
        AddHandler Result.LostMouseCapture, AddressOf Node_MouseCaptureLost
        Return Result
    End Function
    ''' <summary>
    ''' 节点上完整的信息，鼠标指上去时显示。
    ''' </summary>
    Private Function BuildNodeTip(Node As ModGraphNode) As String
        Dim Result As New List(Of String)
        Result.Add(Node.DisplayName)
        If Node.FileName IsNot Nothing Then Result.Add(Node.FileName)
        Result.Add($"Mod ID：{Node.ModId}　版本：{Node.VersionText}　加载器：{Node.Platform}")
        If Not Node.IsEnabled Then Result.Add("该文件当前已被禁用，不参与游戏。")
        If Node.GameVersions.Any() Then Result.Add("声明支持的游戏版本：" & Node.GameVersions.Join("、"))
        If Node.BrokenGameVersions.Any() Then Result.Add("声明冲突的游戏版本：" & Node.BrokenGameVersions.Join("、"))
        If Node.Dependencies.Any() Then Result.Add("依赖：" & Node.Dependencies.Join("、"))
        If Node.Provides.Any() Then Result.Add("同时提供：" & Node.Provides.Join("、"))
        If Node.Conflicts.Any() Then Result.Add("声明冲突：" & Node.Conflicts.Join("、"))
        For Each FlagText In Node.ProblemFlags
            Result.Add("问题：" & FlagText)
        Next
        If Node.CrashSuspect Then Result.Add("崩溃日志中提到了这个 Mod。")
        Result.Add(vbLf & "按住并拖动可以改变位置，单击可以只看与它相关的连线。")
        Return Result.Join(vbLf)
    End Function

    Private Sub Node_MouseDown(sender As Object, e As MouseButtonEventArgs)
        Dim Card = DirectCast(sender, Border)
        Dim Node = TryCast(Card.Tag, ModGraphNode)
        If Node Is Nothing OrElse Not Positions.ContainsKey(Node) Then Return
        DragNode = Node
        ClickCandidate = Node
        DragStart = e.GetPosition(PanGraph)
        DragNodeStart = Positions(Node)
        IsDragging = False
        Card.CaptureMouse()
        e.Handled = True
    End Sub
    Private Sub Node_MouseMove(sender As Object, e As MouseEventArgs)
        If DragNode Is Nothing Then Return
        Dim Card = DirectCast(sender, Border)
        Dim CurrentPos As Point = e.GetPosition(PanGraph)
        If Not IsDragging Then
            If Math.Abs(CurrentPos.X - DragStart.X) < DragThreshold AndAlso Math.Abs(CurrentPos.Y - DragStart.Y) < DragThreshold Then Return
            IsDragging = True
        End If
        Dim NewPos As New Point(Math.Max(0, DragNodeStart.X + CurrentPos.X - DragStart.X), Math.Max(0, DragNodeStart.Y + CurrentPos.Y - DragStart.Y))
        Positions(DragNode) = NewPos
        ManualPositions(DragNode) = NewPos
        Canvas.SetLeft(Card, NewPos.X)
        Canvas.SetTop(Card, NewPos.Y)
        '拖到画布边缘之外时把画布撑大，否则节点会被滚动条挡住
        If NewPos.X + NodeWidth + 20 > PanGraph.Width Then PanGraph.Width = NewPos.X + NodeWidth + 20
        If NewPos.Y + NodeHeight + 20 > PanGraph.Height Then PanGraph.Height = NewPos.Y + NodeHeight + 20
        RefreshNodeLines(DragNode)
        e.Handled = True
    End Sub
    Private Sub Node_MouseUp(sender As Object, e As MouseButtonEventArgs)
        Dim Card = DirectCast(sender, Border)
        Dim Node = TryCast(Card.Tag, ModGraphNode)
        Dim WasDragging = IsDragging
        IsDragging = False
        DragNode = Nothing
        '释放捕获会同步触发 LostMouseCapture，所以先把判断要用的值全部取出来
        If Card.IsMouseCaptured Then Card.ReleaseMouseCapture()
        e.Handled = True
        '没有移动过就是单击：切换选中，只看与它相关的连线
        If Not WasDragging AndAlso Node IsNot Nothing AndAlso ReferenceEquals(Node, ClickCandidate) Then
            SelectedNode = If(SelectedNode Is Node, Nothing, Node)
            ApplyHighlight()
        End If
        ClickCandidate = Nothing
    End Sub
    Private Sub Node_MouseCaptureLost(sender As Object, e As MouseEventArgs)
        '捕获被系统收回（例如 Alt+Tab）时停止跟随，单击判定仍由 MouseUp 负责
        DragNode = Nothing
        IsDragging = False
    End Sub

#End Region

#Region "高亮"

    ''' <summary>
    ''' 按 SelectedNode 调整所有节点与连线的透明度。未选中时恢复默认样式。
    ''' </summary>
    Private Sub ApplyHighlight()
        If SelectedNode IsNot Nothing AndAlso Not Positions.ContainsKey(SelectedNode) Then SelectedNode = Nothing
        Dim Distances As New Dictionary(Of ModGraphNode, Integer)
        If SelectedNode IsNot Nothing Then Distances = GetNodeDistances(SelectedNode)

        For Each Pair In NodeCards
            Dim Node = Pair.Key
            Dim Card = Pair.Value
            Card.BorderBrush = StateStroke(Node.NodeState)
            Card.BorderThickness = New Thickness(If(Node.CrashSuspect, 3, 2))
            Card.Opacity = 1
            If SelectedNode IsNot Nothing Then
                Dim Distance As Integer = 0
                If Not Distances.TryGetValue(Node, Distance) Then
                    Card.Opacity = 0.25
                ElseIf Distance = 0 Then
                    Card.BorderBrush = New SolidColorBrush(Color.FromRgb(19, 112, 243))
                    Card.BorderThickness = New Thickness(4)
                ElseIf Distance = 1 Then
                    Card.Opacity = 1
                ElseIf Distance = 2 Then
                    Card.Opacity = 0.7
                Else
                    Card.Opacity = 0.25
                End If
            End If
        Next

        For Each Line In AllLines
            Dim Link = DirectCast(Line.Tag, ModGraphLink)
            Line.Opacity = 0.75
            Line.StrokeThickness = 1.2
            If SelectedNode IsNot Nothing Then
                Dim FromDistance As Integer, ToDistance As Integer
                Dim IsRelated = Distances.TryGetValue(Link.FromNode, FromDistance) AndAlso Distances.TryGetValue(Link.ToNode, ToDistance)
                If Not IsRelated Then
                    Line.Opacity = 0.05
                ElseIf Math.Min(FromDistance, ToDistance) = 0 Then
                    '直接连到选中节点的线
                    Line.Opacity = 1
                    Line.StrokeThickness = 2.4
                ElseIf Math.Max(FromDistance, ToDistance) <= HighlightDepth Then
                    Line.Opacity = 0.5
                    Line.StrokeThickness = 1.4
                Else
                    Line.Opacity = 0.05
                End If
            End If
        Next
    End Sub
    ''' <summary>
    ''' 从某个节点出发做广度优先，返回它和周围几层节点的距离（0 = 自身）。
    ''' </summary>
    Private Function GetNodeDistances(Start As ModGraphNode) As Dictionary(Of ModGraphNode, Integer)
        Dim Result As New Dictionary(Of ModGraphNode, Integer)
        Result(Start) = 0
        Dim Queue As New List(Of ModGraphNode) From {Start}
        Do While Queue.Any
            Dim CurrentNode = Queue.First
            Queue.RemoveAt(0)
            Dim CurrentDistance = Result(CurrentNode)
            If CurrentDistance >= HighlightDepth Then Continue Do
            For Each Link In GraphLinks
                If Link.FromNode Is Nothing OrElse Link.ToNode Is Nothing Then Continue For
                Dim NextNode As ModGraphNode = Nothing
                If Link.FromNode Is CurrentNode Then
                    NextNode = Link.ToNode
                ElseIf Link.ToNode Is CurrentNode Then
                    NextNode = Link.FromNode
                End If
                If NextNode Is Nothing OrElse Not Positions.ContainsKey(NextNode) Then Continue For
                If Result.ContainsKey(NextNode) Then Continue For
                Result(NextNode) = CurrentDistance + 1
                Queue.Add(NextNode)
            Next
        Loop
        Return Result
    End Function

#End Region

    Private Shared Function StateStroke(NodeState As ModNodeState) As SolidColorBrush
        Select Case NodeState
            Case ModNodeState.危险 : Return New SolidColorBrush(Color.FromRgb(217, 59, 59))
            Case ModNodeState.警告 : Return New SolidColorBrush(Color.FromRgb(224, 138, 0))
            Case Else : Return New SolidColorBrush(Color.FromRgb(111, 138, 168))
        End Select
    End Function
    Private Shared Function StateFill(NodeState As ModNodeState) As SolidColorBrush
        Select Case NodeState
            Case ModNodeState.危险 : Return New SolidColorBrush(Color.FromRgb(255, 245, 245))
            Case ModNodeState.警告 : Return New SolidColorBrush(Color.FromRgb(255, 248, 236))
            Case Else : Return New SolidColorBrush(Color.FromRgb(247, 250, 253))
        End Select
    End Function

    '缩放与翻页
    Private Sub PanScroll_PreviewMouseWheel(sender As Object, e As MouseWheelEventArgs) Handles PanScroll.PreviewMouseWheel
        '按住 Ctrl 滚轮是缩放，直接滚动则是普通的翻页
        If (Keyboard.Modifiers And ModifierKeys.Control) = 0 Then Return
        e.Handled = True
        SetZoom(TransZoom.ScaleX * If(e.Delta > 0, 1.12, 1 / 1.12))
    End Sub
    Private Sub SetZoom(NewValue As Double)
        Dim Scale = Math.Max(0.4, Math.Min(2, NewValue))
        TransZoom.ScaleX = Scale
        TransZoom.ScaleY = Scale
    End Sub

    '在空白处单击：取消选中
    Private Sub PanGraph_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles PanGraph.MouseLeftButtonDown
        If Not ReferenceEquals(e.OriginalSource, PanGraph) Then Return
        If SelectedNode Is Nothing Then Return
        SelectedNode = Nothing
        ApplyHighlight()
    End Sub

    Private Sub BtnFit_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnFit.Click
        If PanGraph.Width <= 0 OrElse PanGraph.Height <= 0 Then Return
        Dim ScaleX = Math.Max(60, PanScroll.ViewportWidth - 20) / PanGraph.Width
        Dim ScaleY = Math.Max(60, PanScroll.ViewportHeight - 20) / PanGraph.Height
        SetZoom(Math.Min(ScaleX, ScaleY))
        PanScroll.ScrollToHorizontalOffset(0)
        PanScroll.ScrollToVerticalOffset(0)
    End Sub
    Private Sub BtnResetLayout_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnResetLayout.Click
        If Not ManualPositions.Any() Then
            Hint("没有拖动过节点，无需重置。", HintType.Blue)
            Return
        End If
        ManualPositions.Clear()
        DrawGraph()
    End Sub
    Private Sub BtnRescan_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnRescan.Click
        If TargetInstance Is Nothing Then
            Hint("请先在 启动 页选择一个版本。", HintType.Red)
            Return
        End If
        PageOtherTest.RescanModGraph(TargetInstance, Nothing)
    End Sub
    Private Sub CheckOnlyProblem_Change(sender As Object, user As Boolean) Handles CheckOnlyProblem.Change
        DrawGraph()
    End Sub

    Private Sub FrmModGraph_Closed(sender As Object, e As EventArgs) Handles Me.Closed
        If ReferenceEquals(Current, Me) Then Current = Nothing
    End Sub

End Class
