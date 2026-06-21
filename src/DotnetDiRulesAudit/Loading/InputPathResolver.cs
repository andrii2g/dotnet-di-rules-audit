namespace DotnetDiRulesAudit.Loading;

public enum InputKind
{
    Solution,
    SolutionX,
    Project
}

public sealed record InputResolution(
    string OriginalPath,
    string SelectedPath,
    InputKind Kind,
    IReadOnlyList<string> Diagnostics);

public sealed class InputPathResolver
{
    public InputResolution Resolve(string inputPath)
    {
        var fullPath = Path.GetFullPath(inputPath);
        if (File.Exists(fullPath))
        {
            return ResolveFile(inputPath, fullPath);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new InputResolutionException($"Input path does not exist: {inputPath}", Array.Empty<string>());
        }

        return ResolveDirectory(inputPath, fullPath);
    }

    private static InputResolution ResolveFile(string originalPath, string fullPath)
    {
        var extension = Path.GetExtension(fullPath);
        return extension.ToLowerInvariant() switch
        {
            ".sln" => new InputResolution(originalPath, fullPath, InputKind.Solution, Array.Empty<string>()),
            ".slnx" => new InputResolution(originalPath, fullPath, InputKind.SolutionX, Array.Empty<string>()),
            ".csproj" => new InputResolution(originalPath, fullPath, InputKind.Project, Array.Empty<string>()),
            _ => throw new InputResolutionException($"Unsupported input file type: {extension}", Array.Empty<string>())
        };
    }

    private static InputResolution ResolveDirectory(string originalPath, string fullPath)
    {
        var rootSolution = GetRootCandidates(fullPath, "*.sln");
        if (rootSolution.Count == 1)
        {
            return Selected(originalPath, rootSolution[0], InputKind.Solution, "Selected root .sln.");
        }

        var rootSolutionX = GetRootCandidates(fullPath, "*.slnx");
        if (rootSolutionX.Count == 1)
        {
            return Selected(originalPath, rootSolutionX[0], InputKind.SolutionX, "Selected root .slnx.");
        }

        var rootProject = GetRootCandidates(fullPath, "*.csproj");
        if (rootProject.Count == 1)
        {
            return Selected(originalPath, rootProject[0], InputKind.Project, "Selected root .csproj.");
        }

        var recursive = FindRecursive(fullPath, "*.sln");
        if (recursive.Count > 0)
        {
            return SelectRecursive(originalPath, recursive, InputKind.Solution);
        }

        recursive = FindRecursive(fullPath, "*.slnx");
        if (recursive.Count > 0)
        {
            return SelectRecursive(originalPath, recursive, InputKind.SolutionX);
        }

        recursive = FindRecursive(fullPath, "*.csproj");
        if (recursive.Count > 0)
        {
            return SelectRecursive(originalPath, recursive, InputKind.Project);
        }

        throw new InputResolutionException($"No .sln, .slnx, or .csproj file found under: {originalPath}", Array.Empty<string>());
    }

    private static InputResolution Selected(string originalPath, string selectedPath, InputKind kind, string diagnostic)
    {
        return new InputResolution(originalPath, selectedPath, kind, new[] { diagnostic, $"Input selected: {selectedPath}" });
    }

    private static InputResolution SelectRecursive(string originalPath, IReadOnlyList<string> candidates, InputKind kind)
    {
        var ordered = candidates.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        var shortestLength = ordered[0].Length;
        var shortest = ordered.Where(p => p.Length == shortestLength).ToArray();
        if (shortest.Length == 1)
        {
            return Selected(originalPath, shortest[0], kind, "Selected uniquely shortest recursive candidate.");
        }

        throw new InputResolutionException(
            "Ambiguous input directory. Multiple recursive candidates have the same shortest path length.",
            ordered);
    }

    private static List<string> GetRootCandidates(string directory, string pattern)
    {
        return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> FindRecursive(string directory, string pattern)
    {
        return Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class InputResolutionException : Exception
{
    public InputResolutionException(string message, IReadOnlyList<string> candidates)
        : base(message)
    {
        Candidates = candidates;
    }

    public IReadOnlyList<string> Candidates { get; }
}
