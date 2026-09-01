using System.Text.Json;
using System.Text.RegularExpressions;
using PublishTool.Extensions;

namespace PublishTool.GitHub;

public partial record ReleaseVersion : IComparable<ReleaseVersion>
{
    public long Id { get; }
    public bool IsPreRelease { get; }
    public string Name { get; }
    public string TagName { get; }

    public uint Major { get; }
    public uint Minor { get; }
    public uint Patch { get; }
    public uint? RcNumber { get; }

    public JsonElement? Json { get; }

    public static Result<ReleaseVersion> Parse(JsonElement element)
    {
        string tagName = element.GetProperty("tag_name").GetString() ?? string.Empty;
        string name = element.GetProperty("name").GetString() ?? string.Empty;
        long id = element.GetProperty("id").GetInt64();
        if (tagName.IsEmptyOrWhiteSpace || name.IsEmptyOrWhiteSpace || id < 1)
        {
            return "Failed to parse version";
        }

        try
        {
            return new ReleaseVersion(id, name, tagName, element);
        }
        catch (Exception e)
        {
            Logger.LogError(e.ToString());
            return "Failed to parse version";
        }
    }

    private ReleaseVersion(long id, string name, string tagName, JsonElement? json)
    {
        Id = id;
        Name = name;
        TagName = tagName;
        Json = json;

        var match = TagNameRegex().Match(tagName);
        if (!match.Success)
        {
            throw new FormatException($"'{tagName}' is not a valid release tag (expected vMAJOR.MINOR.PATCH[-rc.N])");
        }

        Major = uint.Parse(match.Groups["major"].ValueSpan);
        Minor = uint.Parse(match.Groups["minor"].ValueSpan);
        Patch = uint.Parse(match.Groups["patch"].ValueSpan);

        var rc = match.Groups["rc"];
        if (rc.Success)
        {
            RcNumber = uint.Parse(rc.ValueSpan);
            IsPreRelease = true;
        }
    }

    [GeneratedRegex(@"^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-rc\.(?<rc>\d+))?$")]
    private static partial Regex TagNameRegex();

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public int CompareTo(ReleaseVersion? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (other is null)
        {
            return 1;
        }

        int idComparison = Id.CompareTo(other.Id);
        if (idComparison == 0)
        {
            return 0;
        }

        int majorComparision = Major.CompareTo(other.Major);
        if (majorComparision != 0)
        {
            return majorComparision;
        }

        int minorComparision = Minor.CompareTo(other.Minor);
        if (minorComparision != 0)
        {
            return minorComparision;
        }

        int patchComparision = Patch.CompareTo(other.Patch);
        if (patchComparision != 0)
        {
            return patchComparision;
        }

        if (IsPreRelease && !other.IsPreRelease)
        {
            return 1;
        }

        if (!IsPreRelease && other.IsPreRelease)
        {
            return -1;
        }

        if (!IsPreRelease && !other.IsPreRelease)
        {
            return 0;
        }

        return RcNumber!.Value.CompareTo(other.RcNumber!.Value);
    }

    public override string ToString() => TagName;
}