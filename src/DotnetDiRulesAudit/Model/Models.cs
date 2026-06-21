using Microsoft.CodeAnalysis;

namespace DotnetDiRulesAudit.Model;

public enum ServiceLifetimeKind
{
    Unknown = 0,
    Transient = 1,
    Scoped = 2,
    Singleton = 3
}

public enum DiFindingSeverity
{
    Error,
    Warning,
    Info,
    Skipped
}

public sealed record TypeIdentity(
    string DisplayName,
    string MetadataName,
    string? AssemblyName,
    string? SymbolKey)
{
    public static TypeIdentity FromSymbol(ITypeSymbol symbol)
    {
        return new TypeIdentity(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "", StringComparison.Ordinal),
            symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "", StringComparison.Ordinal),
            symbol.ContainingAssembly?.Identity.Name,
            null);
    }

    public static TypeIdentity FromDisplay(string displayName)
    {
        return new TypeIdentity(displayName, displayName, null, null);
    }

    public bool SameType(TypeIdentity other)
    {
        if (!string.IsNullOrWhiteSpace(SymbolKey) && SymbolKey == other.SymbolKey)
        {
            return true;
        }

        return MetadataName == other.MetadataName && AssemblyName == other.AssemblyName;
    }
}

public sealed record SourceLocation(string FilePath, int Line, int Column)
{
    public string ToDisplayPath(string? root = null)
    {
        var path = FilePath;
        if (!string.IsNullOrWhiteSpace(root) && Path.IsPathRooted(path))
        {
            path = Path.GetRelativePath(root, path);
        }

        return $"{path.Replace('\\', '/')}:{Line}";
    }
}

public sealed record ServiceRegistration(
    TypeIdentity ServiceType,
    TypeIdentity? ImplementationType,
    ServiceLifetimeKind Lifetime,
    string RegistrationMethod,
    SourceLocation? Location,
    bool IsFactoryRegistration,
    bool IsOpenGeneric,
    bool IsFrameworkRegistration,
    bool IsGraphNode,
    string? Key);

public sealed record ConstructorDependency(
    TypeIdentity DeclaringType,
    TypeIdentity ParameterType,
    string ParameterName,
    SourceLocation? Location,
    bool IsFrameworkType,
    bool IsConcreteType,
    bool IsInterfaceType);

public sealed record DependencyNode(TypeIdentity Type, ServiceLifetimeKind Lifetime, SourceLocation? Location);

public sealed record DependencyEdge(
    TypeIdentity FromType,
    TypeIdentity ToType,
    ServiceLifetimeKind FromLifetime,
    ServiceLifetimeKind ToLifetime,
    SourceLocation? Location,
    bool IsResolved,
    bool IsFrameworkType);

public sealed record DependencyGraph(
    IReadOnlyList<DependencyNode> Nodes,
    IReadOnlyList<DependencyEdge> Edges,
    IReadOnlyList<ConstructorDependency> UnresolvedDependencies);

public sealed record DiFinding(
    string RuleId,
    string RuleName,
    DiFindingSeverity Severity,
    string Message,
    SourceLocation? Location,
    IReadOnlyDictionary<string, string> Details,
    string Recommendation,
    double Confidence);

public sealed record DiAuditResult(
    string InputPath,
    string SelectedInputPath,
    IReadOnlyList<string> InputDiagnostics,
    IReadOnlyList<string> WorkspaceDiagnostics,
    IReadOnlyList<ServiceRegistration> Registrations,
    IReadOnlyList<ConstructorDependency> ConstructorDependencies,
    DependencyGraph Graph,
    IReadOnlyList<DiFinding> Findings);
