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

    Private TargetInstance As McInstance = Nothing
    Private GraphNodes As List(Of ModGraphNode) = Nothing
    Private GraphLinks As List(Of ModGraphLink) = Nothing

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
            Dim Positions As New Dictionary(Of ModGraphNode, Point)
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
                    Positions(Column(Slot)) = New Point(TargetLayer * (NodeWidth + NodeGapX), Slot * (NodeHeight + NodeGapY))
                Next
                If Column.Count > MaxSlot Then MaxSlot = Column.Count
            Next
            PanGraph.Width = (MaxLayer + 1) * (NodeWidth + NodeGapX) - NodeGapX + 20
            PanGraph.Height = Math.Max(MaxSlot, 1) * (NodeHeight + NodeGapY) + 20

            '先画连线再画节点，让节点压在线上面
            For Each Link In GraphLinks
                DrawLink(Link, Positions)
            Next
            For Each Node In Visible
                Dim Card = BuildNodeCard(Node)
                Canvas.SetLeft(Card, Positions(Node).X)
                Canvas.SetTop(Card, Positions(Node).Y)
                PanGraph.Children.Add(Card)
            Next
        Catch ex As Exception
            Logger.Warn(ex, "绘制模组依赖图失败")
        End Try
    End Sub

    ''' <summary>
    ''' 画一条连线。Path 与 System.IO.Path 同名，必须写全命名空间。
    ''' </summary>
    Private Sub DrawLink(Link As ModGraphLink, Positions As Dictionary(Of ModGraphNode, Point))
        If Link.FromNode Is Nothing OrElse Link.ToNode Is Nothing Then Return
        Dim FromPos As Point = New Point(-1, -1), ToPos As Point = New Point(-1, -1)
        If Not Positions.TryGetValue(Link.FromNode, FromPos) Then Return
        If Not Positions.TryGetValue(Link.ToNode, ToPos) Then Return
        Dim StartX = FromPos.X + NodeWidth
        Dim EndX = ToPos.X
        '目标在左边时改为从左侧出线，避免出现别扭的反向连线
        If ToPos.X < FromPos.X Then
            StartX = FromPos.X
            EndX = ToPos.X + NodeWidth
        End If
        Dim StartPoint As New Point(StartX, FromPos.Y + NodeHeight / 2)
        Dim EndPoint As New Point(EndX, ToPos.Y + NodeHeight / 2)

        Dim LinkLine As New System.Windows.Shapes.Path
        If Link.Kind = ModGraphLinkKind.Dependency Then
            LinkLine.Data = New LineGeometry(StartPoint, EndPoint)
            LinkLine.Stroke = New SolidColorBrush(Color.FromRgb(159, 179, 200))
        Else
            '冲突与缺少前置画成横向外扩的贝塞尔曲线
            Dim Offset = Math.Max(30, Math.Abs(EndPoint.X - StartPoint.X) / 3)
            Dim Figure As New PathFigure With {.StartPoint = StartPoint}
            Dim Segment As New BezierSegment With {.Point1 = New Point(StartPoint.X + Offset, StartPoint.Y), .Point2 = New Point(EndPoint.X - Offset, EndPoint.Y), .Point3 = EndPoint}
            Figure.Segments.Add(Segment)
            Dim LinkGeometry As New PathGeometry
            LinkGeometry.Figures.Add(Figure)
            LinkLine.Data = LinkGeometry
            LinkLine.Stroke = New SolidColorBrush(Color.FromRgb(217, 59, 59))
            LinkLine.StrokeDashArray = New DoubleCollection From {4, 3}
        End If
        LinkLine.StrokeThickness = 1.2
        LinkLine.Opacity = 0.75
        LinkLine.ToolTip = $"{Link.FromNode.DisplayName} → {Link.ToNode.DisplayName}　{Link.Label}"
        PanGraph.Children.Add(LinkLine)
    End Sub

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
            .Child = CardPanel}
        '崩溃日志点名的 Mod 加粗边框
        Result.BorderThickness = New Thickness(If(Node.CrashSuspect, 3, 2))
        Result.ToolTip = BuildNodeTip(Node)
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
        Return Result.Join(vbLf)
    End Function

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

    Private Sub BtnFit_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnFit.Click
        If PanGraph.Width <= 0 OrElse PanGraph.Height <= 0 Then Return
        Dim ScaleX = Math.Max(60, PanScroll.ViewportWidth - 20) / PanGraph.Width
        Dim ScaleY = Math.Max(60, PanScroll.ViewportHeight - 20) / PanGraph.Height
        SetZoom(Math.Min(ScaleX, ScaleY))
        PanScroll.ScrollToHorizontalOffset(0)
        PanScroll.ScrollToVerticalOffset(0)
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
