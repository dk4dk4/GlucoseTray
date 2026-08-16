using System.Text;
using System.Text.Json;

namespace GlucoseTray.Read.Nightscout;

internal class NightscoutReadStrategy(AppSettings settings, IExternalCommunicationAdapter communicator, IGlucoseReadingMapper mapper) : IReadStrategy
{
    public async Task<GlucoseReading> GetLatestGlucoseAsync()
    {
        var response = await GetApiResponseAsync();
        try
        {
            var data = JsonSerializer.Deserialize<List<NightScoutResult>>(response)!.Last();
            var result = mapper.Map(data);
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse Nightscout response: {response}", ex);
        }
    }

    private async Task<string> GetApiResponseAsync()
    {
        var baseUrl = settings.NightscoutUrl.TrimEnd('/');
        var uriBuilder = new UriBuilder(baseUrl)
        {
            Path = "/api/v1/entries/sgv",
            Query = "count=1"
        };

        var url = uriBuilder.Uri.ToString();
        var tokenHeader = !string.IsNullOrWhiteSpace(settings.NightscoutToken)
            ? settings.NightscoutToken
            : null;

        var result = await communicator.GetApiResponseAsync(url, content: null, authHeader: tokenHeader);
        return result;
    }
}
