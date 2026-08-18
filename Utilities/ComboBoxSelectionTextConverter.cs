using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OrbitLauncher.Utilities;

/// <summary>
/// Produces stable text for the closed part of every ComboBox. WPF's
/// SelectionBoxItem can keep an old string when ComboBoxItem content is
/// localized or changed at runtime.
/// </summary>
public sealed class ComboBoxSelectionTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is null || values[0] == DependencyProperty.UnsetValue)
            return string.Empty;

        var item = values[0];
        if (item is ComboBoxItem comboBoxItem)
        {
            var boundContent = values.Length > 2 && values[2] != DependencyProperty.UnsetValue
                ? values[2]
                : comboBoxItem.Content;
            return boundContent?.ToString() ?? string.Empty;
        }

        var displayMemberPath = values.Length > 1 ? values[1]?.ToString() : null;
        if (!string.IsNullOrWhiteSpace(displayMemberPath))
        {
            var property = item.GetType().GetProperty(
                displayMemberPath,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is not null)
                return property.GetValue(item)?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
