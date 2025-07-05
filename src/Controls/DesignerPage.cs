using System.Windows;
using System.Windows.Controls;

namespace Xaml.Effects.Toolkit.Controls
{
    public class DesignerPage : UserControl
    {

        public DesignerPage()
        {
            this.Loaded += DesignerPage_Loaded;
            this.Unloaded += DesignerPage_Unloaded;
        }

        private void DesignerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            //Console.WriteLine("DesignerPage_Unloaded");
        }

        private void DesignerPage_Loaded(object sender, RoutedEventArgs e)
        {
            //Console.WriteLine("DesignerPage_Loaded");
        }




        //#region ZoomValue

        //public Double ZoomValue
        //{
        //    get
        //    {
        //        return (Double)GetValue(ZoomValueProperty);
        //    }
        //    set
        //    {
        //        SetValue(ZoomValueProperty, value);
        //    }
        //}

        //public static readonly DependencyProperty ZoomValueProperty =
        //  DependencyProperty.Register("ZoomValue",
        //                               typeof(Double),
        //                               typeof(DesignerPage),
        //                               new FrameworkPropertyMetadata(1d, ZoomValuePropertyChanged));

        //public static void ZoomValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        //{
        //    //var view = d as DesignerPage;
        //    //if (view != null)
        //    //{
        //    //    view.ZoomValue = (Double)e.NewValue;
        //    //}
        //}


        //#endregion



        //#region SelectedProperty

        //public Object SelectedProperty
        //{
        //    get
        //    {
        //        return (Object)GetValue(SelectedPropertyProperty);
        //    }
        //    set
        //    {
        //        SetValue(SelectedPropertyProperty, value);
        //    }
        //}

        //public static readonly DependencyProperty SelectedPropertyProperty =
        //  DependencyProperty.Register("SelectedProperty",
        //                               typeof(Object),
        //                               typeof(DesignerPage),
        //                               new FrameworkPropertyMetadata(null));


        //#endregion


        ///
    }
}
