using GlucoseTray.Enums;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace GlucoseTray.Display;

public interface ITrayIcon
{
    void ClearMenu();
    void AddAutoRunMenu(bool isAlreadyOn, EventHandler toggleCallback);
    void AddSettingsMenu();
    void AddExitMenu();
    void RefreshIcon(GlucoseDisplay display);
    void ShowNotification(string alertText);
    void Dispose();
}

public class NotificationIcon : ITrayIcon
{
    private readonly NotifyIcon _trayIcon;
    private GlucoseDisplay? _latestGlucose;

    public NotificationIcon()
    {
        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = new ContextMenuStrip(new Container()),
            Visible = true,
        };
        _trayIcon.DoubleClick += ShowBalloon;
    }

    public void ShowNotification(string alertText) => _trayIcon.ShowBalloonTip(2000, "Glucose Alert", alertText, ToolTipIcon.Warning);
    private void ShowBalloon(object? sender, EventArgs e) => _trayIcon?.ShowBalloonTip(2000, "Glucose", _latestGlucose?.GetDisplayMessage(DateTime.UtcNow) ?? "error", ToolTipIcon.Info);

    public void ClearMenu() => _trayIcon?.ContextMenuStrip?.Items.Clear();
    public void AddAutoRunMenu(bool isAlreadyOn, EventHandler toggleCallback) => _trayIcon?.ContextMenuStrip?.Items.Add(new ToolStripMenuItem(isAlreadyOn ? "Disable auto-start" : "Run on startup", null, toggleCallback));
    public void AddSettingsMenu() => _trayIcon?.ContextMenuStrip?.Items.Add(new ToolStripMenuItem("Settings", null, Settings));
    public void AddExitMenu() => _trayIcon?.ContextMenuStrip?.Items.Add(new ToolStripMenuItem("Exit", null, Exit));

    private void Settings(object? sender, EventArgs e)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.GetDirectoryName(AppContext.BaseDirectory) + "\\appsettings.json",
            UseShellExecute = true,
        };
        Process.Start(startInfo);
    }

    public void Dispose() => _trayIcon.Dispose();

    private void Exit(object? sender, EventArgs e)
    {
        Dispose();
        Application.ExitThread();
        Application.Exit();
    }

    public void RefreshIcon(GlucoseDisplay display)
    {
        _latestGlucose = display;
        CreateTextIcon(display);
    }

    private void CreateTextIcon(GlucoseDisplay display)
    {
        const float bitmapSize = 64f;
        const float targetFontSize = 54f;
        const float margin = 4f;

        var bitmapText = new Bitmap((int)bitmapSize, (int)bitmapSize);
        var g = Graphics.FromImage(bitmapText);
        g.Clear(Color.Transparent);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var fontStyle = display.IsStale ? FontStyle.Strikeout : FontStyle.Regular;
        var format = StringFormat.GenericTypographic;
        using var font = new Font("Roboto", targetFontSize, fontStyle, GraphicsUnit.Pixel);
        var measured = g.MeasureString(display.DisplayValue, font, int.MaxValue, format);

        // If text is too wide, compress it horizontally only — preserving height for readability
        var scaleX = measured.Width > bitmapSize - margin
            ? (bitmapSize - margin) / measured.Width
            : 1f;
        if (scaleX < 1f)
            g.ScaleTransform(scaleX, 1f);

        var x = (bitmapSize / scaleX - measured.Width) / 2f;
        var y = Math.Max(0f, (bitmapSize - measured.Height) / 2f);

        var mainColor = Convert(display.Color);
        DrawWithOutline(g, display.DisplayValue, font, format, mainColor, x, y);

        var hIcon = bitmapText.GetHicon();
        var myIcon = Icon.FromHandle(hIcon);
        _trayIcon.Icon = myIcon;

        DestroyMyIcon(myIcon.Handle);
        bitmapText.Dispose();
        g.Dispose();
        myIcon.Dispose();
    }

    private static void DrawWithOutline(Graphics g, string text, Font font, StringFormat format, Color mainColor, float x, float y)
    {
        // Outline color is the perceptual opposite of the text color for maximum contrast
        var outlineColor = IsPerceivedLight(mainColor) ? Color.FromArgb(200, 0, 0, 0) : Color.FromArgb(200, 255, 255, 255);
        using var outlineBrush = new SolidBrush(outlineColor);
        using var mainBrush = new SolidBrush(mainColor);

        // 1px outline at each diagonal to surround the glyph
        g.DrawString(text, font, outlineBrush, x - 1, y - 1, format);
        g.DrawString(text, font, outlineBrush, x + 1, y - 1, format);
        g.DrawString(text, font, outlineBrush, x - 1, y + 1, format);
        g.DrawString(text, font, outlineBrush, x + 1, y + 1, format);

        g.DrawString(text, font, mainBrush, x, y, format);
    }

    private static bool IsPerceivedLight(Color c) =>
        (c.R * 299 + c.G * 587 + c.B * 114) / 1000 >= 128;


    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(nint handle);

    private static void DestroyMyIcon(nint handle) => DestroyIcon(handle);

    private static Color Convert(IconTextColor color) => color switch
    {
        IconTextColor.White => Color.White,
        IconTextColor.Black => Color.Black,
        IconTextColor.Yellow => Color.Yellow,
        IconTextColor.Gold => Color.DarkGoldenrod,
        IconTextColor.Red => Color.OrangeRed,
        IconTextColor.Green => Color.FromArgb(52, 199, 89),     // #34C759 iOS green — bright on dark taskbar
        IconTextColor.DarkGreen => Color.FromArgb(40, 167, 69), // #28A745 — readable on light taskbar
        _ => Color.Black,
    };
}
