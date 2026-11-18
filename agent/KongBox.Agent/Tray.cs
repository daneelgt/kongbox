using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace KongBox.Agent;

/// <summary>Tray icon: open settings, open the panel preview, quit. Runs on its own STA thread.</summary>
static class Tray
{
    public static void Start(string url, Action quit)
    {
        var t = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            using var icon = MakeIcon();
            using var menu = new ContextMenuStrip();
            menu.Items.Add("Configurações", null, (_, _) => Open(url));
            menu.Items.Add("Painel (preview)", null, (_, _) => Open(url + "panel/"));
            menu.Items.Add("Pasta de dados", null, (_, _) => Open(AppPaths.Dir));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, (_, _) => { quit(); Application.Exit(); });
            using var ni = new NotifyIcon { Icon = icon, Text = "KONGBOX", Visible = true, ContextMenuStrip = menu };
            ni.DoubleClick += (_, _) => Open(url);
            Application.Run();
            ni.Visible = false;
        }) { IsBackground = true, Name = "tray" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); } catch { }
    }

    /// <summary>A rounded black square with a "K" and a green cursor, drawn at runtime (no .ico to ship).</summary>
    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var path = new GraphicsPath();
            const int r = 16;
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(64 - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(64 - r * 2, 64 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, 64 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var bg = new SolidBrush(Color.FromArgb(18, 18, 20));
            g.FillPath(bg, path);
            using var font = new Font("Segoe UI", 34, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("K", font, Brushes.White, new PointF(9, 6));
            using var green = new SolidBrush(Color.FromArgb(48, 209, 88));
            g.FillRectangle(green, 42, 42, 12, 6);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
