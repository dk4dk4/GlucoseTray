using GlucoseTray.Display;
using GlucoseTray.Read;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace GlucoseTray;

public class AppRunner(ITray tray, IGlucoseReader reader, IOptionsMonitor<AppSettings> options)
{
    private static readonly TimeSpan ReadingCadence = TimeSpan.FromMinutes(5); // Matches Dexcom Share's server-side update cadence.
    private static readonly TimeSpan PropagationBuffer = TimeSpan.FromSeconds(45); // Slack for Dexcom's upload/processing lag after a reading is due.
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromSeconds(30); // Used to catch a just-missed reading without waiting a full cycle.
    private static readonly TimeSpan SensorGapThreshold = TimeSpan.FromMinutes(20); // Beyond this, assume a sensor gap/warmup rather than a transient miss.
    private static readonly TimeSpan SensorGapCadence = ReadingCadence + PropagationBuffer; // Fallback polling rate during a sensor gap, to avoid fast-polling indefinitely.
    private static readonly TimeSpan ConfigChangeDebounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BaseBackoffDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoffDelay = TimeSpan.FromMinutes(5);
    private static readonly Random Jitter = new();
    private CancellationTokenSource? _configChangeDebounceCts;
    private CancellationTokenSource _wakeCts = new();
    private DateTime? _lastReadingTimestampUtc;
    private int _consecutiveFailures;

    public async Task Start()
    {
        options.OnChange(_ => OnConfigChanged());
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        try
        {
            await Process();

            while (true)
            {
                var interval = ComputeNextDelay(_lastReadingTimestampUtc, _consecutiveFailures, DateTime.UtcNow);

                await WaitForNextCycle(interval);
                await Process();
            }
        }
        catch (Exception ex)
        {
            // GlucoseReader swallows transient fetch errors internally and falls back to cached data,
            // so anything that reaches here (e.g. invalid credentials) is unrecoverable — surface it
            // instead of leaving a frozen tray icon with no indication anything is wrong.
            AppLog.Error("Fatal error in refresh loop; shutting down.", ex);
            tray.Dispose();
            throw;
        }
        finally
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }

    private async Task WaitForNextCycle(TimeSpan interval)
    {
        var wakeToken = _wakeCts.Token;
        try
        {
            await Task.Delay(interval, wakeToken);
        }
        catch (OperationCanceledException) when (wakeToken.IsCancellationRequested)
        {
            AppLog.Warn("Woke from sleep; refreshing immediately.");
        }
        finally
        {
            if (wakeToken.IsCancellationRequested)
            {
                _wakeCts.Dispose();
                _wakeCts = new CancellationTokenSource();
            }
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            _wakeCts.Cancel();
    }

    private void OnConfigChanged()
    {
        // IOptionsMonitor.OnChange commonly fires multiple times for a single file save
        // (editors write via temp-file + rename). Debounce so one save triggers one Process() call.
        var cts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _configChangeDebounceCts, cts);
        previousCts?.Cancel();
        previousCts?.Dispose();

        _ = DebouncedProcessAsync(cts.Token);
    }

    private async Task DebouncedProcessAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ConfigChangeDebounce, token);
            await Process();
        }
        catch (TaskCanceledException)
        {
            // Superseded by a newer config change; ignore.
        }
    }

    public async Task Process()
    {
        var result = await reader.GetLatestGlucoseAsync();
        tray.Refresh(result);

        if (reader.LastFetchFailed)
        {
            _consecutiveFailures++;
            AppLog.Warn($"Poll failed ({_consecutiveFailures} consecutive); backing off.");
        }
        else
        {
            _consecutiveFailures = 0;
        }

        _lastReadingTimestampUtc = result.TimestampUtc;
    }

    // Phase-aligns polling to when Dexcom's next reading is actually expected, rather than
    // polling on a flat timer: sleep until the reading is due (+ a buffer for upload lag), fast-poll
    // briefly if it's just late, and fall back to a flat cadence during a longer sensor gap so we
    // don't fast-poll indefinitely. A run of failures overrides this with exponential backoff.
    public static TimeSpan ComputeNextDelay(DateTime? lastReadingTimestampUtc, int consecutiveFailures, DateTime nowUtc)
    {
        if (consecutiveFailures > 0)
            return ComputeBackoffDelay(consecutiveFailures);

        if (lastReadingTimestampUtc is null)
            return SensorGapCadence;

        var age = nowUtc - lastReadingTimestampUtc.Value;
        var timeUntilNext = ReadingCadence - age;

        if (timeUntilNext >= TimeSpan.Zero)
            return timeUntilNext + PropagationBuffer;

        return age <= SensorGapThreshold ? FastPollInterval : SensorGapCadence;
    }

    private static TimeSpan ComputeBackoffDelay(int consecutiveFailures)
    {
        var shift = Math.Min(consecutiveFailures - 1, 10); // cap the shift so it can't overflow before the MaxBackoffDelay clamp below
        var exponential = TimeSpan.FromTicks(BaseBackoffDelay.Ticks * (1L << shift));
        var capped = exponential > MaxBackoffDelay ? MaxBackoffDelay : exponential;
        var jitterFactor = 0.8 + Jitter.NextDouble() * 0.4; // +/- 20%
        return TimeSpan.FromTicks((long)(capped.Ticks * jitterFactor));
    }
}
