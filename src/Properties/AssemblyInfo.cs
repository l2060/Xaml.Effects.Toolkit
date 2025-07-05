using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;

// 在此类的 SDK 样式项目中，现在，在此文件中早前定义的几个程序集属性将在生成期间自动添加，并使用在项目属性中定义的值进行填充。有关包含的属性以及如何定制此过程的详细信息，请参阅
// https://aka.ms/assembly-info-properties


// 将 ComVisible 设置为 false 会使此程序集中的类型对 COM 组件不可见。如果需要从 COM 访问此程序集中的类型，请将该类型的 ComVisible
// 属性设置为 true。

[assembly: ComVisible(false)]

// 如果此项目向 COM 公开，则下列 GUID 用于 typelib 的 ID。

[assembly: Guid("b4740fd5-02eb-4d04-94e3-2c4562fed2c1")]


[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //主题特定资源词典所处位置
                                     //(未在页面中找到资源时使用，
                                     //或应用程序资源字典中找到时使用)
    ResourceDictionaryLocation.SourceAssembly //常规资源词典所处位置
                                              //(未在页面中找到资源时使用，
                                              //、应用程序或任何主题专用资源字典中找到时使用)
)]


//[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Magic.Actions")]
//[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Magic.Commands")]
//[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Magic.Common")] ///
//[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Magic.Controls")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Actions")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Controls")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Behaviors")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Common")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Converter")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Effects")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Uitity")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Animation")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Animation.Creators")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Animation.Increasers")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Animation.Wrappers")]
[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Styles.Windows")]


//[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Magic.Styles")]
//[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Magic.Styles.Controls")]
//[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit.Styles.Windows")]
//[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Magic.Adorners")]
//[assembly: XmlnsDefinition("http://schemas.l2030.com/xkit", "Magic.Decorators")]

[assembly: XmlnsPrefix("http://schemas.l2030.com/xkit", "Xaml.Effects.Toolkit")]