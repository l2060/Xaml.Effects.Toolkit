using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Assets.Editor.Utils
{
    internal class ConfigureUtil
    {
        private static Dictionary<String, String> configure = new Dictionary<String, String>();

        private static String ConfigFile = String.Empty;



        public static void Init()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.Combine(path, "Assets.Editor");
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
            ConfigFile = Path.Combine(root, "user.json");
            if (!File.Exists(ConfigFile))
            {
                SaveConfigure();
            }
            var json = File.ReadAllText(ConfigFile);
            configure = JsonSerializer.Deserialize<Dictionary<String, String>>(json);
        }


        private static void SaveConfigure()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            //设置支持中文的unicode编码
            options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
            //启用驼峰格式
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            //启用缩进设置
            options.WriteIndented = true;
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(configure, options));
        }





        public static String GetValue(String key)
        {
            if (configure.TryGetValue(key, out var value))
            {
                return value;
            }
            return String.Empty;
        }


        public static void SetValue(String key, String value)
        {
            configure[key] = value;
            SaveConfigure();
        }

    }
}
