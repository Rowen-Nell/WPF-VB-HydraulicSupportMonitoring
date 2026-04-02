Imports System.IO.Ports
Imports Modbus.Device
Imports System.Threading.Tasks

''' <summary>
''' 底层通信服务层 (Modbus/RS485 Communication Layer)
''' 作用：负责与硬件设备（单片机/PLC等）进行串口通信，执行数据采集与控制指令下发。
''' </summary>
Public Class ModbusService

#Region "私有变量与总线锁"
    Private _serialPort As SerialPort
    Private _master As IModbusSerialMaster

    ''' <summary>
    ''' ★ 总线互斥锁 (Mutex Lock) ★
    ''' 【核心机制】：RS485 是半双工总线，物理上绝对不允许同时发送和接收。
    ''' 此锁确保在多线程环境下（例如：后台定时器正在高频轮询时，用户突然点击了"升高"按钮），
    ''' 同一时刻有且只有一个线程能占用串口总线，防止数据包物理碰撞导致 NModbus 底层异常抛出。
    ''' </summary>
    Private ReadOnly _busLock As New Object()
#End Region

#Region "属性与连接管理"

    ''' <summary>
    ''' 获取当前串口是否处于打开并可用的状态
    ''' </summary>
    Public ReadOnly Property IsConnected As Boolean
        Get
            Return _serialPort IsNot Nothing AndAlso _serialPort.IsOpen
        End Get
    End Property

    ''' <summary>
    ''' 动态配置并连接串口
    ''' </summary>
    ''' <param name="portName">串口号 (如 COM3)</param>
    ''' <param name="baudRate">波特率 (如 115200)</param>
    ''' <param name="dataBits">数据位</param>
    ''' <param name="parity">校验位</param>
    ''' <param name="stopBits">停止位</param>
    Public Sub Connect(portName As String, baudRate As Integer, dataBits As Integer, parity As Parity, stopBits As StopBits)
        Try
            ' 如果已经有打开的串口，先安全关闭并彻底释放旧资源
            Disconnect()

            _serialPort = New SerialPort(portName, baudRate, parity, dataBits, stopBits)

            ' 【性能调优】：读写超时设置。
            ' 稍微放宽至 150ms，既能兼容 Windows 系统下软件仿真器的偶发性调度延迟，
            ' 又保证了即便设备掉线，也能在 150ms 内迅速报错放开总线，不至于卡死整个轮询队列。
            _serialPort.ReadTimeout = 150
            _serialPort.WriteTimeout = 150
            _serialPort.Open()

            ' 创建基于 RTU 协议的 Modbus 主站实例
            _master = ModbusSerialMaster.CreateRtu(_serialPort)

            ' ★ 【防卡顿机制】：强行关闭 NModbus 默认的重试机制。
            ' 默认情况下，如果一个设备离线，NModbus 会傻傻地重试 3 次，每次都等超时，极其浪费时间！
            ' 我们设为 0：只要一次读不到，立刻判断离线并放开锁，绝不阻塞后续几十个设备的轮询。
            _master.Transport.Retries = 0

            Console.WriteLine($"[通信层] 成功连接串口: {portName} | 波特率: {baudRate}")

        Catch ex As UnauthorizedAccessException
            ' 精准捕获串口被占用异常 (这是现场排错最常见的问题)
            Throw New Exception($"串口 {portName} 拒绝访问，可能已被其他程序（如串口助手）占用！")
        Catch ex As Exception
            Throw New Exception($"串口打开失败: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' 安全断开串口并释放底层非托管资源
    ''' </summary>
    Public Sub Disconnect()
        ' 必须加锁，防止断开时刚好有线程正在执行读写操作
        SyncLock _busLock
            If _serialPort IsNot Nothing Then
                If _serialPort.IsOpen Then _serialPort.Close()
                _serialPort.Dispose()
                _serialPort = Nothing
            End If
            If _master IsNot Nothing Then
                _master.Dispose()
                _master = Nothing
            End If
        End SyncLock
        Console.WriteLine("[通信层] 串口已安全关闭并释放资源。")
    End Sub

#End Region

#Region "数据读取与控制逻辑"

    ''' <summary>
    ''' 读取指定设备的主传感器数据 (Modbus 功能码 04 - 读取输入寄存器)
    ''' </summary>
    ''' <param name="slaveId">设备从站地址 (1-56)</param>
    ''' <returns>返回元组：角度、压力1、压力2，以及本次通信是否成功的标志位</returns>
    Public Function ReadSupportSensors(slaveId As Byte) As (Angle As Double, Pressure1 As Double, Pressure2 As Double, IsSuccess As Boolean)
        If _master Is Nothing OrElse Not IsConnected Then
            Return (0, 0, 0, False)
        End If

        Try
            Dim registers As UShort()

            ' ★ 进入临界区，独占串口总线进行查询
            SyncLock _busLock
                ' 读取 3 个输入寄存器：起始地址 0，数量 3
                ' [0] 对应 角度, [1] 对应 压力1, [2] 对应 压力2
                registers = _master.ReadInputRegisters(slaveId, 0, 3)
            End SyncLock

            ' 【数据还原解析】：
            ' 单片机/PLC 通常无法直接传输浮点数 (小数)。
            ' 这里的协议约定是：下位机把压力值放大了 100 倍变成整数传输，所以上位机拿到后必须除以 100.0 还原物理量。
            Dim angle As Double = registers(0)
            Dim p1 As Double = registers(1) / 100.0
            Dim p2 As Double = registers(2) / 100.0

            Return (angle, p1, p2, True) ' 通信成功，返回 True

        Catch ex As Exception
            ' 发生任何超时或校验错误 (CRC不匹配等)，均视为该次读取失败（设备可能离线、掉电或线路干扰）
            ' 此处不抛出异常，而是优雅地返回 False，让调用方 (ViewModel) 去更新界面的红灯离线状态
            Return (0, 0, 0, False)
        End Try
    End Function

    ''' <summary>
    ''' 异步下发控制指令给指定设备 (包含寄存器写与线圈触发)
    ''' </summary>
    ''' <param name="slaveId">设备地址</param>
    ''' <param name="registerAddress">要写入的保持寄存器地址 (功能码 06)</param>
    ''' <param name="value">要写入的数值 (如步长、绝对位置)</param>
    ''' <param name="coilAddress">要触发的线圈地址 (功能码 05)</param>
    Public Sub ControlSupport(slaveId As Byte, registerAddress As UShort, value As UShort, coilAddress As UShort)

        ' 【UI 响应优化】：控制指令强制放入后台线程执行 (Task.Run)
        ' 原因：串口写入虽然比读取快，但依然会有几十毫秒的阻塞。
        ' 如果在 UI 主线程直接执行，用户狂点"升高"按钮时界面会产生肉眼可见的卡顿或假死。
        Task.Run(Sub()
                     Try
                         If _master Is Nothing OrElse Not IsConnected Then Return

                         ' ★ 再次进入临界区，独占总线，打断或者等待当前的轮询任务
                         SyncLock _busLock
                             ' 1. 写保持寄存器 (功能码 06) - 传递动作的数值参数
                             _master.WriteSingleRegister(slaveId, registerAddress, value)

                             ' 【硬件兼容性延时】：线程休眠 50ms
                             ' 目的：很多老式 PLC 或单片机处理上一条 Modbus 指令后，串口芯片的收发切换需要时间。
                             ' 连续极速下发两条指令，第二条极大概率会因为下位机还没准备好接收而被丢弃。留 50ms 缓冲非常稳妥。
                             System.Threading.Thread.Sleep(50)

                             ' 2. 写单线圈 (功能码 05) - 发送真正的执行触发信号 (True = ON)
                             _master.WriteSingleCoil(slaveId, coilAddress, True)
                         End SyncLock

                         Console.WriteLine($"[通信层] TX 成功: 设备 {slaveId} 动作已下发 (Reg:{registerAddress} Val:{value} Coil:{coilAddress})")

                     Catch ex As Exception
                         ' 控制下发失败通常由于总线繁忙导致超时
                         Console.WriteLine($"[通信层] TX 错误: 设备 {slaveId} 控制失败 - {ex.Message}")
                     End Try
                 End Sub)
    End Sub

#End Region

End Class