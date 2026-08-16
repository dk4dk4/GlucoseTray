using GlucoseTray.Display;
using GlucoseTray.Read;
using Microsoft.Extensions.Options;
using System.Net;

namespace GlucoseTray;

public class AppRunner(ITray tray, IGlucoseReader reader, IOptionsMonitor<AppSettings> options)
{
    private int _consecutiveFailures = 0;
    private readonly int MaxRetries = 3;

    public async Task Start()
    {
        options.OnChange(async _ => await Process());

        await Process();

        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(options.CurrentValue.RefreshIntervalInMinutes, 1)));
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
