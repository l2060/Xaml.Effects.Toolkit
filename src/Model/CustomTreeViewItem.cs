using Microsoft.Toolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Xaml.Effects.Toolkit.Models
{

    public delegate void CustomTreeViewItemExpandedChanged(CustomTreeViewItem sender, Boolean isExpanded);


    public class CustomTreeViewItem : ObservableObject
    {
        public CustomTreeViewItem()
        {
            Children = new ObservableCollection<CustomTreeViewItem>();
        }

        private String header;

        public String Header
        {
            get
            {
                return this.header;
            }
            set
            {
                if (value != Header)
                {
                    base.SetProperty(ref this.header, value);
                }
            }
        }
        private ObservableCollection<CustomTreeViewItem> children;
        public ObservableCollection<CustomTreeViewItem> Children
        {
            get
            {
                return this.children;
            }
            set
            {
                if (value != Children)
                {
                    base.SetProperty(ref this.children, value);
                }
            }
        }

        public bool HasItems
        {
            get
            {
                return Children != null && Children.Count > 0;
            }
        }
        private Boolean isselected;
        public Boolean IsSelected
        {
            get
            {
                return this.isselected;
            }
            set
            {
                if (value != IsSelected)
                {
                    base.SetProperty(ref this.isselected, value);
                }
            }
        }

        private Boolean isexpanded;
        public Boolean IsExpanded
        {
            get
            {
                return this.isexpanded;
            }
            set
            {
                if (value != IsExpanded)
                {
                    base.SetProperty(ref this.isexpanded, value);
                    ExpandedChanged?.Invoke(this, value);
                }
            }
        }

        private Object icon;
        public Object Icon
        {
            get
            {
                return this.icon;
            }
            set
            {
                base.SetProperty(ref this.icon, value);
            }
        }


        public void loadPath()
        {
            Path p = new Path();
            p.Stroke = new SolidColorBrush(Colors.White);
            p.Fill = null;
            p.SnapsToDevicePixels = true;
            p.StrokeThickness = 1;
            p.Data = Geometry.Parse("M2,2 L9,2 9,5 12,5 12,14 2,14 2,2 M9,2 L12,5 M5,4 L7,4 M5,6 L7,6 M5,8 L9,8 M5,10 L9,10 M5,12 L9,12");
            this.Icon = p;
        }




        public void loadIcon(String imagePath, UriKind uriKind = UriKind.Relative)
        {
            // Create source   
            BitmapImage myBitmapImage = new BitmapImage();
            // BitmapImage.UriSource must be in a BeginInit/EndInit block   
            myBitmapImage.BeginInit();
            myBitmapImage.UriSource = new Uri(imagePath, uriKind);
            // To save significant application memory, set the DecodePixelWidth or     
            // DecodePixelHeight of the BitmapImage value of the image source to the desired    
            // height or width of the rendered image. If you don't do this, the application will    
            // cache the image as though it were rendered as its normal size rather then just    
            // the size that is displayed.   
            // Note: In order to preserve aspect ratio, set DecodePixelWidth   
            // or DecodePixelHeight but not both.   
            //myBitmapImage.DecodePixelWidth = 2048;
            myBitmapImage.EndInit();
            //set image source   
            Image img = new Image();
            img.Source = myBitmapImage;
            this.Icon = img;
        }




        public event CustomTreeViewItemExpandedChanged ExpandedChanged;
    }
}
