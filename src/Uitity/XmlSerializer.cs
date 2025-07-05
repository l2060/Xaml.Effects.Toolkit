using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Xaml.Effects.Toolkit.Uitity
{
    public class XmlSerializer
    {
        public static T Deserialize<T>(string path)
        {
            try
            {
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.CheckCharacters = true;
                settings.ConformanceLevel = ConformanceLevel.Document;
                settings.IgnoreWhitespace = true;
                settings.IgnoreProcessingInstructions = true;
                settings.IgnoreWhitespace = true;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    using (XmlReader reader = XmlReader.Create(stream, settings))
                    {
                        System.Xml.Serialization.XmlSerializer xs = new System.Xml.Serialization.XmlSerializer(typeof(T));
                        return (T)xs.Deserialize(reader);
                    }
                }
            }
            catch (Exception)
            {
                throw new Exception("Xml deserialization failed!");
            }
        }


        /// <summary>
        /// 将一个对象序列化为XML字符串
        /// </summary>
        /// <param name="o">要序列化的对象</param>
        /// <param name="encoding">编码方式</param>
        /// <returns>序列化产生的XML字符串</returns>
        public static void Serialize<T>(string path, T o, Encoding encoding)
        {
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = encoding;
                settings.Indent = true;
                //不生成命名空间
                XmlSerializerNamespaces namespaces = new XmlSerializerNamespaces();
                namespaces.Add("", "");
                //OmitXmlDeclaration表示不生成声明头，默认是false，OmitXmlDeclaration为true，会去掉<?xml version="1.0" encoding="UTF-8"?>
                //settings.OmitXmlDeclaration = true;
                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                {
                    System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
                    serializer.Serialize(writer, o, namespaces);
                }
                ;
            }
        }
    }
}
