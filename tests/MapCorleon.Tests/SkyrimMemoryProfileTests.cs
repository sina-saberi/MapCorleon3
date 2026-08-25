using MapCorleon.Contracts;
using MapCorleon.Games.Skyrim;

namespace MapCorleon.Tests;

public sealed class SkyrimMemoryProfileTests
{
    [Fact]
    public void CalculatesPositionAddressesFromModuleBase()
    {
        var profile = SkyrimWorldMapMemoryProfile.AnniversaryEdition1170;
        var addresses = profile.GetPositionAddresses(nint.CreateChecked(0x140000000L));

        Assert.Equal(nint.CreateChecked(0x14338C8C0L), addresses.X);
        Assert.Equal(nint.CreateChecked(0x14338C8C4L), addresses.Y);
        Assert.Equal(nint.CreateChecked(0x14338C8C8L), addresses.Z);
    }

    [Theory]
    [InlineData("1.6.1170", true)]
    [InlineData("1.6.1170.0", true)]
    [InlineData("1.6.640.0", false)]
    public void MatchesOnlySupportedVersion(string version, bool expected) =>
        Assert.Equal(expected, SkyrimWorldMapMemoryProfile.AnniversaryEdition1170.SupportsVersion(version));

    [Theory]
    [InlineData(23259.77f, -44476.16f, 1.06624915463489, -1.06182578912831)]
    [InlineData(12193.16f, -43371.68f, 1.06767664962872, -1.07704338849510)]
    [InlineData(111047.51f, 101237.60f, 1.27540518916250, -0.93717318313884)]
    public void ConvertsSuppliedCalibrationAnchors(
        float gameX,
        float gameY,
        double expectedLatitude,
        double expectedLongitude)
    {
        var converted = SkyrimWorldMapMemoryProfile.AnniversaryEdition1170.Calibration.TryConvert(
            new GamePosition(gameX, gameY, 0),
            out var location);

        Assert.True(converted);
        Assert.NotNull(location);
        Assert.InRange(Math.Abs(expectedLatitude - location.Latitude), 0, 1e-7);
        Assert.InRange(Math.Abs(expectedLongitude - location.Longitude), 0, 1e-7);
    }
}
