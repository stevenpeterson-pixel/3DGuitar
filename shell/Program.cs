using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AcousticGuitar;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

/// <summary>A plain window that hosts the guitar page in WebView2.</summary>
sealed class MainForm : Form
{
    const string Title = "Concert Acoustic";
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    string version = "";                                     // sent by the page, so the title bar always matches the page inside
    string Caption => version.Length > 0 ? $"{Title} {version}" : Title;
    Form? lyricsForm;   // the lyrics pop-out window while it is open

    public MainForm()
    {
        Text = Title;
        ClientSize = new Size(1600, 860);
        MinimumSize = new Size(1000, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 8, 6);
        try { if (Environment.ProcessPath is string exe) Icon = Icon.ExtractAssociatedIcon(exe); } catch { /* keep the default icon */ }
        web.DefaultBackgroundColor = BackColor;
        Controls.Add(web);
        Load += async (_, _) => await InitAsync();
    }

    /// <summary>File that holds the user's words for a song: lyrics\&lt;song title&gt;.txt next to the .exe.</summary>
    static string LyricsPath(string title)
    {
        string safe = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (safe.Length == 0) safe = "untitled";
        if (safe.Length > 120) safe = safe[..120];
        return Path.Combine(AppContext.BaseDirectory, "lyrics", safe + ".txt");
    }

    async Task InitAsync()
    {
        try
        {
            // Everything lives under %LOCALAPPDATA%\ConcertAcoustic.
            // Each build gets its own folder (unpacked page + private browser profile), because two copies that share
            // a profile also share one browser engine, and a newer build would then be served the older build's files.
            // Settings are kept in one small file so they carry over between builds.
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConcertAcoustic");
            Assembly asm = Assembly.GetExecutingAssembly();
            string build = asm.ManifestModule.ModuleVersionId.ToString("N")[..12];
            string builds = Path.Combine(root, "builds"), mine = Path.Combine(builds, build), site = Path.Combine(mine, "web");
            string settingsFile = Path.Combine(root, "settings.json");
            Directory.CreateDirectory(builds);
            // Remove other builds' folders, but only ones no open copy is using: a folder with open files cannot be
            // renamed, so it is renamed aside first and deleted only if that worked (otherwise another version's page would vanish).
            foreach (string old in Directory.GetDirectories(builds).Where(d => Path.GetFileName(d) != build && !Path.GetFileName(d).StartsWith("old-"))
                         .Concat(new[] { Path.Combine(root, "web"), Path.Combine(root, "profile") }))
            {
                try
                {
                    if (!Directory.Exists(old)) continue;
                    string aside = Path.Combine(builds, "old-" + Guid.NewGuid().ToString("N")[..8]);
                    Directory.Move(old, aside);
                    Directory.Delete(aside, true);
                }
                catch { /* still in use by another open copy */ }
            }
            foreach (string left in Directory.GetDirectories(builds, "old-*")) { try { Directory.Delete(left, true); } catch { } }

            // Unpack the page, writing a file only when it differs: another open copy of this build may be reading these files,
            // and rewriting them under it made pages fail with "access denied".
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith(".html") && !name.EndsWith(".js") && !name.EndsWith(".jpg")) continue;
                string target = Path.Combine(site, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using Stream res = asm.GetManifestResourceStream(name)!;
                using var mem = new MemoryStream(); await res.CopyToAsync(mem); byte[] bytes = mem.ToArray();
                try { if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) continue; } catch { /* unreadable: write it */ }
                string temp = target + "." + Environment.ProcessId + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes);
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Move(temp, target, true); break; }
                    catch (IOException) when (attempt < 20) { await Task.Delay(100); }
                    catch (UnauthorizedAccessException) when (attempt < 20) { await Task.Delay(100); }
                }
            }

            // Let audio start without a click, since this is a dedicated instrument window.
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(mine, "profile"), options);
            await web.EnsureCoreWebView2Async(env);

            var s = web.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsPinchZoomEnabled = false;
            s.IsSwipeNavigationEnabled = false;
            s.IsStatusBarEnabled = false;

            // Hand the saved settings to the page (only if the file really is a JSON object).
            try
            {
                string json = File.ReadAllText(settingsFile);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.__savedSettings = " + doc.RootElement.GetRawText() + ";");
            }
            catch { /* no settings yet */ }

            // The page sends its settings to be saved, and its audio status for the title bar.
            web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    string msg = e.TryGetWebMessageAsString();
                    if (msg.StartsWith("settings:"))
                    {
                        using JsonDocument doc = JsonDocument.Parse(msg[9..]);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object) File.WriteAllText(settingsFile, doc.RootElement.GetRawText());
                    }
                    else if (msg.StartsWith("getLyrics:"))
                    {
                        // Words the user saved for a song: one text file per song in the "lyrics" folder beside the app.
                        string title = msg[10..], path = LyricsPath(title);
                        string text = File.Exists(path) ? File.ReadAllText(path) : "";
                        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "lyrics", title, text }));
                    }
                    else if (msg.StartsWith("saveLyrics:"))
                    {
                        using JsonDocument doc = JsonDocument.Parse(msg[11..]);
                        string title = doc.RootElement.GetProperty("title").GetString() ?? "", text = doc.RootElement.GetProperty("text").GetString() ?? "";
                        if (title.Length > 0 && text.Length < 200_000)
                        {
                            string path = LyricsPath(title);
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            if (text.Trim().Length == 0) { if (File.Exists(path)) File.Delete(path); } else File.WriteAllText(path, text);
                        }
                    }
                    else if (msg == "showLyrics")
                    {
                        // the Lyrics button was clicked while its window is open: bring that window back in front
                        if (lyricsForm is { IsDisposed: false } f)
                        {
                            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
                            f.Show(); f.BringToFront(); f.Activate();
                        }
                        else web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"lyricsClosed\"}");
                    }
                    else if (msg.StartsWith("version:"))
                    {
                        string v = msg[8..].Trim();
                        if (v.Length is > 0 and < 24) { version = v; Text = Caption; }
                    }
                    else Text = $"{Caption}  ·  {msg}";
                }
                catch { /* ignore malformed messages */ }
            };

            // The lyrics pop-out gets its own plain app window (no browser address strip), free to move to another screen.
            web.CoreWebView2.NewWindowRequested += async (_, e) =>
            {
                var deferral = e.GetDeferral();
                try
                {
                    var form = new Form
                    {
                        Text = "Lyrics  ·  " + Title, ClientSize = new Size(620, 860), MinimumSize = new Size(360, 300), BackColor = BackColor, Icon = Icon,
                        StartPosition = FormStartPosition.Manual, Location = new Point(Location.X + 90, Location.Y + 70),
                    };
                    var pop = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
                    form.Controls.Add(pop);
                    // owned by the guitar window, so clicking the guitar never hides the words behind it
                    form.Show(this);
                    lyricsForm = form;
                    form.FormClosed += (_, _) =>
                    {
                        if (lyricsForm == form) lyricsForm = null;
                        try { web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"lyricsClosed\"}"); } catch { }
                    };
                    await pop.EnsureCoreWebView2Async(env);
                    pop.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    pop.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    pop.CoreWebView2.WindowCloseRequested += (_, _) => form.Close();
                    FormClosed += (_, _) => { if (!form.IsDisposed) form.Close(); };
                    e.NewWindow = pop.CoreWebView2;
                    e.Handled = true;
                }
                catch { /* fall back to the default pop-up window */ }
                finally { deferral.Complete(); }
            };

            // Stage mode asks for full screen: the window drops its frame and fills the screen, then returns as it was.
            var savedBorder = FormBorderStyle; var savedState = WindowState;
            web.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
            {
                if (web.CoreWebView2.ContainsFullScreenElement)
                {
                    savedBorder = FormBorderStyle; savedState = WindowState;
                    FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Normal; WindowState = FormWindowState.Maximized;
                }
                else { FormBorderStyle = savedBorder; WindowState = savedState; }
            };

            // Serve the page from a secure (https) origin, which the audio engine requires.
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("guitar.example", site, CoreWebView2HostResourceAccessKind.Allow);
            // If the page cannot be read at first (a file briefly locked, say by a virus scan), try again a few times.
            int retries = 0;
            web.CoreWebView2.NavigationCompleted += async (_, e) =>
            {
                if (e.IsSuccess || retries >= 8) return;
                retries++;
                await Task.Delay(400 * retries);
                web.CoreWebView2.Navigate("https://guitar.example/index.html");
            };
            web.CoreWebView2.Navigate("https://guitar.example/index.html");

            // Start-up switch used for checking a build: opens a song and the lyrics window without a click.
            if (Environment.GetCommandLineArgs().Contains("--demo-lyrics"))
            {
                web.CoreWebView2.NavigationCompleted += async (_, _) =>
                {
                    await Task.Delay(2500);
                    await web.CoreWebView2.ExecuteScriptAsync("(() => { const g = window.__guitar; g.loadSong(g.SONGS.findIndex(s => s.t === 'Amazing Grace')); g.openLyrics(); })()");
                };
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "The guitar could not start. It needs the Microsoft Edge WebView2 Runtime, which ships with Windows 11.\n\n" + ex.Message,
                Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }
}
