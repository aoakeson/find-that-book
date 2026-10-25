using System.Security.Cryptography;
using System.Text;

namespace FindThatBook.Eval.Hosting;

/// <summary>
/// A git-free fingerprint of the code that produced a result: SHA-256 over (relative path, content) of every source
/// and config file, sorted by path. Two summaries with the same fingerprint were produced by the same code.
/// </summary>
public static class CodeFingerprint
{
    private static readonly string[] Extensions = [".cs", ".csproj", ".json", ".props", ".targets"];

    public sealed record Print(string Sha256, int Files)
    {
        public override string ToString() => $"sha256:{Sha256} ({Files} files)";
    }

    private static readonly Lazy<Print> Source = new(ComputeSource);
    private static readonly Lazy<Print> Tool = new(() => Of(Files(Path.Combine(RepoPaths.Root, "tools", "FindThatBook.Eval"))));

    /// <summary>Computes both prints now; Program calls it first, so a run is stamped with the code it started with.</summary>
    public static void Capture()
    {
        _ = Source.Value;
        _ = Tool.Value;
        _ = Binaries.Value;
    }

    private static readonly Lazy<Print> Binaries = new(() =>
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = new[] { typeof(FindThatBook.Core.Search.SearchService).Assembly, typeof(FindThatBook.Infrastructure.DependencyInjection).Assembly }
            .Select(a => a.Location).Where(File.Exists).ToList();
        foreach (var f in files) hash.AppendData(File.ReadAllBytes(f));
        return new Print(Convert.ToHexStringLower(hash.GetHashAndReset())[..16], files.Count);
    });

    /// <summary>
    /// The pipeline assemblies this process actually loaded (Core + Infrastructure). Two runs with the same binary print
    /// ran the same pipeline code even if the source tree changed in between (the src print is read from disk at start).
    /// </summary>
    public static Print OfBinaries() => Binaries.Value;

    /// <summary>The system under test: src/ plus the root build files (as captured at process start).</summary>
    public static Print OfSource() => Source.Value;

    /// <summary>The measuring instrument: tools/FindThatBook.Eval (as captured at process start).</summary>
    public static Print OfEvalTool() => Tool.Value;

    private static Print ComputeSource() => Of(
        Files(Path.Combine(RepoPaths.Root, "src"))
            .Concat(new[] { "Directory.Build.props", "Directory.Packages.props", "global.json" }
                .Select(f => Path.Combine(RepoPaths.Root, f)).Where(File.Exists)));


    private static IEnumerable<string> Files(string folder) =>
        !Directory.Exists(folder) ? [] : Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !IsBuildOutput(f));

    private static bool IsBuildOutput(string file)
    {
        var parts = Path.GetRelativePath(RepoPaths.Root, file).Split(Path.DirectorySeparatorChar);
        return parts.Any(p => p is "bin" or "obj" or "artifacts" or "node_modules" or "wwwroot")
               || Path.GetFileName(file).EndsWith(".local.json", StringComparison.OrdinalIgnoreCase);
    }

    private static Print Of(IEnumerable<string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var list = files.Select(f => (Rel: RepoPaths.Relative(f), Full: f)).OrderBy(f => f.Rel, StringComparer.Ordinal).ToList();
        foreach (var (rel, full) in list)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(rel + "\n"));
            hash.AppendData(File.ReadAllBytes(full));
            hash.AppendData("\n"u8);
        }
        return new Print(Convert.ToHexStringLower(hash.GetHashAndReset())[..16], list.Count);
    }
}
