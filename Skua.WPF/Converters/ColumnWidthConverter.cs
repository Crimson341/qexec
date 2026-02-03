using System;
using System.Globalization;
using System.Windows.Data;

namespace Skua.WPF.Converters;

public class ColumnWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Check for unset or null values
        if (values == null || values.Length < 2 ||
            values[0] == System.Windows.DependencyProperty.UnsetValue ||
            values[1] == System.Windows.DependencyProperty.UnsetValue)
        {
            return 200.0; // Default fallback width
        }

        if (values[0] is double containerWidth && values[1] is int columns)
        {
            if (columns <= 0) columns = 1;
            if (containerWidth <= 0) return 200.0;

            // Calculate width: (containerWidth - scrollbar - padding - margins) / columns
            // Account for: 6px right margin per item, 4px padding on each side of ScrollViewer
            double totalMargin = 6 * columns + 8; // 8 for ScrollViewer padding (4 * 2)
            double availableWidth = containerWidth - totalMargin - 20; // 20 for potential scrollbar
            double itemWidth = availableWidth / columns;

            return Math.Max(100, itemWidth); // Minimum width of 100
        }

        return 200.0; // Default fallback width
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
