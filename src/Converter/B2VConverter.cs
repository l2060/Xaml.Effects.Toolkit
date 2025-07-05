﻿using System;
using System.Windows;
using System.Windows.Data;

namespace Xaml.Effects.Toolkit.Converter
{
    /// <summary>
    /// Boolean to visible converter
    /// </summary>
    public class B2VConverter : System.Windows.Markup.MarkupExtension, IValueConverter
    {
        public B2VConverter()
        {

        }
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            Boolean? ivalue = value as Boolean?;

            if (ivalue.HasValue && ivalue.Value)
            {
                return Visibility.Visible;
            }
            else
            {
                if (parameter != null)
                {
                    return Visibility.Collapsed;
                }
                else
                {
                    return Visibility.Hidden;
                }
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
