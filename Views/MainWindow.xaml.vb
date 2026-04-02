' 注意：这里的 WPF_VB_Monitoring 要和你实际的项目名称完全一致
Imports WPF_VB_Monitoring.ViewModels

Public Class MainWindow
    Public Sub New()
        ' 此调用是设计器所必需的。
        InitializeComponent()

        ' 将 MainWindow 的 DataContext 赋值为 MainViewModel 的实例
        Me.DataContext = New MainViewModel()
    End Sub
End Class