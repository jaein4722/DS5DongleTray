using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DS5DongleTray;

internal static class BatteryIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Create(DongleSnapshot snapshot)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var outline = snapshot.DeviceFound ? Color.FromArgb(36, 42, 54) : Color.FromArgb(130, 130, 130);
        using var outlinePen = new Pen(outline, 2.5f);
        using var capBrush = new SolidBrush(outline);

        var body = new Rectangle(3, 8, 23, 16);
        graphics.DrawRoundedRectangle(outlinePen, body, 3);
        graphics.FillRectangle(capBrush, 26, 13, 3, 6);

        if (!snapshot.DeviceFound || snapshot.BatteryUnsupported || snapshot.Battery is not { HasKnownLevel: true } battery)
        {
            using var slashPen = new Pen(Color.FromArgb(190, 70, 70), 3f);
            graphics.DrawLine(slashPen, 7, 25, 25, 7);
            return ToIcon(bitmap);
        }

        var fillWidth = Math.Max(2, (int)Math.Round(19 * (battery.Percent / 100.0)));
        var fillColor = battery.Percent <= 20
            ? Color.FromArgb(210, 67, 67)
            : battery.IsCharging
                ? Color.FromArgb(67, 157, 94)
                : Color.FromArgb(55, 123, 210);

        using (var fillBrush = new SolidBrush(fillColor))
        {
            graphics.FillRectangle(fillBrush, 6, 11, fillWidth, 10);
        }

        if (battery.IsCharging)
        {
            Point[] bolt =
            [
                new(16, 6),
                new(10, 17),
                new(16, 17),
                new(13, 27),
                new(23, 14),
                new(17, 14)
            ];
            using var boltBrush = new SolidBrush(Color.FromArgb(255, 213, 74));
            graphics.FillPolygon(boltBrush, bolt);
        }

        return ToIcon(bitmap);
    }

    private static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}

internal static class GraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.DrawPath(pen, path);
    }
}
