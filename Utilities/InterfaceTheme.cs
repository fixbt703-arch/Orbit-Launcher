using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace OrbitLauncher.Utilities;

public static class InterfaceTheme
{
    private static bool _initialized;
    private static readonly string[] Keys = ["BackgroundBrush", "PanelBrush", "CardBrush", "CardHoverBrush", "SubtleCardBrush",
        "TitleBarFrameBrush", "NavigationFrameBrush", "BorderBrush", "DividerBrush", "ControlBrush", "InputBrush", "TextBrush",
        "MutedTextBrush", "PrimaryBrush", "PrimaryLightBrush", "PrimaryHoverBrush", "AccentBrush", "SuccessBrush", "WarningBrush",
        "DangerBrush", "TextOnAccentBrush", "FocusBrush", "SelectionBrush", "SoftAccentBrush", "OverlayBrush", "ThumbBrush",
        "PrimaryGradient", "SpaceGradient", "ControlBorderBrush", "ControlHoverBorderBrush", "DefaultShellBrush", "RecentSurfaceBrush", "RecentBorderBrush", "RecentOrbitBrush", "CardHoverBorderBrush", "NavigationSelectionBrush"];

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        ApplyContrast(SystemParameters.HighContrast);
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SystemParameters.HighContrast))
                Application.Current?.Dispatcher.Invoke(() => ApplyContrast(SystemParameters.HighContrast));
        };
    }

    public static void ApplyContrast(bool highContrast)
    {
        if (Application.Current is not { } app) return;
        foreach (var key in Keys)
        {
            if (!highContrast) { app.Resources.Remove(key); continue; }
            Brush brush = key switch
            {
                "RecentOrbitBrush" => Brushes.Transparent,
                "RecentBorderBrush" or "CardHoverBorderBrush" or "ControlBorderBrush" or "ControlHoverBorderBrush" or "TextBrush" or "MutedTextBrush" or "PrimaryLightBrush" or "BorderBrush" or "DividerBrush" or "DangerBrush" or
                "SuccessBrush" or "WarningBrush" or "AccentBrush" or "FocusBrush" => SystemColors.WindowTextBrush,
                "PrimaryBrush" or "PrimaryHoverBrush" or "PrimaryGradient" or "SelectionBrush" or "SoftAccentBrush" => SystemColors.HighlightBrush,
                "TextOnAccentBrush" => SystemColors.HighlightTextBrush,
                "ThumbBrush" => SystemColors.WindowTextBrush,
                _ => SystemColors.WindowBrush
            };
            app.Resources[key] = brush;
        }
    }
}
