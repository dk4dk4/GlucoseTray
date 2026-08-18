namespace GlucoseTray.Tests;

public class AppRunnerSchedulingTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void ShouldPollAtFullCadencePlusBufferWhenNoPriorReading()
    {
        var delay = AppRunner.ComputeNextDelay(lastReadingTimestampUtc: null, consecutiveFailures: 0, nowUtc: Now);
        Assert.That(delay, Is.EqualTo(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(45)));
    }

    [Test]
    public void ShouldSleepUntilNextReadingIsDuePlusBuffer()
    {
        var lastReading = Now.AddMinutes(-2);
        var delay = AppRunner.ComputeNextDelay(lastReading, consecutiveFailures: 0, nowUtc: Now);
        Assert.That(delay, Is.EqualTo(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(45)));
    }

    [Test]
    public void ShouldFastPollWhenReadingIsJustLate()
    {
        var lastReading = Now.AddMinutes(-6);
        var delay = AppRunner.ComputeNextDelay(lastReading, consecutiveFailures: 0, nowUtc: Now);
        Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void ShouldFallBackToFlatCadenceDuringSensorGap()
    {
        var lastReading = Now.AddMinutes(-25);
        var delay = AppRunner.ComputeNextDelay(lastReading, consecutiveFailures: 0, nowUtc: Now);
        Assert.That(delay, Is.EqualTo(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(45)));
    }

    [Test]
    public void ShouldBackOffOnConsecutiveFailuresRegardlessOfReadingAge()
    {
        var lastReading = Now.AddMinutes(-2); // would otherwise sleep ~3min45s
        var delay = AppRunner.ComputeNextDelay(lastReading, consecutiveFailures: 1, nowUtc: Now);
        Assert.That(delay, Is.LessThan(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public void ShouldCapBackoffDelayAtMaximum()
    {
        // Jitter (+/-20%) is applied after the cap, so allow up to 1.2x the 5-minute ceiling.
        var delay = AppRunner.ComputeNextDelay(null, consecutiveFailures: 20, nowUtc: Now);
        Assert.That(delay, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(6)));
    }

    [Test]
    public void ShouldIncreaseBackoffDelayWithMoreFailures()
    {
        var firstFailure = AppRunner.ComputeNextDelay(null, consecutiveFailures: 1, nowUtc: Now);
        var thirdFailure = AppRunner.ComputeNextDelay(null, consecutiveFailures: 3, nowUtc: Now);
        Assert.That(thirdFailure, Is.GreaterThan(firstFailure));
    }
}
