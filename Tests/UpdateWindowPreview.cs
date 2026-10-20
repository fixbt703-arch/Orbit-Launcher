using System.IO;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitLauncher;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

// Render the real update dialog with explicitly fictional release data. No network or installation.
internal static class UpdateWindowPreview
{
    public static void Run(string output, bool verify = false)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var theme in new[] { "SoftIcons", "OrbitTheme" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/OrbitLauncher;component/Themes/{theme}.xaml", UriKind.Relative) });
            LocalizationService.SetLanguage("ru");
            app.DispatcherUnhandledException += (_, e) => { failure = e.Exception; e.Handled = true; app.Shutdown(); };
            app.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    using var http = new HttpClient(new NoNetworkHandler());
                    var sample = new LauncherUpdate(new Version(1, 5, 0, 0), "v1.5.0",
                        "• Более плавное переключение вкладок.\n\n• Улучшения каталога модов и загрузки иконок.\n\n• Исправления стабильности и небольшая полировка интерфейса.",
                        new Uri("https://github.com/fixbt703-arch/Orbit-Launcher/releases/tag/v1.5.0"),
                        new Uri("https://github.com/fixbt703-arch/Orbit-Launcher/releases/download/v1.5.0/OrbitLauncherInstaler.exe"),
                        80000000, new string('0', 64));
                    var window = new UpdateWindow(new GitHubUpdateService(http), sample,
                        (_, _, _) => throw new InvalidOperationException("Preview cannot install an update."))
                    { WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = -4000 };
                    window.Show();
                    await Task.Delay(350);
                    window.UpdateLayout();
                    Directory.CreateDirectory(output);
                    var visual = (FrameworkElement)window.Content;
                    const double dpi = 144;
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * dpi / 96),
                        (int)Math.Ceiling(visual.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var path = Path.Combine(output, "update-window.png");
                    using (var file = File.Create(path)) encoder.Save(file);
                    Console.WriteLine("Rendered actual WPF update dialog: " + path);
                    ((Button)window.FindName("LaterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (window.IsVisible || window.InstallationStarted) throw new Exception("Later changed the launcher");
                    if (verify) await VerifyAsync(output);
                }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Update preview failed", failure);
    }

    private static async Task VerifyAsync(string output)
    {
        var cache = Path.Combine(Path.GetTempPath(), "OrbitUpdateUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        try
        {
            var payload = Enumerable.Range(0, 65536).Select(i => (byte)i).ToArray(); payload[0] = (byte)'M'; payload[1] = (byte)'Z';
            var update = new LauncherUpdate(new Version(1, 5, 0, 0), "v1.5.0", "UI regression fixture",
                new Uri("https://github.com/fixbt703-arch/Orbit-Launcher/releases/tag/v1.5.0"),
                new Uri("https://github.com/fixbt703-arch/Orbit-Launcher/releases/download/v1.5.0/OrbitLauncherInstaler.exe"),
                payload.Length, Convert.ToHexString(SHA256.HashData(payload)));
            var badPayload = payload.ToArray(); badPayload[^1] ^= 1;
            var handler = new UiHandler(payload);
            using var http = new HttpClient(handler);
            var service = new GitHubUpdateService(http, cache);
            int installs = 0; string? installed = null;
            Task Install(LauncherUpdate release, string file, CancellationToken token)
            {
                if (release != update || !File.ReadAllBytes(file).SequenceEqual(payload)) throw new Exception("Unverified file reached installer");
                installs++; installed = file; return Task.CompletedTask;
            }
            UpdateWindow Create()
            {
                var window = new UpdateWindow(service, update, Install)
                { WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = -4000 };
                window.Show(); return window;
            }
            Button ButtonAt(Window window, string name) => (Button)window.FindName(name);
            void Click(Window window, string name) => ButtonAt(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var cancel = Create();
            Click(cancel, "UpdateButton");
            await Until(() => ((ProgressBar)cancel.FindName("DownloadProgress")).Value > 0);
            if (ButtonAt(cancel, "UpdateButton").IsEnabled) throw new Exception("Duplicate update click enabled during download");
            Capture(cancel, output, "update-downloading");
            Click(cancel, "LaterButton");
            await Until(() => ButtonAt(cancel, "UpdateButton").IsEnabled);
            if (installs != 0 || Directory.GetFiles(cache).Length != 0) throw new Exception("Cancellation installed or retained a partial file");
            cancel.Close(); Console.WriteLine("PASS update UI: cancel download and retain launcher");

            handler.Payload = badPayload;
            var retry = Create(); Click(retry, "UpdateButton");
            await Until(() => ButtonAt(retry, "UpdateButton").IsEnabled);
            if (installs != 0 || retry.InstallationStarted) throw new Exception("Bad hash reached installation");
            Capture(retry, output, "update-error");
            handler.Payload = payload;
            Click(retry, "UpdateButton");
            await Until(() => !retry.IsVisible);
            if (installs != 1 || !retry.InstallationStarted || installed is null) throw new Exception("Retry didn't hand off exactly one verified file");
            service.DiscardDownload(installed); Console.WriteLine("PASS update UI: failed checksum, retry, verified handoff");

            var closing = Create(); Click(closing, "UpdateButton");
            await Until(() => ((ProgressBar)closing.FindName("DownloadProgress")).Value > 0);
            closing.Close();
            await Until(() => !Directory.GetFiles(cache).Any());
            if (installs != 1) throw new Exception("Closing downloaded an update");
            Console.WriteLine("PASS update UI: close during download cancels and cleans temporary file");

            LocalizationService.SetLanguage("en");
            var english = Create(); await Task.Delay(250);
            if (ButtonAt(english, "UpdateButton").Content.ToString() != "Update now") throw new Exception("English action missing");
            Capture(english, output, "update-english"); english.Close(); LocalizationService.SetLanguage("ru");
            Console.WriteLine("PASS update UI: Russian and English layouts rendered");
        }
        finally
        {
            var resolved = Path.GetFullPath(cache);
            if (Path.GetDirectoryName(resolved) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                && Path.GetFileName(resolved).StartsWith("OrbitUpdateUi-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }

    private static void Capture(Window window, string output, string name)
    {
        window.UpdateLayout(); var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)(visual.ActualWidth * 1.5), (int)(visual.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Update UI didn't settle"); await Task.Delay(20); }
    }
    private sealed class UiHandler(byte[] payload) : HttpMessageHandler
    {
        public byte[] Payload { get; set; } = payload;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(Payload)) });
    }
    private sealed class SlowStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(45, cancellationToken); return await base.ReadAsync(buffer[..Math.Min(4096, buffer.Length)], cancellationToken); }
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Preview cannot access the network.");
    }
}
