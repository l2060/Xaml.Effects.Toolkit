﻿using System;
using System.Windows;

namespace Xaml.Effects.Toolkit.Animation.Wrappers
{
    public class TimeSpanWrapper : ValueWrapper<TimeSpan>
    {

        public override TimeSpan Value
        {
            get { return (TimeSpan)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(TimeSpan), typeof(TimeSpanWrapper), new PropertyMetadata(default(TimeSpan)));
    }
}
