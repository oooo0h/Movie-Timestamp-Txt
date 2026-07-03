using MovieTimestampNotes.Core;

namespace MovieTimestampNotes.Tests;

public sealed class TimelineClockTests
{
    [Fact]
    public void StartPauseResume_UsesMonotonicElapsedTime()
    {
        var time = new ManualTimeSource();
        var clock = new TimelineClock(time, TimeSpan.FromMinutes(10));

        clock.Start();
        time.Advance(TimeSpan.FromSeconds(12));
        Assert.Equal(TimeSpan.FromSeconds(12), clock.Current);

        clock.Pause();
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(12), clock.Current);

        clock.Start();
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(15), clock.Current);
    }

    [Fact]
    public void ReachingDuration_ClampsAndStops()
    {
        var time = new ManualTimeSource();
        var clock = new TimelineClock(time, TimeSpan.FromSeconds(5));

        clock.Start();
        time.Advance(TimeSpan.FromSeconds(8));

        Assert.Equal(TimeSpan.FromSeconds(5), clock.Current);
        Assert.False(clock.IsRunning);
    }

    [Fact]
    public void Calibrate_RejectsOutOfRangePosition()
    {
        var clock = new TimelineClock(new ManualTimeSource(), TimeSpan.FromMinutes(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Calibrate(TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Calibrate(TimeSpan.FromMinutes(2)));
    }

    private sealed class ManualTimeSource : IMonotonicTimeSource
    {
        public TimeSpan Elapsed { get; private set; }
        public void Advance(TimeSpan amount) => Elapsed += amount;
    }
}
