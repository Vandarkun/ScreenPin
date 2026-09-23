namespace ScreenPin.Core;

/// <summary>物理像素坐标下的截图区域（整个虚拟桌面全局坐标，可能为负）。</summary>
public readonly record struct CaptureRegion(int X, int Y, int Width, int Height)
{
    /// <summary>取全部轨迹点的外接矩形（minX/minY/maxX/maxY，边界点也占一像素）。</summary>
    public static CaptureRegion FromPoints(IReadOnlyList<GPoint> points)
    {
        if (points.Count == 0)
        {
            return new CaptureRegion(0, 0, 0, 0);
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new CaptureRegion(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
