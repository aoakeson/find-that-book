using System.Reflection;

namespace FindThatBook.Eval.Hosting;

/// <summary>Finds the repository root (the folder holding FindThatBook.slnx) and the well-known paths under it.</summary>
public static class RepoPaths
{
    private const string Marker = "FindThatBook.slnx";

    public static string Root { get; } = FindRoot();

    public static string Eval => Path.Combine(Root, "eval");
    public static string Results => Path.Combine(Root, "eval", "results");
    public static string GoldenFixtures => Path.Combine(Root, "tests", "fixtures", "golden");
    public static string ApiProject => Path.Combine(Root, "src", "FindThatBook.Api");

    /// <summary>
    /// Optional: the research archive (not part of this repo): recorded Open Library responses of the prototypes, the
    /// battle-test rounds and their interpretation caches. Set by --research &lt;dir&gt; or FTB_RESEARCH; defaults to a
    /// "research" folder next to the repo. Everything golden/hard/topic/synthetic/held-out needs is in the repo itself.
    /// </summary>
    public static string Research
    {
        get
        {
            if (ResearchOverride is { } chosen) return chosen;
            if (Environment.GetEnvironmentVariable("FTB_RESEARCH") is { Length: > 0 } fromEnv) return Path.GetFullPath(fromEnv);
            return Path.GetFullPath(Path.Combine(Root, "..", "research"));
        }
    }

    public static string? ResearchOverride { get; set; }

    public static bool HasResearch => Directory.Exists(Research);

    /// <summary>The recorded live responses the research prototypes made (format {"url","dt","body"}), newest first.</summary>
    public static IReadOnlyList<string> ResearchCaches =>
    [
        Path.Combine(Research, "12-pipeline-v3", "live_cache"),
        Path.Combine(Research, "11-battle-test", "live_cache"),
        Path.Combine(Research, "07-fix-misses", "cache"),
        Path.Combine(Research, "05-eval-proto", "cache"),
    ];

    /// <summary>A path for reports: repo-relative, "research archive/…" for the optional archive, else its name (no machine paths).</summary>
    public static string Relative(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(Root, StringComparison.Ordinal)) return Path.GetRelativePath(Root, full).Replace('\\', '/');
        if (full.StartsWith(Research, StringComparison.Ordinal)) return "research archive/" + Path.GetRelativePath(Research, full).Replace('\\', '/');
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
    }

    private static string FindRoot()
    {
        if (Environment.GetEnvironmentVariable("FTB_REPO_ROOT") is { Length: > 0 } fromEnv && File.Exists(Path.Combine(fromEnv, Marker)))
            return Path.GetFullPath(fromEnv);
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, Marker))) return dir.FullName;
        }
        // Builds into an artifacts folder outside the repo (--artifacts-path) still know where they came from.
        var baked = typeof(RepoPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "FindThatBook.RepoRoot")?.Value;
        if (baked is not null && File.Exists(Path.Combine(baked, Marker))) return Path.GetFullPath(baked);
        throw new InvalidOperationException($"Cannot find the repository root ({Marker}); set FTB_REPO_ROOT.");
    }
}
