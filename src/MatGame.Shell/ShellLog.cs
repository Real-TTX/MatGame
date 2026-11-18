namespace MatGame.Shell;

/// <summary>A small log in the game's data folder (<c>shell.log</c>, the last ~1 MB): start, errors of the bridge, updates.</summary>
public static class ShellLog
{
    private static readonly Lock Sync = new();
    private static string? _file;

    public static void Open(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _file = Path.Combine(dataDirectory, "shell.log");
        try
        {
            if (File.Exists(_file) && new FileInfo(_file).Length > 1_000_000)
            {
                File.Move(_file, _file + ".old", overwrite: true);
            }
        }
        catch (IOException)
        {
            // another instance holds it: keep writing
        }
    }

    public static void Write(string message)
    {
        if (_file is null)
        {
            return;
        }

        lock (Sync)
        {
            try
            {
                File.AppendAllText(_file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // the log is best effort
            }
        }
    }
}
