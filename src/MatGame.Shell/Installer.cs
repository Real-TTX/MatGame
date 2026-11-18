using System.Diagnostics;
using Microsoft.Win32;

namespace MatGame.Shell;

/// <summary>
/// The game installs itself per user, without administrator rights and without a separate installer tool (the ThixClip pattern): the published
/// <c>&lt;Game&gt;.exe</c> is the setup. Started from anywhere else it offers to copy itself to <c>%LocalAppData%\Programs\&lt;Title&gt;</c>, creates the
/// Start menu entry and the entry in "Installierte Apps". <c>--update</c> installs over the installed copy without asking (the updater starts it),
/// <c>--uninstall</c> removes program, shortcut and entry (the player's data in %APPDATA%\MatGame stays unless asked), <c>--quiet</c> asks nothing.
/// </summary>
public sealed class Installer(ShellOptions options)
{
    public const string UpdateArgument = "--update";
    public const string UninstallArgument = "--uninstall";
    public const string QuietArgument = "--quiet";
    public const string InstallArgument = "--install";

    public string ExecutableName => Path.GetFileName(Environment.ProcessPath ?? $"{options.Title}.exe");

    public string ProgramFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", options.Title);

    public string InstalledExecutable => Path.Combine(ProgramFolder, ExecutableName);

    public string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{options.Title}.lnk");

    private string UninstallKeyPath => $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MatGame.{options.GameId}";

    /// <summary>The signal a running game listens for to end itself (before an update replaces its file).</summary>
    public string ExitSignalName => $"MatGame.{options.GameId}.Exit";

    public bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            return key is not null && File.Exists(InstalledExecutable);
        }
    }

    public bool RunsInstalled =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? string.Empty), Path.GetFullPath(InstalledExecutable), StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies this exe into the program folder and writes shortcut and uninstall entry.</summary>
    public void Install()
    {
        ShellLog.Write($"install {options.Version} to {ProgramFolder}");
        StopRunningInstances();
        Directory.CreateDirectory(ProgramFolder);
        if (!RunsInstalled)
        {
            CopyWithRetry(Environment.ProcessPath!, InstalledExecutable);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(StartMenuShortcut)!);
        ShellLink.Write(StartMenuShortcut, InstalledExecutable, ProgramFolder, options.Title, InstalledExecutable);
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", options.Title);
        key.SetValue("DisplayVersion", options.Version);
        key.SetValue("Publisher", "Matthias Schmoldt");
        key.SetValue("DisplayIcon", InstalledExecutable + ",0");
        key.SetValue("InstallLocation", ProgramFolder);
        key.SetValue("UninstallString", $"\"{InstalledExecutable}\" {UninstallArgument}");
        key.SetValue("QuietUninstallString", $"\"{InstalledExecutable}\" {UninstallArgument} {QuietArgument}");
        if (options.Repository is not null)
        {
            key.SetValue("URLInfoAbout", $"https://github.com/{options.Repository}");
        }

        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExecutable).Length / 1024), RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
    }

    /// <summary>Removes program, shortcut and entry; the data only on request. The program folder goes after this process ended.</summary>
    public void Uninstall(bool deleteData)
    {
        ShellLog.Write("uninstall");
        StopRunningInstances();
        TryDelete(StartMenuShortcut);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        if (deleteData)
        {
            TryDeleteFolder(options.DataDirectory);
            TryDeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MatGame", options.GameId));
        }

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{ProgramFolder}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }

    public void StartInstalled() =>
        Process.Start(new ProcessStartInfo(InstalledExecutable) { UseShellExecute = false, WorkingDirectory = ProgramFolder });

    private void StopRunningInstances()
    {
        var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName))
            .Where(p => p.Id != Environment.ProcessId)
            .ToList();
        if (running.Count == 0)
        {
            return;
        }

        if (EventWaitHandle.TryOpenExisting(ExitSignalName, out var signal))
        {
            signal.Set();
            signal.Dispose();
        }

        foreach (var process in running)
        {
            try
            {
                if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // already gone
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void CopyWithRetry(string source, string target)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, target, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShellLog.Write($"could not delete {path}: {e.Message}");
        }
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShellLog.Write($"could not delete {path}: {e.Message}");
        }
    }
}
