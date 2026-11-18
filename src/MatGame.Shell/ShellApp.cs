namespace MatGame.Shell;

/// <summary>
/// The entry of a game's app: <c>return ShellApp.Run(args, new ShellOptions { ... })</c> in its <c>Main</c> (marked <c>[STAThread]</c>).
/// Handles setup (<c>--install</c>, <c>--update</c>, <c>--uninstall</c>, <c>--quiet</c>), one instance per game, the update check, and the window.
/// </summary>
public static class ShellApp
{
    public static int Run(string[] args, ShellOptions options)
    {
        ShellLog.Open(options.DataDirectory);
        ShellLog.Write($"start {options.GameId} {options.Version} {string.Join(' ', args)}");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var installer = new Installer(options);
        var quiet = args.Contains(Installer.QuietArgument);

        if (args.Contains(Installer.UninstallArgument))
        {
            if (!quiet && MessageBox.Show($"{options.Title} wirklich entfernen?", options.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return 0;
            }

            var deleteData = !quiet && MessageBox.Show("Auch das Offline-Profil und die Einstellungen löschen?", options.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            installer.Uninstall(deleteData);
            return 0;
        }

        if (args.Contains(Installer.UpdateArgument) || args.Contains(Installer.InstallArgument))
        {
            installer.Install();
            installer.StartInstalled();
            return 0;
        }

        // a downloaded exe offers to install itself (a local build and the installed copy just run)
        if (!installer.RunsInstalled && options.Channel != "local" && Environment.GetEnvironmentVariable("MATGAME_PORTABLE") != "1")
        {
            var answer = quiet
                ? DialogResult.Yes
                : MessageBox.Show(
                    installer.IsInstalled
                        ? $"{options.Title} ist schon installiert. Mit dieser Version überschreiben?\n\nJa = installieren und starten · Nein = nur diese Datei starten"
                        : $"{options.Title} installieren? (Startmenü, Updates, kein Administrator nötig)\n\nJa = installieren und starten · Nein = nur diese Datei starten",
                    options.Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                return 0;
            }

            if (answer == DialogResult.Yes)
            {
                installer.Install();
                installer.StartInstalled();
                return 0;
            }
        }

        using var single = new Mutex(true, $"MatGame.{options.GameId}.Instance", out var first);
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting($"MatGame.{options.GameId}.Show", out var show))
            {
                show.Set();
                show.Dispose();
            }

            return 0;
        }

        var files = options.Files ?? DefaultFiles(options);
        var storage = new ShellStorage(options.DataDirectory);
        using var window = new GameWindow(options, files, storage);
        ListenForSignals(options, installer, window);
        window.Shown += async (_, _) => await OfferUpdateAsync(options, window);
        Application.Run(window);
        ShellLog.Write("end");
        return 0;
    }

    private static IWebFiles DefaultFiles(ShellOptions options)
    {
        var folder = Environment.GetEnvironmentVariable("MATGAME_WEBROOT");
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            ShellLog.Write($"web files from {folder}");
            return new FolderWebFiles(folder);
        }

        var embedded = new EmbeddedWebFiles(System.Reflection.Assembly.GetEntryAssembly()!);
        ShellLog.Write($"{embedded.Count} embedded web files");
        return embedded;
    }

    /// <summary>"Show" brings the window to the front (a second start), "Exit" ends the game (an update replaces the file).</summary>
    private static void ListenForSignals(ShellOptions options, Installer installer, Form window)
    {
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, $"MatGame.{options.GameId}.Show");
        var exit = new EventWaitHandle(false, EventResetMode.AutoReset, installer.ExitSignalName);
        var thread = new Thread(() =>
        {
            while (true)
            {
                var which = WaitHandle.WaitAny([show, exit]);
                if (window.IsDisposed)
                {
                    return;
                }

                if (which == 0)
                {
                    window.BeginInvoke(() =>
                    {
                        if (window.WindowState == FormWindowState.Minimized)
                        {
                            window.WindowState = FormWindowState.Maximized;
                        }

                        window.Activate();
                    });
                }
                else
                {
                    window.BeginInvoke(window.Close);
                    return;
                }
            }
        }) { IsBackground = true, Name = "MatGame signals" };
        thread.Start();
    }

    private static async Task OfferUpdateAsync(ShellOptions options, Form window)
    {
        var updater = new Updater(options);
        var manifest = await updater.CheckAsync();
        if (manifest is null || window.IsDisposed)
        {
            return;
        }

        var answer = MessageBox.Show(window, $"Version {manifest.Version} von {options.Title} ist da. Jetzt aktualisieren? Das Spiel startet danach neu.",
            options.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await updater.InstallAsync(manifest);
            window.Close();
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            ShellLog.Write($"update failed: {e.Message}");
            MessageBox.Show(window, $"Das Update hat nicht geklappt: {e.Message}", options.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
