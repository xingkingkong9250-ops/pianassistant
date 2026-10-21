using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 笔记操作撤回历史（v1.6.0）：快照栈，每次笔记数据变更前压入一份
/// <see cref="NotesSnapshot"/>，Ctrl+Z / ↩ 按钮弹出并恢复。
/// 栈上限实时读取设置「撤回步数上限」（默认 5），超出时丢弃最旧快照，
/// 避免长时间会话积累过多数据。
/// </summary>
public sealed class UndoHistoryService
{
    private readonly Func<int> _getLimit;
    private readonly List<NotesSnapshot> _stack = new();

    /// <param name="getLimit">实时读取栈上限的委托（设置保存后即生效）。</param>
    public UndoHistoryService(Func<int> getLimit)
    {
        ArgumentNullException.ThrowIfNull(getLimit);
        _getLimit = getLimit;
    }

    /// <summary>栈内容变化（Push/Pop/Clear 后触发，供命令 CanExecute 刷新）。</summary>
    public event EventHandler? Changed;

    /// <summary>当前可撤回步数。</summary>
    public int Count => _stack.Count;

    public bool CanUndo => _stack.Count > 0;

    /// <summary>压入一份变更前快照；超出上限时丢弃最旧快照。</summary>
    public void Push(NotesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _stack.Add(snapshot);
        var limit = Math.Max(1, _getLimit());
        while (_stack.Count > limit)
            _stack.RemoveAt(0);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>弹出最近一份快照；栈空返回 null。</summary>
    public NotesSnapshot? Pop()
    {
        if (_stack.Count == 0) return null;
        var snapshot = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
        return snapshot;
    }

    /// <summary>清空历史（切换素材时调用）。</summary>
    public void Clear()
    {
        if (_stack.Count == 0) return;
        _stack.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
