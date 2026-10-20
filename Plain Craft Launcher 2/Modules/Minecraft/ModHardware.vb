Imports System.Management
Imports System.Runtime.InteropServices

Public Module ModHardware

    Private Const GpuMaxAge As Integer = 4 '驱动发布时间超过该年限则预警

#Region "阈值"

    ''' <summary>
    ''' 内存预警阈值，单位 GB。设置中保存的是滑动条值（0~31），对应 1~32 GB。
    ''' </summary>
    Public Function HardwareRamThreshold() As Double
        Return Settings.Get(Of Integer)("LaunchHardwareRam") + 1
    End Function
    ''' <summary>
    ''' CPU 预警阈值，单位百分比。设置中保存的是滑动条值（0~49），对应 50~99%。
    ''' </summary>
    Public Function HardwareCpuThreshold() As Integer
        Return Settings.Get(Of Integer)("LaunchHardwareCpu") + 50
    End Function
    ''' <summary>
    ''' 磁盘预警阈值，单位 GB。设置中保存的是滑动条值（0~49），对应 1~50 GB。
    ''' </summary>
    Public Function HardwareDiskThreshold() As Double
        Return Settings.Get(Of Integer)("LaunchHardwareDiskSize") + 1
    End Function

#End Region

#Region "读取"

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function GetSystemTimes(ByRef lpIdleTime As Long, ByRef lpKernelTime As Long, ByRef lpUserTime As Long) As Boolean
    End Function

    ''' <summary>
    ''' 总物理内存，单位 GB。
    ''' </summary>
    Public Function HardwareRamTotal() As Double
        Return My.Computer.Info.TotalPhysicalMemory / 1024 / 1024 / 1024
    End Function
    ''' <summary>
    ''' 当前可用物理内存，单位 GB。
    ''' </summary>
    Public Function HardwareRamAvailable() As Double
        Return My.Computer.Info.AvailablePhysicalMemory / 1024 / 1024 / 1024
    End Function

    ''' <summary>
    ''' 采样约 0.3 秒后返回整机 CPU 占用百分比，无法获取时返回 -1。会阻塞调用线程。
    ''' </summary>
    Public Function HardwareCpuUsage() As Double
        Try
            Dim Idle1 As Long, Kernel1 As Long, User1 As Long
            Dim Idle2 As Long, Kernel2 As Long, User2 As Long
            If Not GetSystemTimes(Idle1, Kernel1, User1) Then Return -1
            Thread.Sleep(300)
            If Not GetSystemTimes(Idle2, Kernel2, User2) Then Return -1
            'KernelTime 包含 IdleTime，因此忙时间为 (Kernel - Idle) + User
            Dim Total As Long = (Kernel2 - Kernel1) + (User2 - User1)
            If Total <= 0 Then Return -1
            Dim Busy As Long = (Kernel2 - Kernel1) - (Idle2 - Idle1) + (User2 - User1)
            Return Math.Round(Math.Max(0, Math.Min(100, Busy * 100 / CDbl(Total))), 1)
        Catch ex As Exception
            Logger.Warn(ex, "获取 CPU 占用失败")
            Return -1
        End Try
    End Function

    ''' <summary>
    ''' 各显卡信息，每项为 {名称, 驱动版本, 驱动日期 yyyyMMdd}。获取失败时返回空列表。
    ''' </summary>
    Public Function HardwareGpuList() As List(Of String())
        Dim Result As New List(Of String())
        Try
            Using Searcher As New ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController")
                Using Infos = Searcher.Get()
                    For Each Info As ManagementObject In Infos
                        Dim Name As String = Info("Name").ToString()
                        Dim Version As String = If(Info("DriverVersion") Is Nothing, "", Info("DriverVersion").ToString())
                        Dim DateText As String = If(Info("DriverDate") Is Nothing, "", Info("DriverDate").ToString())
                        If DateText.Length < 8 Then DateText = ""
                        Result.Add({Name, Version, If(DateText = "", "", DateText.Substring(0, 8))})
                    Next
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "获取显卡信息失败")
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' PCL 所在磁盘的剩余空间，单位 GB。无法获取时返回 -1。
    ''' </summary>
    Public Function HardwareDiskFree() As Double
        Try
            Return New DriveInfo(Path.GetPathRoot(Paths.Base)).AvailableFreeSpace / 1024 / 1024 / 1024
        Catch ex As Exception
            Logger.Warn(ex, "获取磁盘剩余空间失败")
            Return -1
        End Try
    End Function

    Private Function GpuDriverYear(Gpu As String()) As Integer
        If Gpu(2) = "" Then Return -1
        Try
            Return CInt(Val(Gpu(2).Substring(0, 4)))
        Catch
            Return -1
        End Try
    End Function

#End Region

#Region "检测"

    ''' <summary>
    ''' 按当前设置读取各项硬件指标，返回每一条预警的描述文本。列表为空表示硬件状况良好。
    ''' <para/>该方法会阻塞约 0.5 秒，且不受总开关 LaunchHardwareCheck 控制，请由调用方判断是否执行。
    ''' </summary>
    Public Function HardwareDetect() As List(Of String)
        Dim Warnings As New List(Of String)

        '内存，必选项
        Dim RamThreshold As Double = HardwareRamThreshold()
        Dim RamAvailable As Double = HardwareRamAvailable()
        If RamAvailable < RamThreshold Then
            Warnings.Add($"可用内存不足：当前仅剩约 {RamAvailable.ToString("F1")} GB，低于 {RamThreshold} GB 的预警阈值（总内存 {HardwareRamTotal().ToString("F1")} GB）。" & vbCrLf &
                         "关闭一些占内存的程序，或在 设置 → 启动 中调低阈值后再启动。")
        End If

        'CPU，必选项
        Dim CpuThreshold As Integer = HardwareCpuThreshold()
        Dim CpuUsage As Double = HardwareCpuUsage()
        If CpuUsage > CpuThreshold Then
            Warnings.Add($"CPU 占用过高：当前整机占用约 {CpuUsage.ToString("F0")}%，高于 {CpuThreshold}% 的预警阈值。" & vbCrLf &
                         "等正在编译、解压或直播的程序结束后再启动会更流畅。")
        End If

        '显卡，可选项
        If Settings.Get(Of Boolean)("LaunchHardwareGpu") Then
            Dim Gpus = HardwareGpuList()
            If Gpus.Count = 0 Then
                Warnings.Add("无法读取显卡信息：Windows 的显卡驱动服务可能未运行，PCL 无法判断显卡是否够用。")
            Else
                Dim ThisYear = DateTime.Now.Year
                For Each Gpu In Gpus
                    If Gpu(0).ContainsIgnoreCase("Basic Render") Then
                        Warnings.Add($"显卡 {Gpu(0)} 正在使用 Windows 的基础显示驱动，Minecraft 帧率会非常低。请安装显卡厂商的官方驱动。")
                    ElseIf GpuDriverYear(Gpu) < 0 Then
                        Warnings.Add($"显卡 {Gpu(0)} 的驱动版本（{If(Gpu(1) = "", "未知", Gpu(1))}）无法读取发布时间，可能过旧。")
                    ElseIf GpuDriverYear(Gpu) < ThisYear - GpuMaxAge Then
                        Warnings.Add($"显卡 {Gpu(0)} 的驱动发布于 {GpuDriverYear(Gpu)} 年，已经超过 {GpuMaxAge} 年。" & vbCrLf &
                                     "新驱动通常能显著提升 Minecraft 的帧率与稳定性。")
                    End If
                Next
            End If
        End If

        '磁盘，可选项
        If Settings.Get(Of Boolean)("LaunchHardwareDisk") Then
            Dim DiskThreshold As Double = HardwareDiskThreshold()
            Dim DiskFree As Double = HardwareDiskFree()
            If DiskFree >= 0 AndAlso DiskFree < DiskThreshold Then
                Warnings.Add($"磁盘剩余空间不足：PCL 所在磁盘仅剩约 {DiskFree.ToString("F1")} GB，低于 {DiskThreshold} GB 的预警阈值。" & vbCrLf &
                             "空间不足会导致下载失败、存档损坏，请清理后再启动。")
            End If
        End If

        Return Warnings
    End Function

    ''' <summary>
    ''' 当前各项硬件指标的文字描述，用于设置页的立即检测。会阻塞约 0.5 秒。
    ''' </summary>
    Public Function HardwareStatusText() As String
        Dim Lines As New List(Of String)
        Lines.Add($"CPU：{Environment.ProcessorCount} 线程，当前占用 {HardwareCpuUsage().ToString("F0")}%（预警阈值 {HardwareCpuThreshold()}%）")
        Lines.Add($"内存：可用 {HardwareRamAvailable().ToString("F1")} GB / 共 {HardwareRamTotal().ToString("F1")} GB（预警阈值 {HardwareRamThreshold().ToString("F1")} GB）")
        Dim Gpus = HardwareGpuList()
        If Gpus.Count = 0 Then
            Lines.Add("显卡：无法读取")
        Else
            Dim GpuTexts As New List(Of String)
            For Each Gpu In Gpus
                GpuTexts.Add($"{Gpu(0)}，驱动 {If(Gpu(1) = "", "未知", Gpu(1))}{If(Gpu(2) = "", "", "（" & Gpu(2).Substring(0, 4) & " 年）")}")
            Next
            Lines.Add("显卡：" & vbCrLf & String.Join(vbCrLf & " · ", GpuTexts))
        End If
        Lines.Add($"磁盘：{Path.GetPathRoot(Paths.Base)} 剩余 {HardwareDiskFree().ToString("F1")} GB（预警阈值 {HardwareDiskThreshold().ToString("F1")} GB）")
        Return String.Join(vbCrLf, Lines)
    End Function

#End Region

End Module
