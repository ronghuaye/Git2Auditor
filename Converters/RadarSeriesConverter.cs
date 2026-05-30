using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Git2Auditor.Converters;

public class RadarSeriesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double[] values)
        {
            return new ISeries[]
            {
                new PolarLineSeries<double>
                {
                    Values = values,
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2 },
                    Fill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(50)),
                    GeometrySize = 4,
                    GeometryFill = new SolidColorPaint(SKColors.DodgerBlue),
                    GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 1 }
                }
            };
        }
        return Array.Empty<ISeries>();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
