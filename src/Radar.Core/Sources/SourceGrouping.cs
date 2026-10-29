namespace Radar.Core.Sources;

/// <summary>One group of sources of the same type, in the order they should be shown.</summary>
public sealed record SourceGroup(SourceType SourceType, IReadOnlyList<Source> Sources);

/// <summary>Groups sources by type for sidebar-style navigation.</summary>
public static class SourceGrouping
{
    /// <summary>
    /// Groups sources by type, each group keeping the sources' original relative order.
    /// Groups are returned in <see cref="SourceType"/> declaration order (GitHub, then YouTube); a type with no
    /// sources is omitted.
    /// </summary>
    public static IReadOnlyList<SourceGroup> GroupByType(IReadOnlyList<Source> sources) =>
        Enum.GetValues<SourceType>()
            .Select(type => new SourceGroup(type, sources.Where(s => s.SourceType == type).ToList()))
            .Where(group => group.Sources.Count > 0)
            .ToList();
}
