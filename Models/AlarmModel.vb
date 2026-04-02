''' <summary>
''' 报警日志数据模型 (Model)
''' 作用：用于在界面右下角的“报警日志”列表中显示每一条报警记录。
''' 这是一个纯粹的数据实体类 (POCO)，不包含复杂的业务逻辑。
''' </summary>
Public Class AlarmModel

    ' ================= 数据属性 =================

    ''' <summary>
    ''' 报警的严重等级，例如："预警"、"警告" 或 "紧急"
    ''' </summary>
    Public Property Level As String

    ''' <summary>
    ''' 具体的报警内容描述，例如："压力1过高: 360.5 MPa"
    ''' </summary>
    Public Property Description As String

    ''' <summary>
    ''' 报警发生的确切时间，界面上可以通过 StringFormat 格式化显示
    ''' </summary>
    Public Property Timestamp As DateTime

    ''' <summary>
    ''' 报警数据的来源模块，例如："源压-VB" 或 "底层通信"
    ''' </summary>
    Public Property Source As String

    ' ================= 构造函数 =================

    ''' <summary>
    ''' 带参数的构造函数 (推荐使用)
    ''' 作用：在 ViewModel 中生成报警时，可以通过一行代码快速完成对象初始化，代码更简洁。
    ''' </summary>
    ''' <param name="level">报警等级</param>
    ''' <param name="description">描述信息</param>
    ''' <param name="source">来源</param>
    Public Sub New(level As String, description As String, source As String)
        Me.Level = level
        Me.Description = description
        Me.Timestamp = DateTime.Now ' 实例化时自动捕获当前系统时间，无需手动传入
        Me.Source = source
    End Sub

    ''' <summary>
    ''' 无参构造函数
    ''' 作用：保留此函数是为了兼容某些需要反射或进行 JSON/XML 序列化、反序列化的场景。
    ''' </summary>
    Public Sub New()
    End Sub

End Class