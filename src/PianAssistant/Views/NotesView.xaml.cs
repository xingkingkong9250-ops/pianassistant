using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PianAssistant.Models;
using PianAssistant.Services;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

/// <summary>
/// 笔记面板（W1-C2-D 统一时间线）：截图/片段/裁切点混排列表，
/// 支持筛选、Ctrl/Shift 多选、选中导出与裁切点删除（合并相邻片段）。
/// </summary>
public partial class NotesView : UserControl
{
    private static readonly SolidColorBrush ActiveChipForeground =
        new(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly SolidColorBrush InactiveChipForeground =
        new(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly SolidColorBrush ActiveChipBackground =
        new(Color.FromRgb(0x1A, 0x6B, 0x8A));

    private NotesViewModel? Vm => DataContext as NotesViewModel;

    public NotesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is NotesViewModel oldVm)
        {
            oldVm.ExportMarkedRequested -= OnExportMarkedRequested;
            oldVm.ExportDataRequested -= OnExportDataRequested;
            oldVm.ImportDataRequested -= OnImportDataRequested;
            oldVm.PropertyChanged -= OnVmPropertyChanged;
        }
        if (e.NewValue is NotesViewModel newVm)
        {
            newVm.ExportMarkedRequested += OnExportMarkedRequested;
            newVm.ExportDataRequested += OnExportDataRequested;
            newVm.ImportDataRequested += OnImportDataRequested;
            newVm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotesViewModel.ActiveFilter) or nameof(NotesViewModel.OnlyMarked))
            UpdateFilterChips();
    }

    // --- W1-C2-D-01 筛选栏 ---

    private void SetFilter(NotesFilter filter)
    {
        if (Vm is null) return;
        Vm.ActiveFilter = filter;
        UpdateFilterChips();
    }

    private void OnFilterAll(object sender, RoutedEventArgs e) => SetFilter(NotesFilter.All);
    private void OnFilterScreenshots(object sender, RoutedEventArgs e) => SetFilter(NotesFilter.Screenshots);
    private void OnFilterSegments(object sender, RoutedEventArgs e) => SetFilter(NotesFilter.Segments);
    private void OnFilterCutPointsOnly(object sender, RoutedEventArgs e) => SetFilter(NotesFilter.CutPointsOnly);

    /// <summary>⚑ 仅标记：叠加开关，切换后与当前基筛选组合。</summary>
    private void OnToggleMarked(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.OnlyMarked = !Vm.OnlyMarked;
        UpdateFilterChips();
    }

    private void UpdateFilterChips()
    {
        var vm = Vm;
        var active = vm?.ActiveFilter ?? NotesFilter.All;
        SetChip(ChipAll, active == NotesFilter.All);
        SetChip(ChipScreenshots, active == NotesFilter.Screenshots);
        SetChip(ChipSegments, active == NotesFilter.Segments);
        SetChip(ChipCutPoints, active == NotesFilter.CutPointsOnly);
        // ⚑ 叠加开关与基筛选独立显示激活态（可同时高亮）
        SetChip(ChipMarked, vm?.OnlyMarked == true);
    }

    private static void SetChip(Button chip, bool active)
    {
        chip.Foreground = active ? ActiveChipForeground : InactiveChipForeground;
        chip.Background = active ? ActiveChipBackground : Brushes.Transparent;
    }

    // --- 点击跳转（截图/片段；裁切点仅选中） ---

    private void OnItemJumpClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TimelineListItem item && Vm is not null)
            Vm.JumpTo(item.Position);
        // 不拦截：事件继续冒泡，ListBox 正常处理选中
    }

    // --- W1-C2-D-04 截图右键 ---

    private void OnScreenshotOpenFolder(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ScreenshotListItem item &&
            Window.GetWindow(this) is MainWindow main)
            main.Shell?.Screenshots.OpenInExplorer(item.Entry);
    }

    private void OnScreenshotAnnotate(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ScreenshotListItem item &&
            Window.GetWindow(this) is MainWindow main)
            main.OpenAnnotation(item.Entry);
    }

    private void OnScreenshotDelete(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ScreenshotListItem item &&
            Window.GetWindow(this) is MainWindow main)
            main.RemoveScreenshot(item.Entry);
    }

    // --- W1-C2-D-05 片段右键 ---

    private void OnSegmentJump(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is SegmentListItem item && Vm is not null)
            Vm.JumpTo(item.Position);
    }

    private void OnSegmentExport(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.DataContext is not SegmentListItem item) return;
        ExportSegmentsToFolder(new[] { item });
    }

    // --- W1-C2-D-06 裁切点右键删除（合并相邻片段） ---

    private void OnCutPointDelete(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is CutPointListItem item)
            DeleteCutPoints(new[] { item });
    }

    /// <summary>删除裁切点：RemoveCutPoint 触发集合变化 → 分段重算（相邻片段合并）→ 列表重建。</summary>
    private void DeleteCutPoints(IReadOnlyList<CutPointListItem> items)
    {
        var shell = (Window.GetWindow(this) as MainWindow)?.Shell;
        if (shell is null) return;
        foreach (var item in items)
            shell.Player.RemoveCutPoint(item.Position);
    }

    // --- Delete 键删除选中裁切点 ---

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || Vm is null) return;
        var cuts = Vm.GetSelectedItems().OfType<CutPointListItem>().ToList();
        if (cuts.Count == 0) return;
        DeleteCutPoints(cuts);
        e.Handled = true;
    }

    // --- 工具栏入口 ---

    private void OnOpenGallery(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenGallery();
    }

    private void OnOpenClipsPanel(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.ShowClipsPanel();
    }

    // --- 标记导出：截图复制 png，片段 ffmpeg 切出 mp4 ---

    private void OnExportMarkedRequested(object? sender, EventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;
        var marked = vm.GetMarkedItems()
            .Where(i => i.Kind is TimelineItemKind.Screenshot or TimelineItemKind.Segment)
            .ToList();
        if (marked.Count == 0)
        {
            MessageBox.Show("请先勾选要导出的截图或片段（条目右侧勾选框），再点「导出标记」。",
                "导出标记", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ExportSegmentsToFolder(marked);
    }

    // --- 导出数据：裁切点 / 片段 / 截图 / 勾选标记 → CSV（可人工编辑） ---

    private void OnExportDataRequested(object? sender, EventArgs e)
    {
        var vm = Vm;
        var shell = (Window.GetWindow(this) as MainWindow)?.Shell;
        if (vm is null || shell is null) return;
        if (vm.Items.Count == 0)
        {
            MessageBox.Show("当前没有可导出的数据（请先打开视频并产生截图/裁切点）。",
                "导出数据", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultName = Path.GetFileNameWithoutExtension(shell.Player.CurrentPath ?? "拉片数据");
        var dlg = new SaveFileDialog
        {
            Title = "导出数据（Excel / CSV，可人工编辑）",
            FileName = $"{defaultName}_拉片数据.xlsx",
            Filter = "Excel 表格|*.xlsx|CSV 表格|*.csv",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var data = vm.BuildExportData(shell.Player.CurrentPath, shell.Player.Length);
            if (Path.GetExtension(dlg.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                NotesDataXlsxService.Export(dlg.FileName, data);
            else
                NotesDataCsvService.Export(dlg.FileName, data);
            MessageBox.Show(
                $"已导出数据（可人工编辑后用「导入数据」覆盖）：\n裁切点 {data.CutPoints.Count} · 片段 {data.Segments.Count} · 截图 {data.Screenshots.Count}\n{dlg.FileName}",
                "导出数据", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "导出数据", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- 导入数据：人工编辑过的 CSV → 覆盖当前视频笔记数据 ---

    private void OnImportDataRequested(object? sender, EventArgs e)
    {
        var vm = Vm;
        var shell = (Window.GetWindow(this) as MainWindow)?.Shell;
        if (vm is null || shell is null) return;
        if (string.IsNullOrEmpty(shell.Player.CurrentPath))
        {
            MessageBox.Show("请先打开视频，再导入数据。", "导入数据",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "导入数据（Excel / CSV 表格）",
            Filter = "Excel 表格|*.xlsx|CSV 表格|*.csv|所有文件|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        NotesExportData data;
        try
        {
            data = Path.GetExtension(dlg.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? NotesDataXlsxService.Import(dlg.FileName)
                : NotesDataCsvService.Import(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"读取失败：{ex.Message}", "导入数据", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (data.CutPoints.Count == 0 && data.Segments.Count == 0 && data.Screenshots.Count == 0)
        {
            MessageBox.Show("文件中没有有效的数据行（需要「裁切点/片段/截图」类型行）。",
                "导入数据", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var warnOtherVideo = !string.IsNullOrEmpty(data.SourcePath)
                             && !string.Equals(data.SourcePath, shell.Player.CurrentPath, StringComparison.OrdinalIgnoreCase)
                             ? "\n\n⚠ 该数据文件来自其他视频。"
                             : string.Empty;
        // 截图清单管理意图：只要携带截图行即管理截图集合；
        // 文件缺失的行将按时间码后台 ffmpeg 重新抽帧（v1.2.0）
        bool managesScreenshots = data.Screenshots.Count > 0;
        int missingShots = data.Screenshots.Count(
            s => s.File.Length == 0 || !File.Exists(s.File));
        var screenshotRule = managesScreenshots
            ? (missingShots > 0
                ? $"· 文件未列出的截图记录将被移除（png 文件保留）\n· {missingShots} 张缺失截图将按时间码自动重新截取\n"
                : "· 文件未列出的截图记录将被移除（png 文件保留）\n· 截图勾选标记以文件为准覆盖\n")
            : "· 文件未包含截图清单：截图记录与标记保持不变\n";
        var confirm = MessageBox.Show(
            $"将导入并覆盖当前视频的笔记数据：\n裁切点 {data.CutPoints.Count} · 片段 {data.Segments.Count} · 截图 {data.Screenshots.Count}{warnOtherVideo}\n\n" +
            "· 裁切点以文件为准覆盖（自动重算片段）\n" +
            "· 片段勾选标记以文件为准覆盖\n" +
            screenshotRule + "\n确认导入？",
            "导入数据", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            shell.ImportNotesData(data);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "导入数据", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>弹目录选择框，后台执行复制/切出，完成后弹汇总提示。</summary>
    private void ExportSegmentsToFolder(IReadOnlyList<TimelineListItem> items)
    {
        var shell = (Window.GetWindow(this) as MainWindow)?.Shell;
        if (shell is null) return;
        var dlg = new OpenFolderDialog { Title = "选择导出目录" };
        if (dlg.ShowDialog() != true) return;
        var folder = dlg.FolderName;

        Task.Run(() =>
        {
            var ok = 0;
            var errors = new List<string>();
            foreach (var item in items)
            {
                try
                {
                    switch (item)
                    {
                        case ScreenshotListItem shot:
                            File.Copy(shot.Entry.OutputPath,
                                Path.Combine(folder, Path.GetFileName(shot.Entry.OutputPath)),
                                overwrite: true);
                            ok++;
                            break;
                        case SegmentListItem seg:
                            shell.ExportSegmentTo(seg.Segment, folder);
                            ok++;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.Title}：{ex.Message}");
                }
            }
            Dispatcher.Invoke(() =>
            {
                var summary = $"已导出 {ok}/{items.Count} 项到：\n{folder}";
                if (errors.Count > 0)
                    summary += "\n\n失败：\n" + string.Join("\n", errors);
                MessageBox.Show(summary, "导出选中",
                    MessageBoxButton.OK,
                    errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            });
        });
    }

    // --- 裁切点导入/导出（沿用既有流程） ---

    /// <summary>导入裁切点：View 弹对话框 → 解析预览 → 确认替换 → Shell 应用。</summary>
    private void OnImportCutPoints(object sender, RoutedEventArgs e)
    {
        var shell = (Window.GetWindow(this) as MainWindow)?.Shell;
        if (shell is null) return;
        var dlg = new OpenFileDialog
        {
            Title = "导入裁切点",
            Filter = "文本文件|*.txt|所有文件|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var (points, segments) = Services.ClipExportService.ImportCutPoints(
                dlg.FileName, shell.Player.CurrentPath);
            if (points.Count == 0 && segments.Count == 0)
            {
                MessageBox.Show("文件中没有可导入的裁切点/片段行。", "导入裁切点",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var summary = segments.Count > 0
                ? $"（导入 {points.Count} 个裁切点、{segments.Count} 条片段）"
                : $"（导入 {points.Count} 个裁切点）";
            var confirm = MessageBox.Show(
                $"将替换现有 {shell.Player.CutPoints.Count} 个裁切点{summary}，是否继续？",
                "导入裁切点", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            shell.ImportCutPoints(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "导入裁切点",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>导出裁切点：View 弹保存对话框 → Shell 写入清单文件。</summary>
    private void OnExportCutPoints(object sender, RoutedEventArgs e)
    {
        var shell = (Window.GetWindow(this) as MainWindow)?.Shell;
        if (shell is null) return;
        if (shell.Player.CutPoints.Count == 0)
        {
            MessageBox.Show("当前没有裁切点可导出。", "导出裁切点",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = "导出裁切点",
            FileName = "裁切点",
            Filter = "文本文件|*.txt",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            shell.ExportCutPoints(dlg.FileName);
            MessageBox.Show($"已导出 {shell.Player.CutPoints.Count} 个裁切点。", "导出裁切点",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "导出裁切点",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
