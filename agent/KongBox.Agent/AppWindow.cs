using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KongBox.Agent;

/// <summary>
/// The KONGBOX window: the settings editor in its own app window (WebView2, the Edge engine Windows already has)
/// instead of a browser tab. Closing it only hides it; the agent keeps running in the tray.
/// </summary>
sealed class AppWindow : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(22, 22, 24) };
    readonly string url;
    public bool Quitting;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public AppWindow(string url)
    {
        this.url = url;
        Text = "KONGBOX";
        BackColor = Color.FromArgb(22, 22, 24);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        Size = new Size(Math.Min(1360, area.Width - 80), Math.Min(880, area.Height - 60));
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        Controls.Add(web);
        Load += async (_, _) => await Init();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // dark title bar (Windows 10 2004+ / 11), matching the editor
        int on = 1;
        DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
    }

    async Task Init()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.Dir, "WebView2"));
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            core.Settings.IsZoomControlEnabled = false;
            // links that leave the editor (GitHub token page, docs) open in the real browser
            core.NewWindowRequested += (_, a) => { a.Handled = true; OpenExternal(a.Uri); };
            core.NavigationStarting += (_, a) =>
            {
                if (!a.Uri.StartsWith("http://127.0.0.1:8765", StringComparison.OrdinalIgnoreCase)) { a.Cancel = true; OpenExternal(a.Uri); }
            };
            core.DocumentTitleChanged += (_, _) => Text = core.DocumentTitle is { Length: > 0 } t ? t.Replace("KONGBOX · ", "KONGBOX — ") : "KONGBOX";
            web.Source = new Uri(url);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // very old Windows without Edge WebView2: fall back to the browser
            OpenExternal(url);
            Hide();
        }
    }

    static void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); } catch { }
    }

    public void ShowAndFocus()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // the window is just a view: closing it hides it, the tray keeps the agent alive
        if (!Quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }
}
