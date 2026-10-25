namespace FindThatBook.Eval.Cli;

/// <summary>Minimal "--name value" / "--flag" parser: no package, no reflection. Unknown options are errors.</summary>
public sealed class Args
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);

    public string Command { get; }
    public IReadOnlyList<string> Positional { get; }

    private Args(string command, List<string> positional)
    {
        Command = command;
        Positional = positional;
    }

    /// <param name="flags">Options that take no value.</param>
    public static Args Parse(IReadOnlyList<string> argv, IReadOnlySet<string> flags)
    {
        var hasCommand = argv.Count > 0 && !argv[0].StartsWith("--", StringComparison.Ordinal);
        var positional = new List<string>();
        var args = new Args(hasCommand ? argv[0] : "help", positional);
        for (var i = hasCommand ? 1 : 0; i < argv.Count; i++)
        {
            var a = argv[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) { positional.Add(a); continue; }
            var name = a[2..];
            string? inline = null;
            if (name.IndexOf('=') is var eq and > 0) { inline = name[(eq + 1)..]; name = name[..eq]; }
            if (flags.Contains(name)) { args._flags.Add(name); continue; }
            var value = inline ?? (i + 1 < argv.Count ? argv[++i] : throw new ArgumentException($"--{name} needs a value"));
            if (!args._values.TryGetValue(name, out var list)) args._values[name] = list = [];
            list.Add(value);
        }
        return args;
    }

    public bool Flag(string name)
    {
        _read.Add(name);
        return _flags.Contains(name);
    }

    public string? Value(string name)
    {
        _read.Add(name);
        return _values.TryGetValue(name, out var v) ? v[^1] : null;
    }

    public string Value(string name, string fallback) => Value(name) ?? fallback;

    public IReadOnlyList<string> Values(string name)
    {
        _read.Add(name);
        return _values.TryGetValue(name, out var v) ? v : [];
    }

    public int? Int(string name)
    {
        var v = Value(name);
        if (v is null) return null;
        return int.TryParse(v, out var n) && n >= 0 ? n : throw new ArgumentException($"--{name} must be a non-negative integer, got '{v}'");
    }

    /// <summary>A count option that also accepts "unlimited".</summary>
    public int? Budget(string name, int? fallback)
    {
        var v = Value(name);
        if (v is null) return fallback;
        if (v.Equals("unlimited", StringComparison.OrdinalIgnoreCase)) return null;
        return int.TryParse(v, out var n) && n >= 0 ? n : throw new ArgumentException($"--{name} must be a number or 'unlimited', got '{v}'");
    }

    public bool OnOff(string name, bool fallback) => Value(name) switch
    {
        null => fallback,
        "on" or "true" or "yes" => true,
        "off" or "false" or "no" => false,
        var v => throw new ArgumentException($"--{name} must be on or off, got '{v}'"),
    };

    /// <summary>Fails on options nobody read (typos), after the command has read everything it uses.</summary>
    public void RejectUnknown()
    {
        var unknown = _values.Keys.Concat(_flags).Where(k => !_read.Contains(k)).ToList();
        if (unknown.Count > 0) throw new ArgumentException($"Unknown option(s) for '{Command}': {string.Join(", ", unknown.Select(u => "--" + u))}");
    }
}
