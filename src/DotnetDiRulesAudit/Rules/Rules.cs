using DotnetDiRulesAudit.Analysis;
using DotnetDiRulesAudit.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetDiRulesAudit.Rules;

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
    IReadOnlyList<string> WorkspaceDiagnostics);

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
        return context.ConstructorDependencies
            .Where(d => d.IsConcreteType && !d.IsFrameworkType && context.Registrations.Any(r => r.ImplementationType?.SameType(d.ParameterType) == true))
            .Select(d => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, $"Concrete application service `{d.ParameterType.DisplayName}` is injected into `{d.DeclaringType.DisplayName}`.", d.Location, 0.85,
                "Inject an interface when the dependency is a replaceable application service."))
            .ToArray();
    }
}

public sealed class ConstructorDependencyCountRule : IDiRule
{
    public string RuleId => "DI005";
    public string Name => "Too many constructor dependencies";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        return context.ConstructorDependencies
            .GroupBy(d => d.DeclaringType)
            .Where(g => g.Count() >= 7)
            .Select(g => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, $"`{g.Key.DisplayName}` has {g.Count()} constructor dependencies.", g.First().Location, 0.90,
                "Review responsibilities and split the type when there is a clear boundary."))
            .ToArray();
    }
}

public sealed class SingletonCapturesTransientRule : IDiRule
{
    public string RuleId => "DI006";
    public string Name => "Singleton captures transient dependency";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        return RuleHelpers.FindLifetimePaths(context.Graph, ServiceLifetimeKind.Singleton, ServiceLifetimeKind.Transient)
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
        return RuleHelpers.FindLifetimePaths(context.Graph, ServiceLifetimeKind.Singleton, ServiceLifetimeKind.Scoped)
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
        var programRegistrations = context.Registrations
            .Where(r => r.Location?.FilePath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        return programRegistrations.Length >= 20
            ? [RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Info, $"Program.cs contains {programRegistrations.Length} direct registrations.", programRegistrations[0].Location, 0.70,
                "Move related registrations into IServiceCollection extension methods.")]
            : [];
    }
}

public sealed class StartupValidationRule : IDiRule
{
    public string RuleId => "DI009";
    public string Name => "DI validation is not explicitly enabled";

    public IReadOnlyList<DiFinding> Analyze(RuleContext context)
    {
        var hasValidation = context.Projects.Any(project => ProjectContains(project, "UseDefaultServiceProvider") && ProjectContains(project, "ValidateScopes") && ProjectContains(project, "ValidateOnBuild"));
        return hasValidation
            ? []
            : [RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, "DI validation was not detected.", null, 0.75,
                "Enable ValidateScopes and ValidateOnBuild in Development, Test, or CI.")];
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
        foreach (var project in context.Projects.Where(p => !AnalysisHelpers.IsTestProject(p)))
        {
            var compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
            if (compilation is null)
            {
                continue;
            }

            foreach (var tree in compilation.SyntaxTrees.Where(t => !AnalysisHelpers.IsGeneratedFile(t)))
            {
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
        foreach (var edge in context.Graph.Edges.Where(e => e.IsResolved && !e.IsFrameworkType))
        {
            FindCycles(context.Graph, edge, edge.FromType, [], cycles);
        }

        return cycles
            .DistinctBy(RuleHelpers.CanonicalCycleKey)
            .Select(cycle => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Error, $"Circular dependency detected: {RuleHelpers.FormatPath(cycle)}.", cycle.First().Location, 1.00,
                "Break the cycle by extracting shared logic, splitting responsibilities, or moving orchestration into a separate service."))
            .ToArray();
    }

    private static void FindCycles(DependencyGraph graph, DependencyEdge current, TypeIdentity target, List<DependencyEdge> path, List<IReadOnlyList<DependencyEdge>> cycles)
    {
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

            FindCycles(graph, next, target, path, cycles);
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
        return context.ConstructorDependencies
            .Where(d => _classifier.IsConfigurationType(d.ParameterType))
            .Select(d => RuleHelpers.Finding(RuleId, Name, DiFindingSeverity.Warning, $"`{d.DeclaringType.DisplayName}` injects broad configuration type `{d.ParameterType.DisplayName}`.", d.Location, 0.80,
                "Create a strongly typed options class and inject IOptions<T>, IOptionsSnapshot<T>, or IOptionsMonitor<T>."))
            .ToArray();
    }
}

internal static class RuleHelpers
{
public static DiFinding Finding(string ruleId, string name, DiFindingSeverity severity, string message, SourceLocation? location, double confidence, string recommendation)
{
    return new DiFinding(ruleId, name, severity, message, location, new Dictionary<string, string>(), recommendation, confidence);
}

public static IReadOnlyList<IReadOnlyList<DependencyEdge>> FindLifetimePaths(DependencyGraph graph, ServiceLifetimeKind from, ServiceLifetimeKind to)
{
    var results = new List<IReadOnlyList<DependencyEdge>>();
    foreach (var edge in graph.Edges.Where(e => e.IsResolved && e.FromLifetime == from))
    {
        Walk(graph, edge, to, [], results);
    }

    return results.DistinctBy(FormatPath).ToArray();
}

private static void Walk(DependencyGraph graph, DependencyEdge current, ServiceLifetimeKind target, List<DependencyEdge> path, List<IReadOnlyList<DependencyEdge>> results)
{
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
            Walk(graph, next, target, path, results);
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
