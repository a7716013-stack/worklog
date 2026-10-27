namespace WorkJournal.MarketData;

// Protect overlapping application processes during an App Service restart as well as local tools.
internal static class SharedFileLock
{
    public static async Task<FileStream> AcquireAsync(string directory, string name, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(directory,name),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            { await Task.Delay(100,ct); }
        }
    }
}
