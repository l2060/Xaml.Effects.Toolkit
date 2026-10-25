using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "xaml-ui-smoke");
        Directory.CreateDirectory(output);
        var bindingErrors = new StringWriter();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(bindingErrors));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Xaml.Effects.Toolkit;component/Styles/Theme.xaml", UriKind.Relative) });
        app.Resources["GraceFont"] = new FontFamily(new Uri("pack://application:,,,/"), "/Assets.Editor;component/Assets/Fonts/#graceicon");
        foreach (var name in new[] { "DrawingMode", "DirectoryScanMode" })
        {
            var provider = new System.Windows.Data.ObjectDataProvider();
            ((System.ComponentModel.ISupportInitialize)provider).BeginInit();
            provider.ObjectType = typeof(Enum);
            provider.MethodName = "GetValues";
            provider.MethodParameters.Add(typeof(Assets.Editor.Views.ImportDialog).Assembly.GetType("Assets.Editor.Common." + name));
            ((System.ComponentModel.ISupportInitialize)provider).EndInit();
            app.Resources[name + "s"] = provider;
        }
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var types = typeof(Assets.Editor.Views.ImportDialog).Assembly.GetTypes()
                .Where(t => t.Namespace == "Assets.Editor.Views" && typeof(Window).IsAssignableFrom(t))
                .OrderBy(t => t.Name).ToArray();
            foreach (string theme in new[] { "Black", "White" })
            {
                var resources = new ResourceDictionary { Source = new Uri($"/Assets.Editor;component/Assets/Themes/{theme}.xaml", UriKind.Relative) };
                app.Resources.MergedDictionaries.Add(resources);
                foreach (var type in types)
                {
                    var window = (Window)Activator.CreateInstance(type);
                    Render(window, Path.Combine(output, $"{theme}-{type.Name}.png"));
                    CheckBounds((FrameworkElement)window.Content);
                    Console.WriteLine($"PASS {theme} {type.Name} {window.ActualWidth}x{window.ActualHeight}");
                    window.Close();
                }
                VerifyCheckBox(app);
                VerifyDisabledButton();
                if (theme == "White") VerifyLightStates(app, output);
                app.Resources.MergedDictionaries.Remove(resources);
            }
            var main = new Assets.Editor.MainWindow { Width = 800, Height = 560 };
            Console.WriteLine("Rendering main window");
            Render(main, Path.Combine(output, "MainWindow-minimum.png"));
            var root = (Grid)main.Content;
            var toolbar = root.Children.OfType<WrapPanel>().Single();
            if (root.RowDefinitions[0].ActualHeight + 1 < toolbar.DesiredSize.Height)
                throw new Exception("Main toolbar is clipped at minimum window size.");
            var light = new ResourceDictionary { Source = new Uri("/Assets.Editor;component/Assets/Themes/White.xaml", UriKind.Relative) };
            app.Resources.MergedDictionaries.Add(light);
            Render(main, Path.Combine(output, "White-MainWindow.png"));
            app.Resources.MergedDictionaries.Remove(light);
            main.Close();
            if (bindingErrors.ToString().Length != 0)
                throw new Exception("WPF binding errors: " + bindingErrors);
            Console.WriteLine($"PASS main toolbar; images: {output}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally { app.Shutdown(); }
    }

    private static void Render(Window window, string path, Action prepare = null)
    {
        // Exercise real WPF layout without putting test windows on the user's desktop.
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        prepare?.Invoke();
        window.UpdateLayout();
        var frame = (Border)VisualTreeHelper.GetChild(window, 0);
        foreach (string name in new[] { "close_btn", "max_btn", "min_btn1" })
        {
            var captionButton = (Button)window.Template.FindName(name, window);
            if (captionButton.Focusable || captionButton.IsTabStop || captionButton.Focus())
                throw new Exception($"Caption button must not take keyboard focus: {name}");
        }
        var attached = Xaml.Effects.Toolkit.Styles.Windows.WindowTheme.GetTheme(window)?.AttachedButtons as DependencyObject;
        if (attached != null)
            foreach (var button in LogicalChildren(attached).OfType<Button>().Where(b => b.ToolTip as string == "更换主题"))
                if (button.Focusable || button.IsTabStop || button.Focus())
                    throw new Exception("Theme button must not take keyboard focus.");
        if (frame.BorderThickness.Left < 1 || frame.BorderBrush is not SolidColorBrush borderBrush || borderBrush.Color.A == 0 || borderBrush.Opacity == 0)
            throw new Exception("Window theme border is missing.");
        foreach (var canvas in LogicalChildren((FrameworkElement)window.Content).OfType<Assets.Editor.Controls.ImageCanvas>())
            if (!double.IsFinite(canvas.RenderSize.Width) || !double.IsFinite(canvas.RenderSize.Height))
                throw new Exception("ImageCanvas has a non-finite render size.");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var expectedBorder = ((SolidColorBrush)window.FindResource("GLOBAL_WINDOW_BORDER_COLOR")).Color;
        foreach (var point in new[] { (0, bitmap.PixelHeight / 2), (bitmap.PixelWidth - 1, bitmap.PixelHeight / 2), (bitmap.PixelWidth / 2, 0), (bitmap.PixelWidth / 2, bitmap.PixelHeight - 1) })
        {
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(point.Item1, point.Item2, 1, 1), pixel, 4, 0);
            if (pixel[0] != expectedBorder.B || pixel[1] != expectedBorder.G || pixel[2] != expectedBorder.R || pixel[3] != 255)
                throw new Exception($"Window border pixel is missing or incorrect at {point}: {window.GetType().Name}");
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void CheckBounds(FrameworkElement root)
    {
        foreach (var element in LogicalChildren(root))
        {
            if (element is not Control && element is not TextBlock) continue;
            if (!element.IsVisible) continue;
            var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            if (bounds.Left < -1 || bounds.Top < -1 || bounds.Right > root.ActualWidth + 1 || bounds.Bottom > root.ActualHeight + 1)
                throw new Exception($"Clipped {element.GetType().Name}: {bounds}, content {root.RenderSize}");
        }
    }

    private static IEnumerable<FrameworkElement> LogicalChildren(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<FrameworkElement>())
        {
            yield return child;
            foreach (var descendant in LogicalChildren(child)) yield return descendant;
        }
    }

    private static void VerifyLightStates(Application app, string output)
    {
        var states = new[] { "Normal", "Hover", "Pressed", "Selected", "Disabled", "Focus" };
        var grid = new Grid { Margin = new Thickness(20) };
        for (int c = 0; c < states.Length; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < 6; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        var controls = new List<(Control Control, string State)>();
        for (int c = 0; c < states.Length; c++)
        {
            var heading = new TextBlock { Text = states[c], Foreground = (Brush)app.FindResource("GLOBAL_FOREGROUND_COLOR") };
            Grid.SetColumn(heading, c); grid.Children.Add(heading);
            Control[] items = {
                new Button { Content = "Action" },
                new RadioButton { Content = "Option", GroupName = "state" + c, IsChecked = c is 3 or 4 },
                new CheckBox { Content = "Check", IsChecked = c is 3 or 4 },
                new TextBox { Text = "Input text" },
                new ComboBox { ItemsSource = new[] { "Choice", "Other" }, SelectedIndex = 0 }
            };
            for (int r = 0; r < items.Length; r++)
            {
                var control = items[r]; control.Margin = new Thickness(0, 0, 12, 10);
                control.VerticalContentAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, c); Grid.SetRow(control, r + 1); grid.Children.Add(control);
                controls.Add((control, states[c]));
            }
        }
        var window = new Window { Title = "Light theme states", Width = 940, SizeToContent = SizeToContent.Height,
            Content = grid, Style = (Style)app.FindResource("xKit-WindowChrome VisualStudio") };
        Xaml.Effects.Toolkit.Styles.Windows.WindowTheme.SetTheme(window, new Xaml.Effects.Toolkit.Styles.Windows.WindowTheme {
            BorderBrush = (Brush)app.FindResource("GLOBAL_WINDOW_BORDER_COLOR"), MaxButton = Visibility.Collapsed, MinButton = Visibility.Collapsed });
        Render(window, Path.Combine(output, "White-ControlStates.png"), () => {
            foreach (var (control, state) in controls)
            {
                if (state == "Disabled") control.IsEnabled = false;
                if (state == "Hover") SetState(control, typeof(UIElement), "IsMouseOver", true);
                if (state == "Focus")
                {
                    SetState(control, typeof(UIElement), "IsKeyboardFocused", true);
                    SetState(control, typeof(UIElement), "IsKeyboardFocusWithin", true);
                }
                if (state == "Pressed" && control is System.Windows.Controls.Primitives.ButtonBase)
                    SetState(control, typeof(System.Windows.Controls.Primitives.ButtonBase), "IsPressed", true);
            }
        });
        foreach (var (control, state) in controls.Where(c => c.State == "Disabled"))
        {
            var expected = ((SolidColorBrush)app.FindResource("GLOBAL_BUTTON_DISABLE_FOREGROUND")).Color;
            if (((SolidColorBrush)control.Foreground).Color != expected)
                throw new Exception($"Disabled {control.GetType().Name} has incorrect text color.");
        }
        foreach (var pair in new[] {
            ("GLOBAL_FOREGROUND_COLOR", "GLOBAL_BACKGROUND_COLOR"),
            ("GLOBAL_FOREGROUND_COLOR", "GLOBAL_PRIMARY_COLOR"),
            ("GLOBAL_BUTTON_OVER_FOREGROUND", "GLOBAL_BUTTON_OVER_BACKGROUND"),
            ("GLOBAL_BUTTON_PRESSED_FOREGROUND", "GLOBAL_BUTTON_PRESSED_BACKGROUND"),
            ("GLOBAL_RADIO_CHECKED_FOREGROUND", "GLOBAL_RADIO_CHECKED_BACKGROUND") })
        {
            double a = Luminance(((SolidColorBrush)app.FindResource(pair.Item1)).Color);
            double b = Luminance(((SolidColorBrush)app.FindResource(pair.Item2)).Color);
            if ((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) < 4.5)
                throw new Exception($"Insufficient light-theme text contrast: {pair}");
        }
        window.Close();
        Console.WriteLine("PASS light control states and text contrast");
    }

    private static void VerifyDisabledButton()
    {
        var button = new Button { Content = "Disabled", IsEnabled = false };
        button.Measure(new Size(120, 32));
        button.Arrange(new Rect(0, 0, 120, 32));
        button.ApplyTemplate();
        var border = (Border)button.Template.FindName("border", button);
        var presenter = (ContentPresenter)button.Template.FindName("contentPresenter", button);
        var foreground = (SolidColorBrush)System.Windows.Documents.TextElement.GetForeground(presenter);
        var background = (SolidColorBrush)border.Background;
        double a = Luminance(foreground.Color), b = Luminance(background.Color);
        if (foreground.Color.A != 255 || foreground.Opacity != 1 ||
            (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) < 3)
            throw new Exception("Disabled button text is not readable against its rendered background.");
        Console.WriteLine("PASS disabled button text contrast");
    }

    private static void SetState(DependencyObject control, Type owner, string name, bool value)
    {
        var key = (DependencyPropertyKey)owner.GetField(name + "PropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        control.SetValue(key, value);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte component) { double v = component / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    private static void VerifyCheckBox(Application app)
    {
        var check = new CheckBox { Content = "Check", IsChecked = true, IsThreeState = true };
        check.Measure(new Size(200, 32));
        check.Arrange(new Rect(0, 0, 200, 32));
        check.ApplyTemplate();
        // Set the WPF state through its key, so the actual template triggers are evaluated.
        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        check.SetValue(hoverKey, true);
        var border = (Border)check.Template.FindName("checkBoxBorder", check);
        if (((SolidColorBrush)border.Background).Color != ((SolidColorBrush)app.FindResource("GLOBAL_BUTTON_OVER_BACKGROUND")).Color)
            throw new Exception("CheckBox hover background does not follow the theme.");
        check.IsChecked = null;
        var mark = (System.Windows.Shapes.Rectangle)check.Template.FindName("indeterminateMark", check);
        if (mark.Opacity != 1 || ((SolidColorBrush)mark.Fill).Color != ((SolidColorBrush)app.FindResource("GLOBAL_FOREGROUND_COLOR")).Color)
            throw new Exception("CheckBox indeterminate mark is not visible in the theme.");
        Console.WriteLine("PASS checkbox hover and indeterminate states");
    }
}

