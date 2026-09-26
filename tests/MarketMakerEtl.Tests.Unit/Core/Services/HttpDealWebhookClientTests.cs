using System.Net;
using System.Text;
using System.Text.Json;
using MarketMakerEtl.Core.Models.Deals;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class HttpDealWebhookClientTests
{
    private static DealWebhookPayload Payload => new(
        "Test listing",
        "https://example.test/listing/1",
        80m,
        new Dictionary<string, string> { ["model"] = "dualsense" },
        100m,
        0.20m);

    [Test]
    public async Task Should_post_the_payload_to_the_configured_webhook_url()
    {
        var handler = new StubWebhookHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler, "https://example.test/webhook");

        await client.Notify(Payload, CancellationToken.None);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestUri, Is.EqualTo("https://example.test/webhook"));
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo("Test listing"));
            Assert.That(root.GetProperty("landedPrice").GetDecimal(), Is.EqualTo(80m));
            Assert.That(root.GetProperty("discount").GetDecimal(), Is.EqualTo(0.20m));
        });
    }

    [Test]
    public void Should_do_nothing_when_no_webhook_url_is_configured()
    {
        var handler = new StubWebhookHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler, webhookUrl: null);

        Assert.That(async () => await client.Notify(Payload, CancellationToken.None), Throws.Nothing);
        Assert.That(handler.RequestUri, Is.Null);
    }

    [Test]
    public void Should_not_throw_when_the_webhook_endpoint_returns_a_server_error()
    {
        var handler = new StubWebhookHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain")
        });
        var client = CreateClient(handler, "https://example.test/webhook");

        Assert.That(async () => await client.Notify(Payload, CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public void Should_not_throw_when_the_webhook_endpoint_never_responds()
    {
        var handler = new StubWebhookHandler(neverResponds: true);
        var client = CreateClient(handler, "https://example.test/webhook");

        Assert.That(async () => await client.Notify(Payload, CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public async Task Should_never_log_the_full_webhook_url_on_a_server_error()
    {
        const string secretUrl = "https://discord.com/api/webhooks/12345/super-secret-token";
        var handler = new StubWebhookHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain")
        });
        var logger = new CapturingLogger();
        var client = new HttpDealWebhookClient(new HttpClient(handler), new DealsOptions(10, secretUrl), logger);

        await client.Notify(Payload, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(logger.Messages, Is.Not.Empty);
            Assert.That(logger.Messages, Has.None.Contains(secretUrl));
            Assert.That(logger.Messages, Has.None.Contains("super-secret-token"));
            Assert.That(logger.Messages, Has.Some.Contains("discord.com"));
        });
    }

    [Test]
    public async Task Should_never_log_the_full_webhook_url_on_a_timeout()
    {
        const string secretUrl = "https://discord.com/api/webhooks/12345/super-secret-token";
        var handler = new StubWebhookHandler(neverResponds: true);
        var logger = new CapturingLogger();
        var client = new HttpDealWebhookClient(new HttpClient(handler), new DealsOptions(10, secretUrl), logger);

        await client.Notify(Payload, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(logger.Messages, Is.Not.Empty);
            Assert.That(logger.Messages, Has.None.Contains(secretUrl));
            Assert.That(logger.Messages, Has.None.Contains("super-secret-token"));
            Assert.That(logger.Messages, Has.Some.Contains("discord.com"));
        });
    }

    private static HttpDealWebhookClient CreateClient(HttpMessageHandler handler, string? webhookUrl) =>
        new(new HttpClient(handler), new DealsOptions(10, webhookUrl), NullLogger<HttpDealWebhookClient>.Instance);

    private sealed class CapturingLogger : ILogger<HttpDealWebhookClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class StubWebhookHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _respond;
        private readonly bool _neverResponds;

        public StubWebhookHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public StubWebhookHandler(bool neverResponds)
        {
            _neverResponds = neverResponds;
        }

        public string? RequestBody { get; private set; }

        public string? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_neverResponds)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return _respond!(request);
        }
    }
}
