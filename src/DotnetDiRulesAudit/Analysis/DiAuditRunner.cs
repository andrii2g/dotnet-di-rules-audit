using DotnetDiRulesAudit.Loading;
using DotnetDiRulesAudit.Model;
using DotnetDiRulesAudit.Reporting;
using DotnetDiRulesAudit.Rules;

namespace DotnetDiRulesAudit.Analysis;

public sealed class DiAuditRunner
{
    private readonly InputPathResolver _inputPathResolver = new();
    private readonly WorkspaceLoader _workspaceLoader = new();
    private readonly ServiceRegistrationScanner _registrationScanner = new();
    private readonly ConstructorScanner _constructorScanner = new();
    private readonly ServiceGraphBuilder _graphBuilder = new();
    private readonly MarkdownReportWriter _reportWriter = new();

    public async Task<int> RunAsync(string inputPath, string reportPath, CancellationToken cancellationToken)
    {
        InputResolution resolution;
        try
        {
            resolution = _inputPathResolver.Resolve(inputPath);
        }
        catch (InputResolutionException ex)
        {
            Console.Error.WriteLine(ex.Message);
            foreach (var candidate in ex.Candidates)
            {
                Console.Error.WriteLine($"- {candidate}");
            }

            return 2;
        }

        Console.WriteLine($"Input selected: {resolution.SelectedPath}");
        var loadResult = await _workspaceLoader.LoadAsync(resolution, cancellationToken);
        if (loadResult.HasCriticalErrors)
        {
            await WriteLoadFailureReportAsync(inputPath, resolution, loadResult, reportPath, cancellationToken);
            return 3;
        }

        var registrations = new List<ServiceRegistration>();
        var dependencies = new List<ConstructorDependency>();
        var includeTestProjects = resolution.Kind == InputKind.Project && resolution.SelectedPath.Contains("Test", StringComparison.OrdinalIgnoreCase);

        foreach (var project in loadResult.Projects)
        {
            registrations.AddRange(await _registrationScanner.ScanAsync(project, cancellationToken));
            dependencies.AddRange(await _constructorScanner.ScanAsync(project, includeTestProjects, cancellationToken));
        }

        var graph = _graphBuilder.Build(registrations, dependencies);
        var context = new RuleContext(loadResult.Projects, registrations, dependencies, graph, loadResult.Diagnostics);
        var findings = DiRules.All.SelectMany(rule => rule.Analyze(context)).ToArray();
        var result = new DiAuditResult(inputPath, resolution.SelectedPath, resolution.Diagnostics, loadResult.Diagnostics, registrations, dependencies, graph, findings);
        await _reportWriter.WriteAsync(result, reportPath, cancellationToken);

        var errors = findings.Count(f => f.Severity == DiFindingSeverity.Error);
        Console.WriteLine("Dependency Injection audit completed.");
        Console.WriteLine($"Report: {reportPath}");
        Console.WriteLine($"Errors: {errors}");
        Console.WriteLine($"Warnings: {findings.Count(f => f.Severity == DiFindingSeverity.Warning)}");
        Console.WriteLine($"Info: {findings.Count(f => f.Severity == DiFindingSeverity.Info)}");
        Console.WriteLine(errors > 0 ? "Result: FAILED" : "Result: PASSED");

        return errors > 0 ? 1 : 0;
    }

    private async Task WriteLoadFailureReportAsync(string inputPath, InputResolution resolution, ProjectLoadResult loadResult, string reportPath, CancellationToken cancellationToken)
    {
        var result = new DiAuditResult(
            inputPath,
            resolution.SelectedPath,
            resolution.Diagnostics,
            loadResult.Diagnostics,
            Array.Empty<ServiceRegistration>(),
            Array.Empty<ConstructorDependency>(),
            new DependencyGraph(Array.Empty<DependencyNode>(), Array.Empty<DependencyEdge>(), Array.Empty<ConstructorDependency>()),
            Array.Empty<DiFinding>());

        await _reportWriter.WriteAsync(result, reportPath, cancellationToken);
    }
}
