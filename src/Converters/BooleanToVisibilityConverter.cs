using System;
using System.Globalization;
using System.Windows;

namespace QBLyricEditor.Converters;

public class BooleanToVisibilityConverter : BaseValueConverter
{
    public bool Reverse { get; set; }
    public bool UseHidden { get; set; }

    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 用 as 而非强制转换：绑定源可能是可空 bool?（例如三态开关处于 null/不确定状态），
        // 直接 (bool)value 在这种情况下会抛 InvalidCastException
        bool b = value is bool v && v;
        if (Reverse) b = !b;
        if (b) return Visibility.Visible;
        else return UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
