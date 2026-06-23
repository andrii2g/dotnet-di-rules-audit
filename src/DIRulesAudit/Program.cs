using A2G.DIRulesAudit.Analysis;
using A2G.DIRulesAudit.Cli;

var options = CliParser.Parse(args, Console.Error);
if (options is null)
{
    return 2;
}

try
{
    var runner = new DiAuditRunner();
    return await runner.RunAsync(options.InputPath, Path.Combine(Directory.GetCurrentDirectory(), CliOptions.DefaultReportFileName), options.TargetFramework, CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 4;
}
