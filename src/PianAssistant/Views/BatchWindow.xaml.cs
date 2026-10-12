using System.Windows;
using Microsoft.Win32;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

/// <summary>
/// 批量文本操作窗口（M5）：左侧粘贴/编辑指令文本，右侧解析预览与执行结果。
/// 载入文件对话框在 View 层处理，内容经 <see cref="BatchPreviewViewModel.LoadFromFile"/> 填充。
/// </summary>
public partial class BatchWindow : Window
{
    private BatchPreviewViewModel? Vm => DataContext as BatchPreviewViewModel;

    public BatchWindow()
    {
        InitializeComponent();
        // DataContext 变化时订阅载入文件请求（View 层弹 OpenFileDialog）
        DataContextChanged += (_, _) =>
        {
            if (Vm is not null)
                Vm.LoadFileRequested += OnLoadFileRequested;
        };
    }

    /// <summary>弹文件对话框并把选中文件内容填入文本框（经 VM）。</summary>
    private void OnLoadFileRequested(object? sender, EventArgs e)
    {
        if (Vm is null) return;
        var dlg = new OpenFileDialog
        {
            Title = "载入批量指令文件",
            Filter = "文本文件|*.txt|Markdown 文件|*.md|所有文件|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Vm.LoadFromFile(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"读取文件失败：{ex.Message}", "批量操作",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
