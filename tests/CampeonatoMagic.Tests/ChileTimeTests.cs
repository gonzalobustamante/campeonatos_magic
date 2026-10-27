using CampeonatoMagic.Services;

namespace CampeonatoMagic.Tests;

public class ChileTimeTests
{
    [Theory]
    [InlineData(1, 9)]
    [InlineData(7, 8)]
    public void ConvertsUtcToSantiagoWithSeasonalOffset(int month, int expectedHour)
    {
        var local = ChileTime.FromUtc(new DateTime(2026, month, 15, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, month, 15, expectedHour, 0, 0), local);
        Assert.Equal(DateTimeKind.Unspecified, local.Kind);
    }
}
