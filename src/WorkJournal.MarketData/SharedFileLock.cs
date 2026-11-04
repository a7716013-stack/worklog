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
            catch (IOException ex) when (OperatingSystem.IsWindows()
                ? ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
                // Unix FileShare.None exposes EWOULDBLOCK as raw errno (Linux 11, macOS 35).
                : ex.HResult is 11 or 35)
            { await Task.Delay(100,ct); }
        }
    }
}
