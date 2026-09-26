using System.Net;
using System.Text;
using System.Text.Json;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class HttpListingClassifierClientTests
{
    private const string SuccessBody = """
        {
          "model": "ps5-controller",
          "members": 3,
          "results": [
            {
              "answers": {
                "item_type": {
                  "choice": "console",
                  "confidence": 0.9,
                  "agreement": 1.0,
                  "probabilities": { "console": 0.9, "other": 0.1 }
                }
              }
            }
          ]
        }
        """;

    private static ClassifierOptions Options(int timeoutSeconds = 30) =>
        new("http://classifier.test", 64, 5, 2000, timeoutSeconds);

    [Test]
    public async Task Should_post_the_expected_request_body_shape()
    {
        var handler = new StubClassifierHandler(_ => Json(SuccessBody));
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options());

        await client.Classify(BuildRequest(), CancellationToken.None);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        var state = root.GetProperty("states")[0];
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("ps5-controller"));
            Assert.That(root.GetProperty("questions").GetProperty("item_type").GetProperty("type").GetString(), Is.EqualTo("choice"));
            Assert.That(state.GetProperty("title").GetString(), Is.EqualTo("Test title"));
            Assert.That(state.TryGetProperty("mercari_category", out _), Is.False);
            Assert.That(state.TryGetProperty("brand", out _), Is.False);
            Assert.That(state.TryGetProperty("description", out _), Is.False);
        });
    }

    [Test]
    public async Task Should_pin_the_classifier_request_wire_format()
    {
        var handler = new StubClassifierHandler(_ => Json(SuccessBody));
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options());

        await client.Classify(BuildRequest(), CancellationToken.None);

        const string expectedBody =
            "{\"model\":\"ps5-controller\"," +
            "\"questions\":{\"item_type\":{\"type\":\"choice\",\"instructions\":\"What is this?\"," +
            "\"criteria\":{\"a\":\"A.\",\"b\":\"B.\"}}}," +
            "\"states\":[{\"title\":\"Test title\"}]}";
        Assert.That(handler.RequestBody, Is.EqualTo(expectedBody));
    }

    [Test]
    public async Task Should_parse_the_pinned_live_classifier_response()
    {
        var fixture = await File.ReadAllTextAsync(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "Classifier", "classify-response-ps5-controller.json"));
        var handler = new StubClassifierHandler(_ => Json(fixture));
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options());

        var response = await client.Classify(BuildRequest(), CancellationToken.None);

        var itemType = response.Results[0].Answers["item_type"];
        Assert.Multiple(() =>
        {
            Assert.That(response.Model, Is.EqualTo("ps5-controller"));
            Assert.That(response.Members, Is.EqualTo(3));
            Assert.That(itemType.Choice, Is.EqualTo("dualsense_edge"));
            Assert.That(itemType.Confidence, Is.EqualTo(1.0));
            Assert.That(itemType.Agreement, Is.EqualTo(1.0));
            Assert.That(itemType.Probabilities["dualsense_edge"], Is.EqualTo(1.0));
        });
    }

    [Test]
    public void Should_throw_a_typed_exception_for_a_non_success_status()
    {
        var handler = new StubClassifierHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"detail\":\"boom\"}", Encoding.UTF8, "application/json")
        });
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options());

        var exception = Assert.ThrowsAsync<ListingClassifierException>(async () =>
            await client.Classify(BuildRequest(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("500"));
            Assert.That(exception!.IsTimeout, Is.False);
        });
    }

    [Test]
    public void Should_throw_a_typed_timeout_exception_rather_than_operation_cancelled()
    {
        var handler = new StubClassifierHandler(neverResponds: true);
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options(timeoutSeconds: 1));

        var exception = Assert.ThrowsAsync<ListingClassifierException>(async () =>
            await client.Classify(BuildRequest(), CancellationToken.None));

        Assert.That(exception!.IsTimeout, Is.True);
    }

    [Test]
    public void Should_propagate_caller_cancellation()
    {
        var handler = new StubClassifierHandler(neverResponds: true);
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options(timeoutSeconds: 30));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await client.Classify(BuildRequest(), cts.Token));
    }

    [Test]
    public async Task Should_report_reachable_with_loaded_models_on_a_successful_health_check()
    {
        var handler = new StubClassifierHandler(_ => Json("""{"device":"cpu","models":{"ps5-controller":2}}"""));
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options());

        var health = await client.CheckHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Reachable, Is.True);
            Assert.That(health.BaseUrl, Is.EqualTo("http://classifier.test"));
            Assert.That(health.LoadedModels, Does.Contain("ps5-controller"));
        });
    }

    [Test]
    public async Task Should_report_unreachable_when_the_health_endpoint_returns_a_non_success_status()
    {
        var handler = new StubClassifierHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options());

        var health = await client.CheckHealth(CancellationToken.None);

        Assert.That(health.Reachable, Is.False);
    }

    [Test]
    public async Task Should_report_unreachable_when_the_health_check_times_out()
    {
        var handler = new StubClassifierHandler(neverResponds: true);
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options());

        var health = await client.CheckHealth(CancellationToken.None);

        Assert.That(health.Reachable, Is.False);
    }

    [Test]
    public async Task Should_report_unreachable_without_calling_out_when_the_base_url_is_empty()
    {
        var handler = new StubClassifierHandler(_ => throw new InvalidOperationException("should not be called"));
        var client = new HttpListingClassifierClient(new HttpClient(handler), Options() with { BaseUrl = "" });

        var health = await client.CheckHealth(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(health.Reachable, Is.False);
            Assert.That(health.LoadedModels, Is.Empty);
        });
    }

    private static ClassifyRequest BuildRequest() =>
        new(
            "ps5-controller",
            new Dictionary<string, ClassifyQuestion>
            {
                ["item_type"] = new("choice", "What is this?", new Dictionary<string, string> { ["a"] = "A.", ["b"] = "B." })
            },
            [new ClassifyListingState("Test title", null, null, null)]);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubClassifierHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _respond;
        private readonly bool _neverResponds;

        public StubClassifierHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public StubClassifierHandler(bool neverResponds)
        {
            _neverResponds = neverResponds;
        }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_neverResponds)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return _respond!(request);
        }
    }
}
