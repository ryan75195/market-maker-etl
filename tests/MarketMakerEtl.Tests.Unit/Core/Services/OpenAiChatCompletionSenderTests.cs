using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Services;
using Microsoft.Extensions.Time.Testing;

namespace MarketMakerEtl.Tests.Unit.Core.Services;

[TestFixture]
public class OpenAiChatCompletionSenderTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static OpenAiOptions Options(int timeoutSeconds = 30) =>
        new("test-key", "gpt-6-luna", "low", 25, 6, timeoutSeconds);

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

        var result = await OpenAiChatCompletionSender.Send(
            new HttpClient(handler), Options(), TimeProvider.System, new JsonObject(), CancellationToken.None);

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

        var result = await OpenAiChatCompletionSender.Send(
            new HttpClient(handler), Options(), TimeProvider.System, new JsonObject(), CancellationToken.None);

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

        var result = await OpenAiChatCompletionSender.Send(
            new HttpClient(handler), Options(), TimeProvider.System, new JsonObject(), CancellationToken.None);

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

        var exception = Assert.ThrowsAsync<ListingClassifierException>(() => OpenAiChatCompletionSender.Send(
            new HttpClient(handler), Options(), TimeProvider.System, new JsonObject(), CancellationToken.None));

        Assert.That(exception!.IsTimeout, Is.False);
    }

    [Test]
    public async Task Should_convert_a_deadline_expiry_during_the_retry_delay_into_a_timeout()
    {
        var handler = new StubHandler(RateLimitedResponse);
        var fakeTime = new FakeTimeProvider(StartTime);

        var exception = await CatchListingClassifierException(OpenAiChatCompletionSender.Send(
            new HttpClient(handler), Options(timeoutSeconds: 0), fakeTime, new JsonObject(), CancellationToken.None));

        Assert.That(exception.IsTimeout, Is.True);
    }

    [Test]
    public async Task Should_convert_a_deadline_expiry_while_reading_the_response_body_into_a_timeout()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new HangingStream())
        });

        var exception = await CatchListingClassifierException(OpenAiChatCompletionSender.Send(
            new HttpClient(handler), Options(timeoutSeconds: 0), TimeProvider.System, new JsonObject(), CancellationToken.None));

        Assert.That(exception.IsTimeout, Is.True);
    }

    [Test]
    public void Should_propagate_a_genuine_caller_cancellation_instead_of_converting_it_to_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new StubHandler(SuccessResponse);

        Assert.CatchAsync<OperationCanceledException>(() => OpenAiChatCompletionSender.Send(
            new HttpClient(handler), Options(), TimeProvider.System, new JsonObject(), cts.Token));
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
