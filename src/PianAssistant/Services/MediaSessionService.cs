using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 素材会话自动保存/恢复服务（M7）。
/// 两级键：打开瞬间用「路径+大小+mtime」快速键立即还原；后台异步算内容哈希（SHA-256）
/// 作为规范键，命中更新的会话时再次还原并回写。数据变更后 ~2s 防抖落库，
/// 由应用退出/异常兜底 flush。服务层不依赖 ViewModel：通过委托采集/应用会话数据。
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private readonly ProjectRepository _repository;
    private readonly AppSettingsService _settings;
    private readonly Action<Action> _dispatch;
    private readonly Func<string, CancellationToken, Task<string?>> _computeHash;

    private readonly object _gate = new();
    private Timer? _debounceTimer;
    private bool _disposed;

    private string? _currentFastKey;
    private string? _currentPath;
    private string? _currentHash;
    // 会话代次：每次切换素材（BeginSession/EndSession）+1。
    // LoadAsync 的内容哈希计算是慢操作（全文件 SHA-256，大文件数秒），期间用户
    // 可能已切到别的素材——旧加载完成后必须整体丢弃：既不能套用旧会话
    // （否则上一个视频的笔记/截图会刷到新视频），也不能把旧哈希回写到新素材行（v1.7.5）。
    private long _generation;

    /// <summary>防抖窗口（默认 2s；供测试缩短）。</summary>
    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>采集当前会话快照（由 ShellViewModel 注入）。</summary>
    public Func<MediaSessionSnapshot>? CaptureSnapshot { get; set; }

    /// <summary>把会话数据应用到当前会话（由 ShellViewModel 注入，服务保证在 UI 线程回调）。</summary>
    public Action<MediaSession>? ApplySession { get; set; }

    /// <summary>成功还原一次会话（快速键命中 / 哈希升级）后触发，供提示条展示统计。</summary>
    public event EventHandler<SessionRestoreResult>? SessionRestored;

    /// <summary>每次成功落库后触发（测试用于校验防抖只写一次）。</summary>
    public event EventHandler? Persisted;

    public MediaSessionService(
        ProjectRepository repository,
        AppSettingsService settings,
        Action<Action>? dispatch = null,
        Func<string, CancellationToken, Task<string?>>? computeHash = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        // 生产环境由 Shell 注入 Dispatcher；测试默认同步执行。
        _dispatch = dispatch ?? (action => action());
        // 哈希计算可注入（测试用于确定性复现代次竞态）；默认全文件 SHA-256
        _computeHash = computeHash ?? ContentFingerprint.ComputeContentHashAsync;
    }

    /// <summary>
    /// 开始跟踪一个素材（打开视频时调用）：记录快速键与路径，准备后续落库。
    /// 与是否开启自动恢复无关——只要发生变更就应能保存。
    /// </summary>
    public void BeginSession(MediaFile media)
    {
        ArgumentNullException.ThrowIfNull(media);
        lock (_gate)
        {
            _generation++;
            _currentFastKey = ContentFingerprint.ComputeFastKey(media.Path);
            _currentPath = media.Path;
            _currentHash = null;
        }
    }

    /// <summary>
    /// 结束当前素材会话：立即 flush 并清空跟踪状态（切换视频前调用）。
    /// </summary>
    public void EndSession()
    {
        Flush();
        lock (_gate)
        {
            _generation++;
            _currentFastKey = null;
            _currentPath = null;
            _currentHash = null;
        }
    }

    /// <summary>
    /// 按快速键同步尝试还原（打开瞬间即可完成）。返回还原结果（含统计）。
    /// 会话为空时不还原、不创建记录。
    /// </summary>
    public SessionRestoreResult TryRestore(MediaFile media)
    {
        ArgumentNullException.ThrowIfNull(media);
        BeginSession(media);
        MediaSession? session;
        try
        {
            var fastKey = _currentFastKey;
            if (fastKey is null) return SessionRestoreResult.NotRestored;
            session = _repository.LoadSessionByFastKey(fastKey);
        }
        catch (Exception ex)
        {
            AppLog.Error("[MediaSessionService] 快速键查询失败", ex);
            return SessionRestoreResult.NotRestored;
        }
        if (session is null || IsEmpty(session))
            return SessionRestoreResult.NotRestored;
        long gen;
        lock (_gate) { _currentHash = session.ContentHash; gen = _generation; }
        return ApplyAndReport(session, gen);
    }

    /// <summary>
    /// 打开素材后的完整恢复流程：先快速键还原，随后后台算内容哈希，
    /// 若命中「更新的」会话（含另一路径/改名的同一素材）则再次还原。
    /// 哈希计算不阻塞 UI（在后台线程）。
    /// </summary>
    public async Task<SessionRestoreResult> LoadAsync(MediaFile media)
    {
        ArgumentNullException.ThrowIfNull(media);
        var fastResult = TryRestore(media);
        // 捕获代次：哈希计算期间若用户切换素材（BeginSession/EndSession 会 +1），
        // 本次加载的后续套用与哈希回写全部作废
        long gen;
        lock (_gate) gen = _generation;

        string? hash;
        try
        {
            hash = await _computeHash(media.Path, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("[MediaSessionService] 计算内容哈希失败", ex);
            return fastResult;
        }
        if (string.IsNullOrEmpty(hash)) return fastResult;

        lock (_gate)
        {
            if (gen != _generation) return fastResult; // 已切换素材：丢弃
            _currentHash = hash;
        }

        var result = fastResult;
        try
        {
            string? fastKey;
            lock (_gate) fastKey = _currentFastKey;
            var hashSession = _repository.LoadSessionByHash(hash);

            // 哈希命中「另一行」（另一设备/改名）：按 updated_at 取较新者合并。
            if (hashSession is not null && hashSession.FastKey != fastKey)
            {
                var current = fastKey is null
                    ? null
                    : _repository.LoadSessionByFastKey(fastKey);
                var hashIsNewer = current is null || hashSession.UpdatedAt > current.UpdatedAt;
                if (hashIsNewer && !IsEmpty(hashSession))
                    result = ApplyAndReport(hashSession, gen);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("[MediaSessionService] 哈希升级失败", ex);
        }

        // 把哈希回写到当前快速键行（内容哈希已被占用时 repo 会自动置空）。
        TryWriteBackHash(gen);
        return result;
    }

    /// <summary>把当前内容哈希回写到当前快速键会话行（幂等、失败仅记日志）。</summary>
    private void TryWriteBackHash(long gen)
    {
        try
        {
            string? fastKey, hash;
            lock (_gate)
            {
                if (gen != _generation) return; // 已切换素材：禁止把旧哈希写进新素材行
                fastKey = _currentFastKey;
                hash = _currentHash;
            }
            if (fastKey is null || string.IsNullOrEmpty(hash)) return;
            var current = _repository.LoadSessionByFastKey(fastKey);
            if (current is null) return;
            current.ContentHash = hash;
            _repository.UpsertSession(current);
        }
        catch (Exception ex)
        {
            AppLog.Error("[MediaSessionService] 回写内容哈希失败", ex);
        }
    }

    /// <summary>标记会话已变更：~2s 防抖后落库（多次调用只落库一次）。</summary>
    public void MarkDirty()
    {
        lock (_gate)
        {
            if (_disposed || _currentFastKey is null) return;
            if (_debounceTimer is null)
            {
                _debounceTimer = new Timer(_ => FlushCore(), null,
                    DebounceInterval, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>立即落库（应用退出 / 切换素材 / 关闭窗口时调用）。</summary>
    public void Flush()
    {
        lock (_gate)
        {
            _debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        FlushCore();
    }

    private void FlushCore()
    {
        try
        {
            string? fastKey, path, hash;
            lock (_gate)
            {
                fastKey = _currentFastKey;
                path = _currentPath;
                hash = _currentHash;
            }
            if (fastKey is null || path is null) return;

            var snapshot = CaptureSnapshot?.Invoke();
            if (snapshot is null) return;

            var session = BuildSession(fastKey, path, hash, snapshot);
            // 空会话不创建记录（无裁切/截图/笔记/入出点/位置）。
            if (IsEmpty(session)) return;

            _repository.UpsertSession(session);
            Persisted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("[MediaSessionService] 落库失败", ex);
        }
    }

    private SessionRestoreResult ApplyAndReport(MediaSession session, long gen)
    {
        // 若关闭「恢复播放位置」则清零位置与倍速，仅还原标记类数据。
        if (!_settings.RestorePlaybackPosition)
        {
            session.LastPositionMs = 0;
            session.Rate = 1.0;
        }
        var result = new SessionRestoreResult(
            true, session.CutPointsMs.Count, session.Screenshots.Count, session.Notes.Count,
            session.LastPositionMs > 0);
        _dispatch(() =>
        {
            // 派发执行前再校验一次代次：dispatch 排队期间可能已切换素材
            lock (_gate) { if (gen != _generation) return; }
            ApplySession?.Invoke(session);
            SessionRestored?.Invoke(this, result);
        });
        return result;
    }

    private static MediaSession BuildSession(
        string fastKey, string path, string? hash, MediaSessionSnapshot snapshot)
    {
        long size = 0, mtime = 0;
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists) { size = fi.Length; mtime = fi.LastWriteTimeUtc.Ticks; }
        }
        catch { /* 文件元数据读取失败不阻断 */ }

        var session = new MediaSession
        {
            FastKey = fastKey,
            ContentHash = hash,
            Path = path,
            Size = size,
            MtimeUtcTicks = mtime,
            LastPositionMs = (long)snapshot.Position.TotalMilliseconds,
            Rate = snapshot.Rate,
            InPointMs = snapshot.InPoint is { } inP ? (long)inP.TotalMilliseconds : null,
            OutPointMs = snapshot.OutPoint is { } outP ? (long)outP.TotalMilliseconds : null,
            UpdatedAt = DateTime.Now,
        };
        foreach (var cp in snapshot.CutPoints)
            session.CutPointsMs.Add((long)cp.TotalMilliseconds);
        foreach (var s in snapshot.Screenshots)
        {
            session.Screenshots.Add(new SessionScreenshot
            {
                FilePath = s.OutputPath,
                PositionMs = (long)s.Position.TotalMilliseconds,
                CapturedAt = s.CapturedAt,
                Label = s.Label,
            });
        }
        foreach (var n in snapshot.Notes)
        {
            session.Notes.Add(new SessionNote
            {
                PositionMs = (long)n.Position.TotalMilliseconds,
                Text = n.Text,
                CreatedAt = n.CreatedAt,
            });
        }
        return session;
    }

    private static bool IsEmpty(MediaSession s) =>
        s.CutPointsMs.Count == 0
        && s.Screenshots.Count == 0
        && s.Notes.Count == 0
        && s.InPointMs is null
        && s.OutPointMs is null
        && s.LastPositionMs == 0
        && Math.Abs(s.Rate - 1.0) < 0.001;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}

/// <summary>会话还原结果（供提示条展示统计）。</summary>
public readonly record struct SessionRestoreResult(
    bool Restored, int CutPointCount, int ScreenshotCount, int NoteCount, bool PositionRestored)
{
    public static readonly SessionRestoreResult NotRestored = new(false, 0, 0, 0, false);

    /// <summary>右下角提示条文本。</summary>
    public string Summary =>
        $"已恢复上次拉片记录：{CutPointCount} 处裁切 / {ScreenshotCount} 张截图 / {NoteCount} 条笔记";
}
