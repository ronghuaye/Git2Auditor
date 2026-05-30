using System;
using System.Globalization;
using System.Windows.Data;

namespace Git2Auditor.Converters;

public class GroupMatchToHighlightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        
        if (values[0] is int itemGroupId && values[1] is int selectedGroupId)
        {
            return itemGroupId == selectedGroupId;
        }
        
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
