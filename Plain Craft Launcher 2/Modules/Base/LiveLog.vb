Imports System.Collections.Generic

''' <summary>
''' 实时显示日志：在日志线程把每一条日志放入缓冲区，再由独立窗口增量读取。
''' </summary>
Public Module LiveLog

    ''' <summary>
    ''' 缓冲区最多保留的行数。
    ''' </summary>
    Public Const MaxLines As Integer = 2000
    ''' <summary>
    ''' 是否正在记录日志。会在日志线程上被读取。
    ''' </summary>
    Public IsEnabled As Boolean = False

    Private ReadOnly BufferLock As New Object
    Private ReadOnly Lines As New List(Of String)
    Private FirstSequence As Long = 0
    Private TotalSequence As Long = 0
    Private Frm As FrmLiveLog = Nothing

    ''' <summary>
    ''' 将一条格式化后的日志放入缓冲区。可能在任意线程上被调用。
    ''' </summary>
    Public Sub Append(Text As String)
        If Not IsEnabled OrElse Text Is Nothing Then Return
        Try
            SyncLock BufferLock
                For Each Line In Text.SplitLines(False)
                    Lines.Add(Line)
                    TotalSequence += 1
                Next
                '超出上限时丢弃最前面的部分
                If Lines.Count > MaxLines Then
                    Dim Drop As Integer = Lines.Count - MaxLines
                    Lines.RemoveRange(0, Drop)
                    FirstSequence += Drop
                End If
            End SyncLock
        Catch
            '记录日志失败不应该影响日志本身
        End Try
    End Sub
    ''' <summary>
    ''' 获取 Index 之后的所有日志行，并更新 Index。若没有新日志则返回空数组。
    ''' </summary>
    Public Function GetNew(ByRef Index As Long) As String()
        SyncLock BufferLock
            If Index < FirstSequence Then Index = FirstSequence '缓冲区头部已被丢弃
            If Index >= TotalSequence Then Return New String() {}
            Dim Result = Lines.GetRange(CInt(Index - FirstSequence), CInt(TotalSequence - Index)).ToArray
            Index = TotalSequence
            Return Result
        End SyncLock
    End Function
    ''' <summary>
    ''' 获取缓冲区中的全部日志行。
    ''' </summary>
    Public Function GetAll() As String()
        SyncLock BufferLock
            Return Lines.ToArray
        End SyncLock
    End Function
    ''' <summary>
    ''' 缓冲区中当前的日志行数。
    ''' </summary>
    Public Function Count() As Integer
        SyncLock BufferLock
            Return Lines.Count
        End SyncLock
    End Function
    ''' <summary>
    ''' 清空缓冲区。
    ''' </summary>
    Public Sub Clear()
        SyncLock BufferLock
            Lines.Clear()
            FirstSequence = TotalSequence
        End SyncLock
    End Sub

    ''' <summary>
    ''' 按 Value 开始或停止记录，并相应地打开或关闭窗口。必须在 UI 线程上调用。
    ''' </summary>
    Public Sub Apply(Value As Boolean)
        IsEnabled = Value
        If Value Then
            If Frm Is Nothing Then
                Frm = New FrmLiveLog()
                If FrmMain IsNot Nothing Then Frm.Owner = FrmMain
                Frm.Show()
            Else
                Frm.Activate()
            End If
        ElseIf Frm IsNot Nothing Then
            Dim Temp = Frm
            Frm = Nothing
            Temp.CloseReal()
        End If
    End Sub
    ''' <summary>
    ''' 窗口被用户主动关闭时调用，使设置项与实际状态保持一致。
    ''' </summary>
    Public Sub CloseByUser()
        Settings.Set("SystemLogLive", False)
        Apply(False)
    End Sub

End Module
