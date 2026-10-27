using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WorkJournal.MarketData;

public sealed class MarketDataException(string message) : HttpRequestException(message);
public sealed class MarketDataOptions
{
    public string CacheDirectory { get; set; } = "App_Data/market-data";
    public int FinMindRequestsPerHour { get; set; } = 200;
    public int OfficialRequestsPerHour { get; set; } = 240;
    public int OfficialIntervalMilliseconds { get; set; } = 1100;
    public int InteractiveFinMindReserve { get; set; } = 50;
    public int InteractiveOfficialReserve { get; set; } = 80;
}

// Singleton within one local application. Atomic files survive restarts; no tokens are stored.
public sealed class SourceCache(MarketDataOptions options, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public long Hits { get; private set; }
    public long Fetches { get; private set; }
    private sealed record Entry(DateTimeOffset ExpiresAt, JsonElement[] Rows);
    public async Task<JsonElement[]> GetAsync(string key, TimeSpan lifetime, Func<Task<JsonElement[]>> fetch, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var fileLock = await SharedFileLock.AcquireAsync(options.CacheDirectory,"cache.lock",ct);
            Directory.CreateDirectory(options.CacheDirectory);
            var path = Path.Combine(options.CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
            if (File.Exists(path))
            {
                try
                {
                    var saved = JsonSerializer.Deserialize<Entry>(await File.ReadAllTextAsync(path, ct));
                    if (saved != null && saved.ExpiresAt > clock.GetUtcNow()) { Hits++; return saved.Rows; }
                }
                catch (JsonException) { }
            }
            var rows = await fetch(); Fetches++;
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new Entry(clock.GetUtcNow().Add(lifetime), rows)), ct);
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return rows;
        }
        finally { gate.Release(); }
    }
}

// Persist the rolling budget and provider cooldown, including failed requests.
public sealed class RequestBudget(MarketDataOptions options, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public sealed class State
    {
        public List<DateTimeOffset> Requests { get; set; } = [];
        public DateTimeOffset BlockedUntil { get; set; }
    }
    private string PathFor(string source) => Path.Combine(options.CacheDirectory, source == "FinMind" ? "budget-finmind.json" : "budget-official.json");
    private async Task<State> Read(string source, CancellationToken ct)
    {
        var path = PathFor(source);
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(path, ct)) ?? new(); }
        catch (JsonException) { throw new MarketDataException("查詢預算紀錄損壞，請先檢查本機資料快取。"); }
    }
    private async Task Save(string source, State state, CancellationToken ct)
    {
        Directory.CreateDirectory(options.CacheDirectory);
        var path = PathFor(source); var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state), ct); File.Move(temp, path, true);
    }
    public async Task AcquireAsync(string source, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var fileLock = await SharedFileLock.AcquireAsync(options.CacheDirectory,"budget.lock",ct);
            var state = await Read(source, ct); var now = clock.GetUtcNow();
            state.Requests.RemoveAll(x => x <= now.AddHours(-1));
            var limit = source == "FinMind" ? options.FinMindRequestsPerHour : options.OfficialRequestsPerHour;
            if (MarketDataWork.IsBackground)
                limit = Math.Max(0,limit - Math.Max(0,source == "FinMind" ? options.InteractiveFinMindReserve : options.InteractiveOfficialReserve));
            if (now < state.BlockedUntil || state.Requests.Count >= Math.Max(0, limit))
                throw new MarketDataException($"{source} 已進入冷卻或達本機每小時查詢預算；已存資料仍可使用，請稍後再補資料。");
            if (source != "FinMind" && state.Requests.Count > 0)
            {
                var wait = state.Requests[^1].AddMilliseconds(Math.Max(0, options.OfficialIntervalMilliseconds)) - now;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            }
            state.Requests.Add(clock.GetUtcNow()); await Save(source, state, ct);
        }
        finally { gate.Release(); }
    }
    public async Task BlockAsync(string source, TimeSpan duration, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var fileLock = await SharedFileLock.AcquireAsync(options.CacheDirectory,"budget.lock",ct);
            var state = await Read(source, ct); state.BlockedUntil = clock.GetUtcNow().Add(duration); await Save(source, state, ct);
        }
        finally { gate.Release(); }
    }
}
