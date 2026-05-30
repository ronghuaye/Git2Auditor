using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Git2Auditor.Converters;

public class SoloHeroToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSoloHero && isSoloHero)
        {
            return new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)); // 浅红色背景
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
