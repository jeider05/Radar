using Radar.Infrastructure.Persistence;

namespace Radar.Infrastructure.Tests;

// Checks the production location only; it never opens or modifies the real database.
public sealed class RadarDatabaseTests
{
    private static readonly string ExpectedProductionPath = Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "Radar", "radar.db");

    [Fact]
    public void DefaultPath_points_to_LocalAppData_Radar_radar_db()
    {
        Assert.Equal(ExpectedProductionPath, RadarDatabase.DefaultPath, ignoreCase: true);
    }

    [Fact]
    public void CreateDefault_uses_DefaultPath()
    {
        Assert.Equal(ExpectedProductionPath, RadarDatabase.CreateDefault().DatabasePath, ignoreCase: true);
    }
}
