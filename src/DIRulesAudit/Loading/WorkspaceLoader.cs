using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace A2G.DIRulesAudit.Loading;

public sealed record ProjectLoadResult(
    IReadOnlyList<Project> Projects,
    IReadOnlyList<string> Diagnostics,
    bool HasCriticalErrors);

public sealed class WorkspaceLoader
{
    public async Task<ProjectLoadResult> LoadAsync(InputResolution resolution, string? targetFramework, CancellationToken cancellationToken)
    {
        RegisterMSBuild();

        using var workspace = MSBuildWorkspace.Create();
        var diagnostics = new List<string>();
        workspace.RegisterWorkspaceFailedHandler(e => diagnostics.Add($"{e.Diagnostic.Kind}: {e.Diagnostic.Message}"));

        try
        {
            Solution solution;
            if (resolution.Kind is InputKind.Solution or InputKind.SolutionX)
            {
                solution = await workspace.OpenSolutionAsync(resolution.SelectedPath, cancellationToken: cancellationToken);
            }
            else
            {
                var project = await workspace.OpenProjectAsync(resolution.SelectedPath, cancellationToken: cancellationToken);
                solution = project.Solution;
            }

            var projects = SelectProjects(solution.Projects
                .Where(p => string.Equals(p.Language, LanguageNames.CSharp, StringComparison.Ordinal))
                .ToArray(), targetFramework, diagnostics);

            if (projects.Length == 0)
            {
                diagnostics.Add("No C# projects were loaded.");
                return new ProjectLoadResult(projects, diagnostics, true);
            }

            return new ProjectLoadResult(projects, diagnostics, false);
        }
        catch (Exception ex)
        {
            diagnostics.Add(ex.Message);
            return new ProjectLoadResult(Array.Empty<Project>(), diagnostics, true);
        }
    }

    private static Project[] SelectProjects(Project[] projects, string? targetFramework, List<string> diagnostics)
    {
        var selected = new List<Project>();
        foreach (var group in projects.GroupBy(ProjectFileKey))
        {
            var candidates = group.ToArray();
            if (candidates.Length == 1)
            {
                selected.Add(candidates[0]);
                continue;
            }

            var selectedProject = SelectProjectTargetFramework(candidates, targetFramework, diagnostics);
            selected.Add(selectedProject);

            foreach (var skipped in candidates.Where(p => p.Id != selectedProject.Id))
            {
                diagnostics.Add($"Skipped duplicate target framework '{GetTargetFramework(skipped) ?? "unknown"}' for project '{skipped.FilePath ?? skipped.Name}'; selected '{GetTargetFramework(selectedProject) ?? "unknown"}'.");
            }
        }

        return selected.ToArray();
    }

    private static Project SelectProjectTargetFramework(Project[] candidates, string? targetFramework, List<string> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            var requested = candidates.FirstOrDefault(p => string.Equals(GetTargetFramework(p), targetFramework, StringComparison.OrdinalIgnoreCase));
            if (requested is not null)
            {
                return requested;
            }

            diagnostics.Add($"Requested target framework '{targetFramework}' was not loaded for project '{candidates[0].FilePath ?? candidates[0].Name}'; selected '{GetTargetFramework(candidates[0]) ?? "unknown"}' instead.");
        }

        return candidates[0];
    }

    private static string ProjectFileKey(Project project)
    {
        return project.FilePath is not null
            ? Path.GetFullPath(project.FilePath).ToUpperInvariant()
            : project.Id.Id.ToString("N");
    }

    private static string? GetTargetFramework(Project project)
    {
        var fromName = GetTargetFrameworkFromProjectName(project.Name);
        if (fromName is not null)
        {
            return fromName;
        }

        var symbols = project.ParseOptions?.PreprocessorSymbolNames ?? [];
        foreach (var symbol in symbols)
        {
            if (symbol.StartsWith("NETSTANDARD", StringComparison.Ordinal))
            {
                return FormatTargetFramework("netstandard", symbol["NETSTANDARD".Length..]);
            }

            if (symbol.StartsWith("NETFRAMEWORK", StringComparison.Ordinal))
            {
                return FormatTargetFramework("net", symbol["NETFRAMEWORK".Length..]);
            }

            if (symbol.StartsWith("NET", StringComparison.Ordinal))
            {
                return FormatTargetFramework("net", symbol["NET".Length..]);
            }
        }

        return null;
    }

    private static string? GetTargetFrameworkFromProjectName(string projectName)
    {
        var close = projectName.LastIndexOf(')');
        if (close != projectName.Length - 1)
        {
            return null;
        }

        var open = projectName.LastIndexOf('(');
        if (open < 0 || open >= close - 1)
        {
            return null;
        }

        var value = projectName[(open + 1)..close];
        return value.StartsWith("net", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    private static string? FormatTargetFramework(string prefix, string version)
    {
        var parts = version.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => !int.TryParse(p, out _)))
        {
            return null;
        }

        return $"{prefix}{string.Join('.', parts)}";
    }

    private static void RegisterMSBuild()
    {
        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        var instances = MSBuildLocator.QueryVisualStudioInstances().OrderByDescending(i => i.Version).ToArray();
        if (instances.Length > 0)
        {
            MSBuildLocator.RegisterInstance(instances[0]);
            return;
        }

        MSBuildLocator.RegisterDefaults();
    }
}
