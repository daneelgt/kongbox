using System.Diagnostics;
using Microsoft.Win32;

namespace KongBox.Agent;

/// <summary>
/// The release exe is also its own installer: run from anywhere, it offers to install itself for the current user
/// (no admin rights): %LOCALAPPDATA%\Programs\KongBox, a Start menu shortcut, start with Windows and an entry in
/// Settings › Apps with a real uninstaller. Run again from a newer download, it updates the installed copy.
/// </summary>
static class Installer
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KongBox");
    public static readonly string Exe = Path.Combine(Dir, "KongBoxAgent.exe");
    static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "KONGBOX.lnk");
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KongBox";
    const string Title = "KONGBOX";

    static string Version => typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "0";

    public static bool RunningInstalled => string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true when this process should exit (it installed/updated and started the installed copy, or was cancelled).</summary>
    public static bool OfferInstall()
    {
        if (RunningInstalled) return false;
        var installed = File.Exists(Exe);
        var installedVersion = installed ? FileVersionInfo.GetVersionInfo(Exe).ProductVersion ?? "?" : null;
        var text = installed
            ? $"O KONGBOX {installedVersion} já está instalado.\n\nAtualizar para a versão {Version}?"
            : "Instalar o KONGBOX neste PC?\n\n• fica no Menu Iniciar\n• inicia junto com o Windows\n• pode ser desinstalado em Configurações › Apps\n\nNão precisa de administrador.";
        var answer = MessageBox.Show(text, Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
        if (answer == DialogResult.Cancel) return true;
        if (answer == DialogResult.No) return false; // run this copy without installing (portable)
        try
        {
            StopInstalledAgent();
            Directory.CreateDirectory(Dir);
            CopyWithRetry(Environment.ProcessPath!, Exe);
            CreateShortcut();
            Register();
            Autostart.Set(on: true, path: Exe);
            Process.Start(new ProcessStartInfo(Exe, "--open") { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception e)
        {
            MessageBox.Show("Não consegui instalar: " + e.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return true;
        }
    }

    /// <summary>"KongBoxAgent.exe --uninstall", called from Settings › Apps.</summary>
    public static int Uninstall()
    {
        if (MessageBox.Show("Desinstalar o KONGBOX deste PC?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
        var wipe = MessageBox.Show("Apagar também as suas páginas, atalhos e celulares pareados?\n\n(Não = manter para uma próxima instalação)",
            Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        StopInstalledAgent();
        try { File.Delete(Shortcut); } catch { }
        Autostart.Set(on: false, path: Exe);
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        // this exe may be the one being deleted: a short-lived cmd removes the folder once we have exited
        var targets = "\"" + Dir + "\"" + (wipe ? " \"" + AppPaths.Dir + "\"" : "");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q {targets}") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
        MessageBox.Show("KONGBOX desinstalado.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    /// <summary>Asks a running agent to quit (POST /api/quit), then waits for the exe to be free.</summary>
    static void StopInstalledAgent()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:8765/api/quit");
            req.Headers.Add("X-Kong", "1");
            http.Send(req);
        }
        catch { /* not running */ }
        foreach (var p in Process.GetProcessesByName("KongBoxAgent"))
        {
            using (p)
            {
                if (p.Id == Environment.ProcessId) continue;
                try { if (!p.WaitForExit(5000)) p.Kill(true); } catch { }
            }
        }
    }

    static void CopyWithRetry(string from, string to)
    {
        for (int i = 0; ; i++)
        {
            try { File.Copy(from, to, overwrite: true); return; }
            catch (IOException) when (i < 10) { Thread.Sleep(500); }
        }
    }

    static void CreateShortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell indisponível");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(Shortcut);
        link.TargetPath = Exe;
        link.Arguments = "--open";
        link.WorkingDirectory = Dir;
        link.Description = "KONGBOX: painel de mesa para o PC";
        link.IconLocation = Exe + ",0";
        link.Save();
    }

    static void Register()
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", "KONGBOX");
        k.SetValue("DisplayVersion", Version);
        k.SetValue("Publisher", "KONGBOX");
        k.SetValue("DisplayIcon", Exe + ",0");
        k.SetValue("InstallLocation", Dir);
        k.SetValue("UninstallString", $"\"{Exe}\" --uninstall");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
    }
}
