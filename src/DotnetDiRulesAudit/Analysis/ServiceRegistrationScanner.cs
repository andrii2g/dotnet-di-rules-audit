using DotnetDiRulesAudit.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetDiRulesAudit.Analysis;

public sealed class ServiceRegistrationScanner
{
    private static readonly Dictionary<string, ServiceLifetimeKind> LifetimeMethods = new(StringComparer.Ordinal)
    {
        ["AddTransient"] = ServiceLifetimeKind.Transient,
        ["TryAddTransient"] = ServiceLifetimeKind.Transient,
        ["AddKeyedTransient"] = ServiceLifetimeKind.Transient,
        ["AddScoped"] = ServiceLifetimeKind.Scoped,
        ["TryAddScoped"] = ServiceLifetimeKind.Scoped,
        ["AddKeyedScoped"] = ServiceLifetimeKind.Scoped,
        ["AddSingleton"] = ServiceLifetimeKind.Singleton,
        ["TryAddSingleton"] = ServiceLifetimeKind.Singleton,
        ["AddKeyedSingleton"] = ServiceLifetimeKind.Singleton
    };

    public async Task<IReadOnlyList<ServiceRegistration>> ScanAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
        {
            return Array.Empty<ServiceRegistration>();
        }

        var registrations = new List<ServiceRegistration>();
        foreach (var tree in compilation.SyntaxTrees.Where(t => !AnalysisHelpers.IsGeneratedFile(t)))
        {
            var root = await tree.GetRootAsync(cancellationToken);
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var registration = TryCreateRegistration(model, invocation, cancellationToken);
                if (registration is not null)
                {
                    registrations.Add(registration);
                }
            }
        }

        return registrations;
    }

    private static ServiceRegistration? TryCreateRegistration(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        var methodName = method?.Name ?? GetInvocationName(invocation);
        if (string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        if (LifetimeMethods.TryGetValue(methodName, out var lifetime))
        {
            return CreateLifetimeRegistration(model, invocation, method, methodName, lifetime, cancellationToken);
        }

        return methodName switch
        {
            "AddDbContext" or "AddDbContextPool" => CreateDbContextRegistration(invocation, method, methodName),
            "AddDbContextFactory" or "AddPooledDbContextFactory" => CreateDbContextFactoryRegistration(invocation, method, methodName),
            "AddHostedService" => CreateHostedServiceRegistration(invocation, method),
            "Configure" or "AddOptions" => CreateOptionsMetadataRegistration(invocation, method, methodName),
            _ => null
        };
    }

    private static ServiceRegistration? CreateLifetimeRegistration(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method,
        string methodName,
        ServiceLifetimeKind lifetime,
        CancellationToken cancellationToken)
    {
        ITypeSymbol? service = null;
        ITypeSymbol? implementation = null;
        if (method is { TypeArguments.Length: > 0 })
        {
            service = method.TypeArguments[0];
            implementation = method.TypeArguments.Length > 1 ? method.TypeArguments[1] : method.TypeArguments[0];
        }
        else
        {
            var args = invocation.ArgumentList.Arguments;
            if (args.Count > 0)
            {
                service = AnalysisHelpers.GetTypeOfExpression(model, args[0], cancellationToken);
            }

            if (args.Count > 1)
            {
                implementation = AnalysisHelpers.GetTypeOfExpression(model, args[1], cancellationToken);
            }
        }

        if (service is null)
        {
            return null;
        }

        var isFactory = invocation.ArgumentList.Arguments.Any(a => a.Expression is LambdaExpressionSyntax);
        implementation ??= GetFactoryCreatedType(model, invocation, cancellationToken);
        var key = methodName.StartsWith("AddKeyed", StringComparison.Ordinal) ? TryReadKey(invocation) : null;

        return new ServiceRegistration(
            TypeIdentity.FromSymbol(service),
            implementation is null ? null : TypeIdentity.FromSymbol(implementation),
            lifetime,
            methodName,
            invocation.ToSourceLocation(),
            isFactory,
            AnalysisHelpers.IsOpenGeneric(service),
            false,
            key is null,
            key);
    }

    private static ServiceRegistration? CreateDbContextRegistration(InvocationExpressionSyntax invocation, IMethodSymbol? method, string methodName)
    {
        var context = method?.TypeArguments.FirstOrDefault();
        return context is null
            ? null
            : new ServiceRegistration(TypeIdentity.FromSymbol(context), TypeIdentity.FromSymbol(context), ServiceLifetimeKind.Scoped, methodName, invocation.ToSourceLocation(), false, false, true, true, null);
    }

    private static ServiceRegistration? CreateDbContextFactoryRegistration(InvocationExpressionSyntax invocation, IMethodSymbol? method, string methodName)
    {
        var context = method?.TypeArguments.FirstOrDefault();
        if (context is null)
        {
            return null;
        }

        return new ServiceRegistration(
            TypeIdentity.FromDisplay($"Microsoft.EntityFrameworkCore.IDbContextFactory<{context.ToDisplayString()}>"),
            null,
            ServiceLifetimeKind.Singleton,
            methodName,
            invocation.ToSourceLocation(),
            false,
            false,
            true,
            false,
            null);
    }

    private static ServiceRegistration? CreateHostedServiceRegistration(InvocationExpressionSyntax invocation, IMethodSymbol? method)
    {
        var worker = method?.TypeArguments.FirstOrDefault();
        if (worker is null)
        {
            return null;
        }

        return new ServiceRegistration(
            TypeIdentity.FromDisplay("Microsoft.Extensions.Hosting.IHostedService"),
            TypeIdentity.FromSymbol(worker),
            ServiceLifetimeKind.Singleton,
            "AddHostedService",
            invocation.ToSourceLocation(),
            false,
            false,
            true,
            true,
            null);
    }

    private static ServiceRegistration? CreateOptionsMetadataRegistration(InvocationExpressionSyntax invocation, IMethodSymbol? method, string methodName)
    {
        var options = method?.TypeArguments.FirstOrDefault();
        return options is null
            ? null
            : new ServiceRegistration(TypeIdentity.FromSymbol(options), null, ServiceLifetimeKind.Unknown, methodName, invocation.ToSourceLocation(), false, false, true, false, null);
    }

    private static ITypeSymbol? GetFactoryCreatedType(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        foreach (var lambda in invocation.ArgumentList.Arguments.Select(a => a.Expression).OfType<LambdaExpressionSyntax>())
        {
            var creation = lambda.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
            if (creation is not null)
            {
                return model.GetTypeInfo(creation, cancellationToken).Type;
            }
        }

        return null;
    }

    private static string? TryReadKey(InvocationExpressionSyntax invocation)
    {
        return invocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Select(l => l.Token.ValueText)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    private static string? GetInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };
    }
}
