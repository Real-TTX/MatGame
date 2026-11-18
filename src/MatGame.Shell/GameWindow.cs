using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MatGame.Shell;

/// <summary>
/// The window of the game: WebView2 over the whole client area. Every request to <see cref="ShellOptions.Origin"/> is answered from the game's files
/// (never the network), extension-less paths below the game fall back to the start page (the game's own routes such as <c>/bunnywarfare/Play/{token}</c>).
/// Links to other sites open in the system browser. F11 toggles full screen.
/// </summary>
internal sealed class GameWindow : Form
{
    private readonly ShellOptions _options;
    private readonly IWebFiles _files;
    private readonly WebView2 _view;
    private readonly Bridge _bridge;
    private FormWindowState _stateBeforeFullscreen = FormWindowState.Maximized;
    private bool _fullscreen;

    public GameWindow(ShellOptions options, IWebFiles files, ShellStorage storage)
    {
        _options = options;
        _files = files;
        Text = options.Title;
        BackColor = options.Background;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 600);
        Size = new Size(1366, 800);
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            // the default icon
        }

        _view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = options.Background };
        Controls.Add(_view);
        _bridge = new Bridge(options, storage, PostToPage, SetFullscreen, Close);
        Load += async (_, _) => await InitializeAsync();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F11)
            {
                SetFullscreen(!_fullscreen);
                e.Handled = true;
            }
        };
        FormClosed += async (_, _) => await _bridge.DisposeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MatGame", _options.GameId, "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _view.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this, "Für das Spiel fehlt die „Microsoft Edge WebView2 Runtime“. Bitte von microsoft.com installieren und das Spiel neu starten.",
                _options.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = _view.CoreWebView2;
        var devTools = Environment.GetEnvironmentVariable("MATGAME_DEVTOOLS") == "1" || _options.Channel == "local";
        core.Settings.AreDevToolsEnabled = devTools;
        core.Settings.AreDefaultContextMenusEnabled = devTools;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = devTools;

        core.AddWebResourceRequestedFilter($"{_options.Origin}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => Serve(core, e);
        core.WebMessageReceived += async (_, e) => await _bridge.HandleAsync(e.WebMessageAsJson);
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(_options.Origin + "/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                OpenExternal(e.Uri);
            }
        };
        core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);
        core.ProcessFailed += (_, e) => ShellLog.Write($"browser process failed: {e.ProcessFailedKind} {e.Reason}");
        core.Navigate(_options.StartUrl);
        ShellLog.Write($"window open: {_options.StartUrl}");
    }

    private void Serve(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var stream = _files.Open(path);
        var servedPath = path;
        var gameRoot = $"/{_options.GameId}/";
        if (stream is null && (path == "/" || (path.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase) && !Path.HasExtension(path))))
        {
            servedPath = gameRoot + _options.StartPage;
            stream = _files.Open(servedPath);
        }

        if (stream is null)
        {
            e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        // the stream must stay readable after this handler: WebView2 reads it later (copy small embedded streams into memory)
        var memory = new MemoryStream();
        using (stream)
        {
            stream.CopyTo(memory);
        }

        memory.Position = 0;
        var headers = $"Content-Type: {MimeTypes.Of(servedPath)}\r\nCache-Control: no-cache\r\nAccess-Control-Allow-Origin: {_options.Origin}";
        e.Response = core.Environment.CreateWebResourceResponse(memory, 200, "OK", headers);
    }

    private void PostToPage(string json)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => PostToPage(json));
            return;
        }

        _view.CoreWebView2?.PostWebMessageAsJson(json);
    }

    private void SetFullscreen(bool on)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetFullscreen(on));
            return;
        }

        if (on == _fullscreen)
        {
            return;
        }

        _fullscreen = on;
        if (on)
        {
            _stateBeforeFullscreen = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = _stateBeforeFullscreen;
        }
    }

    private static void OpenExternal(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
