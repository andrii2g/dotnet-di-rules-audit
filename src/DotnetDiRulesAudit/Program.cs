using DotnetDiRulesAudit.Analysis;
using DotnetDiRulesAudit.Cli;

var options = CliParser.Parse(args, Console.Error);
if (options is null)
{
    return 2;
}

try
{
    var runner = new DiAuditRunner();
    return await runner.RunAsync(options.InputPath, Path.Combine(Directory.GetCurrentDirectory(), CliOptions.DefaultReportFileName), CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 4;
}
