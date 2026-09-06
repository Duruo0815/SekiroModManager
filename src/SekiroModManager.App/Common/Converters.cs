using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SekiroModManager.App.Common;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var isEmpty = count == 0;
        if (Invert) isEmpty = !isEmpty;
        return isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var severity = value as string ?? "Info";
        var app = Application.Current;
        if (app == null) return Brushes.Gray;

        return severity switch
        {
            "Success" => app.TryFindResource("SuccessBrush") as Brush ?? Brushes.Green,
            "Warning" => app.TryFindResource("WarningBrush") as Brush ?? Brushes.Orange,
            "Danger" => app.TryFindResource("DangerBrush") as Brush ?? Brushes.Red,
            _ => app.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
