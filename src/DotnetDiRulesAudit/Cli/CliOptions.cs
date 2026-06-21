namespace DotnetDiRulesAudit.Cli;

public sealed record CliOptions(string InputPath)
{
    public const string DefaultReportFileName = "DI_AUDIT_REPORT.md";
}

public static class CliParser
{
    public static CliOptions? Parse(string[] args, TextWriter error)
    {
        if (args.Length != 1 || args[0] is "-h" or "--help")
        {
            error.WriteLine("Usage: dotnet-di-rules-audit <path>");
            return null;
        }

        return new CliOptions(args[0]);
    }
}
