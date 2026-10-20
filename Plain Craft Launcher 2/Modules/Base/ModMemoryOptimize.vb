Imports System.Runtime.InteropServices

''' <summary>
''' 内存优化：关闭指定进程，并整理进程工作集或系统文件缓存。
''' </summary>
Public Module ModMemoryOptimize

    Public Enum MemoryOptimizeMode
        ''' <summary>依次执行逐进程工作集整理与系统内存列表清理。</summary>
        Auto = 0
        ''' <summary>逐个进程清空工作集。</summary>
        WorkingSet = 1
        ''' <summary>通过 NtSetSystemInformation 整理系统工作集并清空待机页面列表。</summary>
        SystemList = 2
    End Enum

    Private Const PROCESS_ALL_ACCESS As UInteger = &H1F0FFF
    Private Const QUOTA_LIMITS_HARDWS_MIN_DISABLE As UInteger = &H2
    Private Const TOKEN_ADJUST_PRIVILEGES As UInteger = &H20
    Private Const TOKEN_QUERY As UInteger = &H8
    Private Const SE_PRIVILEGE_ENABLED As UInteger = &H2
    'SYSTEM_INFORMATION_CLASS.SystemMemoryListInformation
    Private Const SystemMemoryListInformation As Integer = &H58
    'SYSTEM_MEMORY_LIST_COMMAND
    Private Const MemoryEmptyWorkingSets As Integer = 2
    Private Const MemoryPurgeStandbyList As Integer = 4

    <StructLayout(LayoutKind.Sequential)>
    Private Structure LUID
        Public LowPart As UInteger
        Public HighPart As UInteger
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Private Structure LUID_AND_ATTRIBUTES
        Public Luid As LUID
        Public Attributes As UInteger
    End Structure
    <StructLayout(LayoutKind.Sequential)>
    Private Structure TOKEN_PRIVILEGES
        Public PrivilegeCount As UInteger
        Public Privileges As LUID_AND_ATTRIBUTES
    End Structure

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function OpenProcess(dwDesiredAccess As UInteger, bInheritHandle As Boolean, dwProcessId As UInteger) As IntPtr
    End Function
    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function CloseHandle(hObject As IntPtr) As Boolean
    End Function
    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function SetProcessWorkingSetSizeEx(hProcess As IntPtr, lpMinimumWorkingSetSize As IntPtr, lpMaximumWorkingSetSize As IntPtr, dwFlags As UInteger) As Boolean
    End Function
    <DllImport("kernel32.dll")>
    Private Function GetCurrentProcess() As IntPtr
    End Function
    <DllImport("ntdll.dll")>
    Private Function NtSetSystemInformation(SystemInformationClass As Integer, ByRef Buffer As Integer, Length As Integer) As Integer
    End Function
    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function OpenProcessToken(ProcessHandle As IntPtr, DesiredAccess As UInteger, ByRef TokenHandle As IntPtr) As Boolean
    End Function
    <DllImport("advapi32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Private Function LookupPrivilegeValue(lpSystemName As String, lpName As String, ByRef lpLuid As LUID) As Boolean
    End Function
    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function AdjustTokenPrivileges(TokenHandle As IntPtr, DisableAllPrivileges As Boolean, ByRef NewState As TOKEN_PRIVILEGES, BufferLength As Integer, PreviousState As IntPtr, ReturnLength As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Function GetWindowTextLength(hWnd As IntPtr) As Integer
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Function GetWindowText(hWnd As IntPtr, lpString As StringBuilder, nMaxCount As Integer) As Integer
    End Function

    ''' <summary>
    ''' 一个可被选择关闭的进程条目。
    ''' </summary>
    Public Class MemoryProcessEntry
        Public Id As Integer
        Public Name As String
        Public Title As String
        Public Ram As Long
        Public Function DisplayName() As String
            Dim RamText = If(Ram > 0, $"，{Ram / 1024 / 1024:F0} MB", "")
            Return If(String.IsNullOrEmpty(Title), $"{Name}{RamText}", $"{Title}（{Name}{RamText}）")
        End Function
    End Class

    '绝不允许关闭的进程，避免把系统拖死
    Private ReadOnly ProtectedNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "dwm", "fontdrvhost", "explorer", "taskhostw", "sihost", "ctfmon", "audiodg", "runtimebroker",
        "searchhost", "searchindexer", "startmenuexperiencehost", "textinputhost", "shellexperiencehost",
        "memory compression", "dllhost", "conhost", "win32sysloader"}

    Private Function IsSelf(Name As String) As Boolean
        Return String.Equals(Name, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' 列出可安全选择关闭的进程（拥有主窗口的应用程序），按内存占用从高到低排列。
    ''' </summary>
    Public Function GetProcessList() As List(Of MemoryProcessEntry)
        Dim Result As New List(Of MemoryProcessEntry)
        For Each P In Process.GetProcesses()
            Try
                If P.MainWindowHandle <> IntPtr.Zero Then
                    Dim Name = P.ProcessName
                    If Not ProtectedNames.Contains(Name) AndAlso Not IsSelf(Name) Then
                        Result.Add(New MemoryProcessEntry With {
                            .Id = P.Id,
                            .Name = Name,
                            .Title = GetWindowTitle(P.MainWindowHandle),
                            .Ram = P.WorkingSet64})
                    End If
                End If
            Catch ex As Exception
                Logger.Warn(ex, "枚举进程失败")
            End Try
        Next
        Return Result.OrderByDescending(Function(P) P.Ram).ToList()
    End Function

    Private Function GetWindowTitle(hWnd As IntPtr) As String
        Try
            Dim Length = GetWindowTextLength(hWnd)
            If Length <= 0 Then Return ""
            Dim Builder As New StringBuilder(Length + 2)
            GetWindowText(hWnd, Builder, Builder.Capacity)
            Return Builder.ToString.Trim
        Catch ex As Exception
            Logger.Warn(ex, "获取窗口标题失败")
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' 高级设置中保存的、内存优化时需要关闭的进程名。
    ''' </summary>
    Public Function ReadKillList() As List(Of String)
        Dim Raw = Settings.Get(Of String)("MemoryOptimizeKill")
        If String.IsNullOrEmpty(Raw) Then Return New List(Of String)
        Return Raw.Split(","c).Select(Function(s) s.Trim).Where(Function(s) s <> "").Distinct(StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Public Sub WriteKillList(List As List(Of String))
        Settings.Set("MemoryOptimizeKill", String.Join(",", List.Where(Function(s) Not ProtectedNames.Contains(s) AndAlso Not IsSelf(s))))
    End Sub

    Public Function ReadMode() As MemoryOptimizeMode
        Return CType(Settings.Get(Of Integer)("MemoryOptimizeMode"), MemoryOptimizeMode)
    End Function

    Public Sub WriteMode(Mode As MemoryOptimizeMode)
        Settings.Set("MemoryOptimizeMode", CInt(Mode))
    End Sub

    ''' <summary>
    ''' 启用调试与配额权限，使管理员身份能够触达其他用户的进程。
    ''' </summary>
    Private Sub EnableMemoryPrivileges()
        For Each Name In {"SeDebugPrivilege", "SeProfileSingleProcessPrivilege", "SeIncreaseQuotaPrivilege"}
            Dim Token = IntPtr.Zero
            Try
                If Not OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES Or TOKEN_QUERY, Token) Then Continue For
                Dim Luid As LUID
                If Not LookupPrivilegeValue(Nothing, Name, Luid) Then Continue For
                Dim NewState As New TOKEN_PRIVILEGES With {
                    .PrivilegeCount = 1,
                    .Privileges = New LUID_AND_ATTRIBUTES With {.Luid = Luid, .Attributes = SE_PRIVILEGE_ENABLED}}
                AdjustTokenPrivileges(Token, False, NewState, 0, IntPtr.Zero, IntPtr.Zero)
            Catch ex As Exception
                Logger.Warn(ex, $"启用权限失败：{Name}")
            Finally
                If Token <> IntPtr.Zero Then CloseHandle(Token)
            End Try
        Next
    End Sub

    ''' <summary>
    ''' 清空各进程的工作集，把暂不使用的内存页交还给系统。返回成功整理的进程数。
    ''' </summary>
    Private Function TrimProcessWorkingSets(SelfId As Integer, ByRef FailedCount As Integer) As Integer
        Dim Finished As Integer = 0
        FailedCount = 0
        '本进程只能使用当前进程句柄，OpenProcess 拿到的自身句柄权限不足
        SetProcessWorkingSetSizeEx(GetCurrentProcess(), New IntPtr(-1), New IntPtr(-1), QUOTA_LIMITS_HARDWS_MIN_DISABLE)
        For Each P In Process.GetProcesses()
            Dim Handle = IntPtr.Zero
            Try
                If P.Id = SelfId Then Continue For
                '只申请 PROCESS_SET_QUOTA 时，该调用会对所有外部进程返回 ACCESS_DENIED
                Handle = OpenProcess(PROCESS_ALL_ACCESS, False, P.Id)
                If Handle = IntPtr.Zero Then
                    FailedCount += 1
                    Continue For
                End If
                If SetProcessWorkingSetSizeEx(Handle, New IntPtr(-1), New IntPtr(-1), QUOTA_LIMITS_HARDWS_MIN_DISABLE) Then
                    Finished += 1
                Else
                    FailedCount += 1
                End If
            Catch ex As Exception
                FailedCount += 1
            Finally
                If Handle <> IntPtr.Zero Then CloseHandle(Handle)
            End Try
        Next
        GC.Collect()
        GC.WaitForPendingFinalizers()
        GC.Collect()
        Return Finished
    End Function

    ''' <summary>
    ''' 通过系统调用整理系统工作集并清空待机页面列表。返回成功的指令数。
    ''' </summary>
    Private Function PurgeSystemMemoryLists() As Integer
        Dim Ok As Integer = 0
        For Each Command As Integer In New Integer() {MemoryEmptyWorkingSets, MemoryPurgeStandbyList}
            Try
                Dim Status = NtSetSystemInformation(SystemMemoryListInformation, Command, 4)
                Logger.Info($"清理系统内存列表（命令 {Command}）：NTSTATUS = 0x{Status:X8}")
                If Status = 0 Then Ok += 1
            Catch ex As Exception
                Logger.Warn(ex, $"清理系统内存列表失败（命令 {Command}）")
            End Try
        Next
        Return Ok
    End Function

    Private Function KillSelectedProcesses(SelfId As Integer) As List(Of String)
        Dim Killed As New List(Of String)
        If Not Settings.Get(Of Boolean)("MemoryOptimizeKillEnabled") Then Return Killed
        For Each Name In ReadKillList()
            If ProtectedNames.Contains(Name) OrElse IsSelf(Name) Then Continue For
            For Each P In Process.GetProcessesByName(Name)
                Try
                    If P.Id = SelfId Then Continue For
                    P.Kill()
                    P.WaitForExit(3000)
                    If Not Killed.Contains(Name, StringComparer.OrdinalIgnoreCase) Then Killed.Add(Name)
                Catch ex As Exception
                    Logger.Warn(ex, $"内存优化时关闭进程失败：{Name}")
                End Try
            Next
        Next
        Return Killed
    End Function

    ''' <summary>
    ''' 执行内存优化。必须在非 UI 线程调用。
    ''' </summary>
    ''' <param name="ShowHint">是否弹出结果提示。</param>
    Public Sub MemoryOptimizeRun(ShowHint As Boolean)
        Dim Before = My.Computer.Info.AvailablePhysicalMemory
        Dim SelfId = Process.GetCurrentProcess().Id

        Dim Killed = KillSelectedProcesses(SelfId)
        If Killed.Any Then Thread.Sleep(500) '等待被关闭进程的内存被系统回收
        If WindowsUtils.HasAdminRole() Then EnableMemoryPrivileges()

        Dim Actions As New List(Of String)
        Dim FailedCount As Integer
        Dim Preferred = ReadMode()
        Dim PrimaryOk As Boolean

        Select Case Preferred
            Case MemoryOptimizeMode.WorkingSet
                Dim Finished = TrimProcessWorkingSets(SelfId, FailedCount)
                PrimaryOk = Finished > 0
                If PrimaryOk Then Actions.Add($"整理 {Finished} 个进程的工作集")
            Case MemoryOptimizeMode.SystemList
                Dim Ok = PurgeSystemMemoryLists()
                PrimaryOk = Ok > 0
                If PrimaryOk Then Actions.Add("清理系统工作集与待机页面列表")
            Case Else
                Dim Finished = TrimProcessWorkingSets(SelfId, FailedCount)
                If Finished > 0 Then
                    Actions.Add($"整理 {Finished} 个进程的工作集")
                    PrimaryOk = True
                End If
                If PurgeSystemMemoryLists() > 0 Then
                    Actions.Add("清理系统工作集与待机页面列表")
                    PrimaryOk = True
                End If
        End Select

        '首选逻辑没有生效，自动改用另一种
        If Not PrimaryOk Then
            If Preferred = MemoryOptimizeMode.WorkingSet Then
                If PurgeSystemMemoryLists() > 0 Then Actions.Add("清理系统工作集与待机页面列表（已自动改用）")
            Else
                Dim Finished = TrimProcessWorkingSets(SelfId, FailedCount)
                If Finished > 0 Then Actions.Add($"整理 {Finished} 个进程的工作集（已自动改用）")
            End If
        End If

        Dim After = My.Computer.Info.AvailablePhysicalMemory
        Dim Freed = If(After > Before, CLng(After - Before), 0)
        Logger.Info($"内存优化（{Preferred}）：{String.Join("，", Actions)}，无法访问 {FailedCount} 个进程，关闭进程 {Killed.Count} 个，可用内存增加 {Freed / 1024 / 1024} MB")
        If Not ShowHint Then Return

        If Not Actions.Any Then
            Hint("内存优化没有生效：PCL 无法修改其他进程的内存。" & vbCrLf & vbCrLf &
                 "请完全关闭 PCL 后，右键选择 属性 → 兼容性 → 以管理员身份运行此程序，再试一次。" & vbCrLf &
                 "也可以在 高级设置 中切换 首选逻辑 后重试。", HintType.Red)
            Return
        End If
        Dim Result = $"内存优化完成：{String.Join("，", Actions)}，可用内存增加约 {Freed / 1024 / 1024} MB。"
        If Killed.Any Then Result &= vbCrLf & $"已关闭进程：{String.Join("、", Killed)}"
        If FailedCount > 0 AndAlso Not WindowsUtils.HasAdminRole() Then Result &= vbCrLf & $"有 {FailedCount} 个进程无法访问，以管理员身份运行可以扩大优化范围。"
        Hint(Result, HintType.Green)
    End Sub

End Module
