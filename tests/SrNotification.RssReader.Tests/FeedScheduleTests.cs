using SrNotification.RssReader.Feeds;

namespace SrNotification.RssReader.Tests;

public class FeedScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly RssReaderOptions Options = new()
    {
        RefreshInterval = TimeSpan.FromMinutes(5),
        MaxBackoff = TimeSpan.FromHours(1),
    };

    [Fact]
    public void Never_checked_feed_is_due() =>
        Assert.True(FeedSchedule.IsDue(null, 0, Now, Options));

    [Fact]
    public void Recently_checked_feed_is_not_due() =>
        Assert.False(FeedSchedule.IsDue(Now.AddMinutes(-2), 0, Now, Options));

    [Fact]
    public void Feed_is_due_once_refresh_interval_elapsed() =>
        Assert.True(FeedSchedule.IsDue(Now.AddMinutes(-5), 0, Now, Options));

    [Fact]
    public void Small_timer_drift_does_not_skip_a_cycle() =>
        Assert.True(FeedSchedule.IsDue(Now.AddMinutes(-5).AddMilliseconds(300), 0, Now, Options));

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 60)]   // capped at MaxBackoff
    [InlineData(50, 60)]  // no overflow
    public void Failing_feeds_back_off_exponentially(int failures, int expectedMinutes) =>
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), FeedSchedule.GetWait(failures, Options));

    [Fact]
    public void Failing_feed_is_not_retried_before_its_backoff() =>
        Assert.False(FeedSchedule.IsDue(Now.AddMinutes(-8), 1, Now, Options));
}
