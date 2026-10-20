using System.IO.Compression;
using System.Text.RegularExpressions;

namespace OrbitLauncher.Services;

/// <summary>Reads owned mod declarations separately from Forge/NeoForge dependency tables.</summary>
internal sealed record ForgeModManifest(IReadOnlyDictionary<string, string> Mods, IReadOnlyList<ForgeModManifest.Dependency> Dependencies)
{
    internal sealed record Dependency(string Id, string Range, bool Required, string Side);
    public static ForgeModManifest Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var mods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dependencies = new List<Dependency>();
        var entry = zip.GetEntry("META-INF/neoforge.mods.toml") ?? zip.GetEntry("META-INF/mods.toml");
        if (entry is null || entry.Length > 2_000_000) return new(mods, dependencies);
        var section = "";
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        void Flush()
        {
            if (!properties.TryGetValue("modId", out var id) || id.Length == 0) return;
            if (section == "mods") mods[id] = properties.GetValueOrDefault("version", "");
            if (section.StartsWith("dependencies.", StringComparison.Ordinal))
                dependencies.Add(new(id, properties.GetValueOrDefault("versionRange", ""),
                    properties.GetValueOrDefault("mandatory") == "true" || properties.GetValueOrDefault("type") == "required",
                    properties.GetValueOrDefault("side", "BOTH")));
        }
        using var reader = new StreamReader(entry.Open());
        string? multiline = null;
        while (reader.ReadLine() is { } line)
        {
            if (multiline is not null) { if (line.Contains(multiline, StringComparison.Ordinal)) multiline = null; continue; }
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) continue;
            foreach (var quote in new[] { "\"\"\"", "'''" })
            {
                var start = line.IndexOf(quote, StringComparison.Ordinal);
                if (start >= 0 && line.IndexOf(quote, start + 3, StringComparison.Ordinal) < 0) { multiline = quote; break; }
            }
            if (multiline is not null) continue;
            if (trimmed.StartsWith('['))
            {
                Flush(); properties.Clear();
                var header = Regex.Match(line, @"^\s*\[\[\s*(.*?)\s*\]\]\s*(?:#.*)?$");
                section = header.Success ? header.Groups[1].Value : "";
                continue;
            }
            var match = Regex.Match(line, "^\\s*([A-Za-z_][A-Za-z_0-9]*)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|(true|false))\\s*(?:#.*)?$");
            if (match.Success) properties[match.Groups[1].Value] = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
        }
        Flush();
        if (mods.Values.Any(version => version.Contains("${", StringComparison.Ordinal)) && zip.GetEntry("META-INF/MANIFEST.MF") is { Length: < 2_000_000 } manifest)
        {
            using var manifestReader = new StreamReader(manifest.Open());
            var version = Regex.Match(manifestReader.ReadToEnd(), @"(?m)^Implementation-Version:\s*([^\r\n]+)").Groups[1].Value.Trim();
            foreach (var id in mods.Keys.ToArray()) if (mods[id].Contains("${", StringComparison.Ordinal)) mods[id] = version;
        }
        return new(mods, dependencies);
    }

    public static bool Matches(string version, string range)
    {
        if (string.IsNullOrWhiteSpace(range) || range == "*") return true;
        var intervals = Regex.Matches(range, @"[\[(][^\])]*[\])]");
        if (intervals.Count == 0) return version.Equals(range, StringComparison.OrdinalIgnoreCase);
        foreach (Match interval in intervals)
        {
            var body = interval.Value[1..^1].Split(',');
            if (body.Length == 1) { if (Compare(version, body[0]) == 0) return true; continue; }
            var lower = body[0].Trim(); var upper = body[1].Trim();
            if ((lower.Length == 0 || Compare(version, lower) is var left && (left > 0 || left == 0 && interval.Value[0] == '['))
                && (upper.Length == 0 || Compare(version, upper) is var right && (right < 0 || right == 0 && interval.Value[^1] == ']'))) return true;
        }
        return false;
    }

    private static int Compare(string left, string right)
    {
        var a = Regex.Match(left, @"^\d+(?:\.\d+)*"); var b = Regex.Match(right, @"^\d+(?:\.\d+)*");
        if (!a.Success || !b.Success) return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        var x = a.Value.Split('.'); var y = b.Value.Split('.');
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var value = (i < x.Length && long.TryParse(x[i], out var n) ? n : 0).CompareTo(i < y.Length && long.TryParse(y[i], out var m) ? m : 0);
            if (value != 0) return value;
        }
        var suffixA = left[a.Length..].TrimStart('-', '.'); var suffixB = right[b.Length..].TrimStart('-', '.');
        if (suffixA.Length == 0 && suffixB.Length != 0) return 1;
        if (suffixB.Length == 0 && suffixA.Length != 0) return -1;
        return StringComparer.OrdinalIgnoreCase.Compare(suffixA, suffixB);
    }
}
