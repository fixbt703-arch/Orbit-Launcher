using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace OrbitLauncher.Utilities;

/// <summary>Per-element transitions. Shared theme brushes and layout bounds never animate.</summary>
public static class InterfaceMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(InterfaceMotion),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, OnMotionPreferenceChanged));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static bool CanAnimate => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    internal static bool Allows(DependencyObject value) => CanAnimate && GetEnabled(value);

    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        "Fill", typeof(Brush), typeof(InterfaceMotion), new PropertyMetadata(null, OnFillChanged));
    public static Brush? GetFill(DependencyObject value) => (Brush?)value.GetValue(FillProperty);
    public static void SetFill(DependencyObject value, Brush? brush) => value.SetValue(FillProperty, brush);

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.RegisterAttached(
        "Stroke", typeof(Brush), typeof(InterfaceMotion), new PropertyMetadata(null, OnStrokeChanged));
    public static Brush? GetStroke(DependencyObject value) => (Brush?)value.GetValue(StrokeProperty);
    public static void SetStroke(DependencyObject value, Brush? brush) => value.SetValue(StrokeProperty, brush);

    public static readonly DependencyProperty TargetOpacityProperty = DependencyProperty.RegisterAttached(
        "TargetOpacity", typeof(double), typeof(InterfaceMotion), new PropertyMetadata(1d, OnOpacityChanged));
    public static double GetTargetOpacity(DependencyObject value) => (double)value.GetValue(TargetOpacityProperty);
    public static void SetTargetOpacity(DependencyObject value, double opacity) => value.SetValue(TargetOpacityProperty, opacity);

    public static readonly DependencyProperty OffsetXProperty = DependencyProperty.RegisterAttached(
        "OffsetX", typeof(double), typeof(InterfaceMotion), new PropertyMetadata(0d, OnOffsetChanged));
    public static double GetOffsetX(DependencyObject value) => (double)value.GetValue(OffsetXProperty);
    public static void SetOffsetX(DependencyObject value, double offset) => value.SetValue(OffsetXProperty, offset);

    private sealed class Entrance
    {
        public Transform? Original;
        public TranslateTransform? Shift;
        public int Revision;
    }
    private static readonly ConditionalWeakTable<UIElement, Entrance> Entrances = new();

    static InterfaceMotion()
    {
        EventManager.RegisterClassHandler(typeof(ButtonBase), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnButtonLoaded));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnMenuOpened));
    }

    private static void OnButtonLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button) return;
        if (button.TryFindResource("OrbitFocus") is Style focus)
            button.SetCurrentValue(Control.FocusVisualStyleProperty, focus);
        if (button.ToolTip is string label && string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)))
            AutomationProperties.SetName(button, label);
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window && window is not MainWindow && window.Content is Border { Child: UIElement body })
            Reveal(body);
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu) Reveal(menu, 0);
    }

    private static void OnFillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ApplyFill(d);

    private static void ApplyFill(DependencyObject d)
    {
        if (d is Border border) ApplyBrush(border, Border.BackgroundProperty, GetFill(d));
        else if (d is Shape shape) ApplyBrush(shape, Shape.FillProperty, GetFill(d));
    }

    private static void OnStrokeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border border) ApplyBrush(border, Border.BorderBrushProperty, GetStroke(d));
    }

    private static void ApplyBrush(FrameworkElement element, DependencyProperty property, Brush? target)
    {
        var current = element.GetValue(property) as SolidColorBrush;
        if (target is not SolidColorBrush solid || current is null || !element.IsLoaded || !Allows(element))
        {
            element.SetCurrentValue(property, target);
            return;
        }
        // Sample the rendered color before replacing a running clock; rapid reversals stay continuous.
        var from = current.Color;
        var to = solid.Color;
        // Brushes.Transparent contains white RGB with zero alpha. Interpolating it
        // directly reveals that white before the dark hover color is reached.
        // Invisible endpoints borrow the visible endpoint's hue in both directions.
        if (from.A == 0) from = Color.FromArgb(0, to.R, to.G, to.B);
        if (to.A == 0) to = Color.FromArgb(0, from.R, from.G, from.B);
        var owned = new SolidColorBrush(solid.Color) { Opacity = solid.Opacity };
        element.SetCurrentValue(property, owned);
        if (from != to)
            owned.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, to, TimeSpan.FromMilliseconds(150))
            { EasingFunction = EaseOut(), FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        AnimateOpacity(element, GetTargetOpacity(element), element.IsLoaded && Allows(element));
    }

    private static void AnimateOpacity(UIElement element, double target, bool animate)
    {
        double from = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        // This attached property owns opacity. A real base value survives clock removal;
        // SetCurrentValue's coerced value can be discarded when WPF starts an animation.
        element.SetValue(UIElement.OpacityProperty, target);
        if (animate && from != target)
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(120))
            { EasingFunction = EaseOut(), FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        // This property owns only the switch thumb transform, never alignment or margins.
        if (element.RenderTransform is not TranslateTransform shift)
            element.RenderTransform = shift = new TranslateTransform();
        double from = shift.X;
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.X = GetOffsetX(element);
        if (element.IsLoaded && Allows(element))
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, shift.X, TimeSpan.FromMilliseconds(150))
            { EasingFunction = EaseOut(), FillBehavior = FillBehavior.Stop });
    }

    private static void OnMotionPreferenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue) return;
        if (GetFill(d) is not null) ApplyFill(d);
        if (d is Border border && GetStroke(d) is not null) ApplyBrush(border, Border.BorderBrushProperty, GetStroke(d));
        if (d is UIElement element && Entrances.TryGetValue(element, out var entrance)) ResetEntrance(element, entrance);
        if (DependencyPropertyHelper.GetValueSource(d, TargetOpacityProperty).BaseValueSource != BaseValueSource.Default && d is UIElement visual)
            AnimateOpacity(visual, GetTargetOpacity(d), false);
        if (d is FrameworkElement { RenderTransform: TranslateTransform shift } &&
            DependencyPropertyHelper.GetValueSource(d, OffsetXProperty).BaseValueSource != BaseValueSource.Default)
        { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = GetOffsetX(d); }
        if (d is ScrollViewer viewer) SmoothScroll.Cancel(viewer);
    }

    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };

    private static void ResetEntrance(UIElement surface, Entrance state)
    {
        state.Revision++;
        surface.BeginAnimation(UIElement.OpacityProperty, null);
        if (state.Shift is not null)
        {
            state.Shift.BeginAnimation(TranslateTransform.YProperty, null);
            surface.SetCurrentValue(UIElement.RenderTransformProperty, state.Original);
            state.Shift = null;
        }
    }

    public static void Reveal(UIElement surface) => Reveal(surface, 4);

    private static void Reveal(UIElement surface, double distance)
    {
        var state = Entrances.GetOrCreateValue(surface);
        ResetEntrance(surface, state);
        if (!Allows(surface)) return;
        int revision = state.Revision;
        state.Original = surface.RenderTransform;
        var shift = new TranslateTransform(0, distance);
        state.Shift = shift;
        var transforms = new TransformGroup();
        if (state.Original is not null) transforms.Children.Add(state.Original);
        transforms.Children.Add(shift);
        surface.SetCurrentValue(UIElement.RenderTransformProperty, transforms);
        var fade = new DoubleAnimation(.94 * surface.Opacity, surface.Opacity, TimeSpan.FromMilliseconds(200))
        { EasingFunction = EaseOut(), FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => { if (state.Revision == revision) ResetEntrance(surface, state); };
        surface.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance, 0, TimeSpan.FromMilliseconds(200))
        { EasingFunction = EaseOut(), FillBehavior = FillBehavior.Stop });
    }
}
