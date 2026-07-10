namespace WinRaycastEditor.Core;

public readonly record struct MapCellFields(
    byte SolidWallTexture,
    byte CeilingTexture,
    byte FloorTexture,
    byte TransparentWallTexture,
    byte UpperWallTexture)
{
    public static MapCellFields Decode(ulong packedCell)
    {
        return new MapCellFields(
            SolidWallTexture: (byte)(packedCell & 0xff),
            CeilingTexture: (byte)((packedCell >> 8) & 0xff),
            FloorTexture: (byte)((packedCell >> 16) & 0xff),
            TransparentWallTexture: (byte)((packedCell >> 24) & 0xff),
            UpperWallTexture: (byte)((packedCell >> 32) & 0xff));
    }

    public ulong Encode()
    {
        return SolidWallTexture
            | ((ulong)CeilingTexture << 8)
            | ((ulong)FloorTexture << 16)
            | ((ulong)TransparentWallTexture << 24)
            | ((ulong)UpperWallTexture << 32);
    }

    public bool HasSolidWall => SolidWallTexture != 0;
    public bool HasUpperWall => UpperWallTexture != 0;
    public bool HasTransparentWall => TransparentWallTexture != 0;
    public bool IsEmptySpace => !HasSolidWall && !HasUpperWall && !HasTransparentWall;
}
