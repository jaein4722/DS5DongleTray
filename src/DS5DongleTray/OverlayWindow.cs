using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DS5DongleTray;

internal sealed class OverlayWindow : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private readonly System.Windows.Forms.Timer hideTimer;
    private string reason = "Battery";
    private string primaryText = "DS5Dongle";
    private string secondaryText = "Battery unknown";
    private int? percent;
    private bool charging;
    private bool lowBattery;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Size = new Size(320, 96);
        BackColor = Color.Black;
        Opacity = 0.92;
        DoubleBuffered = true;

        hideTimer = new System.Windows.Forms.Timer();
        hideTimer.Tick += (_, _) => HideOverlay();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExTransparent | WsExNoActivate;
            return cp;
        }
    }

    public void ShowSnapshot(DongleSnapshot snapshot, string showReason, int displaySeconds)
    {
        reason = showReason;
        percent = snapshot.Battery?.HasKnownLevel == true ? snapshot.Battery.Percent : null;
        charging = snapshot.Battery?.IsCharging == true || snapshot.Battery?.PowerState == 0x02;
        lowBattery = showReason.Contains("Low", StringComparison.OrdinalIgnoreCase);
        primaryText = percent.HasValue ? $"{percent.Value}%" : "Unknown";
        secondaryText = BuildSecondaryText(snapshot);

        PositionWindow();
        hideTimer.Stop();
        hideTimer.Interval = Math.Max(1, displaySeconds) * 1000;
        Invalidate();
        Show();
        hideTimer.Start();
    }

    public void HideOverlay()
    {
        hideTimer.Stop();
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var background = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 14);
        using var fillBrush = new SolidBrush(Color.FromArgb(238, 22, 24, 28));
        using var borderPen = new Pen(BorderColor(), 2);
        e.Graphics.FillPath(fillBrush, background);
        e.Graphics.DrawPath(borderPen, background);

        DrawBatteryIcon(e.Graphics, new Rectangle(22, 28, 56, 32));

        using var reasonFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var primaryFont = new Font("Segoe UI", 21f, FontStyle.Bold);
        using var secondaryFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var mutedBrush = new SolidBrush(Color.FromArgb(190, 235, 238, 242));
        using var textBrush = new SolidBrush(Color.White);

        e.Graphics.DrawString(reason, reasonFont, mutedBrush, new PointF(96, 15));
        e.Graphics.DrawString(primaryText, primaryFont, textBrush, new PointF(94, 31));
        e.Graphics.DrawString(secondaryText, secondaryFont, mutedBrush, new PointF(168, 46));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hideTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PositionWindow()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        Location = new Point(area.Right - Width - 24, area.Top + 24);
    }

    private string BuildSecondaryText(DongleSnapshot snapshot)
    {
        if (!snapshot.DeviceFound)
        {
            return "DS5Dongle not connected";
        }

        if (snapshot.Battery is null || !snapshot.Battery.HasKnownLevel)
        {
            return "Battery unknown";
        }

        return snapshot.Battery.StateName;
    }

    private Color BorderColor()
    {
        if (lowBattery)
        {
            return Color.FromArgb(242, 92, 84);
        }

        if (charging)
        {
            return Color.FromArgb(92, 220, 132);
        }

        return Color.FromArgb(92, 156, 255);
    }

    private void DrawBatteryIcon(Graphics graphics, Rectangle rect)
    {
        using var outlinePen = new Pen(Color.FromArgb(225, 240, 242, 245), 2);
        using var capBrush = new SolidBrush(Color.FromArgb(225, 240, 242, 245));
        using var fillBrush = new SolidBrush(BorderColor());
        using var bodyPath = RoundedRect(rect, 5);
        graphics.DrawPath(outlinePen, bodyPath);
        graphics.FillRectangle(capBrush, rect.Right + 2, rect.Top + 9, 5, 14);

        if (percent.HasValue)
        {
            var fillWidth = Math.Max(3, (rect.Width - 8) * percent.Value / 100);
            graphics.FillRectangle(fillBrush, rect.Left + 4, rect.Top + 4, fillWidth, rect.Height - 8);
        }

        if (charging)
        {
            using var boltPen = new Pen(Color.White, 2);
            var points = new[]
            {
                new Point(rect.Left + 28, rect.Top + 6),
                new Point(rect.Left + 20, rect.Top + 18),
                new Point(rect.Left + 29, rect.Top + 18),
                new Point(rect.Left + 23, rect.Top + 28)
            };
            graphics.DrawLines(boltPen, points);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
