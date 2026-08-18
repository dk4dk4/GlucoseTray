using GlucoseTray.Enums;
using GlucoseTray.Read;
using NSubstitute;

namespace GlucoseTray.Tests.DSL.Read;

internal class ReadAssertionDriver(ReadProvider provider, ReadBehaviorDriver behaviorDriver)
{
    public ReadBehaviorDriver When => behaviorDriver;

    public ReadAssertionDriver ShouldHaveMgValueOf(int value)
    {
        provider.Tray.Received().Refresh(Arg.Is<GlucoseReading>(x => x.MgValue == value));
        provider.Tray.ClearReceivedCalls();
        return this;
    }

    public ReadAssertionDriver ShouldHaveMmolValueOf(float value)
    {
        provider.Tray.Received().Refresh(Arg.Is<GlucoseReading>(x => x.MmolValue == value));
        provider.Tray.ClearReceivedCalls();
        return this;
    }

    public ReadAssertionDriver ShouldHaveUnknownTrend()
    {
        provider.Tray.Received().Refresh(Arg.Is<GlucoseReading>(x => x.Trend == Trend.Unknown));
        provider.Tray.ClearReceivedCalls();
        return this;
    }

    public ReadAssertionDriver ShouldHaveMarkedFetchAsFailed()
    {
        Assert.That(provider.Reader.LastFetchFailed, Is.True);
        return this;
    }

    public ReadAssertionDriver ShouldNotHaveMarkedFetchAsFailed()
    {
        Assert.That(provider.Reader.LastFetchFailed, Is.False);
        return this;
    }
}
