using DotnetDiRulesAudit.Loading;
using DotnetDiRulesAudit.Model;
using DotnetDiRulesAudit.Reporting;
using DotnetDiRulesAudit.Rules;
using System.Diagnostics;

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
        var progress = new AuditProgress();
        progress.Write("Starting Dependency Injection audit.");
        progress.Write($"Input path: {inputPath}");

        InputResolution resolution;
        try
        {
            progress.Write("Resolving input path...");
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

        progress.Write($"Input selected: {resolution.SelectedPath}");
        progress.Write("Loading solution/project with MSBuildWorkspace. This can take a while on large repositories...");
        var loadResult = await _workspaceLoader.LoadAsync(resolution, cancellationToken);
        if (loadResult.HasCriticalErrors)
        {
            progress.Write("Workspace load failed. Writing diagnostic report...");
            await WriteLoadFailureReportAsync(inputPath, resolution, loadResult, reportPath, cancellationToken);
            return 3;
        }

        progress.Write($"Workspace loaded: {loadResult.Projects.Count} C# project(s), {loadResult.Diagnostics.Count} diagnostic(s).");
        var registrations = new List<ServiceRegistration>();
        var dependencies = new List<ConstructorDependency>();
        var includeTestProjects = resolution.Kind == InputKind.Project && resolution.SelectedPath.Contains("Test", StringComparison.OrdinalIgnoreCase);

        for (var i = 0; i < loadResult.Projects.Count; i++)
        {
            var project = loadResult.Projects[i];
            progress.Write($"[{i + 1}/{loadResult.Projects.Count}] Scanning project: {project.Name}");

            progress.Write($"[{i + 1}/{loadResult.Projects.Count}]   Scanning service registrations...");
            var projectRegistrations = await _registrationScanner.ScanAsync(project, cancellationToken);
            registrations.AddRange(projectRegistrations);
            progress.Write($"[{i + 1}/{loadResult.Projects.Count}]   Registrations found: {projectRegistrations.Count}");

            progress.Write($"[{i + 1}/{loadResult.Projects.Count}]   Scanning constructor dependencies...");
            var projectDependencies = await _constructorScanner.ScanAsync(project, includeTestProjects, cancellationToken);
            dependencies.AddRange(projectDependencies);
            progress.Write($"[{i + 1}/{loadResult.Projects.Count}]   Constructor dependencies found: {projectDependencies.Count}");
        }

        progress.Write($"Building dependency graph from {registrations.Count} registration(s) and {dependencies.Count} constructor dependency record(s)...");
        var graph = _graphBuilder.Build(registrations, dependencies);
        progress.Write($"Dependency graph built: {graph.Nodes.Count} node(s), {graph.Edges.Count} edge(s), {graph.UnresolvedDependencies.Count} unresolved dependency record(s).");

        var context = new RuleContext(loadResult.Projects, registrations, dependencies, graph, loadResult.Diagnostics);
        var findings = new List<DiFinding>();
        foreach (var rule in DiRules.All)
        {
            progress.Write($"Running rule {rule.RuleId}: {rule.Name}...");
            var ruleFindings = rule.Analyze(context);
            findings.AddRange(ruleFindings);
            progress.Write($"Rule {rule.RuleId} completed: {ruleFindings.Count} finding(s).");
        }

        progress.Write($"Writing Markdown report: {reportPath}");
        var result = new DiAuditResult(inputPath, resolution.SelectedPath, resolution.Diagnostics, loadResult.Diagnostics, registrations, dependencies, graph, findings);
        await _reportWriter.WriteAsync(result, reportPath, cancellationToken);

        var errors = findings.Count(f => f.Severity == DiFindingSeverity.Error);
        progress.Write("Dependency Injection audit completed.");
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

    private sealed class AuditProgress
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public void Write(string message)
        {
            Console.WriteLine($"[{_stopwatch.Elapsed:hh\\:mm\\:ss}] {message}");
        }
    }
}
