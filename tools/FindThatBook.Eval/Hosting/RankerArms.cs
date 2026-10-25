using System.Reflection;
using FindThatBook.Core.Matching;

namespace FindThatBook.Eval.Hosting;

/// <summary>
/// Ranker ablation arms over the internal <see cref="RankerOptions"/> (Core grants the eval InternalsVisibleTo).
/// The switches are discovered from RankerOptions' bool properties, so a rule added later is ablated without an eval
/// change. Reflection is confined to this measuring tool; production code only ever uses RankerOptions.Default.
/// </summary>
public static class RankerArms
{
    /// <summary>Every rule switch, in declaration order.</summary>
    public static IReadOnlyList<string> Switches { get; } =
    [
        .. typeof(RankerOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.CanWrite)
            .OrderBy(p => p.MetadataToken)
            .Select(p => p.Name),
    ];

    /// <summary>"default" or "baseline" (alias "v3": the measured prototype rules, every addition off), minus the switches in <paramref name="off"/>.</summary>
    internal static RankerOptions Build(string baseArm, IReadOnlyList<string> off)
    {
        var options = baseArm.ToLowerInvariant() switch
        {
            "default" => RankerOptions.Default,
            "v3" or "baseline" => RankerOptions.Baseline,
            _ => throw new ArgumentException($"--ranker must be default or baseline (alias v3), got '{baseArm}'"),
        };
        if (off.Count == 0) return options;
        var copy = options with { };
        foreach (var name in off)
        {
            var property = typeof(RankerOptions).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null || property.PropertyType != typeof(bool))
                throw new ArgumentException($"Unknown ranker switch '{name}'. Switches: {string.Join(", ", Switches)}");
            property.SetValue(copy, false);
        }
        return copy;
    }

    /// <summary>Throws <see cref="ArgumentException"/> for an unknown arm or switch name (checked before anything runs).</summary>
    public static void Validate(string baseArm, IReadOnlyList<string> off) => _ = Build(baseArm, off);

    /// <summary>The switches that are on in an arm (for the summary).</summary>
    public static IReadOnlyList<string> OnIn(string baseArm, IReadOnlyList<string> off)
    {
        var options = Build(baseArm, off);
        return [.. Switches.Where(s => (bool)typeof(RankerOptions).GetProperty(s)!.GetValue(options)!)];
    }

    public static string Describe(string baseArm, IReadOnlyList<string> off) =>
        off.Count == 0 ? baseArm.ToLowerInvariant() : $"{baseArm.ToLowerInvariant()} -{string.Join(" -", off)}";
}
