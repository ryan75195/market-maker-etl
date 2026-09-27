using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MarketMakerEtl.Core.Interfaces;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class OpenAiChatCompletionSenderTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static OpenAiOptions Options(int timeoutSeconds = 30) =>
        new("test-key", "gpt-6-luna", "low", 25, 6, timeoutSeconds);

    private static OpenAiPricingOptions Pricing() =>
        new(
            new Dictionary<string, OpenAiModelPricing>(StringComparer.OrdinalIgnoreCase)
            {
                ["gpt-6-luna"] = new OpenAiModelPricing(0.10m, 0.50m)
            },
            new OpenAiModelPricing(2.0m, 10.0m));

    private static IOpenAiBudgetService NotExhaustedBudget()
    {
        var budget = Substitute.For<IOpenAiBudgetService>();
        budget.IsExhausted(Arg.Any<CancellationToken>()).Returns(false);
        budget.GetMonthToDateSpend(Arg.Any<CancellationToken>()).Returns(0m);
        budget.MonthlyBudgetUsd.Returns(20m);
        return budget;
    }

    private static OpenAiChatCompletionSender CreateSender(
        TimeProvider? timeProvider = null,
        IOpenAiBudgetService? budget = null,
        IOpenAiUsageStore? usage = null,
        OpenAiPricingOptions? pricing = null) =>
        new(
            timeProvider ?? TimeProvider.System,
            budget ?? NotExhaustedBudget(),
            usage ?? Substitute.For<IOpenAiUsageStore>(),
            pricing ?? Pricing());

    [Test]
    public async Task Should_retry_a_transport_level_network_error_and_then_succeed()
    {
        var attempt = 0;
        var handler = new StubHandler(() =>
        {
            attempt++;
            return attempt == 1
                ? throw new HttpRequestException("connection reset")
                : SuccessResponse();
        });

        var result = await CreateSender().Send(
            new HttpClient(handler), Options(), OpenAiUsagePurpose.Classification, new JsonObject(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(attempt, Is.EqualTo(2));
            Assert.That(result.Content, Is.EqualTo("hello"));
        });
    }

    [Test]
    public async Task Should_capture_prompt_and_completion_token_usage_from_the_envelope()
    {
        var handler = new StubHandler(SuccessResponse);

        var result = await CreateSender().Send(
            new HttpClient(handler), Options(), OpenAiUsagePurpose.Classification, new JsonObject(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.PromptTokens, Is.EqualTo(12));
            Assert.That(result.CompletionTokens, Is.EqualTo(34));
        });
    }

    [Test]
    public async Task Should_default_token_usage_to_zero_when_the_envelope_omits_it()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"content":"hello"}}]}""", Encoding.UTF8, "application/json")
        });

        var result = await CreateSender().Send(
            new HttpClient(handler), Options(), OpenAiUsagePurpose.Classification, new JsonObject(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.PromptTokens, Is.EqualTo(0));
            Assert.That(result.CompletionTokens, Is.EqualTo(0));
        });
    }

    [Test]
    public void Should_convert_persistent_network_errors_into_a_listing_classifier_exception()
    {
        var handler = new StubHandler(() => throw new HttpRequestException("connection reset"));

        var exception = Assert.ThrowsAsync<ListingClassifierException>(() => CreateSender().Send(
            new HttpClient(handler), Options(), OpenAiUsagePurpose.Classification, new JsonObject(), CancellationToken.None));

        Assert.That(exception!.IsTimeout, Is.False);
    }

    [Test]
    public async Task Should_convert_a_deadline_expiry_during_the_retry_delay_into_a_timeout()
    {
        var handler = new StubHandler(RateLimitedResponse);
        var fakeTime = new FakeTimeProvider(StartTime);

        var exception = await CatchListingClassifierException(CreateSender(timeProvider: fakeTime).Send(
            new HttpClient(handler), Options(timeoutSeconds: 0), OpenAiUsagePurpose.Classification, new JsonObject(), CancellationToken.None));

        Assert.That(exception.IsTimeout, Is.True);
    }

    [Test]
    public async Task Should_convert_a_deadline_expiry_while_reading_the_response_body_into_a_timeout()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new HangingStream())
        });

        var exception = await CatchListingClassifierException(CreateSender().Send(
            new HttpClient(handler), Options(timeoutSeconds: 0), OpenAiUsagePurpose.Classification, new JsonObject(), CancellationToken.None));

        Assert.That(exception.IsTimeout, Is.True);
    }

    [Test]
    public void Should_propagate_a_genuine_caller_cancellation_instead_of_converting_it_to_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new StubHandler(SuccessResponse);

        Assert.CatchAsync<OperationCanceledException>(() => CreateSender().Send(
            new HttpClient(handler), Options(), OpenAiUsagePurpose.Classification, new JsonObject(), cts.Token));
    }

    [Test]
    public void Should_throw_a_budget_exceeded_exception_without_calling_the_http_client_when_the_budget_is_exhausted()
    {
        var called = false;
        var handler = new StubHandler(() =>
        {
            called = true;
            return SuccessResponse();
        });

        var budget = Substitute.For<IOpenAiBudgetService>();
        budget.IsExhausted(Arg.Any<CancellationToken>()).Returns(true);
        budget.GetMonthToDateSpend(Arg.Any<CancellationToken>()).Returns(20m);
        budget.MonthlyBudgetUsd.Returns(20m);

        Assert.ThrowsAsync<OpenAiBudgetExceededException>(() => CreateSender(budget: budget).Send(
            new HttpClient(handler), Options(), OpenAiUsagePurpose.Classification, new JsonObject(), CancellationToken.None));

        Assert.That(called, Is.False);
    }

    [Test]
    public async Task Should_record_usage_with_the_cost_computed_from_configured_pricing_before_parsing_content()
    {
        var handler = new StubHandler(SuccessResponse);
        var usage = Substitute.For<IOpenAiUsageStore>();

        await CreateSender(usage: usage).Send(
            new HttpClient(handler), Options(), OpenAiUsagePurpose.Classification, new JsonObject(), CancellationToken.None);

        await usage.Received(1).Record(
            "gpt-6-luna", OpenAiUsagePurpose.Classification, 12, 34, 0.0000182m, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Should_use_the_fallback_price_when_the_model_has_no_configured_pricing()
    {
        var handler = new StubHandler(SuccessResponse);
        var usage = Substitute.For<IOpenAiUsageStore>();
        var options = Options() with { Model = "gpt-unknown-model" };

        await CreateSender(usage: usage).Send(
            new HttpClient(handler), options, OpenAiUsagePurpose.Classification, new JsonObject(), CancellationToken.None);

        await usage.Received(1).Record(
            "gpt-unknown-model", OpenAiUsagePurpose.Classification, 12, 34, 0.000364m, Arg.Any<CancellationToken>());
    }

    private static async Task<ListingClassifierException> CatchListingClassifierException(
        Task<OpenAiCompletionResult> send)
    {
        try
        {
            await send;
        }
        catch (ListingClassifierException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Expected a ListingClassifierException.");
    }

    private static HttpResponseMessage SuccessResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            """
            {"choices":[{"message":{"content":"hello"}}],"usage":{"prompt_tokens":12,"completion_tokens":34}}
            """,
            Encoding.UTF8,
            "application/json")
    };

    private static HttpResponseMessage RateLimitedResponse() => new((HttpStatusCode)429)
    {
        Content = new StringContent("{\"error\":\"rate limited\"}", Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public StubHandler(Func<HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_respond());
        }
    }

    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }
    }
}
