Imports System.IO.Ports

Public Class SerialCommunicationService
    Private WithEvents _serialPort As SerialPort
    Public Event DataReceived(rawData As String)

    Public Sub InitializePort(portName As String, baudRate As Integer)
        Try
            _serialPort = New SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            _serialPort.ReadTimeout = 1000 ' 捕获常见异常：通信超时
            _serialPort.Open()
        Catch ex As UnauthorizedAccessException
            Throw New Exception("串口被占用，请检查连接。")
        Catch ex As Exception
            Throw New Exception($"串口连接失败: {ex.Message}")
        End Try
    End Sub

    ' 关键算法注释：下位机通信协议解析
    Private Sub _serialPort_DataReceived(sender As Object, e As SerialDataReceivedEventArgs) Handles _serialPort.DataReceived
        Try
            Dim sp As SerialPort = CType(sender, SerialPort)
            Dim incomingData As String = sp.ReadLine()
            ' 触发事件交由 ViewModel 处理
            RaiseEvent DataReceived(incomingData)
        Catch ex As TimeoutException
            ' 处理读取超时异常
            Console.WriteLine("数据读取超时")
        End Try
    End Sub

    ' 控制指令下发 (满足图2要求：支持手动控制)
    Public Sub SendCommand(commandCode As String)
        If _serialPort IsNot Nothing AndAlso _serialPort.IsOpen Then
            Try
                _serialPort.WriteLine(commandCode)
            Catch ex As Exception
                Console.WriteLine("指令下发失败: " & ex.Message)
            End Try
        End If
    End Sub
End Class