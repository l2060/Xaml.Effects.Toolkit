using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace Xaml.Effects.Toolkit.Uitity
{
    public static class ExtendHelper
    {
        static ExtendHelper()
        {
            EnumDictionary = new ConcurrentDictionary<Enum, string>();
        }

        /// <summary>
        /// 获取Enum 枚举类型的注释
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static string GetDescription(this Enum obj)
        {
            return EnumDictionary.GetOrAdd(obj, (o) =>
            {
                var type = o.GetType();
                FieldInfo field = type.GetField(Enum.GetName(type, o));
                DescriptionAttribute descAttr = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;
                if (descAttr == null)
                {
                    return string.Empty;
                }
                return descAttr.Description;
            });
        }





        public static T[] Split<T>(this String src, char[] separator, StringSplitOptions options)
        {
            List<T> lst = new List<T>();
            var arry = src.Split(separator, options);
            foreach (var item in arry)
            {
                var rv = (T)Convert.ChangeType(item, typeof(T));
                lst.Add(rv);
            }
            return lst.ToArray();
        }

        /// <summary>
        /// 将wpf的颜色对象转换为整型的Winform颜色值
        /// </summary>
        /// <param name="brush"></param>
        /// <returns></returns>
        public static System.Drawing.Color ToColor(this System.Windows.Media.Color color)
        {
            return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }


        /// <summary>
        /// 将来自winform的argb整型颜色值转换为wpf颜色对象
        /// </summary>
        /// <param name="wfargb"></param>
        /// <returns></returns>
        public static System.Windows.Media.Color ToColor(this System.Drawing.Color wfargb)
        {
            return System.Windows.Media.Color.FromArgb(wfargb.A, wfargb.R, wfargb.G, wfargb.B);
        }


        public static String ToHex26(this Int32 n)
        {
            string s = string.Empty;
            while (n > 0)
            {
                int m = n % 26;
                if (m == 0) m = 26;
                s = (char)(m + 64) + s;
                n = (n - m) / 26;
            }
            return s;
        }

        public static String ToHex26(this Int16 n)
        {
            string s = string.Empty;
            while (n > 0)
            {
                int m = n % 26;
                if (m == 0) m = 26;
                s = (char)(m + 64) + s;
                n = (Int16)((n - m) / 26);
            }
            return s;
        }
        public static String ToHex26(this Byte n)
        {
            string s = string.Empty;
            while (n > 0)
            {
                int m = n % 26;
                if (m == 0) m = 26;
                s = (char)(m + 64) + s;
                n = (Byte)((n - m) / 26);
            }
            return s;
        }

        public static Point Round(this Point n, Int32 decimals)
        {
            var x = Math.Round(n.X, decimals);
            var y = Math.Round(n.Y, decimals);
            return new Point(x, y);
        }


        public static Int32? ToInt32(this String Text)
        {
            Int32 i = 0;
            if (Int32.TryParse(Text, out i))
            {
                return i;
            }
            return null;
        }


        public static Double? ToDouble(this String Text)
        {
            Double i = 0;
            if (Double.TryParse(Text, out i))
            {
                return i;
            }
            return null;
        }

        public static Decimal? ToDecimal(this String Text)
        {
            Decimal i = 0;
            if (Decimal.TryParse(Text, out i))
            {
                return i;
            }
            return null;
        }


        /// <summary>
        /// 获取指定枚举类型的所有成员
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T[] GetMembers<T>() where T : Enum
        {
            var array = Enum.GetValues(typeof(T));
            T[] Result = new T[array.Length];
            array.CopyTo(Result, 0);
            return Result;
        }



        public static List<T> TakeAndRemove<T>(this List<T> source, Int32 count)
        {
            var src = source.Take(count).ToList();
            source.RemoveAll(s => src.Contains(s));
            return src.ToList();
        }


        /// <summary>
        /// 指定某个字段的重复
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<T> Distinct<T, V>(this IEnumerable<T> source, Func<T, V> keySelector)
        {
            return source.Distinct(Equality<T>.CreateComparer(keySelector));
        }
        /// <summary>
        /// 指定某个字段的重复
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<T> Distinct<T, V>(this IEnumerable<T> source, Func<T, V> keySelector, IEqualityComparer<V> comparer)
        {
            return source.Distinct(Equality<T>.CreateComparer(keySelector, comparer));
        }

        /// <summary>
        /// 指定某个字段的包含
        /// </summary>
        /// <returns></returns>
        public static bool Contains<T, V>(this IEnumerable<T> source, T value, Func<T, V> keySelector)
        {
            return source.Contains(value, Equality<T>.CreateComparer(keySelector));
        }


        /// <summary>
        /// 枚举注释字典
        /// </summary>
        private static ConcurrentDictionary<Enum, String> EnumDictionary { get; set; }
    }

    /// <summary>
    /// 比较类生成
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public static class Equality<T>
    {
        public static IEqualityComparer<T> CreateComparer<V>(Func<T, V> keySelector)
        {
            return new CommonEqualityComparer<V>(keySelector);
        }
        public static IEqualityComparer<T> CreateComparer<V>(Func<T, V> keySelector, IEqualityComparer<V> comparer)
        {
            return new CommonEqualityComparer<V>(keySelector, comparer);
        }

        public class CommonEqualityComparer<V> : IEqualityComparer<T>
        {
            private Func<T, V> keySelector;
            private IEqualityComparer<V> comparer;

            public CommonEqualityComparer(Func<T, V> keySelector, IEqualityComparer<V> comparer)
            {
                this.keySelector = keySelector;
                this.comparer = comparer;
            }
            public CommonEqualityComparer(Func<T, V> keySelector)
                : this(keySelector, EqualityComparer<V>.Default)
            { }

            public bool Equals(T x, T y)
            {
                return comparer.Equals(keySelector(x), keySelector(y));
            }
            public int GetHashCode(T obj)
            {
                return comparer.GetHashCode(keySelector(obj));
            }
        }
    }


}
