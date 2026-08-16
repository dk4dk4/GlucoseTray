
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace GlucoseTray.Read;

public interface IExternalCommunicationAdapter
{
    Task<string> PostApiResponseAsync(string url, string? content = null);
    Task<string> GetApiResponseAsync(string url, string? content = null, string? authHeader = null);
}

public class ExternalCommunicationAdapter(IHttpClientFactory httpClientFactory) : IExternalCommunicationAdapter
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private const string UserAgent = "GlucoseTray/1.0";

    public async Task<string> PostApiResponseAsync(string url, string? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("User-Agent", UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (content is not null)
        {
            var requestContent = new StringContent(content, Encoding.UTF8, "application/json");
            request.Content = requestContent;
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        var result = await DoApiResponseAsync(request);
        return result;
    }

    public async Task<string> GetApiResponseAsync(string url, string? content = null, string? authHeader = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            request.Headers.Add("Authorization", $"Bearer {authHeader}");
        }

        if (content is not null)
        {
            var requestContent = new StringContent(content, Encoding.UTF8, "application/json");
            request.Content = requestContent;
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        var result = await DoApiResponseAsync(request);
        return result;
    }

    private async Task<string> DoApiResponseAsync(HttpRequestMessage request)
    {
        HttpResponseMessage? response = null;
        try
        {
            var client = httpClientFactory.CreateClient();
            using var cts = new CancellationTokenSource(RequestTimeout);

            response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                throw new HttpRequestException(
                    $"API returned {response.StatusCode}: {errorContent}",
                    null,
                    response.StatusCode
                );
            }

            var result = await response.Content.ReadAsStringAsync(cts.Token);
            return result;
        }
        finally
        {
            request?.Dispose();
            response?.Dispose();
        }
    }
}