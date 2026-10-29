using System.Globalization;
using Radar.Core.Contents;
using Radar.Core.Sources;

namespace Radar.Desktop;

/// <summary>
/// A selectable scope: "Novedades" (all sources, <see cref="SourceId"/> null) or one source, grouped as
/// Repositorios/Canales in the sidebar. Shared between <see cref="MainWindow"/> (which owns the sidebar) and
/// <see cref="NovedadesView"/> (which renders the list for whatever is currently selected).
/// </summary>
public sealed record SourceFilterInfo(long? SourceId, string Name, int Unread, string GroupName, bool IsPaused)
{
    public static readonly SourceFilterInfo Empty = new(null, "Novedades", 0, "", false);

    public bool IsAllFilter => SourceId is null;

    public string UnreadText => Unread > 0 ? Unread.ToString(CultureInfo.InvariantCulture) : "";

    public bool ShowUnreadDot => !IsAllFilter && Unread > 0;

    public string DisplayName => IsPaused ? $"{Name} · pausada" : Name;

    public static SourceFilterInfo All(IReadOnlyList<NewsItem> rows) =>
        new(null, "Novedades", rows.Count(r => !r.IsRead), GroupName: "", IsPaused: false);

    public static SourceFilterInfo For(Source source, IReadOnlyList<NewsItem> rows, string groupName)
    {
        var sourceRows = rows.Where(r => r.SourceId == source.Id).ToList();
        return new(source.Id, source.DisplayName, sourceRows.Count(r => !r.IsRead), groupName, IsPaused: !source.IsActive);
    }
}
