using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitLauncher;
using OrbitLauncher.Models;
using OrbitLauncher.Services.Connect;
using OrbitLauncher.Utilities;
using System.Reflection;

internal static class ConnectUiSmoke
{
    public static void Run(string output)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (string name in new[] { "SoftIcons", "OrbitTheme" }) app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/OrbitLauncher;component/Themes/{name}.xaml", UriKind.Relative) });
                var type = typeof(GameInstance).Assembly.GetType("OrbitLauncher.Dialogs.OrbitConnectDialog") ?? throw new Exception("Connect panel missing");
                var window = (Window)Activator.CreateInstance(type, [new GameInstance { Name = "Выживание с друзьями", GameVersion = "26.2" }, false])!;
                window.Show(); window.UpdateLayout();
                foreach (string control in new[] { "InviteBox", "JoinButton", "CopyButton", "StatusText", "StopButton" })
                    if (window.FindName(control) is not FrameworkElement element || element.ActualWidth < 20) throw new Exception("Missing usable control " + control);
                if (((Button)window.FindName("CopyButton")).IsEnabled) throw new Exception("Copy enabled without invitation");
                Directory.CreateDirectory(output);
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(output, "connect-idle.png"))) encoder.Save(file);
                void Snapshot(string name)
                {
                    window.UpdateLayout(); var view = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); view.Render(window);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(view)); using var file = File.Create(Path.Combine(output, name + ".png")); png.Save(file);
                }
                var render = type.GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(ConnectStatus)], null)!;
                render.Invoke(window, [new ConnectStatus("hosting", "Мир открыт. Резерв Terracotta готов.", ConnectInvitation.Create(25565, "Выживание", "26.2").Encode())]);
                if (!((Button)window.FindName("CopyButton")).IsEnabled) throw new Exception("Ready invitation cannot be copied");
                Snapshot("connect-hosting");
                LocalizationService.SetLanguage("en"); LocalizationService.Apply(window);
                window.Width = 520;
                render.Invoke(window, [new ConnectStatus("connected", "Мир доступен. Подключитесь к адресу ниже в Minecraft.", Address: "127.0.0.1:32123", PingMs: 24)]);
                if (((TextBlock)window.FindName("StatusText")).Text.Contains("Мир")) throw new Exception("Dynamic Connect state not translated");
                if (((Button)window.FindName("JoinButton")).Content?.ToString() != "Connect" || ((Button)window.FindName("StopButton")).Content?.ToString() != "Disconnect" || ((TextBlock)window.FindName("PhaseText")).Text != "Connected")
                    throw new Exception("English Connect action/phase labels incomplete");
                Snapshot("connect-english-compact");
                render.Invoke(window, [new ConnectStatus("error", "Не удалось найти доступный маршрут. Проверьте подключение и повторите попытку.")]);
                if (((Button)window.FindName("CopyButton")).IsEnabled) throw new Exception("Stale invitation copied after error");
                Snapshot("connect-error");
                window.Close(); app.Shutdown();
                Console.WriteLine("PASS Connect UI: rendered idle/hosting/connected/error, compact English, copy guarded by readiness");
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Connect UI regression", failure);
    }
}
