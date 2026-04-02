Imports System.ComponentModel

''' <summary>
''' 液压支架设备数据模型 (Model)
''' 作用：代表一台物理上的液压支架。包含其基础信息和实时状态。
''' 特性：实现了 INotifyPropertyChanged 接口，当属性值发生变化时，会自动通知 WPF 前端界面进行重绘。
''' </summary>
Public Class SupportModel
    Implements INotifyPropertyChanged

    ' ================= 基础属性 =================

    ''' <summary>
    ''' 支架的物理 ID / 从站地址 (例如：1 到 56)
    ''' </summary>
    Public Property SupportID As Integer

    ''' <summary>
    ''' 界面列表上显示的格式化名称，例如："##001 - 液压支架"
    ''' </summary>
    Public Property DisplayName As String

    ' ================= 状态属性 (核心UI绑定逻辑) =================

    Private _isOnline As Boolean
    ''' <summary>
    ''' 核心状态：指示该支架当前是否能够成功进行 Modbus 通信
    ''' </summary>
    Public Property IsOnline As Boolean
        Get
            Return _isOnline
        End Get
        Set(value As Boolean)
            ' 【性能防抖机制】：如果轮询读取到的在线状态和当前一样，直接退出，绝不触发 UI 刷新。
            ' 对于 56 台设备的高频轮询，这一行能极大节省 CPU 开销，防止界面卡顿。
            If _isOnline = value Then Return

            _isOnline = value

            ' 当状态确实发生翻转（如从在线变离线）时，通知 UI 更新三个相关属性。
            ' 【安全规范】：使用 NameOf(属性名) 替代 "属性名" 字符串，避免以后修改属性名时忘记改字符串导致的绑定失效。
            OnPropertyChanged(NameOf(IsOnline))
            OnPropertyChanged(NameOf(StatusText))   ' 联动通知：文字变了，UI请刷新
            OnPropertyChanged(NameOf(StatusColor))  ' 联动通知：颜色变了，UI请刷新
        End Set
    End Property

    ''' <summary>
    ''' 根据 IsOnline 状态，自动推导出需要在界面上显示的文字。
    ''' 注意：这是一个只读属性 (ReadOnly)，它自身不保存数据，而是依赖 IsOnline 的值。
    ''' </summary>
    Public ReadOnly Property StatusText As String
        Get
            Return If(_isOnline, "在线", "离线")
        End Get
    End Property

    ''' <summary>
    ''' 根据 IsOnline 状态，自动推导出界面指示灯和文字的颜色。
    ''' WPF 支持直接将这种颜色字符串 (如 "LimeGreen", "Red") 绑定到 Fill 或 Foreground 属性上。
    ''' </summary>
    Public ReadOnly Property StatusColor As String
        Get
            Return If(_isOnline, "LimeGreen", "Red")
        End Get
    End Property

    ' ================= 控制参数属性 =================

    Private _currentPosition As UShort = 0
    ''' <summary>
    ''' 记录该支架当前的绝对位置（针对步进移动控制）。
    ''' 每个支架独立记忆自己的位置，防止切换选中设备时位置数据错乱。
    ''' </summary>
    Public Property CurrentPosition As UShort
        Get
            Return _currentPosition
        End Get
        Set(value As UShort)
            ' 同样引入防抖机制：只有数值真变了才通知界面更新
            If _currentPosition = value Then Return

            _currentPosition = value
            OnPropertyChanged(NameOf(CurrentPosition))
        End Set
    End Property

    ' ================= INotifyPropertyChanged 接口实现 =================

    ''' <summary>
    ''' 属性变更事件，WPF 的 Binding 机制会监听这个事件
    ''' </summary>
    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    ''' <summary>
    ''' 触发 PropertyChanged 事件的辅助方法
    ''' </summary>
    ''' <param name="name">发生变化的属性名称</param>
    Protected Sub OnPropertyChanged(name As String)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub

End Class