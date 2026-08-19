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
    private const int MinSessionIdLength = 32; // Dexcom session IDs are GUIDs; shorter responses are garbage/error bodies.

    public async Task<GlucoseReading> GetLatestGlucoseAsync()
    {
        try
        {
            return await GetLatestGlucoseInternalAsync();
        }
        catch (HttpRequestException ex) when (IsSessionInvalidError(ex))
        {
            // The server can invalidate a session (e.g. after the PC sleeps overnight) well
            // before our local TTL expires, so a rejected session must force a fresh login
            // instead of being retried as-is on the next poll.
            _cachedSessionId = null;
            return await GetLatestGlucoseInternalAsync();
        }
    }

    private static bool IsSessionInvalidError(HttpRequestException ex) =>
        ex.Message.Contains("SessionNotValid") || ex.Message.Contains("SessionIdNotFound");

    private async Task<GlucoseReading> GetLatestGlucoseInternalAsync()
    {
        string sessionId = await GetSessionIdAsync();

        var dataRange = await GetDataRangeAsync(sessionId);
        if (dataRange?.LatestEgvTimeMs > 0)
        {
            var latestEgvTime = UnixTimeStampToDateTime(dataRange.LatestEgvTimeMs);
            if (latestEgvTime > _lastSyncTime)
            {
                string response = await GetApiResponseAsync(sessionId);
                var data = JsonSerializer.Deserialize<List<DexcomResult>>(response)!;
                if (data.Count == 0)
                    return new GlucoseReading { TimestampUtc = _lastSyncTime, Trend = Trend.Unknown };

                _lastSyncTime = latestEgvTime;
                var result = mapper.Map(data.First());
                return result;
            }
            else if (_lastSyncTime > DateTime.MinValue)
            {
                return new GlucoseReading { TimestampUtc = _lastSyncTime, Trend = Trend.Unknown };
            }
        }

        string directResponse = await GetApiResponseAsync(sessionId);
        var directData = JsonSerializer.Deserialize<List<DexcomResult>>(directResponse)!;
        // Empty array (e.g. sensor warmup) is expected, not a failure — surface the last known
        // reading instead of throwing, so it isn't mistaken for a communication error upstream.
        if (directData.Count == 0)
            return new GlucoseReading { TimestampUtc = _lastSyncTime, Trend = Trend.Unknown };

        _lastSyncTime = DateTime.UtcNow;

        var directResult = mapper.Map(directData.First());
        return directResult;
    }

    private async Task<DexcomDataRange?> GetDataRangeAsync(string sessionId)
    {
        try
        {
            var url = $"https://{GetDexComServer()}/ShareWebServices/Services/Publisher/ReadPublisherDataRange?sessionId={sessionId}&minutes=180";
            var response = await communicator.PostApiResponseAsync(url, sessionId);
            return JsonSerializer.Deserialize<DexcomDataRange>(response);
        }
        catch
        {
            return null;
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

        var response = await communicator.PostApiResponseAsync(url, loginRequest);
        ThrowIfDexcomError(response);

        var sessionId = DeserializeStringResponse(response);

        // Dexcom returns a GUID session ID on success; a short/garbage 200 response shouldn't be
        // cached and trusted as a valid session for subsequent reads.
        if (sessionId.Length < MinSessionIdLength)
            throw new InvalidOperationException($"Dexcom login returned an invalid session ID: {sessionId}");

        _cachedSessionId = sessionId;
        _sessionExpiry = DateTime.UtcNow.Add(SessionTTL);

        return sessionId;
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

    private static void ThrowIfDexcomError(string responseBody)
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
