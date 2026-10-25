using System;
using System.Collections;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Xaml.Effects.Toolkit.Styles.Windows
{
    /// <summary>
    /// 窗口主题对象，包含了主题的配置
    /// </summary>
    public class WindowTheme : Freezable
    {
        public WindowTheme()
        {
            Services = new ServiceCollection();
        }


        #region Attached Theme
        public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.RegisterAttached("Theme", typeof(WindowTheme), typeof(WindowTheme),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Theme_Changle));




        /// <summary>
        /// 主题的设置附加
        /// </summary>
        /// <param name="s"></param>
        /// <param name="e"></param>
        private static void Theme_Changle(DependencyObject s, DependencyPropertyChangedEventArgs e)
        {
            var newTheme = e.NewValue as WindowTheme;
            var oldTheme = e.OldValue as WindowTheme;
            if (newTheme == oldTheme)
            {
                return;
            }
            if (oldTheme != null)
            {
                oldTheme.Detach();
            }
            if (newTheme != null)
            {
                newTheme.Attach(s);
            }
        }




        /// <summary>
        /// 获取一个对象的主题属性
        /// </summary>
        /// <param name="dpo"></param>
        /// <returns></returns>
        public static WindowTheme GetTheme(DependencyObject dpo)
        {
            return (WindowTheme)dpo.GetValue(ThemeProperty);
        }

        /// <summary>
        /// 设置一个对象的主题属性
        /// </summary>
        /// <param name="dpo"></param>
        /// <param name="value"></param>
        public static void SetTheme(DependencyObject dpo, WindowTheme value)
        {
            dpo.SetValue(ThemeProperty, value);
        }
        #endregion

        #region BorderThickness
        public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register("BorderThickness", typeof(Thickness), typeof(WindowTheme),
        new FrameworkPropertyMetadata(new Thickness(1)));

        /// <summary>
        /// 获取/设置对象的边框厚度属性
        /// </summary>
        public Thickness BorderThickness
        {
            get
            {
                return (Thickness)this.GetValue(BorderThicknessProperty);
            }
            set
            {
                this.SetValue(BorderThicknessProperty, value);
            }
        }
        #endregion

        #region BorderBrush
        public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register("BorderBrush", typeof(Brush), typeof(WindowTheme),
        new FrameworkPropertyMetadata(new SolidColorBrush(Colors.Transparent)));

        /// <summary>
        /// 获取/设置对象的边框画刷
        /// </summary>
        public Brush BorderBrush
        {
            get
            {
                return (Brush)this.GetValue(BorderBrushProperty);
            }
            set
            {
                this.SetValue(BorderBrushProperty, value);
            }
        }
        #endregion

        #region MaxButton

        public static readonly DependencyProperty MaxButtonProperty =
        DependencyProperty.Register("MaxButton", typeof(Visibility), typeof(WindowTheme),
        new FrameworkPropertyMetadata(Visibility.Visible));

        /// <summary>
        /// 获取/设置窗口最大化按钮是否可见
        /// </summary>
        public Visibility MaxButton
        {
            get
            {
                return (Visibility)this.GetValue(MaxButtonProperty);
            }
            set
            {
                this.SetValue(MaxButtonProperty, value);
            }
        }
        #endregion

        #region MinButton
        public static readonly DependencyProperty MinButtonProperty =
        DependencyProperty.Register("MinButton", typeof(Visibility), typeof(WindowTheme),
        new FrameworkPropertyMetadata(Visibility.Visible));

        /// <summary>
        /// 获取/设置窗口最小化按钮是否可见
        /// </summary>
        public Visibility MinButton
        {
            get
            {
                return (Visibility)this.GetValue(MinButtonProperty);
            }
            set
            {
                this.SetValue(MinButtonProperty, value);
            }
        }
        #endregion

        #region Menu
        public static readonly DependencyProperty MenuProperty =
        DependencyProperty.Register("Menu", typeof(Object), typeof(WindowTheme),
        new FrameworkPropertyMetadata(null));

        /// <summary>
        /// 获取/设置窗口的主菜单对象
        /// </summary>
        public Object Menu
        {
            get
            {
                return (Object)this.GetValue(MenuProperty);
            }
            set
            {
                this.SetValue(MenuProperty, value);
            }
        }
        #endregion

        #region AttachedButtons
        public static readonly DependencyProperty AttachedButtonsProperty =
        DependencyProperty.Register("AttachedButtons", typeof(Object), typeof(WindowTheme),
        new FrameworkPropertyMetadata(null));

        /// <summary>
        /// 获取/设置 窗口的附加按钮对象
        /// </summary>
        public Object AttachedButtons
        {
            get
            {
                return (Object)this.GetValue(AttachedButtonsProperty);
            }
            set
            {
                this.SetValue(AttachedButtonsProperty, value);
            }
        }
        #endregion

        #region ToolBars
        public static readonly DependencyProperty ToolBarsProperty =
        DependencyProperty.Register("ToolBars", typeof(Object), typeof(WindowTheme),
        new FrameworkPropertyMetadata(null));

        /// <summary>
        /// 设置/获取 窗口的工具栏对象
        /// </summary>
        public Object ToolBars
        {
            get
            {
                return (Object)this.GetValue(ToolBarsProperty);
            }
            set
            {
                this.SetValue(ToolBarsProperty, value);
            }
        }
        #endregion

        #region Services
        public static readonly DependencyProperty ServicesProperty = DependencyProperty.Register("Services", typeof(IList), typeof(WindowTheme));

        /// <summary>
        /// 设置获取窗口的扩展功能列表对象列表 Plug-in
        /// </summary>
        public IList Services
        {
            get
            {
                return (IList)this.GetValue(ServicesProperty);
            }
            set
            {
                this.SetValue(ServicesProperty, value);
            }
        }
        #endregion

        #region ExitCommand
        public static readonly DependencyProperty ExitCommandProperty =
        DependencyProperty.Register("ExitCommand", typeof(ICommand), typeof(WindowTheme),
        new PropertyMetadata(null));

        /// <summary>
        /// 获取/设置 窗口的关闭按钮命令,如果指定了命令默认关闭按钮功能将无效
        /// </summary>
        public ICommand ExitCommand
        {
            get
            {
                return (ICommand)this.GetValue(ExitCommandProperty);
            }
            set
            {
                this.SetValue(ExitCommandProperty, value);
            }
        }
        #endregion

        #region Attach
        /// <summary>
        /// 附加到窗口
        /// </summary>
        /// <param name="s"></param>
        private void Attach(DependencyObject s)
        {

            if (!this.Attached)
            {
                this.Window = s as Window;
                if (this.Window != null)
                {
                    this.Window.SourceInitialized += Window_SourceInitialized;
                    this.Window.Closed += Window_Closed;
                    if (this.Services != null)
                    {
                        foreach (var service in this.Services)
                        {
                            if (service is IService serv)
                            {
                                serv.Attach(this.Window);
                            }
                        }
                        //this.Services.Initialize(this.Window);
                    }
                    this.Attached = true;
                }
            }
        }
        #endregion

        #region Detach && destroy  
        private void Detach()
        {
            if (this.Attached)
            {
                this.Attached = false;
                this.Window.Closed -= Window_Closed;
                if (this.Services != null)
                {
                    //this.Services.UnInitialize();
                    foreach (var service in this.Services)
                    {
                        if (service is IService serv)
                        {
                            serv.UnAttach();
                        }
                    }
                    this.Services.Clear();
                    this.Services = null;
                }
                this.Window = null;
                if (hwndSource != null)
                {
                    hwndSource.RemoveHook(WindowProc);
                    hwndSource.Dispose();
                    hwndSource = null;
                }
                this.BorderBrush = null;
                this.Menu = null;
                this.AttachedButtons = null;
                this.ToolBars = null;

            }
        }
        #endregion

        #region  Window Events
        private void Window_Closed(object sender, EventArgs e)
        {
            this.Detach();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            if (!this.Attached)
            {
                return;
            }

            this.Window.SourceInitialized -= Window_SourceInitialized;
            IntPtr handle = (new WindowInteropHelper(this.Window)).Handle;
            this.hwndSource = HwndSource.FromHwnd(handle);
            if (this.hwndSource != null)
            {
                this.hwndSource.AddHook(WindowProc);
            }
        }
        #endregion

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (this.Services != null)
            {
                foreach (var service in this.Services)
                {
                    if (service is IService serv)
                    {
                        var result = serv.RouteMessage(hwnd, msg, wParam, lParam, ref handled);
                        if (handled)
                        {
                            return result;
                        }
                    }
                }
                //return this.Services.HandleMessage(hwnd, msg, wParam, lParam, ref handled);
            }
            return IntPtr.Zero;
        }

        protected override Freezable CreateInstanceCore()
        {
            Type type = base.GetType();
            return (Freezable)Activator.CreateInstance(type);
        }

        /// <summary>
        /// 所属窗口
        /// </summary>
        private Window Window { get; set; }

        private HwndSource hwndSource { get; set; }

        public Boolean Attached { get; private set; }
    }
}
