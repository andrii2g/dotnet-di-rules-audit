using A2G.DIRulesAudit.Loading;
using FluentAssertions;

namespace A2G.DIRulesAudit.Tests;

public sealed class InputPathResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "di-audit-tests", Guid.NewGuid().ToString("N"));

    public InputPathResolverTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Resolve_prefers_root_solution_over_other_candidates()
    {
        File.WriteAllText(Path.Combine(_root, "App.sln"), "");
        File.WriteAllText(Path.Combine(_root, "App.slnx"), "");
        File.WriteAllText(Path.Combine(_root, "App.csproj"), "");

        var result = new InputPathResolver().Resolve(_root);

        result.Kind.Should().Be(InputKind.Solution);
        result.SelectedPath.Should().EndWith("App.sln");
    }

    [Fact]
    public void Resolve_rejects_ambiguous_recursive_candidates()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a"));
        Directory.CreateDirectory(Path.Combine(_root, "b"));
        File.WriteAllText(Path.Combine(_root, "a", "A.sln"), "");
        File.WriteAllText(Path.Combine(_root, "b", "B.sln"), "");

        var action = () => new InputPathResolver().Resolve(_root);

        action.Should().Throw<InputResolutionException>()
            .Which.Candidates.Should().HaveCount(2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
