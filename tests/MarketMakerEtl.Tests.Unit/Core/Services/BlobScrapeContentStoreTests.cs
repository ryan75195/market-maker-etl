using MarketMakerEtl.Core.Models.Scraper;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class BlobScrapeContentStoreTests
{
    private static readonly BlobScrapeContentStore Store = new(
        new ScrapeContentOptions("UseDevelopmentStorage=true", "html"));

    [Test]
    public void Should_extract_the_blob_name_from_a_container_uri()
    {
        var name = Store.BuildBlobName(
            "http://127.0.0.1:10000/devstoreaccount1/html/job-1/https___www_ebay_co_uk_itm_1.html");

        Assert.That(name, Is.EqualTo("job-1/https___www_ebay_co_uk_itm_1.html"));
    }

    [Test]
    public void Should_reject_a_uri_outside_the_content_container()
    {
        Assert.That(
            () => Store.BuildBlobName("http://127.0.0.1:10000/devstoreaccount1/other/x.html"),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Should_fail_when_the_blob_uri_is_not_a_uri()
    {
        Assert.That(
            async () => await Store.GetHtml("not-a-uri", CancellationToken.None),
            Throws.TypeOf<UriFormatException>());
    }
}
