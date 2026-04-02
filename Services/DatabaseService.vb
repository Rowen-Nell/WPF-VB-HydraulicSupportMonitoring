Imports System.Data.SQLite
Imports System.IO

''' <summary>
''' 数据库服务层 (Database Layer)
''' 作用：负责 SQLite 本地数据库的创建、数据持久化存储以及历史记录拉取。
''' </summary>
Public Class DatabaseService

    ' 数据库文件路径 (保存在软件运行目录下)
    Private ReadOnly _dbPath As String = "SupportMonitor.db"

    ' 数据库连接字符串
    Private ReadOnly _connString As String

    Public Sub New()
        ' 【核心性能优化】: 启用 WAL 模式 (Journal Mode=WAL) 和 连接池 (Pooling=True)
        ' 作用：支持高频读写并发！后台轮询线程写入数据时，前台 UI 依然可以无阻塞地读取波形数据，彻底告别 "Database is locked" 报错。
        _connString = $"Data Source={_dbPath};Version=3;Journal Mode=WAL;Pooling=True;"
        InitializeDatabase()
    End Sub

    ''' <summary>
    ''' 初始化数据库环境。如果文件或表不存在，则自动创建。
    ''' </summary>
    Private Sub InitializeDatabase()
        Try
            ' 如果文件不存在，就自动创建一个 .db 文件
            If Not File.Exists(_dbPath) Then
                SQLiteConnection.CreateFile(_dbPath)
                Console.WriteLine("[数据库] 成功创建全新的 SQLite 数据库文件。")
            End If

            ' 连接数据库并执行建表 SQL 语句 (Using 语法确保即使发生异常也能自动释放数据库连接)
            Using conn As New SQLiteConnection(_connString)
                conn.Open()

                ' 建表语句：RecordTime 使用默认时间，无需每次手动插入
                Dim sql As String = "
                    CREATE TABLE IF NOT EXISTS SensorHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SupportID INTEGER NOT NULL,
                        Angle REAL NOT NULL,
                        Pressure1 REAL NOT NULL,
                        Pressure2 REAL NOT NULL,
                        RecordTime DATETIME DEFAULT CURRENT_TIMESTAMP
                    )"

                Using cmd As New SQLiteCommand(sql, conn)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            Console.WriteLine("[数据库] SQLite 初始化完毕，表结构就绪！")

        Catch ex As Exception
            ' 实际工业项目中，建议将此类严重异常写入本地 txt 日志文件
            Console.WriteLine($"[数据库] 初始化致命失败: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' 将单台设备的传感器数据插入到数据库中
    ''' </summary>
    ''' <param name="supportId">支架编号 (1-56)</param>
    ''' <param name="angle">倾角传感器数值</param>
    ''' <param name="p1">压力传感器 1 数值</param>
    ''' <param name="p2">压力传感器 2 数值</param>
    Public Sub InsertSensorData(supportId As Integer, angle As Double, p1 As Double, p2 As Double)
        Try
            Using conn As New SQLiteConnection(_connString)
                conn.Open()

                ' 【安全规范】：严格使用参数化查询 (@sid等)，坚决杜绝字符串拼接，防止 SQL 注入。
                ' 【时间优化】：使用 SQLite 内置的 datetime('now', 'localtime') 获取本地时间，减轻 VB 端的运算和传参负担。
                Dim sql As String = "INSERT INTO SensorHistory (SupportID, Angle, Pressure1, Pressure2, RecordTime) 
                                     VALUES (@sid, @ang, @p1, @p2, datetime('now', 'localtime'))"

                Using cmd As New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@sid", supportId)
                    cmd.Parameters.AddWithValue("@ang", angle)
                    cmd.Parameters.AddWithValue("@p1", p1)
                    cmd.Parameters.AddWithValue("@p2", p2)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            Console.WriteLine($"[数据库] 数据插入失败 (设备ID {supportId}): {ex.Message}")
        End Try
    End Sub

    ' ================= 从数据库极速拉取历史记录 =================

    ''' <summary>
    ''' 从数据库极速拉取指定设备的最近 N 条历史记录，直接喂给前台的波形图图表
    ''' </summary>
    ''' <param name="supportId">需要查询的支架编号</param>
    ''' <param name="limit">拉取的条数限制 (例如拉取最近 20 条)</param>
    ''' <returns>返回包含元组的列表: (角度, 压力1, 压力2, 格式化后的时间戳)</returns>
    Public Function GetSensorHistory(supportId As Integer, limit As Integer) As List(Of (Angle As Double, Pressure1 As Double, Pressure2 As Double, RecordTime As String))
        Dim result As New List(Of (Double, Double, Double, String))()

        Try
            Using conn As New SQLiteConnection(_connString)
                conn.Open()

                ' 【核心算法注释】
                ' 需求：提取最新数据的同时，保证时间线从左到右递增（波形图要求）。
                ' 实现逻辑 (嵌套查询)：
                ' 1. 内层查询 (sub): 按时间倒序 (DESC) 查出最新的 limit 条数据。
                ' 2. 外层查询: 把这 limit 条数据，重新按照时间正序 (ASC) 排列。
                ' 3. strftime: 让 SQLite 引擎直接输出 "14:30:05" 格式的字符串，避免读取出来后再用 VB.NET 转换。
                Dim sql As String = $"
                    SELECT Angle, Pressure1, Pressure2, strftime('%H:%M:%S', RecordTime) as RTime 
                    FROM (
                        SELECT * FROM SensorHistory 
                        WHERE SupportID = @sid 
                        ORDER BY RecordTime DESC 
                        LIMIT @limit
                    ) sub 
                    ORDER BY RecordTime ASC"

                Using cmd As New SQLiteCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@sid", supportId)
                    cmd.Parameters.AddWithValue("@limit", limit)

                    ' 使用 ExecuteReader 是一种只读、向前推进的游标，读取速度比 DataTable 填充快几个数量级，极其适合高频实时数据
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            result.Add((
                                Convert.ToDouble(reader("Angle")),
                                Convert.ToDouble(reader("Pressure1")),
                                Convert.ToDouble(reader("Pressure2")),
                                reader("RTime").ToString()
                            ))
                        End While
                    End Using
                End Using
            End Using

        Catch ex As Exception
            Console.WriteLine($"[数据库] 读取历史数据失败 (设备ID {supportId}): {ex.Message}")
        End Try

        Return result
    End Function

End Class