using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarketMakerEtl.Core.Models.Classification;
using MarketMakerEtl.Core.Models.Taxonomies;
using MarketMakerEtl.Core.Services;

const string GoldRoot = @"C:\mmgold\iphone-15";
const string KeyPath = @"C:\Users\RyanK\.keys\openai.txt";
const string Family = "iphone-15";
const double BenchmarkMean = 0.994;
const double AcceptanceFloor = 0.985;

var gatedQuestions = new[] { "model", "storage", "carrier", "condition" };
var scoredQuestions = new[] { "item_type", "model", "storage", "carrier", "condition" };

var apiKey = ResolveApiKey();

var taxonomyJson = AugmentTaxonomy(File.ReadAllText(Path.Combine(GoldRoot, "taxonomy.json")), gatedQuestions);
var taxonomy = TaxonomyDocumentParser.Parse(taxonomyJson);
var questions = BuildQuestions(taxonomy);

var listings = LoadListings(GoldRoot);
var gold = LoadGold(GoldRoot);

Console.WriteLine($"Loaded {listings.Count} listings, {gold.Count} gold labels from {GoldRoot}");

var taxonomyOnly = await RunPass(questions, listings, gold, guidance: null, apiKey, scoredQuestions);
PrintReport("Taxonomy-only", taxonomyOnly);

if (taxonomyOnly.Mean < BenchmarkMean - 0.01)
{
    Console.WriteLine("Taxonomy-only mean is more than 1 point below the 99.4% benchmark; retrying with guidance.");
    var guidance = ExtractConventions(File.ReadAllText(Path.Combine(GoldRoot, "guide.md")));
    var guided = await RunPass(questions, listings, gold, guidance, apiKey, scoredQuestions);
    PrintReport("Taxonomy + guidance", guided);
    Console.WriteLine(guided.Mean >= AcceptanceFloor ? "RESULT: PASS (with guidance)" : "RESULT: FAIL (with guidance)");
}
else
{
    Console.WriteLine(taxonomyOnly.Mean >= AcceptanceFloor ? "RESULT: PASS (taxonomy-only)" : "RESULT: FAIL (taxonomy-only)");
}

static string ResolveApiKey()
{
    var fromEnv = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    var key = string.IsNullOrWhiteSpace(fromEnv) ? File.ReadAllText(KeyPath).Trim() : fromEnv.Trim();
    Environment.SetEnvironmentVariable("OPENAI_API_KEY", key);
    return key;
}

static string AugmentTaxonomy(string json, IReadOnlyCollection<string> gatedQuestions)
{
    var root = JsonNode.Parse(json)!.AsObject();
    var questionsNode = root["questions"]!.AsObject();
    foreach (var key in gatedQuestions)
    {
        var question = questionsNode[key]!.AsObject();
        question["askWhen"] = new JsonArray(new JsonObject
        {
            ["question"] = "item_type",
            ["anyOf"] = new JsonArray(JsonValue.Create("iphone_15_family"))
        });
    }

    return root.ToJsonString();
}

static Dictionary<string, ClassifyQuestion> BuildQuestions(TaxonomyDocument taxonomy) =>
    taxonomy.Questions.ToDictionary(question => question.Key, BuildQuestion);

static ClassifyQuestion BuildQuestion(TaxonomyQuestion question)
{
    if (question.AskWhen.Count == 0)
    {
        return new ClassifyQuestion("choice", question.Instructions, question.Criteria);
    }

    var criteria = new Dictionary<string, string>(question.Criteria)
    {
        ["not_applicable"] = "This question does not apply to this listing given its other answers."
    };
    return new ClassifyQuestion("choice", question.Instructions, criteria);
}

static List<ClassifyListingState> LoadListings(string root)
{
    var listings = new List<ClassifyListingState>();
    for (var i = 0; i < 3; i++)
    {
        var path = Path.Combine(root, $"test_batch_{i}.json");
        var batch = JsonSerializer.Deserialize<List<ClassifyListingState>>(File.ReadAllText(path))!;
        listings.AddRange(batch);
    }

    return listings;
}

static Dictionary<string, Dictionary<string, string>> LoadGold(string root)
{
    var gold = new Dictionary<string, Dictionary<string, string>>();
    for (var i = 0; i < 3; i++)
    {
        var path = Path.Combine(root, $"test_labels_{i}.json");
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(File.ReadAllText(path))!;
        foreach (var row in rows)
        {
            gold[row["id"]] = row;
        }
    }

    return gold;
}

static string ExtractConventions(string guide)
{
    const string StartMarker = "## Conventions";
    const string EndMarker = "\n## Output";
    var start = guide.IndexOf(StartMarker, StringComparison.Ordinal);
    if (start < 0)
    {
        return guide;
    }

    var end = guide.IndexOf(EndMarker, start, StringComparison.Ordinal);
    return end < 0 ? guide[start..] : guide[start..end];
}

static async Task<EvalReport> RunPass(
    IReadOnlyDictionary<string, ClassifyQuestion> questions,
    IReadOnlyList<ClassifyListingState> listings,
    IReadOnlyDictionary<string, Dictionary<string, string>> gold,
    string? guidance,
    string apiKey,
    IReadOnlyList<string> scoredQuestions)
{
    var usage = new UsageCapturingHandler(new HttpClientHandler());
    using var http = new HttpClient(usage) { Timeout = Timeout.InfiniteTimeSpan };
    var options = new OpenAiOptions(apiKey, "gpt-6-luna", "low", 25, 6, 120);
    var reviewOptions = new ClassificationReviewOptions(0.9);
    var client = new OpenAiListingClassifierClient(http, options, reviewOptions, TimeProvider.System);
    var request = new ClassifyRequest(Family, questions, listings, guidance);

    var started = DateTime.UtcNow;
    var response = await client.Classify(request, CancellationToken.None);
    var elapsed = DateTime.UtcNow - started;

    var predicted = new Dictionary<string, ClassifyResult>(StringComparer.Ordinal);
    for (var i = 0; i < listings.Count; i++)
    {
        predicted[listings[i].Id] = response.Results[i];
    }

    var perQuestion = scoredQuestions.ToDictionary(q => q, q => ScoreQuestion(q, gold, predicted));
    var errorCount = predicted.Values.Count(r => r.Error is not null);
    var cost = usage.PromptTokens / 1_000_000.0 * 0.10 + usage.CompletionTokens / 1_000_000.0 * 0.50;

    return new EvalReport(
        perQuestion, perQuestion.Values.Average(), errorCount, usage.PromptTokens, usage.CompletionTokens, cost, elapsed);
}

static double ScoreQuestion(
    string question,
    IReadOnlyDictionary<string, Dictionary<string, string>> gold,
    IReadOnlyDictionary<string, ClassifyResult> predicted)
{
    var rows = gold.Values
        .Where(row => question == "item_type" || row["item_type"] == "iphone_15_family")
        .ToList();
    if (rows.Count == 0)
    {
        return 1.0;
    }

    var correct = rows.Count(row =>
        predicted.TryGetValue(row["id"], out var result) &&
        result.Answers.TryGetValue(question, out var answer) &&
        answer.Choice == row[question]);
    return (double)correct / rows.Count;
}

static void PrintReport(string label, EvalReport report)
{
    Console.WriteLine($"== {label} ==");
    foreach (var (question, accuracy) in report.PerQuestion)
    {
        Console.WriteLine($"  {question,-10} {accuracy:P1}");
    }

    Console.WriteLine($"  mean       {report.Mean:P1}");
    Console.WriteLine($"  errors     {report.ErrorCount}");
    Console.WriteLine($"  tokens     in={report.InputTokens} out={report.OutputTokens}");
    Console.WriteLine($"  cost       ${report.Cost:F4}");
    Console.WriteLine($"  elapsed    {report.Elapsed.TotalSeconds:F1}s");
}

internal sealed record EvalReport(
    Dictionary<string, double> PerQuestion,
    double Mean,
    int ErrorCount,
    long InputTokens,
    long OutputTokens,
    double Cost,
    TimeSpan Elapsed);

internal sealed class UsageCapturingHandler : DelegatingHandler
{
    private long _promptTokens;
    private long _completionTokens;

    public UsageCapturingHandler(HttpMessageHandler inner)
        : base(inner)
    {
    }

    public long PromptTokens => Interlocked.Read(ref _promptTokens);

    public long CompletionTokens => Interlocked.Read(ref _completionTokens);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        CaptureUsage(body);
        response.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return response;
    }

    private void CaptureUsage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("usage", out var usage))
            {
                return;
            }

            Interlocked.Add(ref _promptTokens, usage.GetProperty("prompt_tokens").GetInt64());
            Interlocked.Add(ref _completionTokens, usage.GetProperty("completion_tokens").GetInt64());
        }
        catch (JsonException)
        {
        }
    }
}
