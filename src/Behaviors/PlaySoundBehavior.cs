using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Xaml.Effects.Toolkit.Behaviors
{
    public class PlaySoundBehavior : TriggerAction<DependencyObject>
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(Uri), typeof(PlaySoundBehavior), null);

        public static readonly DependencyProperty VolumeProperty = DependencyProperty.Register("Volume", typeof(double), typeof(PlaySoundBehavior), new PropertyMetadata(0.5));

        public Uri Source
        {
            get
            {
                return (Uri)base.GetValue(PlaySoundBehavior.SourceProperty);
            }
            set
            {
                base.SetValue(PlaySoundBehavior.SourceProperty, value);
            }
        }

        public double Volume
        {
            get
            {
                return (double)base.GetValue(PlaySoundBehavior.VolumeProperty);
            }
            set
            {
                base.SetValue(PlaySoundBehavior.VolumeProperty, value);
            }
        }

        protected virtual void SetMediaElementProperties(MediaElement mediaElement)
        {
            if (mediaElement != null)
            {
                mediaElement.Source = this.Source;
                mediaElement.Volume = this.Volume;
            }
        }

        protected override void Invoke(object parameter)
        {
            if (this.Source == null || base.AssociatedObject == null)
            {
                return;
            }
            Popup popup = new Popup();
            MediaElement mediaElement = new MediaElement();
            popup.Child = mediaElement;
            mediaElement.Visibility = Visibility.Collapsed;
            this.SetMediaElementProperties(mediaElement);
            mediaElement.MediaEnded += delegate (object param0, RoutedEventArgs param1)
            {
                popup.Child = null;
                popup.IsOpen = false;
            };
            mediaElement.MediaFailed += delegate (object param0, ExceptionRoutedEventArgs param1)
            {
                popup.Child = null;
                popup.IsOpen = false;
            };
            popup.IsOpen = true;
        }
    }

}
