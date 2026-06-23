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
    public async Task<ProjectLoadResult> LoadAsync(InputResolution resolution, CancellationToken cancellationToken)
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

            var projects = solution.Projects
                .Where(p => string.Equals(p.Language, LanguageNames.CSharp, StringComparison.Ordinal))
                .ToArray();

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
