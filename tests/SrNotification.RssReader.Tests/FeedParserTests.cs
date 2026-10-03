using SrNotification.Infrastructure.Feeds;

namespace SrNotification.RssReader.Tests;

public class FeedParserTests
{
    private static ParsedFeed ParseFile(string name)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", name));
        return FeedParser.Parse(stream);
    }

    [Fact]
    public void Rss20_reads_feed_metadata()
    {
        var feed = ParseFile("rss20.xml");

        Assert.Equal("Example News", feed.Title);
        Assert.Equal("https://example.com/", feed.SiteUrl);
    }

    [Fact]
    public void Rss20_removes_duplicate_items_within_the_feed()
    {
        var feed = ParseFile("rss20.xml");

        Assert.Equal(4, feed.Items.Count);
        Assert.Single(feed.Items, i => i.ExternalId == "article-1");
    }

    [Fact]
    public void Rss20_uses_guid_then_link_then_hash_as_external_id()
    {
        var feed = ParseFile("rss20.xml");

        Assert.Equal("article-1", feed.Items[0].ExternalId);
        Assert.Equal("https://example.com/articles/2", feed.Items[1].ExternalId);
        Assert.StartsWith("sha256:", feed.Items[2].ExternalId);
    }

    [Fact]
    public void Rss20_reads_item_fields()
    {
        var item = ParseFile("rss20.xml").Items[0];

        Assert.Equal("First article", item.Title);
        Assert.Equal("https://example.com/articles/1", item.Link);
        Assert.Equal("Summary of the first article", item.Summary);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), item.PublishedAt);
    }

    [Fact]
    public void Dates_are_normalised_to_utc()
    {
        var feed = ParseFile("rss20.xml");

        // +0200 offset without a colon
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 30, 0, TimeSpan.Zero), feed.Items[1].PublishedAt);
        Assert.Equal(TimeSpan.Zero, feed.Items[1].PublishedAt!.Value.Offset);

        // Named zone (EST = UTC-5)
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 13, 0, 0, TimeSpan.Zero), feed.Items[3].PublishedAt);
    }

    [Fact]
    public void Unparseable_date_keeps_the_item_without_a_date()
    {
        var item = ParseFile("rss20.xml").Items[2];

        Assert.Equal("Third article, odd date and no link", item.Title);
        Assert.Null(item.PublishedAt);
        Assert.Null(item.Link);
    }

    [Fact]
    public void Atom_feed_is_supported()
    {
        var feed = ParseFile("atom.xml");

        Assert.Equal("Example Atom Feed", feed.Title);
        Assert.Equal("https://example.org/", feed.SiteUrl);

        var entry = Assert.Single(feed.Items);
        Assert.Equal("urn:uuid:1225c695-cfb8-4ebb-aaaa-80da344efa6a", entry.ExternalId);
        Assert.Equal("https://example.org/2026/10/02/atom-entry", entry.Link);
        Assert.Equal("John Doe", entry.Author);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero), entry.PublishedAt);
    }

    [Fact]
    public void Non_feed_xml_is_rejected()
    {
        using var stream = new MemoryStream("<html><body>Not a feed</body></html>"u8.ToArray());

        Assert.Throws<FormatException>(() => FeedParser.Parse(stream));
    }

    [Fact]
    public void Hash_external_id_is_stable()
    {
        var a = FeedParser.BuildExternalId(null, null, "Title", "Summary");
        var b = FeedParser.BuildExternalId(" ", "", "Title", "Summary");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Overlong_external_id_is_hashed_to_fit()
    {
        var id = FeedParser.BuildExternalId(new string('x', 5000), null, null, null);

        Assert.StartsWith("sha256:", id);
        Assert.True(id.Length <= SrNotification.Data.FieldLengths.ExternalId);
    }
}
