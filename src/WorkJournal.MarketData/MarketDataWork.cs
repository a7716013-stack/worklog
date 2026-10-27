namespace WorkJournal.MarketData;

public static class MarketDataWork
{
    private static readonly AsyncLocal<bool> background = new();
    public static bool IsBackground => background.Value;
    public static IDisposable BackgroundScope()
    {
        var previous = background.Value;
        background.Value = true;
        return new Restore(previous);
    }
    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => background.Value = previous;
    }
}
