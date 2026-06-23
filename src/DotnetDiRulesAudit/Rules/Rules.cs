using A2G.DIRulesAudit.Analysis;
using A2G.DIRulesAudit.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;

namespace A2G.DIRulesAudit.Rules;

public interface IDiRule
{
    string RuleId { get; }
    string Name { get; }
    IReadOnlyList<DiFinding> Analyze(RuleContext context);
}

public sealed record RuleContext(
    IReadOnlyList<Project> Projects,
    IReadOnlyList<ServiceRegistration> Registrations,
    IReadOnlyList<ConstructorDependency> ConstructorDependencies,
    DependencyGraph Graph,
    IReadOnlyList<string> WorkspaceDiagnostics,
    Action<string>? Progress = null);

public static class DiRules
{
    public static IReadOnlyList<IDiRule> All { get; } =
    [
        new PreferInterfacesRule(),
        new ConstructorDependencyCountRule(),
        new SingletonCapturesTransientRule(),
        new SingletonCapturesScopedRule(),
        new RegistrationOrganizationRule(),
        new StartupValidationRule(),
        new ServiceLocatorUsageRule(),
        new CircularDependencyRule(),
        new OptionsPatternRule()
    ];
}

public sealed class PreferInterfacesRule : IDiRule
{
    public string RuleId => "DI004";
    public string Name => "Prefer interface-based injection";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        context.Progress?.Invoke($"{RuleId}: checking {context.ConstructorDependencies.Count} constructor dependency record(s) against {context.Registrations.Count} registration(s).");
        var findings = context.ConstructorDependencies
            .Where(d => d.IsConcreteType && !d.IsFrameworkType && context.Registrations.Any(r => r.ImplementationType?.SameType(d.ParameterType) == true))
            .Select(d => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, $"Concrete application service `{d.ParameterType.DisplayName}` is injected into `{d.DeclaringType.DisplayName}`.", d.Location, 0.85,
                "Inject an interface when the dependency is a replaceable application service."))
            .ToArray();
        context.Progress?.Invoke($"{RuleId}: checked constructor dependencies, findings {findings.Length}.");
        return findings;
    }
}

public sealed class ConstructorDependencyCountRule : IDiRule
{
    public string RuleId => "DI005";
    public string Name => "Too many constructor dependencies";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        context.Progress?.Invoke($"{RuleId}: grouping {context.ConstructorDependencies.Count} constructor dependency record(s) by declaring type.");
        var findings = context.ConstructorDependencies
            .GroupBy(d => d.DeclaringType)
            .Where(g => g.Count() >= 7)
            .Select(g => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, $"`{g.Key.DisplayName}` has {g.Count()} constructor dependencies.", g.First().Location, 0.90,
                "Review responsibilities and split the type when there is a clear boundary."))
            .ToArray();
        context.Progress?.Invoke($"{RuleId}: constructor dependency grouping completed, findings {findings.Length}.");
        return findings;
    }
}

public sealed class SingletonCapturesTransientRule : IDiRule
{
    public string RuleId => "DI006";
    public string Name => "Singleton captures transient dependency";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        return RuleHelpers.FindLifetimePaths(context.Graph, ServiceLifetimeKind.Singleton, ServiceLifetimeKind.Transient, RuleId, context.Progress)
            .Select(path => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, $"Singleton dependency path captures transient service: {RuleHelpers.FormatPath(path)}.", path.First().Location, 0.80,
                "Review whether the transient is stateless and thread-safe."))
            .ToArray();
    }
}

public sealed class SingletonCapturesScopedRule : IDiRule
{
    public string RuleId => "DI007";
    public string Name => "Singleton captures scoped dependency";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        return RuleHelpers.FindLifetimePaths(context.Graph, ServiceLifetimeKind.Singleton, ServiceLifetimeKind.Scoped, RuleId, context.Progress)
            .Select(path => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Error, $"Singleton dependency path captures scoped service: {RuleHelpers.FormatPath(path)}.", path.First().Location, 1.00,
                "Inject IServiceScopeFactory and resolve scoped services inside a created scope."))
            .ToArray();
    }
}

public sealed class RegistrationOrganizationRule : IDiRule
{
    public string RuleId => "DI008";
    public string Name => "Too many direct registrations in Program.cs";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        context.Progress?.Invoke($"{RuleId}: scanning {context.Registrations.Count} registration(s) for direct Program.cs registrations.");
        var programRegistrations = context.Registrations
            .Where(r => r.Location?.FilePath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        DiFinding[] findings = programRegistrations.Length >= 20
            ? [RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Info, $"Program.cs contains {programRegistrations.Length} direct registrations.", programRegistrations[0].Location, 0.70,
                "Move related registrations into IServiceCollection extension methods.")]
            : [];
        context.Progress?.Invoke($"{RuleId}: found {programRegistrations.Length} direct Program.cs registration(s), findings {findings.Length}.");
        return findings;
    }
}

public sealed class StartupValidationRule : IDiRule
{
    public string RuleId => "DI009";
    public string Name => "DI validation is not explicitly enabled";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        context.Progress?.Invoke($"{RuleId}: checking startup validation patterns across {context.Projects.Count} project(s).");
        var checkedProjects = 0;
        var hasValidation = false;
        foreach (var project in context.Projects)
        {
            checkedProjects++;
            context.Progress?.Invoke($"{RuleId}: checking project {checkedProjects}/{context.Projects.Count}: {project.Name}.");
            if (ProjectContains(project, "UseDefaultServiceProvider") && ProjectContains(project, "ValidateScopes") && ProjectContains(project, "ValidateOnBuild"))
            {
                hasValidation = true;
                break;
            }
        }

        DiFinding[] findings = hasValidation
            ? []
            : [RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, "DI validation was not detected.", null, 0.75,
                "Enable ValidateScopes and ValidateOnBuild in Development, Test, or CI.")];
        context.Progress?.Invoke($"{RuleId}: startup validation check completed, validation detected: {hasValidation}, findings {findings.Length}.");
        return findings;
    }

    private static bool ProjectContains(Project project, string text)
    {
        return project.Documents.Any(d => d.GetTextAsync().GetAwaiter().GetResult().ToString().Contains(text, StringComparison.Ordinal));
    }
}

public sealed class ServiceLocatorUsageRule : IDiRule
{
    public string RuleId => "DI010";
    public string Name => "Service locator usage";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        var findings = new List<DiFinding>();
        var projects = context.Projects.Where(p => !AnalysisHelpers.IsTestProject(p)).ToArray();
        context.Progress?.Invoke($"{RuleId}: scanning service locator usage across {projects.Length} non-test project(s).");
        for (var projectIndex = 0; projectIndex < projects.Length; projectIndex++)
        {
            var project = projects[projectIndex];
            context.Progress?.Invoke($"{RuleId}: loading compilation for project {projectIndex + 1}/{projects.Length}: {project.Name}.");
            var compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
            if (compilation is null)
            {
                context.Progress?.Invoke($"{RuleId}: skipped project {project.Name}; compilation was null.");
                continue;
            }

            var trees = compilation.SyntaxTrees.Where(t => !AnalysisHelpers.IsGeneratedFile(t)).ToArray();
            context.Progress?.Invoke($"{RuleId}: scanning {trees.Length} syntax tree(s) in project {project.Name}.");
            for (var treeIndex = 0; treeIndex < trees.Length; treeIndex++)
            {
                var tree = trees[treeIndex];
                if ((treeIndex + 1) % 25 == 0 || treeIndex == 0 || treeIndex == trees.Length - 1)
                {
                    context.Progress?.Invoke($"{RuleId}: project {project.Name}, syntax tree {treeIndex + 1}/{trees.Length}, findings so far {findings.Count}.");
                }

                var root = tree.GetRoot();
                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var name = invocation.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : null;
                    if (name is not ("GetService" or "GetRequiredService"))
                    {
                        continue;
                    }

                    var file = tree.FilePath;
                    if ((file.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith("Startup.cs", StringComparison.OrdinalIgnoreCase)) &&
                        invocation.Ancestors().OfType<TypeDeclarationSyntax>().Any() == false)
                    {
                        continue;
                    }

                    if (invocation.Ancestors().OfType<LambdaExpressionSyntax>().Any())
                    {
                        continue;
                    }

                    if (invocation.Expression.ToString().Contains("scope.ServiceProvider", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    findings.Add(RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, "Service locator usage detected in application code.", invocation.ToSourceLocation(), 0.85,
                        "Prefer constructor injection. Use IServiceScopeFactory only when a singleton or hosted service needs scoped work."));
                }
            }
        }

        context.Progress?.Invoke($"{RuleId}: service locator scan completed, findings {findings.Count}.");
        return findings;
    }
}

public sealed class CircularDependencyRule : IDiRule
{
    public string RuleId => "DI011";
    public string Name => "Circular dependency detected";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        var cycles = new List<IReadOnlyList<DependencyEdge>>();
        var rootEdges = context.Graph.Edges.Where(e => e.IsResolved && !e.IsFrameworkType).ToArray();
        var traversedEdges = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastProgress = TimeSpan.Zero;

        context.Progress?.Invoke($"{RuleId}: circular dependency search started: {rootEdges.Length} root edge(s), {context.Graph.Edges.Count} total edge(s).");

        for (var i = 0; i < rootEdges.Length; i++)
        {
            FindCycles(context.Graph, rootEdges[i], rootEdges[i].FromType, [], cycles, ref traversedEdges, () =>
            {
                if (stopwatch.Elapsed - lastProgress < TimeSpan.FromSeconds(2))
                {
                    return;
                }

                lastProgress = stopwatch.Elapsed;
                context.Progress?.Invoke($"{RuleId}: circular dependency search still running: root {i + 1}/{rootEdges.Length}, traversed {traversedEdges} edge visit(s), cycles so far {cycles.Count}.");
            });
        }

        var findings = cycles
            .DistinctBy(RuleHelpers.CanonicalCycleKey)
            .Select(cycle => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Error, $"Circular dependency detected: {RuleHelpers.FormatPath(cycle)}.", cycle.First().Location, 1.00,
                "Break the cycle by extracting shared logic, splitting responsibilities, or moving orchestration into a separate service."))
            .ToArray();
        context.Progress?.Invoke($"{RuleId}: circular dependency search completed: traversed {traversedEdges} edge visit(s), raw cycles {cycles.Count}, findings {findings.Length}.");
        return findings;
    }

    private static void FindCycles(
        DependencyGraph graph,
        DependencyEdge current,
        TypeIdentity target,
        List<DependencyEdge> path,
        List<IReadOnlyList<DependencyEdge>> cycles,
        ref int traversedEdges,
        Action reportProgress)
    {
        traversedEdges++;
        if (traversedEdges % 1_000 == 0)
        {
            reportProgress();
        }

        if (path.Count > 20)
        {
            return;
        }

        path.Add(current);
        if (current.ToType.SameType(target))
        {
            cycles.Add(path.ToArray());
            path.RemoveAt(path.Count - 1);
            return;
        }

        foreach (var next in graph.Edges.Where(e => e.IsResolved && !e.IsFrameworkType && e.FromType.SameType(current.ToType)))
        {
            if (path.Any(p => p.FromType.SameType(next.FromType) && p.ToType.SameType(next.ToType)))
            {
                continue;
            }

            FindCycles(graph, next, target, path, cycles, ref traversedEdges, reportProgress);
        }

        path.RemoveAt(path.Count - 1);
    }
}

public sealed class OptionsPatternRule : IDiRule
{
    private readonly FrameworkTypeClassifier _classifier = new();
    public string RuleId => "DI012";
    public string Name => "Prefer strongly typed options over IConfiguration injection";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        context.Progress?.Invoke($"{RuleId}: checking {context.ConstructorDependencies.Count} constructor dependency record(s) for broad configuration injection.");
        var findings = context.ConstructorDependencies
            .Where(d => _classifier.IsConfigurationType(d.ParameterType))
            .Select(d => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, $"`{d.DeclaringType.DisplayName}` injects broad configuration type `{d.ParameterType.DisplayName}`.", d.Location, 0.80,
                "Create a strongly typed options class and inject IOptions<T>, IOptionsSnapshot<T>, or IOptionsMonitor<T>."))
            .ToArray();
        context.Progress?.Invoke($"{RuleId}: options-pattern check completed, findings {findings.Length}.");
        return findings;
    }
}

internal static class RuleHelpers
{
public static DiFinding Finding(string ruleId, string name, DiFindingSeverity severity, string message, SourceLocation? location, double confidence, string recommendation)
{
    return new DiFinding(ruleId, name, severity, message, location, new Dictionary<string, string>(), recommendation, confidence);
}

public static IReadOnlyList<IReadOnlyList<DependencyEdge>> FindLifetimePaths(
    DependencyGraph graph,
    ServiceLifetimeKind from,
    ServiceLifetimeKind to,
    string ruleId,
    Action<string>? progress)
{
    var results = new List<IReadOnlyList<DependencyEdge>>();
    var rootEdges = graph.Edges.Where(e => e.IsResolved && e.FromLifetime == from).ToArray();
    var traversedEdges = 0;
    var stopwatch = Stopwatch.StartNew();
    var lastProgress = TimeSpan.Zero;

    progress?.Invoke($"{ruleId}: lifetime path search started: {rootEdges.Length} root edge(s), {graph.Edges.Count} total edge(s), target lifetime {to}.");

    for (var i = 0; i < rootEdges.Length; i++)
    {
        Walk(graph, rootEdges[i], to, [], results, ref traversedEdges, () =>
        {
            if (stopwatch.Elapsed - lastProgress < TimeSpan.FromSeconds(2))
            {
                return;
            }

            lastProgress = stopwatch.Elapsed;
            progress?.Invoke($"{ruleId}: lifetime path search still running: root {i + 1}/{rootEdges.Length}, traversed {traversedEdges} edge visit(s), findings so far {results.Count}.");
        });
    }

    var distinctResults = results.DistinctBy(FormatPath).ToArray();
    progress?.Invoke($"{ruleId}: lifetime path search completed: traversed {traversedEdges} edge visit(s), findings {distinctResults.Length}.");
    return distinctResults;
}

private static void Walk(
    DependencyGraph graph,
    DependencyEdge current,
    ServiceLifetimeKind target,
    List<DependencyEdge> path,
    List<IReadOnlyList<DependencyEdge>> results,
    ref int traversedEdges,
    Action reportProgress)
{
    traversedEdges++;
    if (traversedEdges % 1_000 == 0)
    {
        reportProgress();
    }

    if (path.Count > 20)
    {
        return;
    }

    path.Add(current);
    if (current.ToLifetime == target)
    {
        results.Add(path.ToArray());
        path.RemoveAt(path.Count - 1);
        return;
    }

    foreach (var next in graph.Edges.Where(e => e.IsResolved && !e.IsFrameworkType && e.FromType.SameType(current.ToType)))
    {
        if (!path.Any(p => p.FromType.SameType(next.FromType) && p.ToType.SameType(next.ToType)))
        {
            Walk(graph, next, target, path, results, ref traversedEdges, reportProgress);
        }
    }

    path.RemoveAt(path.Count - 1);
}

public static string FormatPath(IEnumerable<DependencyEdge> path)
{
    var edges = path.ToArray();
    if (edges.Length == 0)
    {
        return "";
    }

    return string.Join(" -> ", edges.Select(e => $"{e.FromType.DisplayName} [{e.FromLifetime}]").Append($"{edges[^1].ToType.DisplayName} [{edges[^1].ToLifetime}]"));
}

public static string CanonicalCycleKey(IEnumerable<DependencyEdge> path)
{
    var nodes = path.Select(e => e.FromType.DisplayName).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    return string.Join("|", nodes);
}
}
