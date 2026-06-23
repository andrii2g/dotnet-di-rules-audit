using A2G.DIRulesAudit.Cli;
using FluentAssertions;

namespace A2G.DIRulesAudit.Tests;

public sealed class CliParserTests
{
    [Fact]
    public void Parse_accepts_input_path_only()
    {
        using var error = new StringWriter();

        var options = CliParser.Parse(["./app.sln"], error);

        options.Should().NotBeNull();
        options!.InputPath.Should().Be("./app.sln");
        options.TargetFramework.Should().BeNull();
    }

    [Fact]
    public void Parse_accepts_preferred_target_framework()
    {
        using var error = new StringWriter();

        var options = CliParser.Parse(["--framework", "net8.0", "./app.sln"], error);

        options.Should().NotBeNull();
        options!.InputPath.Should().Be("./app.sln");
        options.TargetFramework.Should().Be("net8.0");
    }
}
