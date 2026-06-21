using DotnetDiRulesAudit.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetDiRulesAudit.Analysis;

public sealed class ConstructorScanner
{
    private readonly FrameworkTypeClassifier _frameworkTypeClassifier = new();

    public async Task<IReadOnlyList<ConstructorDependency>> ScanAsync(Project project, bool includeTestProjects, CancellationToken cancellationToken)
    {
        if (!includeTestProjects && AnalysisHelpers.IsTestProject(project))
        {
            return Array.Empty<ConstructorDependency>();
        }

        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
        {
            return Array.Empty<ConstructorDependency>();
        }

        var dependencies = new List<ConstructorDependency>();
        foreach (var tree in compilation.SyntaxTrees.Where(t => !AnalysisHelpers.IsGeneratedFile(t)))
        {
            var root = await tree.GetRootAsync(cancellationToken);
            var model = compilation.GetSemanticModel(tree);
            foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = model.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;
                if (typeSymbol is null || typeSymbol.IsImplicitlyDeclared)
                {
                    continue;
                }

                foreach (var parameter in SelectConstructorParameters(typeSymbol))
                {
                    var parameterType = parameter.Type;
                    if (parameterType is null)
                    {
                        continue;
                    }

                    var location = parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken).ToSourceLocation();
                    dependencies.Add(new ConstructorDependency(
                        TypeIdentity.FromSymbol(typeSymbol),
                        TypeIdentity.FromSymbol(parameterType),
                        parameter.Name,
                        location,
                        _frameworkTypeClassifier.IsFrameworkType(parameterType),
                        parameterType.TypeKind == TypeKind.Class,
                        parameterType.TypeKind == TypeKind.Interface));
                }
            }
        }

        return dependencies;
    }

    private static IReadOnlyList<IParameterSymbol> SelectConstructorParameters(INamedTypeSymbol typeSymbol)
    {
        var publicConstructors = typeSymbol.Constructors
            .Where(c => !c.IsImplicitlyDeclared && c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .ToArray();

        if (publicConstructors.Length > 0)
        {
            return publicConstructors[0].Parameters;
        }

        var primaryConstructor = typeSymbol.InstanceConstructors
            .Where(c => !c.IsImplicitlyDeclared)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        return primaryConstructor?.Parameters.ToArray() ?? Array.Empty<IParameterSymbol>();
    }
}
