namespace DrSoft.Drawing.Utility.AOP;

public static class DebugLogHub
{
    private const int MaxBufferedLines = 2000;
    private static readonly object SyncRoot = new();
    private static readonly Queue<string> BufferedLines = new();

    public static event Action<string>? MessageAppended;
    public static event Action? Cleared;

    public static void Append(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (SyncRoot)
        {
            BufferedLines.Enqueue(message);
            while (BufferedLines.Count > MaxBufferedLines)
            {
                BufferedLines.Dequeue();
            }
        }

        MessageAppended?.Invoke(message);
    }

    public static IReadOnlyCollection<string> GetSnapshot()
    {
        lock (SyncRoot)
        {
            return BufferedLines.ToArray();
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            BufferedLines.Clear();
        }

        Cleared?.Invoke();
    }
}
