Imports System.Windows.Threading

Public Class FrmLiveLog

    Private ReadOnly UpdateTimer As New DispatcherTimer
    ''' <summary>
    ''' 已经读取到的日志序号。
    ''' </summary>
    Private Index As Long = 0
    Private DisplayedLines As Integer = 0
    Private IsRealClosing As Boolean = False

    Public Sub New()
        Try
            InitializeComponent()
            CheckAutoScroll.Checked = True
            AddHandler UpdateTimer.Tick, Sub() RefreshText()
            UpdateTimer.Interval = New TimeSpan(0, 0, 0, 0, 250)
            UpdateTimer.Start()
            RefreshText()
        Catch ex As Exception
            Logger.Error(ex, "初始化实时日志窗口失败", LogBehavior.Toast)
        End Try
    End Sub

    ''' <summary>
    ''' 把缓冲区里新增的日志追加到窗口中。
    ''' </summary>
    Private Sub RefreshText()
        Try
            Dim NewLines = LiveLog.GetNew(Index)
            If Not NewLines.Any() Then Return
            TxtLog.AppendText(NewLines.Join(vbCrLf) & vbCrLf)
            DisplayedLines += NewLines.Length
            LabCount.Text = $"缓冲区 {LiveLog.Count()} 行"
            '窗口中的文本同样只保留最近的一部分，避免内存无限增长
            If DisplayedLines >= LiveLog.MaxLines Then RebuildText()
            If CheckAutoScroll.Checked Then TxtLog.ScrollToEnd()
        Catch ex As Exception
            Logger.Warn(ex, "刷新实时日志窗口失败")
        End Try
    End Sub
    ''' <summary>
    ''' 用缓冲区中的全部内容替换窗口中的文本。
    ''' </summary>
    Private Sub RebuildText()
        Index = 0
        Dim NewLines = LiveLog.GetNew(Index)
        TxtLog.Text = If(NewLines.Any(), NewLines.Join(vbCrLf) & vbCrLf, "")
        DisplayedLines = NewLines.Length
    End Sub

    Private Sub CheckAutoScroll_Change() Handles CheckAutoScroll.Change
        If CheckAutoScroll.Checked Then TxtLog.ScrollToEnd()
    End Sub
    Private Sub BtnCopy_Click() Handles BtnCopy.Click
        ClipboardSet(TxtLog.Text)
    End Sub
    Private Sub BtnClear_Click() Handles BtnClear.Click
        LiveLog.Clear()
        RebuildText()
    End Sub

    Private Sub FrmLiveLog_Closing(sender As Object, e As ComponentModel.CancelEventArgs) Handles Me.Closing
        UpdateTimer.Stop()
        '由 X 关闭时先改回设置项，再由 LiveLog 真正关闭窗口，以保证勾选框与设置一致
        If Not IsRealClosing AndAlso Not IsProgramEnding Then
            e.Cancel = True
            LiveLog.CloseByUser()
        End If
    End Sub
    ''' <summary>
    ''' 真正关闭窗口，由 LiveLog 调用。
    ''' </summary>
    Public Sub CloseReal()
        IsRealClosing = True
        Close()
    End Sub

End Class
