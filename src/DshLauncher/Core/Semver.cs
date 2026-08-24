using System.Text.RegularExpressions;

namespace DshLauncher.Core;

/// <summary>极简语义版本（支持 1.2.3 与 1.2.3-rc.7 预发布）解析与比较。</summary>
public sealed class Semver : IComparable<Semver>
{
    private static readonly Regex Pattern = new(
        @"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$", RegexOptions.Compiled);

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }

    private Semver(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public static Semver? TryParse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Pattern.Match(s.Trim());
        if (!m.Success) return null;
        return new Semver(
            int.Parse(m.Groups[1].Value),
            int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value),
            m.Groups[4].Success ? m.Groups[4].Value : null);
    }

    /// <summary>从命令输出中提取第一个能解析为语义版本的行（抵御 npx/npm 混入 stderr 的 notice）。</summary>
    public static string? ExtractVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        foreach (var raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = raw.Trim();
            if (TryParse(t) is not null) return t;
        }
        return null;
    }

    public int CompareTo(Semver? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Prerelease is null && other.Prerelease is null) return 0;
        if (Prerelease is null) return 1;        // 正式版 > 预发布
        if (other.Prerelease is null) return -1;
        return string.CompareOrdinal(Prerelease, other.Prerelease);
    }

    public override string ToString()
        => Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
