using System.Text;
using A2G.DIRulesAudit.Model;

namespace A2G.DIRulesAudit.Reporting;

public sealed class MarkdownReportWriter
{
    public async Task WriteAsync(DiAuditResult result, string outputPath, CancellationToken cancellationToken)
    {
        var root = Directory.GetCurrentDirectory();
        var builder = new StringBuilder();
        builder.AppendLine("# Dependency Injection Audit Report");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Input: `{result.InputPath}`");
        builder.AppendLine($"Selected input: `{result.SelectedInputPath}`");
        builder.AppendLine("Tool: `dotnet-di-rules-audit`");
        builder.AppendLine("Version: `0.1.0`");
        builder.AppendLine();
        AppendSummary(builder, result);
        AppendFindings(builder, "Blocking Findings", result.Findings.Where(f => f.Severity == DiFindingSeverity.Error), root);
        AppendFindings(builder, "Warnings", result.Findings.Where(f => f.Severity == DiFindingSeverity.Warning), root);
        AppendFindings(builder, "Informational Findings", result.Findings.Where(f => f.Severity == DiFindingSeverity.Info), root);
        AppendRegistrations(builder, result);
        AppendUnresolved(builder, result, root);
        AppendDiagnostics(builder, "Input Diagnostics", result.InputDiagnostics);
        AppendDiagnostics(builder, "Workspace Diagnostics", result.WorkspaceDiagnostics);
        AppendSkipped(builder, result);
        AppendFixOrder(builder);

        await File.WriteAllTextAsync(outputPath, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static void AppendSummary(StringBuilder builder, DiAuditResult result)
    {
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("| Severity | Count |");
        builder.AppendLine("|---|---:|");
        foreach (var severity in Enum.GetValues<DiFindingSeverity>())
        {
            builder.AppendLine($"| {severity} | {result.Findings.Count(f => f.Severity == severity)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Overall Result");
        builder.AppendLine();
        builder.AppendLine(result.Findings.Any(f => f.Severity == DiFindingSeverity.Error)
            ? "FAILED - blocking Dependency Injection issues found."
            : "PASSED - no blocking Dependency Injection issues found.");
        builder.AppendLine();
        builder.AppendLine("## Rule Coverage");
        builder.AppendLine();
        builder.AppendLine("| Rule | Name | Findings |");
        builder.AppendLine("|---|---|---:|");
        foreach (var group in result.Findings.GroupBy(f => (f.RuleId, f.RuleName)).OrderBy(g => g.Key.RuleId))
        {
            builder.AppendLine($"| {group.Key.RuleId} | {group.Key.RuleName} | {group.Count()} |");
        }

        builder.AppendLine();
    }

    private static void AppendFindings(StringBuilder builder, string title, IEnumerable<DiFinding> findings, string root)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        var any = false;
        foreach (var finding in findings.OrderBy(f => f.RuleId).ThenBy(f => f.Location?.FilePath))
        {
            any = true;
            builder.AppendLine($"### {finding.RuleId} - {finding.RuleName}");
            builder.AppendLine();
            builder.AppendLine($"**Severity:** {finding.Severity}  ");
            builder.AppendLine($"**Confidence:** {finding.Confidence:0.00}  ");
            if (finding.Location is not null)
            {
                builder.AppendLine($"**File:** `{finding.Location.ToDisplayPath(root)}`  ");
            }

            builder.AppendLine();
            builder.AppendLine(finding.Message);
            builder.AppendLine();
            builder.AppendLine("**Recommendation**");
            builder.AppendLine();
            builder.AppendLine(finding.Recommendation);
            builder.AppendLine();
        }

        if (!any)
        {
            builder.AppendLine("None.");
            builder.AppendLine();
        }
    }

    private static void AppendRegistrations(StringBuilder builder, DiAuditResult result)
    {
        builder.AppendLine("## Service Registrations");
        builder.AppendLine();
        builder.AppendLine("| Lifetime | Service | Implementation | Method | Notes |");
        builder.AppendLine("|---|---|---|---|---|");
        foreach (var registration in result.Registrations.OrderBy(r => r.ServiceType.DisplayName))
        {
            var notes = registration.Key is not null ? $"key: {registration.Key}" : registration.IsGraphNode ? "" : "metadata/partial";
            builder.AppendLine($"| {registration.Lifetime} | `{registration.ServiceType.DisplayName}` | `{registration.ImplementationType?.DisplayName ?? ""}` | {registration.RegistrationMethod} | {notes} |");
        }

        builder.AppendLine();
    }

    private static void AppendUnresolved(StringBuilder builder, DiAuditResult result, string root)
    {
        builder.AppendLine("## Unresolved Dependencies");
        builder.AppendLine();
        if (result.Graph.UnresolvedDependencies.Count == 0)
        {
            builder.AppendLine("None.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Type | Dependency | Location |");
        builder.AppendLine("|---|---|---|");
        foreach (var dependency in result.Graph.UnresolvedDependencies)
        {
            builder.AppendLine($"| `{dependency.DeclaringType.DisplayName}` | `{dependency.ParameterType.DisplayName}` | `{dependency.Location?.ToDisplayPath(root) ?? ""}` |");
        }

        builder.AppendLine();
    }

    private static void AppendDiagnostics(StringBuilder builder, string title, IReadOnlyList<string> diagnostics)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (diagnostics.Count == 0)
        {
            builder.AppendLine("None.");
        }
        else
        {
            foreach (var diagnostic in diagnostics)
            {
                builder.AppendLine($"- {diagnostic}");
            }
        }

        builder.AppendLine();
    }

    private static void AppendSkipped(StringBuilder builder, DiAuditResult result)
    {
        builder.AppendLine("## Ambiguous or Skipped Analysis");
        builder.AppendLine();
        builder.AppendLine("| Area | Reason | Impact |");
        builder.AppendLine("|---|---|---|");
        if (result.Registrations.Any(r => r.Key is not null))
        {
            builder.AppendLine("| Keyed services | Key relationship may not be statically obvious | Excluded from blocking graph rules unless obvious |");
        }

        if (result.Registrations.Any(r => r.IsFactoryRegistration && r.ImplementationType is null))
        {
            builder.AppendLine("| Factory registration | Implementation could not be inferred | Reported as partial support |");
        }

        builder.AppendLine();
    }

    private static void AppendFixOrder(StringBuilder builder)
    {
        builder.AppendLine("## Suggested Fix Order");
        builder.AppendLine();
        builder.AppendLine("1. Fix DI007 singleton-to-scoped dependencies.");
        builder.AppendLine("2. Fix DI011 circular dependencies.");
        builder.AppendLine("3. Review DI010 service locator usage.");
        builder.AppendLine("4. Replace broad IConfiguration injection with strongly typed options.");
        builder.AppendLine("5. Review remaining design warnings.");
    }
}
