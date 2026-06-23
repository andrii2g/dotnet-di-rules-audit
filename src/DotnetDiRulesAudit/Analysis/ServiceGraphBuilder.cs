using A2G.DIRulesAudit.Model;

namespace A2G.DIRulesAudit.Analysis;

public sealed class ServiceGraphBuilder
{
    private readonly FrameworkTypeClassifier _frameworkTypeClassifier = new();

    public DependencyGraph Build(IReadOnlyList<ServiceRegistration> registrations, IReadOnlyList<ConstructorDependency> dependencies)
    {
        var graphRegistrations = registrations.Where(r => r.IsGraphNode).ToArray();
        var nodes = graphRegistrations
            .SelectMany(r => new[] { r.ServiceType, r.ImplementationType }.Where(t => t is not null).Select(t => t!))
            .DistinctBy(TypeKey)
            .Select(t => new DependencyNode(t, ResolveLifetime(t, graphRegistrations), graphRegistrations.FirstOrDefault(r => r.ServiceType.SameType(t) || r.ImplementationType?.SameType(t) == true)?.Location))
            .ToArray();

        var edges = new List<DependencyEdge>();
        var unresolved = new List<ConstructorDependency>();

        foreach (var dependency in dependencies)
        {
            var fromRegistration = graphRegistrations.FirstOrDefault(r => r.ImplementationType?.SameType(dependency.DeclaringType) == true || r.ServiceType.SameType(dependency.DeclaringType));
            if (fromRegistration is null)
            {
                continue;
            }

            var target = graphRegistrations.FirstOrDefault(r => r.ServiceType.SameType(dependency.ParameterType) || r.ImplementationType?.SameType(dependency.ParameterType) == true);
            var isFramework = dependency.IsFrameworkType || _frameworkTypeClassifier.IsFrameworkType(dependency.ParameterType);
            if (target is null)
            {
                if (!isFramework)
                {
                    unresolved.Add(dependency);
                }

                edges.Add(new DependencyEdge(
                    fromRegistration.ImplementationType ?? fromRegistration.ServiceType,
                    dependency.ParameterType,
                    fromRegistration.Lifetime,
                    ServiceLifetimeKind.Unknown,
                    dependency.Location,
                    false,
                    isFramework));
                continue;
            }

            edges.Add(new DependencyEdge(
                fromRegistration.ImplementationType ?? fromRegistration.ServiceType,
                target.ImplementationType ?? target.ServiceType,
                fromRegistration.Lifetime,
                target.Lifetime,
                dependency.Location,
                true,
                isFramework));
        }

        return new DependencyGraph(nodes, edges, unresolved);
    }

    private static string TypeKey(TypeIdentity type)
    {
        return type.SymbolKey ?? $"{type.AssemblyName}:{type.MetadataName}";
    }

    private static ServiceLifetimeKind ResolveLifetime(TypeIdentity type, IReadOnlyList<ServiceRegistration> registrations)
    {
        return registrations.FirstOrDefault(r => r.ServiceType.SameType(type) || r.ImplementationType?.SameType(type) == true)?.Lifetime ?? ServiceLifetimeKind.Unknown;
    }
}
