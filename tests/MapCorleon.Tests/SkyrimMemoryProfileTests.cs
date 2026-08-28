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

    [Fact]
    public void CalculatesPlayerCharacterSingletonAddressFromModuleBase()
    {
        var profile = SkyrimWorldMapMemoryProfile.AnniversaryEdition1170;

        var address = profile.GetPlayerCharacterSingletonAddress(nint.CreateChecked(0x140000000L));

        Assert.Equal(nint.CreateChecked(0x1431874F8L), address);
    }

    [Theory]
    [InlineData("1.6.1170", true)]
    [InlineData("1.6.1170.0", true)]
    [InlineData("1.6.640.0", false)]
    public void MatchesOnlySupportedVersion(string version, bool expected) =>
        Assert.Equal(expected, SkyrimWorldMapMemoryProfile.AnniversaryEdition1170.SupportsVersion(version));

    [Theory]
    [InlineData(-27335f, -103523.39f, 0.98553925728996, -1.1287987846142)]
    [InlineData(17673.97f, -10506.01f, 1.1119435693444, -1.0692937596266)]
    [InlineData(-131156.44f, 39191.19f, 1.177501221163, -1.2711489248576)]
    [InlineData(-120485.66f, 129753.97f, 1.2991702874051, -1.2550219713245)]
    [InlineData(121661.84f, 121235.88f, 1.2889470432032, -0.9323139056396)]
    [InlineData(168459.05f, 37979.82f, 1.1761434297543, -0.86453751318368)]
    [InlineData(172548.20f, -91071.42f, 1.0039602447541, -0.86185505251615)]
    [InlineData(136141.20f, -108352.70f, 0.97792856335013, -0.9116240865111)]
    [InlineData(70776.28f, -4056.19f, 1.1244554493068, -0.99324868216308)]
    public void KeepsDistributedCalibrationSamplesNearTheirMapMarkers(
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
        var error = Math.Sqrt(
            Math.Pow(expectedLatitude - location.Latitude, 2) +
            Math.Pow(expectedLongitude - location.Longitude, 2));
        Assert.InRange(error, 0, 0.007);
    }
}
