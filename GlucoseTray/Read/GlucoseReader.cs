using GlucoseTray.Enums;
using GlucoseTray.Read.Dexcom;
using GlucoseTray.Read.Nightscout;
using Microsoft.Extensions.Options;

namespace GlucoseTray.Read;

public interface IGlucoseReader
{
    Task<GlucoseReading> GetLatestGlucoseAsync();

    // True when the most recent GetLatestGlucoseAsync call fell back to cached/placeholder data
    // instead of a fresh fetch, so callers (AppRunner's poll scheduler) can back off on repeated failures.
    bool LastFetchFailed { get; }
}

public class GlucoseReader(IOptionsMonitor<AppSettings> options, IExternalCommunicationAdapter communicator, IGlucoseReadingMapper mapper) : IGlucoseReader
{
    private GlucoseReading? _latestReading;
    private IReadStrategy? _cachedStrategy;
    private GlucoseSource _cachedStrategySource;
    private bool _subscribedToConfigChanges;

    public bool LastFetchFailed { get; private set; }

    public async Task<GlucoseReading> GetLatestGlucoseAsync()
    {
        IReadStrategy strategy = GetReadStrategy();

        try
        {
            _latestReading = await strategy.GetLatestGlucoseAsync();
            LastFetchFailed = false;
            return _latestReading;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No cached reading"))
        {
            LastFetchFailed = true;
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Glucose fetch failed, falling back to last known reading: {ex.GetType().Name}: {ex.Message}");
            LastFetchFailed = true;
            return _latestReading ?? new GlucoseReading() { TimestampUtc = DateTime.UtcNow, Trend = Trend.Unknown };
        }
    }

    // Reused across polls (rather than recreated each time) so DexcomReadStrategy's session
    // cache actually persists between calls instead of forcing a fresh login every poll.
    private IReadStrategy GetReadStrategy()
    {
        if (!_subscribedToConfigChanges)
        {
            // Settings are captured by the strategy at construction time, so the cached strategy
            // must be dropped whenever config changes (e.g. edited credentials) — otherwise it
            // would silently keep using stale settings.
            options.OnChange(_ => _cachedStrategy = null);
            _subscribedToConfigChanges = true;
        }

        var source = options.CurrentValue.DataSource;
        if (_cachedStrategy is not null && _cachedStrategySource == source)
            return _cachedStrategy;

        _cachedStrategySource = source;
        _cachedStrategy = source == GlucoseSource.Dexcom
            ? new DexcomReadStrategy(options.CurrentValue, communicator, mapper)
            : new NightscoutReadStrategy(options.CurrentValue, communicator, mapper);
        return _cachedStrategy;
    }
}
