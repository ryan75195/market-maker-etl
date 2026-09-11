using FluentAssertions;
using MarketMakerEtl.Core.Models.Runs;

namespace MarketMakerEtl.Tests.Architecture;

[TestFixture]
public class RunStatusLiteralPlacementTests
{
    private const string RunStatusTypeFileName = "ScrapeRunStatus.cs";

    [Test]
    public void Should_keep_run_status_names_out_of_string_literals_outside_the_status_type()
    {
        var solutionRoot = FindSolutionRoot();
        var statusNames = Enum.GetNames<ScrapeRunStatus>();
        var violations = new List<string>();

        foreach (var file in SourceFiles(Path.Combine(solutionRoot, "src")))
        {
            if (Path.GetFileName(file) == RunStatusTypeFileName)
            {
                continue;
            }

            var content = File.ReadAllText(file);
            foreach (var statusName in statusNames)
            {
                if (content.Contains($"\"{statusName}\"", StringComparison.Ordinal))
                {
                    var relativePath = Path.GetRelativePath(solutionRoot, file);
                    violations.Add($"  {relativePath}: \"{statusName}\"");
                }
            }
        }

        violations.Should().BeEmpty(
            $"Run status names must flow through {nameof(ScrapeRunStatus)} instead of string literals. " +
            $"Violations ({violations.Count}):\n{string.Join("\n", violations)}");
    }

    private static IEnumerable<string> SourceFiles(string srcRoot) =>
        Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(file => !file.Contains(
                Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static string FindSolutionRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (Directory.GetFiles(directory, "*.slnx").Length > 0)
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("Could not find solution root (no .slnx file found)");
    }
}
