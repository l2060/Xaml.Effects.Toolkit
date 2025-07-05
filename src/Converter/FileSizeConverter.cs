using System;
using System.Windows.Data;

namespace Xaml.Effects.Toolkit.Converter
{
    /// <summary>
    /// Boolean to visible converter
    /// </summary>
    public class FileSizeConverter : System.Windows.Markup.MarkupExtension, IValueConverter
    {
        public FileSizeConverter()
        {

        }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return "Unknown";

            if (!Int64.TryParse(value.ToString(), out var filesize))
            {
                return "Unknown";
            }

            if (filesize < 0)
            {
                return "Unknown";
            }
            else if (filesize >= 1024 * 1024 * 1024)
            {
                return string.Format("{0:0.00} GB", (double)filesize / (1024 * 1024 * 1024));
            }
            else if (filesize >= 1024 * 1024)
            {
                return string.Format("{0:0.00} MB", (double)filesize / (1024 * 1024));
            }
            else if (filesize >= 1024)
            {
                return string.Format("{0:0.00} KB", (double)filesize / 1024);
            }
            else
            {
                return string.Format("{0:0.00} bytes", filesize);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}

