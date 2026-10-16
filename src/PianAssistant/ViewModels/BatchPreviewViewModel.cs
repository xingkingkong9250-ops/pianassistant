using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;
using PianAssistant.Services;

namespace PianAssistant.ViewModels;

/// <summary>
/// 批量文本操作预览窗 ViewModel（M5）：
/// 「载入文件」→ 文本填充；「解析预览」→ 指令列表 + 状态统计；
/// 「执行」→ 标记/裁切点经回调直接分发到笔记/播放器，
/// 截图/片段指令经 <see cref="BatchExecutionService"/> 跑 ffmpeg（可取消）。
/// </summary>
public sealed class BatchPreviewViewModel : ObservableObject
{
    private readonly BatchTextParser _parser;
    private readonly BatchExecutionService _executor;
    private readonly Func<string?> _getVideoPath;
    private readonly Action<TimeSpan> _addCutPoint;
    private CancellationTokenSource? _cts;

    private string _text = string.Empty;
    private string _statsText = "共 0 条";
    private string _statusText = string.Empty;
    private double _progress;
    private bool _isRunning;
    private bool _showResults;

    public BatchPreviewViewModel(
        BatchTextParser parser,
        BatchExecutionService executor,
        Action<TimeSpan> addCutPoint,
        Func<string?>? getVideoPath = null)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _addCutPoint = addCutPoint ?? throw new ArgumentNullException(nameof(addCutPoint));
        _getVideoPath = getVideoPath ?? (() => null);

        LoadFileCommand = new RelayCommand(
            () => LoadFileRequested?.Invoke(this, EventArgs.Empty));
        ParseCommand = new RelayCommand(Parse, () => !IsRunning);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
    }

    // --- 绑定状态 ---

    /// <summary>指令文本（可编辑多行，粘贴清单用）。</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    /// <summary>解析后的指令列表（预览 DataGrid 数据源）。</summary>
    public ObservableCollection<BatchInstruction> Items { get; } = new();

    /// <summary>执行结果列表（结果 DataGrid 数据源）。</summary>
    public ObservableCollection<BatchResult> Results { get; } = new();

    /// <summary>解析统计文本（共 N · 有效 M · 错误 K）。</summary>
    public string StatsText
    {
        get => _statsText;
        private set => SetProperty(ref _statsText, value);
    }

    /// <summary>执行状态/结果汇总文本。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>执行进度 0..100。</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                ParseCommand.RaiseCanExecuteChanged();
                ExecuteCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>true 时右侧切到执行结果视图；解析预览时复位为 false。</summary>
    public bool ShowResults
    {
        get => _showResults;
        private set => SetProperty(ref _showResults, value);
    }

    // --- 命令与事件 ---

    /// <summary>载入文件请求：View 层弹 OpenFileDialog 后调用 <see cref="LoadFromFile"/>。</summary>
    public event EventHandler? LoadFileRequested;

    public RelayCommand LoadFileCommand { get; }
    public RelayCommand ParseCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>View 层选定文件后调用：读取文件内容填充文本框并立即解析预览。</summary>
    public void LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Text = File.ReadAllText(path);
        Parse();
    }

    // --- 解析与执行 ---

    /// <summary>解析当前文本：刷新 Items 与统计；复位结果视图。</summary>
    public void Parse()
    {
        var instructions = _parser.Parse(Text);
        Items.Clear();
        foreach (var ins in instructions)
            Items.Add(ins);
        Results.Clear();
        ShowResults = false;
        StatusText = string.Empty;
        var valid = instructions.Count(i => i.IsValid);
        StatsText = instructions.Count == 0
            ? "共 0 条"
            : $"共 {instructions.Count} 条 · 有效 {valid} · 错误 {instructions.Count - valid}";
    }

    /// <summary>
    /// 执行流程：裁切点先经回调分发到播放器（即时生效）；标记指令已不支持
    /// （文字笔记功能已随统一时间线重构移除），仅统计提示；截图/片段交给
    /// 执行器逐条跑 ffmpeg；完成或取消后汇总状态。
    /// </summary>
    private async Task ExecuteAsync()
    {
        Parse();
        var valid = Items.Where(i => i.IsValid).ToList();

        // 裁切点：文本层直接分发（不走 ffmpeg）；标记已不支持
        var markers = valid.Where(i => i.Type == BatchActionKind.Marker).ToList();
        var cutPoints = valid.Where(i => i.Type == BatchActionKind.CutPoint).ToList();
        foreach (var ins in cutPoints)
            _addCutPoint(ins.Start);
        // 汇总文本中的标记提示（已不支持）
        var markerNote = markers.Count > 0 ? $"；{markers.Count} 条标记指令已不支持" : string.Empty;

        // 截图/片段：需要已打开的视频
        var media = valid.Where(i => i.Type is BatchActionKind.Screenshot or BatchActionKind.Clip).ToList();
        if (media.Count == 0)
        {
            StatusText = markers.Count + cutPoints.Count == 0
                ? "没有可执行的指令"
                : $"已添加 {cutPoints.Count} 个裁切点{markerNote}（无截图/片段指令）";
            return;
        }
        var videoPath = _getVideoPath();
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            StatusText = "请先在主窗口打开视频文件";
            return;
        }

        IsRunning = true;
        Progress = 0;
        Results.Clear();
        ShowResults = true;
        _cts = new CancellationTokenSource();
        var done = 0;
        var ok = 0;
        try
        {
            await _executor.ExecuteAsync(media, videoPath, _cts.Token,
                (_, total, _, result) =>
                {
                    done++;
                    if (result.Success) ok++;
                    Results.Add(result);
                    Progress = total > 0 ? done * 100.0 / total : 100;
                });
            StatusText = $"执行完成：成功 {ok} · 失败 {media.Count - ok}" +
                         $"（已添加 {cutPoints.Count} 个裁切点{markerNote}）";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"已取消（完成 {done} · 成功 {ok}）";
        }
        catch (Exception ex)
        {
            StatusText = $"执行失败：{ex.Message}";
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }
}
