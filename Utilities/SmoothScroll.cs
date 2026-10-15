using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OrbitLauncher.Utilities;

/// <summary>Short wheel interpolation for physical lists; keyboard, touch and logical scrolling remain native.</summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnEnabledChanged));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0d,
            (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
    private sealed class State { public double Target; public bool Active; public int Revision; }
    private static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer) return;
        if ((bool)e.NewValue)
        {
            viewer.PreviewMouseWheel += OnWheel;
            viewer.PreviewMouseDown += OnDirectInput;
            viewer.PreviewKeyDown += OnDirectInput;
            viewer.Unloaded += OnUnloaded;
            viewer.ScrollChanged += OnScrollChanged;
        }
        else
        {
            viewer.PreviewMouseWheel -= OnWheel;
            viewer.PreviewMouseDown -= OnDirectInput;
            viewer.PreviewKeyDown -= OnDirectInput;
            viewer.Unloaded -= OnUnloaded;
            viewer.ScrollChanged -= OnScrollChanged;
            Cancel(viewer);
        }
    }

    private static bool Eligible(ScrollViewer viewer) => GetEnabled(viewer) && !viewer.CanContentScroll &&
        viewer.ScrollableHeight > 0 && viewer.TemplatedParent is not System.Windows.Controls.Primitives.TextBoxBase;

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        if (e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None || !Eligible(viewer)) return;
        // Preview tunnels outside in: give the nearest nested scroller first choice.
        for (var child = e.OriginalSource as DependencyObject; child is not null && !ReferenceEquals(child, viewer); child = Parent(child))
        {
            if (child is ScrollViewer nested && nested.ScrollableHeight > 0 &&
                (e.Delta < 0 ? nested.VerticalOffset < nested.ScrollableHeight : nested.VerticalOffset > 0)) return;
        }
        int lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return;
        double step = lines < 0 ? viewer.ViewportHeight * .9 : lines * 24d;
        e.Handled = ScrollBy(viewer, -e.Delta / 120d * step);
    }

    private static DependencyObject? Parent(DependencyObject element) => element is Visual || element is System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    // The rendered regression uses the same accumulation and cancellation path as wheel input.
    public static bool ScrollBy(ScrollViewer viewer, double delta)
    {
        if (!Eligible(viewer)) return false;
        var state = States.GetOrCreateValue(viewer);
        double from = viewer.VerticalOffset;
        double target = Math.Clamp((state.Active ? state.Target : from) + delta, 0, viewer.ScrollableHeight);
        if (Math.Abs(target - from) < .1 && !state.Active) return false;
        Cancel(viewer);
        state.Target = target;
        if (!InterfaceMotion.Allows(viewer)) { viewer.ScrollToVerticalOffset(target); return true; }
        state.Active = true;
        int revision = state.Revision;
        // Base value is the destination: removing the completed clock cannot restore an old offset.
        viewer.SetValue(OffsetProperty, target);
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(170))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        animation.Completed += (_, _) =>
        {
            if (state.Revision != revision) return;
            state.Active = false;
            viewer.BeginAnimation(OffsetProperty, null);
            viewer.ScrollToVerticalOffset(Math.Min(target, viewer.ScrollableHeight));
        };
        viewer.BeginAnimation(OffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
        return true;
    }

    public static void Cancel(ScrollViewer viewer)
    {
        if (!States.TryGetValue(viewer, out var state)) return;
        state.Revision++;
        state.Active = false;
        double offset = viewer.VerticalOffset;
        viewer.BeginAnimation(OffsetProperty, null);
        viewer.SetValue(OffsetProperty, offset);
        viewer.ScrollToVerticalOffset(offset);
    }
    private static void OnDirectInput(object sender, InputEventArgs e) => Cancel((ScrollViewer)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs e) => Cancel((ScrollViewer)sender);
    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource)) return;
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0) Cancel((ScrollViewer)sender);
    }
}
