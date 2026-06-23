using A2G.DIRulesAudit.Model;
using Microsoft.CodeAnalysis;

namespace A2G.DIRulesAudit.Analysis;

public sealed class FrameworkTypeClassifier
{
    private static readonly string[] FrameworkPrefixes =
    {
        "Microsoft.Extensions.Logging.ILogger",
        "Microsoft.Extensions.Options.IOptions",
        "Microsoft.Extensions.Configuration.IConfiguration",
        "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory",
        "System.IServiceProvider",
        "Microsoft.Extensions.Hosting.IHostApplicationLifetime",
        "Microsoft.AspNetCore.Hosting.IWebHostEnvironment",
        "System.Threading.CancellationToken",
        "Microsoft.Extensions.Caching.Memory.IMemoryCache",
        "Microsoft.Extensions.Caching.Distributed.IDistributedCache",
        "Microsoft.EntityFrameworkCore.DbContextOptions"
    };

    private static readonly HashSet<string> FrameworkExact = new(StringComparer.Ordinal)
    {
        "System.Net.Http.IHttpClientFactory",
        "System.Net.Http.HttpClient",
        "Microsoft.Extensions.Logging.ILoggerFactory"
    };

    public bool IsFrameworkType(TypeIdentity identity)
    {
        return FrameworkExact.Contains(identity.MetadataName) ||
               FrameworkPrefixes.Any(prefix => identity.MetadataName.StartsWith(prefix, StringComparison.Ordinal));
    }

    public bool IsFrameworkType(ITypeSymbol symbol)
    {
        return IsFrameworkType(TypeIdentity.FromSymbol(symbol));
    }

    public bool IsConfigurationType(TypeIdentity identity)
    {
        return identity.MetadataName is
            "Microsoft.Extensions.Configuration.IConfiguration" or
            "Microsoft.Extensions.Configuration.IConfigurationRoot" or
            "Microsoft.Extensions.Configuration.IConfigurationSection";
    }

    public bool IsServiceProvider(TypeIdentity identity)
    {
        return identity.MetadataName is "System.IServiceProvider" ||
               identity.DisplayName.EndsWith(".IServiceProvider", StringComparison.Ordinal);
    }
}
