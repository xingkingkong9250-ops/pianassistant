using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PianAssistant.Services;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

/// <summary>
/// 片段批量导出面板：队列（入点→出点/时长/状态）、整体进度条、
/// 全部导出 / 拼接导出 / 取消。拼接输出路径由本视图弹 SaveFileDialog 选定。
/// </summary>
public partial class ClipsView : UserControl
{
    private ClipListViewModel? Vm => DataContext as ClipListViewModel;

    public ClipsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ClipListViewModel oldVm)
        {
            oldVm.ConcatRequested -= OnConcatRequested;
            oldVm.ConcatError -= OnConcatError;
            oldVm.BatchError -= OnBatchError;
        }
        if (e.NewValue is ClipListViewModel newVm)
        {
            newVm.ConcatRequested += OnConcatRequested;
            newVm.ConcatError += OnConcatError;
            newVm.BatchError += OnBatchError;
        }
    }

    /// <summary>拼接请求：弹出保存对话框后调用 VM 的拼接任务。</summary>
    private void OnConcatRequested(object? sender, EventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;
        // 默认扩展名跟随「视频格式」导出预设（v1.4.0）
        var format = (Window.GetWindow(this) as MainWindow)?.Shell?.Services.Settings.VideoExportFormat
                     ?? ExportPresets.DefaultVideoFormat;
        var dlg = new SaveFileDialog
        {
            Title = "拼接导出片段",
            FileName = $"{vm.ConcatSourceName}_拼接.{format}",
            Filter = $"{format.ToUpperInvariant()} 视频|*.{format}|所有文件|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        _ = vm.ConcatSelectedAsync(dlg.FileName);
    }

    private void OnConcatError(object? sender, string message)
    {
        MessageBox.Show(message, "拼接导出", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>批量导出失败（如 ffmpeg.exe 缺失）：弹中文提示，不静默失败。</summary>
    private void OnBatchError(object? sender, string message)
    {
        MessageBox.Show(message, "批量导出", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
