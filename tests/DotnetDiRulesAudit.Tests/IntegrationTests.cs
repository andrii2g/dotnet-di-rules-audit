using A2G.DIRulesAudit.Analysis;
using FluentAssertions;

namespace A2G.DIRulesAudit.Tests;

public sealed class IntegrationTests
{
    [Fact]
    public async Task Good_sample_has_no_blocking_findings()
    {
        var report = Path.Combine(Path.GetTempPath(), $"good-di-audit-{Guid.NewGuid():N}.md");
        var exitCode = await new DiAuditRunner().RunAsync(Path.Combine(RepositoryRoot(), "samples", "GoodDiSample"), report, CancellationToken.None);

        exitCode.Should().Be(0);
        var text = await File.ReadAllTextAsync(report);
        text.Should().Contain("PASSED - no blocking Dependency Injection issues found.");
        text.Should().NotContain("### DI007");
        text.Should().NotContain("### DI011");
        text.Should().NotContain("### DI010");
    }

    [Fact]
    public async Task Bad_sample_reports_blocking_and_warning_rules()
    {
        var report = Path.Combine(Path.GetTempPath(), $"bad-di-audit-{Guid.NewGuid():N}.md");
        var exitCode = await new DiAuditRunner().RunAsync(Path.Combine(RepositoryRoot(), "samples", "BadDiSample"), report, CancellationToken.None);

        exitCode.Should().Be(1);
        var text = await File.ReadAllTextAsync(report);
        text.Should().Contain("### DI007");
        text.Should().Contain("### DI011");
        text.Should().Contain("### DI010");
        text.Should().Contain("### DI012");
    }

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "DotnetDiRulesAudit.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
