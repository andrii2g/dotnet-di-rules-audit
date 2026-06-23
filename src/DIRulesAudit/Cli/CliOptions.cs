namespace A2G.DIRulesAudit.Cli;

public sealed record CliOptions(string InputPath, string? TargetFramework)
{
    public const string DefaultReportFileName = "DI_AUDIT_REPORT.md";
}

public static class CliParser
{
    public static CliOptions? Parse(string[] args, TextWriter error)
    {
        if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        {
            WriteUsage(error);
            return null;
        }

        string? inputPath = null;
        string? targetFramework = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--framework" or "-f")
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    error.WriteLine($"{arg} requires a target framework value, for example net8.0.");
                    WriteUsage(error);
                    return null;
                }

                targetFramework = args[++i];
                continue;
            }

            if (inputPath is not null)
            {
                error.WriteLine($"Unexpected argument: {arg}");
                WriteUsage(error);
                return null;
            }

            inputPath = arg;
        }

        if (inputPath is null)
        {
            WriteUsage(error);
            return null;
        }

        return new CliOptions(inputPath, targetFramework);
    }

    private static void WriteUsage(TextWriter error)
    {
        error.WriteLine("Usage: dotnet-di-rules-audit [--framework <tfm>] <path>");
    }
}
