using GlucoseTray.Enums;
using System.Text.Json;

namespace GlucoseTray.Read.Dexcom;

internal class DexcomReadStrategy(AppSettings settings, IExternalCommunicationAdapter communicator, IGlucoseReadingMapper mapper) : IReadStrategy
{
    private string? _cachedSessionId;
    private DateTime _sessionExpiry = DateTime.MinValue;
    private readonly TimeSpan SessionTTL = TimeSpan.FromHours(1);

    public async Task<GlucoseReading> GetLatestGlucoseAsync()
    {
        string sessionId = await GetSessionIdAsync();
        string response = await GetApiResponseAsync(sessionId);

        var data = JsonSerializer.Deserialize<List<DexcomResult>>(response)!.First();

        var result = mapper.Map(data);
        return result;
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

        string accountId = await GetAccountIdAsync();
        var sessionIdRequestJson = JsonSerializer.Serialize(new
        {
            accountId,
            applicationId = "d8665ade-9673-4e27-9ff6-92db4ce13d13",
            password = settings.DexcomPassword
        });

        var sessionUrl = $"https://{GetDexComServer()}/ShareWebServices/Services/General/LoginPublisherAccountById";
        var result = await communicator.PostApiResponseAsync(sessionUrl, sessionIdRequestJson);
        var sessionId = DeserializeStringResponse(result);

        _cachedSessionId = sessionId;
        _sessionExpiry = DateTime.UtcNow.Add(SessionTTL);

        return sessionId;
    }

    private async Task<string> GetAccountIdAsync()
    {
        var accountIdRequestJson = JsonSerializer.Serialize(new
        {
            accountName = settings.DexcomUsername,
            applicationId = "d8665ade-9673-4e27-9ff6-92db4ce13d13",
            password = settings.DexcomPassword
        });

        var accountUrl = $"https://{GetDexComServer()}/ShareWebServices/Services/General/AuthenticatePublisherAccount";

        var result = await communicator.PostApiResponseAsync(accountUrl, accountIdRequestJson);
        var accountId = DeserializeStringResponse(result);

        return accountId;
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

    public string GetDexComServer() => settings.DexcomServer switch
    {
        DexcomServer.DexcomShare1 => "share1.dexcom.com",
        DexcomServer.DexcomShare2 => "share2.dexcom.com",
        DexcomServer.DexcomInternational => "shareous1.dexcom.com",
        _ => "share1.dexcom.com",
    };
}
