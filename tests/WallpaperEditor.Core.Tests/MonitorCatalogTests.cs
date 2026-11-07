using WallpaperEditor.Core.Monitors;

namespace WallpaperEditor.Core.Tests;

public class MonitorCatalogTests
{
    private const string Guid = "{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private static readonly MonitorId Ultrawide = new($@"\\?\DISPLAY#VIE2900#7&2f1c3d63&1&UID260#{Guid}");
    private static readonly MonitorId Vertical = new($@"\\?\DISPLAY#STD2025#7&2f1c3d63&1&UID256#{Guid}");
    private static readonly MonitorId FullHd = new($@"\\?\DISPLAY#SAM093E#7&2f1c3d63&1&UID268#{Guid}");
    private static readonly MonitorId LegacyGhost = new("legacy_monitor_001");
    private static readonly MonitorId DisconnectedGhost = new($@"\\?\DISPLAY#STD2025#1&8713bca&0&UID0#{Guid}");

    // The enumeration measured on the reference machine, in the order the OS returned it.
    private static readonly MonitorCandidate[] Measured =
    [
        new(Ultrawide, new PixelRect(0, 0, 2560, 1080)),
        new(Vertical, new PixelRect(2560, -452, 3640, 1468)),
        new(FullHd, new PixelRect(-1920, 0, 0, 1080)),
        new(LegacyGhost, null),
        new(DisconnectedGhost, null),
    ];

    private static readonly Dictionary<string, string> NoNames = [];

    [Fact]
    public void Ghost_entries_without_a_rectangle_are_removed()
    {
        var monitors = MonitorCatalog.Build(Measured, NoNames);

        Assert.Equal(3, monitors.Count);
        Assert.DoesNotContain(monitors, m => m.Id == LegacyGhost);
        Assert.DoesNotContain(monitors, m => m.Id == DisconnectedGhost);
    }

    [Fact]
    public void Entries_with_an_empty_rectangle_are_removed()
    {
        var monitors = MonitorCatalog.Build([new(Ultrawide, new PixelRect(0, 0, 0, 0))], NoNames);

        Assert.Empty(monitors);
    }

    [Fact]
    public void Monitors_are_ordered_by_position_not_by_enumeration()
    {
        var monitors = MonitorCatalog.Build(Measured, NoNames);

        Assert.Equal([FullHd, Ultrawide, Vertical], monitors.Select(m => m.Id));
    }

    [Fact]
    public void Only_the_monitor_taller_than_wide_is_portrait()
    {
        var portrait = Assert.Single(MonitorCatalog.Build(Measured, NoNames), m => m.IsPortrait);

        Assert.Equal(Vertical, portrait.Id);
        Assert.Equal(1080, portrait.Bounds.Width);
        Assert.Equal(1920, portrait.Bounds.Height);
    }

    [Fact]
    public void Only_the_monitor_at_the_origin_is_primary()
    {
        var primary = Assert.Single(MonitorCatalog.Build(Measured, NoNames), m => m.IsPrimary);

        Assert.Equal(Ultrawide, primary.Id);
    }

    [Fact]
    public void Friendly_names_are_matched_ignoring_case()
    {
        var names = new Dictionary<string, string> { [Ultrawide.Value.ToUpperInvariant()] = "Meu ultrawide" };

        var monitor = Assert.Single(MonitorCatalog.Build([Measured[0]], names));

        Assert.Equal("Meu ultrawide", monitor.Name);
    }

    [Fact]
    public void A_monitor_without_a_friendly_name_falls_back_to_its_model_code()
    {
        var monitor = Assert.Single(MonitorCatalog.Build([Measured[2]], NoNames));

        Assert.Equal("SAM093E", monitor.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_monitor_id_cannot_exist(string value)
    {
        // Passing no id to SetWallpaper would change every monitor.
        Assert.Throws<ArgumentException>(() => new MonitorId(value));
    }

    [Fact]
    public void A_null_monitor_id_cannot_exist()
    {
        Assert.Throws<ArgumentNullException>(() => new MonitorId(null!));
    }
}
