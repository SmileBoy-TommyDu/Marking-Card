namespace DrSoft.Drawing.Model;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IDrawingCommand
{
    string Description { get; }
    void Execute();
    /// <summary>
    /// 撤销命令。返回 true 表示撤销成功，返回 false 表示撤销被拒绝（如最后一个图层不允许撤销）。
    /// 当返回 false 时，CommandManager 会将命令推回撤销栈，不加入重做栈。
    /// </summary>
    bool Undo();
}

/// <summary>
/// 延迟捕获命令：在操作开始时捕获 Before 状态，操作完成时调用 CaptureAfterState 捕获 After 状态。
/// 支持 ToolSelect 拖拽操作的两阶段快照模式。
/// </summary>
public interface IDeferredCommand : IDrawingCommand
{
    void CaptureAfterState();
}

/// <summary>
/// 可合并命令：支持将连续的同类操作合并为一步撤销。
/// 典型场景：参数面板滑块拖动、方向键连续微调。
/// </summary>
public interface ICoalescableCommand : IDrawingCommand
{
    /// <summary>
    /// 尝试将 incoming 命令合并到当前命令中。
    /// 合并后当前命令应包含完整的 Before→After 状态变化。
    /// 返回 true 表示合并成功（incoming 将被丢弃），false 表示不可合并。
    /// </summary>
    bool TryMergeWith(ICoalescableCommand incoming);
}


public class CompositeCommand : IDrawingCommand
{
    private readonly List<IDrawingCommand> _cmds;
    public string Description { get; }

    /// <summary>需要组合多个 CompositeCommand</summary>
    public IReadOnlyList<IDrawingCommand> Commands => _cmds;

    public CompositeCommand(string desc, List<IDrawingCommand> cmds)
    {
        Description = desc;
        _cmds       = cmds;
    }

    public void Execute() { foreach (var c in _cmds)               c.Execute(); }
    public bool Undo()    { foreach (var c in _cmds.AsEnumerable().Reverse()) { if (!c.Undo()) return false; } return true; }
}

// ── Manager ───────────────────────────────────────────────────────────────────

public class CommandHistory
{
    public readonly record struct HistoryStateSnapshot(
        int UndoCount,
        int RedoCount,
        int MutationVersion);

    public readonly record struct HistoryCommandSnapshot(
        IReadOnlyList<IDrawingCommand> PendingCommands,
        IReadOnlyList<IDrawingCommand> UndoCommands,
        IReadOnlyList<IDrawingCommand> RedoCommands,
        bool IsInTransaction,
        string? TransactionDescription);

    private readonly Stack<IDrawingCommand> _undo = new();
    private readonly Stack<IDrawingCommand> _redo = new();
    private readonly int _maxHistory;
    private int _mutationVersion;

    // ── 事务/批次 ──────────────────────────────────────────────────
    private List<IDrawingCommand>? _transactionBuffer;
    private string? _transactionDescription;

    /// <summary>
    /// 后处理回调：在 Execute/Undo/Redo 完成后自动调用。
    /// 用于统一执行选区刷新、填充重建等操作，避免各 Command 重复编写样板代码。
    /// 回调参数为刚执行/撤销/重做的命令。
    /// </summary>
    public Action<IDrawingCommand>? PostProcessCallback { get; set; }

    public bool   CanUndo          => _undo.Count > 0;
    public bool   CanRedo          => _redo.Count > 0;
    public string? UndoDescription => _undo.TryPeek(out var c) ? c.Description : null;
    public string? RedoDescription => _redo.TryPeek(out var c) ? c.Description : null;
    public event EventHandler? CommandExecuted;
    public CommandHistory(int maxHistory = 50)
    {
        _maxHistory = maxHistory;
    }
    public void Execute(IDrawingCommand cmd)
    {
        cmd.Execute();
        PushExecutedCommand(cmd);
        // 注意：首次 Execute 不调用 PostProcessCallback，由各 Command 自行处理 UI 更新。
        // PostProcessCallback 仅在 Undo/Redo 时调用，统一刷新选区和填充。
    }

    public void PushExecutedCommand(IDrawingCommand cmd)
    {
        // 事务中：收集到缓冲区，不直接压栈
        if (_transactionBuffer != null)
        {
            _transactionBuffer.Add(cmd);
            _mutationVersion++;
            CommandExecuted?.Invoke(this, EventArgs.Empty);
            return;
        }

        // 尝试与栈顶命令合并（Coalescing）
        if (cmd is ICoalescableCommand coalescable
            && _undo.TryPeek(out var top)
            && top is ICoalescableCommand topCoalescable
            && topCoalescable.TryMergeWith(coalescable))
        {
            // 合并成功：栈顶命令已吸收 incoming，无需压栈
            _redo.Clear();
            _mutationVersion++;
            CommandExecuted?.Invoke(this, EventArgs.Empty);
            return;
        }

        _undo.Push(cmd);
        _redo.Clear();

        // 超出最大历史数时，丢弃最旧记录
        if (_undo.Count > _maxHistory)
            TrimStack();

        _mutationVersion++;
        CommandExecuted?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!_undo.TryPop(out var cmd)) return;
        bool success = cmd.Undo();
        if (success)
        {
            _redo.Push(cmd);
            PostProcessCallback?.Invoke(cmd);
            _mutationVersion++;
        }
        else
        {
            // 撤销被拒绝（如最后一个图层不允许撤销），将命令推回撤销栈
            _undo.Push(cmd);
        }
        CommandExecuted?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var cmd)) return;
        cmd.Execute();
        _undo.Push(cmd);
        PostProcessCallback?.Invoke(cmd);
        _mutationVersion++;
        CommandExecuted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 开始事务：此后所有 PushExecutedCommand 调用将收集到缓冲区，
    /// 直到 EndTransaction() 将它们合并为一个 CompositeCommand 压入撤销栈。
    /// </summary>
    public void BeginTransaction(string description)
    {
        _transactionBuffer = new List<IDrawingCommand>();
        _transactionDescription = description;
    }

    /// <summary>
    /// 结束事务：将缓冲区内的命令合并为一个 CompositeCommand 压入撤销栈。
    /// 若缓冲区内只有 0 或 1 条命令，则直接压栈（不额外包装）。
    /// </summary>
    public void EndTransaction()
    {
        var buffer = _transactionBuffer;
        var desc = _transactionDescription;
        _transactionBuffer = null;
        _transactionDescription = null;

        if (buffer == null || buffer.Count == 0) return;

        IDrawingCommand cmd = buffer.Count == 1
            ? buffer[0]
            : new CompositeCommand(desc ?? "事务", buffer);

        _undo.Push(cmd);
        _redo.Clear();
        if (_undo.Count > _maxHistory)
            TrimStack();
        _mutationVersion++;
        CommandExecuted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 是否正处于事务中。
    /// </summary>
    public bool IsInTransaction => _transactionBuffer != null;

    /// <summary>
    /// 清空撤销/重做栈，释放命令对象引用（用于画布关闭时释放内存）。
    /// </summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _mutationVersion++;
        CommandExecuted?.Invoke(this, EventArgs.Empty);
    }

    public HistoryStateSnapshot CaptureStateSnapshot()
    {
        var snapshot = new HistoryStateSnapshot(
            _undo.Count,
            _redo.Count,
            _mutationVersion);
        return snapshot;
    }

    public HistoryCommandSnapshot CaptureCommandSnapshot()
    {
        var pendingCommands = _transactionBuffer?.ToArray() ?? Array.Empty<IDrawingCommand>();
        var undoCommands = _undo.ToArray();
        var redoCommands = _redo.ToArray();

        var snapshot = new HistoryCommandSnapshot(
            pendingCommands,
            undoCommands,
            redoCommands,
            _transactionBuffer != null,
            _transactionDescription);
        return snapshot;
    }

    private void TrimStack()
    {
        // Stack 不支持直接移除底部，转换后重建
        var items = _undo.ToArray(); // 顶部在前
        _undo.Clear();
        foreach (var item in items.Take(_maxHistory - 1).Reverse())
            _undo.Push(item);
    }
}
