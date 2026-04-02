Imports System.ComponentModel
Imports System.Collections.ObjectModel
Imports System.Windows.Threading
Imports System.Windows.Input
Imports LiveCharts
Imports LiveCharts.Wpf
Imports LiveCharts.Defaults
Imports System.Threading.Tasks

' ================================================================
' 基础命令类 (RelayCommand)
' 作用：WPF 中按钮点击事件的 MVVM 实现标准，将 UI 动作绑定到后台方法
' ================================================================
Public Class RelayCommand
    Implements ICommand
    Private ReadOnly _execute As Action(Of Object)

    Public Sub New(execute As Action(Of Object))
        _execute = execute
    End Sub

    Public Event CanExecuteChanged As EventHandler Implements ICommand.CanExecuteChanged

    Public Function CanExecute(parameter As Object) As Boolean Implements ICommand.CanExecute
        Return True ' 默认所有命令始终可执行
    End Function

    Public Sub Execute(parameter As Object) Implements ICommand.Execute
        _execute(parameter)
    End Sub
End Class

' ================================================================
' 主视图模型 (MainViewModel)
' ================================================================
Public Class MainViewModel
    Implements INotifyPropertyChanged

    ' 核心服务与调度器
    Private _timer As DispatcherTimer
    Private _isPolling As Boolean = False
    Private _modbusService As New ModbusService()
    Private _dbService As New DatabaseService()

#Region "系统状态栏属性 (左下角与顶部提示)"
    Private _serialStatusText As String = "Serial COM: 初始化..."
    Public Property SerialStatusText As String
        Get
            Return _serialStatusText
        End Get
        Set(value As String)
            _serialStatusText = value
            OnPropertyChanged(NameOf(SerialStatusText))
        End Set
    End Property

    Private _serialStatusColor As String = "Gray"
    Public Property SerialStatusColor As String
        Get
            Return _serialStatusColor
        End Get
        Set(value As String)
            _serialStatusColor = value
            OnPropertyChanged(NameOf(SerialStatusColor))
        End Set
    End Property

    Private _dbStatusText As String = "SQLite DB: 初始化..."
    Public Property DbStatusText As String
        Get
            Return _dbStatusText
        End Get
        Set(value As String)
            _dbStatusText = value
            OnPropertyChanged(NameOf(DbStatusText))
        End Set
    End Property

    Private _dbStatusColor As String = "Gray"
    Public Property DbStatusColor As String
        Get
            Return _dbStatusColor
        End Get
        Set(value As String)
            _dbStatusColor = value
            OnPropertyChanged(NameOf(DbStatusColor))
        End Set
    End Property

    Private _parameterStatusText As String = "参数状态: 未知"
    Public Property ParameterStatusText As String
        Get
            Return _parameterStatusText
        End Get
        Set(value As String)
            _parameterStatusText = value
            OnPropertyChanged(NameOf(ParameterStatusText))
        End Set
    End Property

    Private _parameterStatusColor As String = "Gray"
    Public Property ParameterStatusColor As String
        Get
            Return _parameterStatusColor
        End Get
        Set(value As String)
            _parameterStatusColor = value
            OnPropertyChanged(NameOf(ParameterStatusColor))
        End Set
    End Property

    Private _diagnosticText As String = "系统诊断: 正在自检..."
    Public Property DiagnosticText As String
        Get
            Return _diagnosticText
        End Get
        Set(value As String)
            _diagnosticText = value
            OnPropertyChanged(NameOf(DiagnosticText))
        End Set
    End Property
#End Region

#Region "串口配置弹窗绑定的属性"
    Public Property AvailablePorts As List(Of String)
    Public Property SelectedPort As String

    Public Property BaudRates As List(Of Integer) = New List(Of Integer) From {9600, 19200, 38400, 57600, 115200}
    Public Property SelectedBaudRate As Integer = 115200

    Public Property DataBitsList As List(Of Integer) = New List(Of Integer) From {7, 8}
    Public Property SelectedDataBits As Integer = 8

    Public Property Parities As List(Of String) = New List(Of String) From {"None", "Odd", "Even"}
    Public Property SelectedParity As String = "None"

    Public Property StopBitsList As List(Of String) = New List(Of String) From {"1", "2"}
    Public Property SelectedStopBits As String = "1"

    Public Property OpenSerialConfigCommand As ICommand
    Public Property ConnectSerialCommand As ICommand
    Public Property DisconnectSerialCommand As ICommand
#End Region

#Region "基础传感器数据与集合"
    Private _currentPressure1 As Double = 0.0
    Public Property CurrentPressure1 As Double
        Get
            Return _currentPressure1
        End Get
        Set(value As Double)
            _currentPressure1 = value
            OnPropertyChanged(NameOf(CurrentPressure1))
            CheckAlarms() ' 每次更新压力 1 时，触发一次报警阈值检测
        End Set
    End Property

    Private _currentPressure2 As Double = 0.0
    Public Property CurrentPressure2 As Double
        Get
            Return _currentPressure2
        End Get
        Set(value As Double)
            _currentPressure2 = value
            OnPropertyChanged(NameOf(CurrentPressure2))
        End Set
    End Property

    Private _currentAngle As Double = 0.0
    Public Property CurrentAngle As Double
        Get
            Return _currentAngle
        End Get
        Set(value As Double)
            _currentAngle = value
            OnPropertyChanged(NameOf(CurrentAngle))
        End Set
    End Property

    Private _currentTime As String
    Public Property CurrentTime As String
        Get
            Return _currentTime
        End Get
        Set(value As String)
            _currentTime = value
            OnPropertyChanged(NameOf(CurrentTime))
        End Set
    End Property

    Public Property SupportList As ObservableCollection(Of SupportModel)
    Public Property AlarmLogs As ObservableCollection(Of AlarmModel)
#End Region

#Region "多设备管理与当前选中状态"
    Private _scanIndex As Integer = 0 ' 后台扫描用游标

    Private _selectedSupport As SupportModel
    Public Property SelectedSupport As SupportModel
        Get
            Return _selectedSupport
        End Get
        Set(value As SupportModel)
            _selectedSupport = value
            OnPropertyChanged(NameOf(SelectedSupport))

            ' 切换设备时，清空旧波形图数据
            TrendAngleSeries?.Clear()
            TrendPressure1Series?.Clear()
            TrendPressure2Series?.Clear()
            TimeLabels?.Clear()
            SparklineValues1?.Clear()
            SparklineValues2?.Clear()

            ' 瞬间拉取所选设备的历史记录画图，实现无缝切换体验
            If _selectedSupport IsNot Nothing Then
                CurrentMovePosition = _selectedSupport.CurrentPosition
                LoadHistoryData(_selectedSupport.SupportID)
            End If
        End Set
    End Property
#End Region

#Region "LiveCharts 图表数据集合"
    Public Property TrendPressure1Series As ChartValues(Of ObservableValue)
    Public Property TrendPressure2Series As ChartValues(Of ObservableValue)
    Public Property TrendAngleSeries As ChartValues(Of ObservableValue)
    Public Property TimeLabels As ObservableCollection(Of String)
    Public Property YAxisFormatter As Func(Of Double, String)
    Public Property SparklineValues1 As ChartValues(Of Double)
    Public Property SparklineValues2 As ChartValues(Of Double)
#End Region

#Region "绝对位置记忆与控制参数"
    Private _currentMovePosition As UShort = 0
    Public Property CurrentMovePosition As UShort
        Get
            Return _currentMovePosition
        End Get
        Set(value As UShort)
            _currentMovePosition = value
            OnPropertyChanged(NameOf(CurrentMovePosition))
        End Set
    End Property

    Private _moveStep As UShort = 10
    Public Property MoveStep As UShort
        Get
            Return _moveStep
        End Get
        Set(value As UShort)
            _moveStep = value
            OnPropertyChanged(NameOf(MoveStep))
        End Set
    End Property
#End Region

#Region "控制命令"
    Public Property EmergencyStopCommand As ICommand
    Public Property RaiseCommand As ICommand
    Public Property LowerCommand As ICommand
    Public Property MoveForwardCommand As ICommand
    Public Property MoveBackwardCommand As ICommand
#End Region

#Region "INotifyPropertyChanged 实现"
    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
    Protected Sub OnPropertyChanged(name As String)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub
#End Region

    ''' <summary>
    ''' ViewModel 构造函数：初始化所有集合、命令、定时器
    ''' </summary>
    Public Sub New()
        SupportList = New ObservableCollection(Of SupportModel)()
        AlarmLogs = New ObservableCollection(Of AlarmModel)()

        TrendAngleSeries = New ChartValues(Of ObservableValue)()
        TrendPressure1Series = New ChartValues(Of ObservableValue)()
        TrendPressure2Series = New ChartValues(Of ObservableValue)()
        TimeLabels = New ObservableCollection(Of String)()
        SparklineValues1 = New ChartValues(Of Double)()
        SparklineValues2 = New ChartValues(Of Double)()

        YAxisFormatter = Function(value) value.ToString("F1")

        InitSupports()
        InitCommands()

        SerialStatusText = "Serial COM: Disconnected"
        SerialStatusColor = "Red"
        DbStatusText = "SQLite DB: Connected"
        DbStatusColor = "LimeGreen"

        ' 初始化并启动后台轮询定时器 (UI 线程调度，保证图表更新安全)
        _timer = New DispatcherTimer()
        _timer.Interval = TimeSpan.FromMilliseconds(1000) ' 1秒轮询一次
        AddHandler _timer.Tick, AddressOf UpdateSensorData
        _timer.Start()
    End Sub

    Private Sub InitSupports()
        SupportList.Clear()
        ' 循环生成 56 个液压支架
        For i As Integer = 1 To 56
            SupportList.Add(New SupportModel With {
                .SupportID = i,
                .DisplayName = $"##{i:D3} - 液压支架",
                .IsOnline = False
            })
        Next
        ' 启动时默认选中第一个支架
        If SupportList.Count > 0 Then SelectedSupport = SupportList(0)
    End Sub

    Private Sub InitCommands()
        EmergencyStopCommand = New RelayCommand(Sub(o) Console.WriteLine("【控制】触发紧急停止！"))

        ' 升高指令 (点动)
        RaiseCommand = New RelayCommand(Sub(o)
                                            If SelectedSupport Is Nothing Then Return
                                            Try
                                                Dim id As Byte = CByte(SelectedSupport.SupportID)
                                                _modbusService.ControlSupport(id, 2, 1, 1)
                                            Catch ex As Exception
                                                Console.WriteLine($"下发升高指令失败: {ex.Message}")
                                            End Try
                                        End Sub)

        ' 降低指令 (点动)
        LowerCommand = New RelayCommand(Sub(o)
                                            If SelectedSupport Is Nothing Then Return
                                            Try
                                                Dim id As Byte = CByte(SelectedSupport.SupportID)
                                                _modbusService.ControlSupport(id, 1, 1, 0)
                                            Catch ex As Exception
                                                Console.WriteLine($"下发降低指令失败: {ex.Message}")
                                            End Try
                                        End Sub)

        ' 前进指令
        MoveForwardCommand = New RelayCommand(Sub(o)
                                                  If SelectedSupport Is Nothing Then Return
                                                  Try
                                                      Dim id As Byte = CByte(SelectedSupport.SupportID)
                                                      Dim nextPos As Integer = CInt(CurrentMovePosition) + CInt(MoveStep)
                                                      If nextPos > 65535 Then nextPos = 65535 ' 防溢出

                                                      CurrentMovePosition = CUShort(nextPos)
                                                      SelectedSupport.CurrentPosition = CurrentMovePosition

                                                      _modbusService.ControlSupport(id, 3, CurrentMovePosition, 2)
                                                  Catch ex As Exception
                                                      Console.WriteLine($"下发前进指令失败: {ex.Message}")
                                                  End Try
                                              End Sub)

        ' 后退指令
        MoveBackwardCommand = New RelayCommand(Sub(o)
                                                   If SelectedSupport Is Nothing Then Return
                                                   Try
                                                       Dim id As Byte = CByte(SelectedSupport.SupportID)
                                                       Dim nextPos As Integer = CInt(CurrentMovePosition) - CInt(MoveStep)
                                                       If nextPos < 0 Then nextPos = 0 ' 防负数

                                                       CurrentMovePosition = CUShort(nextPos)
                                                       SelectedSupport.CurrentPosition = CurrentMovePosition

                                                       _modbusService.ControlSupport(id, 3, CurrentMovePosition, 2)
                                                   Catch ex As Exception
                                                       Console.WriteLine($"下发后退指令失败: {ex.Message}")
                                                   End Try
                                               End Sub)

        ' ================= 串口配置命令 =================
        OpenSerialConfigCommand = New RelayCommand(Sub(o)
                                                       AvailablePorts = System.IO.Ports.SerialPort.GetPortNames().ToList()
                                                       If AvailablePorts.Count > 0 AndAlso String.IsNullOrEmpty(SelectedPort) Then
                                                           SelectedPort = AvailablePorts(0)
                                                       End If
                                                       OnPropertyChanged(NameOf(AvailablePorts))
                                                       OnPropertyChanged(NameOf(SelectedPort))

                                                       Dim win As New Views.SerialConfigWindow()
                                                       win.DataContext = Me ' 让弹窗共用 MainViewModel
                                                       win.Owner = Application.Current.MainWindow
                                                       win.ShowDialog()
                                                   End Sub)

        ConnectSerialCommand = New RelayCommand(Sub(o)
                                                    Try
                                                        Dim p As System.IO.Ports.Parity = If(SelectedParity = "Odd", System.IO.Ports.Parity.Odd, If(SelectedParity = "Even", System.IO.Ports.Parity.Even, System.IO.Ports.Parity.None))
                                                        Dim sb As System.IO.Ports.StopBits = If(SelectedStopBits = "2", System.IO.Ports.StopBits.Two, System.IO.Ports.StopBits.One)

                                                        _modbusService.Connect(SelectedPort, SelectedBaudRate, SelectedDataBits, p, sb)

                                                        SerialStatusText = $"Serial COM: Connected ({SelectedPort})"
                                                        SerialStatusColor = "LimeGreen"
                                                        System.Windows.MessageBox.Show("串口连接成功！", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information)
                                                    Catch ex As Exception
                                                        System.Windows.MessageBox.Show(ex.Message, "连接错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error)
                                                        SerialStatusText = "Serial COM: Disconnected"
                                                        SerialStatusColor = "Red"
                                                    End Try
                                                End Sub)

        DisconnectSerialCommand = New RelayCommand(Sub(o)
                                                       _modbusService.Disconnect()
                                                       SerialStatusText = "Serial COM: Disconnected"
                                                       SerialStatusColor = "Red"
                                                       System.Windows.MessageBox.Show("串口已关闭！", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information)
                                                   End Sub)
    End Sub

    Private Sub LoadHistoryData(supportId As Integer)
        Dim history = _dbService.GetSensorHistory(supportId, 20)

        If history.Count = 0 Then
            CurrentAngle = 0
            CurrentPressure1 = 0
            CurrentPressure2 = 0
            Return
        End If

        Dim lastRecord = history.Last()
        CurrentAngle = lastRecord.Angle
        CurrentPressure1 = lastRecord.Pressure1
        CurrentPressure2 = lastRecord.Pressure2

        For Each item In history
            TrendAngleSeries.Add(New ObservableValue(item.Angle))
            TrendPressure1Series.Add(New ObservableValue(item.Pressure1))
            TrendPressure2Series.Add(New ObservableValue(item.Pressure2))
            TimeLabels.Add(item.RecordTime)

            SparklineValues1.Add(item.Pressure1)
            If SparklineValues1.Count > 15 Then SparklineValues1.RemoveAt(0)

            SparklineValues2.Add(item.Pressure2)
            If SparklineValues2.Count > 15 Then SparklineValues2.RemoveAt(0)
        Next
    End Sub

    ''' <summary>
    ''' 核心轮询引擎 (极致丝滑的 UI 更新)
    ''' 策略：每次 Tick 必定优先读取当前前台设备，然后在后台静默读取 4 个其余设备。
    ''' </summary>
    Private Async Sub UpdateSensorData(sender As Object, e As EventArgs)
        ' 防堵塞锁：如果上一次轮询因为网络或硬件延迟还没结束，直接放弃本次 Tick
        If _isPolling Then Return
        _isPolling = True

        Try
            CurrentTime = DateTime.Now.ToString("HH:mm:ss")

            Dim currentId As Byte = 0
            If SelectedSupport IsNot Nothing Then
                currentId = CByte(SelectedSupport.SupportID)
            End If

            Dim sensorData As (Angle As Double, Pressure1 As Double, Pressure2 As Double, IsSuccess As Boolean)
            Dim onlineStatusList As New List(Of (Index As Integer, IsOnline As Boolean))

            ' 【异步通信分离】：将耗时的 Modbus 读取完全放入后台线程，彻底解放 UI 线程
            If _modbusService.IsConnected Then
                Await Task.Run(Sub()
                                   ' 1. 优先读取前台用户正在查看的设备
                                   If currentId > 0 Then
                                       sensorData = _modbusService.ReadSupportSensors(currentId)
                                       If sensorData.IsSuccess Then
                                           _dbService.InsertSensorData(currentId, sensorData.Angle, sensorData.Pressure1, sensorData.Pressure2)
                                       End If
                                   End If

                                   ' 2. 后台静默推进扫描 4 个设备 (56 个设备只需 14 秒即可全部刷新一轮状态)
                                   For i As Integer = 1 To 4
                                       _scanIndex += 1
                                       If _scanIndex > 56 Then _scanIndex = 1
                                       If currentId > 0 AndAlso _scanIndex = currentId Then Continue For ' 跳过刚才已经读过的前台设备

                                       Dim bgData = _modbusService.ReadSupportSensors(CByte(_scanIndex))
                                       onlineStatusList.Add((_scanIndex, bgData.IsSuccess))

                                       If bgData.IsSuccess Then
                                           _dbService.InsertSensorData(_scanIndex, bgData.Angle, bgData.Pressure1, bgData.Pressure2)
                                       End If
                                   Next
                               End Sub)
            End If

            ' === 此时 Await 结束，代码自动回到 UI 主线程，开始安全地刷新图表 ===

            If SelectedSupport IsNot Nothing Then
                SelectedSupport.IsOnline = sensorData.IsSuccess

                If sensorData.IsSuccess Then
                    ParameterStatusText = "参数状态: 数据同步中"
                    ParameterStatusColor = "LimeGreen"
                    CurrentAngle = Math.Round(sensorData.Angle, 1)
                    CurrentPressure1 = Math.Round(sensorData.Pressure1, 1)
                    CurrentPressure2 = Math.Round(sensorData.Pressure2, 1)

                    TrendAngleSeries.Add(New ObservableValue(CurrentAngle))
                    TrendPressure1Series.Add(New ObservableValue(CurrentPressure1))
                    TrendPressure2Series.Add(New ObservableValue(CurrentPressure2))
                Else
                    ParameterStatusText = "参数状态: 设备离线"
                    ParameterStatusColor = "Red"
                    ' 离线时数据归零，或者你可以选择保留最后一次的数值 (注释掉下方三行即可保留)
                    CurrentAngle = 0
                    CurrentPressure1 = 0
                    CurrentPressure2 = 0

                    TrendAngleSeries.Add(New ObservableValue(0))
                    TrendPressure1Series.Add(New ObservableValue(0))
                    TrendPressure2Series.Add(New ObservableValue(0))
                End If

                TimeLabels.Add(DateTime.Now.ToString("HH:mm:ss"))

                ' 控制主波形图只显示最新 20 个点，防止内存爆炸
                If TrendPressure1Series.Count > 20 Then
                    TrendAngleSeries.RemoveAt(0)
                    TrendPressure1Series.RemoveAt(0)
                    TrendPressure2Series.RemoveAt(0)
                    TimeLabels.RemoveAt(0)
                End If

                ' 控制 Sparkline (迷你趋势图) 只显示最新 15 个点
                SparklineValues1.Add(CurrentPressure1)
                If SparklineValues1.Count > 15 Then SparklineValues1.RemoveAt(0)
                SparklineValues2.Add(CurrentPressure2)
                If SparklineValues2.Count > 15 Then SparklineValues2.RemoveAt(0)
            End If

            ' 批量更新左侧列表的红绿灯状态
            For Each item In onlineStatusList
                SupportList(item.Index - 1).IsOnline = item.IsOnline
            Next

            ' 系统全局诊断逻辑
            Dim offlineCount = SupportList.Where(Function(s) Not s.IsOnline).Count()
            If offlineCount = 0 AndAlso _modbusService.IsConnected Then
                DiagnosticText = "系统诊断: 运行正常 | 56台设备全部在线通信中"
            ElseIf Not _modbusService.IsConnected Then
                DiagnosticText = "系统诊断: 串口未连接"
            Else
                DiagnosticText = $"系统诊断: 发现异常 | 当前有 {offlineCount} 台设备处于离线状态！"
            End If

        Finally
            ' ★ 无论中间发生什么异常，必须解锁，允许下一次轮询
            _isPolling = False
        End Try
    End Sub

    Private Sub CheckAlarms()
        ' 【Bug修复】：修正了原代码中两个判断条件完全一样的逻辑错误
        If CurrentPressure1 > 350.0 Then
            AddAlarm("紧急", $"压力过高 (超350阈值): {CurrentPressure1} MPa")
        ElseIf CurrentPressure1 > 300.0 Then
            AddAlarm("警告", $"压力偏高 (超300阈值): {CurrentPressure1} MPa")
        End If
    End Sub

    Private Sub AddAlarm(level As String, desc As String)
        ' 使用优化后的 AlarmModel 构造函数
        AlarmLogs.Insert(0, New AlarmModel(level, desc, "源压-VB"))
        ' 限制最多保留 50 条报警日志，防止 ListView 越来越卡
        If AlarmLogs.Count > 50 Then AlarmLogs.RemoveAt(AlarmLogs.Count - 1)
    End Sub
End Class