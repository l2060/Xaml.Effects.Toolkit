using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Resources;

namespace Xaml.Effects.Toolkit
{
    public class ThemeManager
    {

        static ThemeManager()
        {
            ThemeDictionary = (from s in Application.Current.Resources.MergedDictionaries where s.Source.OriginalString.EndsWith("Theme.xaml") select s).SingleOrDefault();
            getThemeName();
        }

        /// <summary>
        /// 系统主题字典
        /// </summary>
        public static ResourceDictionary ThemeDictionary { get; private set; }


        /// <summary>
        /// 当前主题名称
        /// </summary>
        public static String CurrentTheme { get; private set; }




        public static void LoadTheme(Stream stream, String resourceSearchDirectory = null)
        {
            if (ThemeDictionary == null)
            {
                ThemeDictionary = (from s in Application.Current.Resources.MergedDictionaries where s.Source.OriginalString.EndsWith("Theme.xaml") select s).SingleOrDefault();
            }
            if (ThemeDictionary != null)
            {
                if (String.IsNullOrEmpty(resourceSearchDirectory))
                {
                    resourceSearchDirectory = Environment.CurrentDirectory;
                    if (!resourceSearchDirectory.EndsWith("\\")) resourceSearchDirectory = resourceSearchDirectory + "\\";
                }
                ParserContext pc = new ParserContext();
                pc.XmlnsDictionary.Add("", "Xaml.Effect.Demo");
                pc.XmlnsDictionary.Add("", "Xaml.Effects.Toolkit");
                pc.BaseUri = new Uri(resourceSearchDirectory, UriKind.Absolute);
                ResourceDictionary resourceDictionary = XamlReader.Load(stream, pc) as ResourceDictionary;
                foreach (DictionaryEntry key in resourceDictionary)
                {
                    if (ThemeDictionary.Contains(key.Key))
                    {
                        ThemeDictionary.Remove(key.Key);
                    }
                    ThemeDictionary.Add(key.Key, key.Value);
                }
                getThemeName();
            }

        }

        // /Xaml.Effect.Demo;component/Assets/Themes/background.png

        /// <summary>
        /// 从资源加载
        /// /Xaml.Effect.Demo;component/Assets/Themes/White.xaml
        /// </summary>
        /// <param name="resourcePath"></param>
        /// <param name="resourceSearchDirectory"></param>
        public static void LoadThemeFromResource(string resourcePath, String resourceSearchDirectory = null)
        {
            Uri uri = new Uri(resourcePath, UriKind.Relative);
            StreamResourceInfo info = Application.GetResourceStream(uri);
            LoadTheme(info.Stream, resourceSearchDirectory);
        }



        /// <summary>
        /// 从内嵌的资源加载
        /// Xaml.Effect.Demo://Themes.White.xaml
        /// </summary>
        /// <param name="resourcePath"></param>
        /// <param name="resourceSearchDirectory"></param>
        public static void LoadThemeFromEmbeddedResource(string resourcePath, String resourceSearchDirectory = null)
        {
            var spaces = resourcePath.Split("://");
            Assembly _assembly = Assembly.Load(spaces[0]);
            Stream istr = _assembly.GetManifestResourceStream(String.Join('.', spaces));
            LoadTheme(istr, resourceSearchDirectory);
        }


        public static void LoadTheme(String fileName, String resourceSearchDirectory = null)
        {
            using (var stream = File.OpenRead(fileName))
            {
                LoadTheme(stream, resourceSearchDirectory);
            }
            ;
        }



        private static void getThemeName()
        {
            if (ThemeDictionary != null)
            {
                foreach (DictionaryEntry item in ThemeDictionary)
                {
                    if (item.Key.Equals("ThemeName"))
                    {
                        CurrentTheme = item.Value.ToString();
                        return;
                    }
                }
            }
            CurrentTheme = String.Empty;
        }


    }
}
