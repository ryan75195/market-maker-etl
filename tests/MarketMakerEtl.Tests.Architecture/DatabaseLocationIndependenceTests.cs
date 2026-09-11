using FluentAssertions;

namespace MarketMakerEtl.Tests.Architecture;

[TestFixture]
public class DatabaseLocationIndependenceTests
{
    private static readonly string[] DatabaseConfigurationTokens =
    [
        "UseSqlite",
        "Data Source",
        "DataSource",
        "marketmakeretl.db"
    ];

    private static readonly string[] WorkingDirectoryTokens =
    [
        "Directory.GetCurrentDirectory",
        "Environment.CurrentDirectory"
    ];

    [Test]
    public void Should_not_derive_the_database_location_from_the_process_working_directory()
    {
        var solutionRoot = FindSolutionRoot();
        var violations = new List<string>();

        foreach (var file in SourceFiles(Path.Combine(solutionRoot, "src")))
        {
            var content = File.ReadAllText(file);
            var configuresDatabase = DatabaseConfigurationTokens
                .Any(token => content.Contains(token, StringComparison.Ordinal));

            if (!configuresDatabase)
            {
                continue;
            }

            foreach (var workingDirectoryToken in WorkingDirectoryTokens)
            {
                if (content.Contains(workingDirectoryToken, StringComparison.Ordinal))
                {
                    var relativePath = Path.GetRelativePath(solutionRoot, file);
                    violations.Add($"  {relativePath}: {workingDirectoryToken}");
                }
            }
        }

        violations.Should().BeEmpty(
            "The database location must not be derived from the process working directory. " +
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
