using GlucoseTray.Display;
using GlucoseTray.Read;
using Microsoft.Extensions.Options;
using System.Net;

namespace GlucoseTray;

public class AppRunner(ITray tray, IGlucoseReader reader, IOptionsMonitor<AppSettings> options)
{
    private int _consecutiveFailures = 0;
    private readonly int MaxRetries = 3;
    private const int MinRefreshIntervalMinutes = 5; // Matches Dexcom Share's server-side update cadence; polling faster only burns API calls and risks a rate-limit lockout.
    private static readonly TimeSpan ConfigChangeDebounce = TimeSpan.FromSeconds(2);
    private CancellationTokenSource? _configChangeDebounceCts;

    public async Task Start()
    {
        options.OnChange(_ => OnConfigChanged());

        await Process();

        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(options.CurrentValue.RefreshIntervalInMinutes, MinRefreshIntervalMinutes)));
                await Process();
                _consecutiveFailures = 0;
            }
            catch
            {
                tray.Dispose();
                throw;
            }
        }
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
        try
        {
            var result = await reader.GetLatestGlucoseAsync();
            tray.Refresh(result);
            _consecutiveFailures = 0;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid Dexcom") || ex.Message.Contains("account not found") || ex.Message.Contains("account locked"))
        {
            tray.Dispose();
            throw;
        }
        catch (HttpRequestException ex) when (IsRetryable(ex) && _consecutiveFailures < MaxRetries)
        {
            _consecutiveFailures++;
            var retryDelay = Math.Pow(2, _consecutiveFailures - 1);
            await Task.Delay(TimeSpan.FromSeconds(retryDelay * 5));
            await Process();
        }
    }

    private static bool IsRetryable(HttpRequestException ex)
    {
        if (ex.StatusCode == null)
            return true;

        return ex.StatusCode switch
        {
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout or
            (HttpStatusCode)429 => true,
            _ => false
        };
    }
}
