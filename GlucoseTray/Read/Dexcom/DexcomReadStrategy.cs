using GlucoseTray.Enums;
using System.Text.Json;

namespace GlucoseTray.Read.Dexcom;

internal class DexcomReadStrategy(AppSettings settings, IExternalCommunicationAdapter communicator, IGlucoseReadingMapper mapper) : IReadStrategy
{
    private string? _cachedSessionId;
    private DateTime _sessionExpiry = DateTime.MinValue;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private readonly TimeSpan SessionTTL = TimeSpan.FromHours(24);
    private readonly TimeSpan ProactiveRefreshPoint = TimeSpan.FromHours(12);

    public async Task<GlucoseReading> GetLatestGlucoseAsync()
    {
        string sessionId = await GetSessionIdAsync();

        var dataRange = await GetDataRangeAsync(sessionId);
        if (dataRange?.LatestEgvTimeMs == null || dataRange.LatestEgvTimeMs <= 0)
        {
            throw new InvalidOperationException("Failed to get data range from Dexcom");
        }

        var latestEgvTime = UnixTimeStampToDateTime(dataRange.LatestEgvTimeMs);
        if (latestEgvTime <= _lastSyncTime)
        {
            return GetLastReadingOrThrow();
        }

        string response = await GetApiResponseAsync(sessionId);
        var data = JsonSerializer.Deserialize<List<DexcomResult>>(response)!.First();
        _lastSyncTime = latestEgvTime;

        var result = mapper.Map(data);
        return result;
    }

    private GlucoseReading GetLastReadingOrThrow()
    {
        if (_lastSyncTime == DateTime.MinValue)
        {
            throw new InvalidOperationException("No cached reading available and no new data from Dexcom");
        }
        return new GlucoseReading { TimestampUtc = _lastSyncTime, Trend = Trend.Unknown };
    }

    private async Task<DexcomDataRange?> GetDataRangeAsync(string sessionId)
    {
        try
        {
            var url = $"https://{GetDexComServer()}/ShareWebServices/Services/Publisher/ReadPublisherDataRange?sessionId={sessionId}&minutes=180";
            var response = await communicator.PostApiResponseAsync(url, sessionId);
            return JsonSerializer.Deserialize<DexcomDataRange>(response);
        }
        catch (Exception ex)
        {
            ThrowDexcomSpecificError(ex.Message);
            throw;
        }
    }

    private async Task<string> GetApiResponseAsync(string sessionId)
    {
        var url = $"https://{GetDexComServer()}/ShareWebServices/Services/Publisher/ReadPublisherLatestGlucoseValues?sessionId={sessionId}&minutes=1440&maxCount=1";
        var result = await communicator.PostApiResponseAsync(url, sessionId);
        return result;
    }

    private async Task<string> GetSessionIdAsync()
    {
        if (!string.IsNullOrEmpty(_cachedSessionId) && DateTime.UtcNow < _sessionExpiry)
            return _cachedSessionId;

        if (!string.IsNullOrEmpty(_cachedSessionId) && DateTime.UtcNow >= _sessionExpiry.Subtract(ProactiveRefreshPoint))
        {
            return await RefreshSessionAsync();
        }

        return await LoginAsync();
    }

    private async Task<string> LoginAsync()
    {
        var loginRequest = JsonSerializer.Serialize(new
        {
            accountName = settings.DexcomUsername,
            password = settings.DexcomPassword,
            applicationId = "d8665ade-9673-4e27-9ff6-92db4ce13d13"
        });

        var url = $"https://{GetDexComServer()}/ShareWebServices/Services/General/LoginPublisherAccountByName";

        try
        {
            var response = await communicator.PostApiResponseAsync(url, loginRequest);
            var sessionId = DeserializeStringResponse(response);

            _cachedSessionId = sessionId;
            _sessionExpiry = DateTime.UtcNow.Add(SessionTTL);

            return sessionId;
        }
        catch (Exception ex)
        {
            ThrowDexcomSpecificError(ex.Message);
            throw;
        }
    }

    private async Task<string> RefreshSessionAsync()
    {
        _cachedSessionId = null;
        return await LoginAsync();
    }

    private static string DeserializeStringResponse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(json) ?? throw new InvalidOperationException("Response was null");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to deserialize response: {json}", ex);
        }
    }

    private static void ThrowDexcomSpecificError(string responseBody)
    {
        if (responseBody.Contains("AccountPasswordInvalid") || responseBody.Contains("PasswordInvalid"))
            throw new InvalidOperationException("Invalid Dexcom password. Fix credentials and restart.");

        if (responseBody.Contains("AccountNotFound"))
            throw new InvalidOperationException("Dexcom account not found. Check username.");

        if (responseBody.Contains("AccountLocked"))
            throw new InvalidOperationException("Dexcom account locked. Contact Dexcom support.");

        if (responseBody.Contains("SessionIdNotFound") || responseBody.Contains("SessionNotValid"))
            throw new InvalidOperationException("Session expired on Dexcom server. Will re-login.");
    }

    private static DateTime UnixTimeStampToDateTime(long ms)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddMilliseconds(ms);
    }

    public string GetDexComServer() => settings.DexcomServer switch
    {
        DexcomServer.DexcomShare1 => "share1.dexcom.com",
        DexcomServer.DexcomShare2 => "share2.dexcom.com",
        DexcomServer.DexcomInternational => "shareous1.dexcom.com",
        _ => "share1.dexcom.com",
    };
}
