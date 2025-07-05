using System;
using System.Windows.Data;

namespace Xaml.Effects.Toolkit.Converter
{
    /// <summary>
    /// Boolean to visible converter
    /// </summary>
    public class TimeSpanMillisecondsConverter : System.Windows.Markup.MarkupExtension, IValueConverter
    {
        public TimeSpanMillisecondsConverter()
        {

        }
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            TimeSpan? ivalue = value as TimeSpan?;
            return ivalue.Value.TotalMilliseconds;

        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return new TimeSpan(0, 0, 0, 0, System.Convert.ToInt32(value));
        }
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}
