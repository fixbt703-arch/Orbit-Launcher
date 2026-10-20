using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CmlLib.Core.Auth;
using OrbitLauncher;
using OrbitLauncher.Controls;
using OrbitLauncher.Dialogs;
using OrbitLauncher.Models;
using OrbitLauncher.Services;
using OrbitLauncher.Utilities;

internal static class SkinUiChecks
{
    public static async Task RunAsync(MainWindow main, ElySkinsPanel panel, LauncherSettings settings, string output)
    {
        LocalizationService.SetLanguage("ru"); LocalizationService.Apply(main);
        ((RadioButton)main.FindName("SkinsNav")).IsChecked = true;
        settings.OfflineModeEnabled = false;
        var library = new SkinLibraryService(Path.Combine(output, "library")); Set(main, "_skinLibraryService", library);
        var png = Path.Combine(output, "synthetic-skin.png"); await File.WriteAllBytesAsync(png, Skin());
        var item = await library.ImportAsync(png, "classic"); item.Name = "Демонстрационный скин";
        await library.SaveAsync([item]);
        Set(main, "_skinLibraryLoaded", false); await Invoke(main, "RefreshSkinPageAsync", true);
        Require(main.SkinLibrary.Count == 1 && !((Border)main.FindName("SkinLibraryEmptyPanel")).IsVisible, "populated library hidden");
        var uploadHandler = new UploadHandler();
        using var upload = new HttpClient(uploadHandler);
        var auth = new AuthService(upload, () => Task.FromResult(new MSession { Username = "OrbitLicensed", UUID = "11111111222243338444555555555555", AccessToken = "fixture-token" }));
        await auth.GetRequiredSessionAsync(settings);
        Set(main, "_authService", auth); Set(main, "_minecraftSkinService", new MinecraftSkinService(upload));
        var heads = new PlayerSkinService(upload, Path.Combine(output, "licensed-preview")); heads.RememberSkin("OrbitLicensed", Skin()); Set(main, "_playerSkinService", heads);
        await Invoke(main, "UpdateAccountCardAsync");
        var applying = Invoke(main, "ApplyLibrarySkinAsync", item);
        Require(!((Button)main.FindName("AddSkinButton")).IsEnabled && ((ProgressBar)main.FindName("SkinOperationProgress")).IsVisible, "upload has no busy state");
        await applying;
        Require(File.Exists(item.FilePath) && main.SkinLibrary.Count == 1 && ((Button)main.FindName("AddSkinButton")).IsEnabled, "failed upload lost skin or blocked retry");
        Require(((TextBlock)main.FindName("SkinOperationText")).Text.Contains("Не удалось применить"), "upload failure not explained");
        await Settle(main); Capture(main, output, "skins-upload-error");
        uploadHandler.Success = true;
        await Invoke(main, "ApplyLibrarySkinAsync", item);
        Require(((TextBlock)main.FindName("SkinOperationText")).Text.Contains("Скин применён") && ((Button)main.FindName("AddSkinButton")).IsEnabled, "successful upload did not finish or show its actual result");
        foreach (var name in new[] { "Классический", "Тонкие руки", "Скин с очень длинным названием для проверки" })
        {
            var extra = await library.ImportAsync(png, name == "Тонкие руки" ? "slim" : "classic"); extra.Name = name; main.SkinLibrary.Add(extra);
        }
        await library.SaveAsync(main.SkinLibrary);
        await File.WriteAllTextAsync(Path.Combine(output, "library", "library.json"), "{broken fixture");
        // Remove the valid backup so this exercises the visible recovery path, not automatic JSON recovery.
        var backup = Path.Combine(output, "library", "library.json.bak"); if (File.Exists(backup)) File.Delete(backup);
        Set(main, "_skinLibraryLoaded", false); await Invoke(main, "RefreshSkinPageAsync", true);
        Require(((Button)main.FindName("SkinRetryButton")).IsVisible && main.SkinLibrary.Count == 4, "library failure hid saved skins or omitted retry");
        await Settle(main); Capture(main, output, "skins-library-load-error");
        await library.SaveAsync(main.SkinLibrary); await Invoke(main, "RefreshSkinPageAsync", true);
        Require(!((Button)main.FindName("SkinRetryButton")).IsVisible && main.SkinLibrary.Count == 4, "library retry did not recover");
        ((Border)main.FindName("SkinStatusPanel")).Visibility = Visibility.Collapsed;
        await Invoke(main, "UpdateAccountCardAsync"); await Settle(main); Capture(main, output, "skins-library-ru");

        main.Width = 1060; main.Height = 680;
        LocalizationService.SetLanguage("en"); LocalizationService.Apply(main); await Invoke(main, "UpdateAccountCardAsync"); await Settle(main);
        var items = (ItemsControl)main.FindName("SkinLibraryItems");
        var actionTops = Descendants(items).OfType<Button>().Where(button => button.Tag is SkinLibraryItem && button.Content is string)
            .Select(button => Math.Round(button.TransformToAncestor(items).Transform(new Point()).Y, 1)).Distinct().ToArray();
        Require(actionTops.Length == 1, "long skin names shifted the card action row");
        var apply = Descendants(items).OfType<Button>().First(button => button.Tag is SkinLibraryItem && button.Content is string);
        Require((string)apply.Content == "Apply" && apply.Focus(), "library keyboard action or translation missing");
        var remove = Descendants(items).OfType<Button>().First(button => button.ToolTip is string && button.Tag is SkinLibraryItem);
        Require(new ButtonAutomationPeer(remove).GetName() == "Remove from library", "delete button lacks localized automation name");
        Require(apply.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "keyboard focus cannot leave apply action");
        Capture(main, output, "skins-library-en");

        var page = (Grid)main.FindName("SkinsPage");
        main.Width = 1320; main.Height = 900;
        foreach (var scale in new[] { 1.25, 1.5, 2.0 })
        {
            page.LayoutTransform = new ScaleTransform(scale, scale); await Settle(main);
            var add = (Button)main.FindName("AddSkinButton");
            var bounds = add.TransformToAncestor((Visual)main.Content).TransformBounds(new Rect(add.RenderSize));
            Require(bounds.Left >= 0 && bounds.Right <= ((FrameworkElement)main.Content).ActualWidth + 1, "primary action clipped during layout scaling");
        }
        Capture(main, output, "skins-layout-scale-200"); page.LayoutTransform = Transform.Identity;
        InterfaceTheme.ApplyContrast(true); await Settle(main); Capture(main, output, "skins-high-contrast"); InterfaceTheme.ApplyContrast(false);

        LocalizationService.SetLanguage("ru");
        var dialog = new NewSkinDialog { ShowInTaskbar = false, Left = -5000, Top = -5000, WindowStartupLocation = WindowStartupLocation.Manual };
        dialog.Show(); await Settle(dialog);
        typeof(NewSkinDialog).GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [false]);
        Require(((TextBlock)dialog.FindName("ErrorText")).Text.Length > 0 && ((TextBox)dialog.FindName("NameBox")).IsKeyboardFocused, "missing-name recovery is not inline");
        typeof(NewSkinDialog).GetProperty("SkinFilePath")!.SetValue(dialog, png);
        ((TextBox)dialog.FindName("FileBox")).Text = Path.GetFileName(png);
        ((TextBox)dialog.FindName("NameBox")).Text = "Скины";
        typeof(NewSkinDialog).GetMethod("UpdatePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
        ((RadioButton)dialog.FindName("SlimModelRadio")).IsChecked = true;
        Require(((Image)dialog.FindName("SkinPreview")).Source is not null && ((RadioButton)dialog.FindName("SlimModelRadio")).Focus(), "model preview or keyboard choice missing");
        ((TextBlock)dialog.FindName("ErrorText")).Text = "";
        LocalizationService.SetLanguage("en"); LocalizationService.Apply(dialog);
        Require(((TextBox)dialog.FindName("NameBox")).Text == "Скины", "localization changed user input");
        await Settle(dialog); Capture(dialog, output, "skin-import-preview-en"); dialog.Close();
        var savedDialog = new NewSkinDialog { Owner = main, ShowInTaskbar = false, Left = -5000, Top = -5000, WindowStartupLocation = WindowStartupLocation.Manual };
        savedDialog.Loaded += (_, _) =>
        {
            ((TextBox)savedDialog.FindName("NameBox")).Text = "Saved fixture";
            typeof(NewSkinDialog).GetProperty("SkinFilePath")!.SetValue(savedDialog, png);
            typeof(NewSkinDialog).GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(savedDialog, [false]);
        };
        Require(savedDialog.ShowDialog() == true && !savedDialog.ApplyAfterSave && savedDialog.SkinFilePath == png, "import dialog did not return the validated selection");
        LocalizationService.SetLanguage("ru"); LocalizationService.Apply(main);
        main.Width = 1060; main.Height = 680;
        settings.OfflineModeEnabled = true; await Invoke(main, "UpdateAccountCardAsync");
        using var failing = new HttpClient(new FailureHandler());
        Set(panel, "_skins", new ElySkinService(failing, Path.Combine(output, "failed-heads")));
        await panel.RefreshAsync();
        Require(((TextBlock)panel.FindName("StatusText")).Text.Contains("Не удалось") && ((Button)panel.FindName("RefreshButton")).IsEnabled, "Ely failure has no retry path");
        await Settle(main); Capture(main, output, "ely-refresh-error");
        Console.WriteLine("PASS skins: populated library, upload failure retains PNG, busy state, keyboard actions, localized automation names, 125/150/200% layout scaling, high contrast, real import preview, inline validation and preserved user input");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static Task Invoke(object target, string name, params object[] args) => (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Settle(Window window) { window.UpdateLayout(); await Task.Delay(180); window.UpdateLayout(); }
    private static void Capture(Window window, string output, string name)
    {
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * 1.5), (int)Math.Ceiling(visual.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }
    private static byte[] Skin()
    {
        var pixels = new byte[64 * 64 * 4];
        void Fill(int x, int y, int width, int height, byte r, byte g, byte b)
        { for (int row = y; row < y + height; row++) for (int col = x; col < x + width; col++) { var index = (row * 64 + col) * 4; pixels[index] = b; pixels[index + 1] = g; pixels[index + 2] = r; pixels[index + 3] = 255; } }
        Fill(8, 8, 8, 8, 225, 174, 137); Fill(8, 8, 8, 2, 62, 43, 45); Fill(8, 10, 1, 3, 62, 43, 45);
        Fill(9, 11, 2, 1, 245, 244, 255); Fill(13, 11, 2, 1, 245, 244, 255); Fill(10, 11, 1, 1, 74, 103, 170); Fill(13, 11, 1, 1, 74, 103, 170); Fill(11, 14, 2, 1, 136, 74, 64);
        Fill(20, 20, 8, 12, 117, 97, 179); Fill(23, 20, 2, 2, 225, 174, 137); Fill(23, 24, 2, 3, 180, 169, 223);
        Fill(44, 20, 4, 5, 117, 97, 179); Fill(44, 25, 4, 7, 225, 174, 137); Fill(36, 52, 4, 5, 117, 97, 179); Fill(36, 57, 4, 7, 225, 174, 137);
        Fill(4, 20, 4, 10, 46, 53, 81); Fill(20, 52, 4, 10, 46, 53, 81); Fill(4, 30, 4, 2, 28, 32, 48); Fill(20, 62, 4, 2, 28, 32, 48);
        var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 256);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private sealed class UploadHandler : HttpMessageHandler
    {
        public bool Success { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await Task.Delay(180, token);
            return Success ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"11111111222243338444555555555555\",\"name\":\"OrbitLicensed\",\"skins\":[]}") }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("fixture unavailable") };
        }
    }
    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
