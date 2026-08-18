using GlucoseTray.Read.Dexcom;
using GlucoseTray.Read.Nightscout;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Text.Json;

namespace GlucoseTray.Tests.DSL.Read;

internal class ReadBehaviorDriver(ReadProvider provider, DexcomResult dexcomResult, NightScoutResult nightscoutResult)
{
    // Dexcom's login endpoint returns a JSON-encoded GUID string; DexcomReadStrategy now
    // requires >=32 chars to accept it as a valid session ID, so the mock must match that shape.
    private const string SessionId = "11111111-1111-1111-1111-111111111111";

    public ReadBehaviorDriver GettingLatestDexcomReading()
    {
        provider.ExternalCommunicationAdapter.PostApiResponseAsync(Arg.Any<string>(), Arg.Is<string>(x => x.Contains("bob"))).Returns($"\"{SessionId}\"");
        var data = JsonSerializer.Serialize(new List<DexcomResult> { dexcomResult });
        provider.ExternalCommunicationAdapter.PostApiResponseAsync(Arg.Any<string>(), Arg.Is<string>(x => x.Contains(SessionId))).Returns(data);
        provider.Runner.Process().Wait();
        return this;
    }

    public ReadBehaviorDriver GettingLatestNightScoutReading()
    {
        var data = JsonSerializer.Serialize(new List<NightScoutResult> { nightscoutResult });
        provider.ExternalCommunicationAdapter.GetApiResponseAsync(Arg.Any<string>()).Returns(data);
        provider.Runner.Process().Wait();
        return this;
    }

    public ReadBehaviorDriver GettingLatestDexcomReadingWithNoNewData()
    {
        provider.ExternalCommunicationAdapter.PostApiResponseAsync(Arg.Any<string>(), Arg.Is<string>(x => x.Contains("bob"))).Returns($"\"{SessionId}\"");
        var emptyData = JsonSerializer.Serialize(new List<DexcomResult>());
        provider.ExternalCommunicationAdapter.PostApiResponseAsync(Arg.Any<string>(), Arg.Is<string>(x => x.Contains(SessionId))).Returns(emptyData);
        provider.Runner.Process().Wait();
        return this;
    }

    public ReadBehaviorDriver GettingLatestDexcomReadingWithInvalidSessionId()
    {
        provider.ExternalCommunicationAdapter.PostApiResponseAsync(Arg.Any<string>(), Arg.Is<string>(x => x.Contains("bob"))).Returns("\"tooshort\"");
        provider.Runner.Process().Wait();
        return this;
    }

    public ReadBehaviorDriver CommunicationErrorOccurs()
    {
        provider.ExternalCommunicationAdapter.GetApiResponseAsync(Arg.Any<string>()).ThrowsAsync(x => throw new Exception());
        provider.ExternalCommunicationAdapter.PostApiResponseAsync(Arg.Any<string>()).ThrowsAsync(x => throw new Exception());
        provider.Runner.Process().Wait();
        return this;
    }

    public ReadAssertionDriver Then => new(provider, this);
}
