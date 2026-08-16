using NuRcade.Editor.Core;

namespace NuRcade.Editor.Tests;

[TestClass]
public sealed class MapCellFieldsTests
{
    [TestMethod]
    public void DecodeReadsPackedCellFields()
    {
        var fields = MapCellFields.Decode(0x0a00090c0eUL);

        Assert.AreEqual(0x0e, fields.SolidWallTexture);
        Assert.AreEqual(0x0c, fields.CeilingTexture);
        Assert.AreEqual(0x09, fields.FloorTexture);
        Assert.AreEqual(0x00, fields.TransparentWallTexture);
        Assert.AreEqual(0x0a, fields.UpperWallTexture);
    }

    [TestMethod]
    public void EncodeRoundTripsPackedCell()
    {
        const ulong packed = 0x0a00090c0eUL;

        Assert.AreEqual(packed, MapCellFields.Decode(packed).Encode());
    }
}
