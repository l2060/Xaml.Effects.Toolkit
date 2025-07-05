using Resource.Package.Assets.Common;
using Resource.Package.Assets.Secure;
using Resource.Package.Assets.Version;
using System.Drawing;
using System.Text;


namespace Resource.Package.Assets
{
    public class AssetFileStream : IDisposable
    {
        private static Byte[] defaultKey = Encoding.UTF8.GetBytes("!1a@2b#3c$4d%5e^6f&7g*8h(9i)0j.+");
        private static readonly UInt64 MAGIC = 8319385958189441315; // #!Assets
        private FileHeader header;
        private Byte[] password;
        private List<FileInfomation> Infomations = new List<FileInfomation>();
        private FileStream fileStream;

        public static AssetFileStream Create(String filename, String password, CompressionOption compressionOption = CompressionOption.NeverCompress)
        {
            var pwd = BuildPassword(password);
            var meta = new FileHeader();
            meta.CompressOption = compressionOption;
            meta.Magic = MAGIC;
            meta.Version = new byte[] { 1, 0, 2 };
            meta.TableDataSize = 0;
            meta.TableDataAddr = 24;
            meta.NumberOfFiles = 0;
            using (var file = File.Open(filename, FileMode.Create))
            {
                using (var writer = new BinaryWriter(file))
                {
                    writer.Write(meta.Magic);
                    writer.Write(meta.Version);
                    writer.Write((Byte)meta.CompressOption);
                    writer.Write(meta.NumberOfFiles);
                    writer.Write(meta.TableDataAddr);
                    writer.Write(meta.TableDataSize);
                    var tab = AES.Encrypt(new Byte[0], pwd);
                    writer.Write(tab);
                    writer.Seek(20, SeekOrigin.Begin);
                    writer.Write(tab.Length);
                }
            }
            ;
            return Open(filename, password);
        }


        public UInt32 NumberOfFiles
        {
            get
            {
                return this.header.NumberOfFiles;
            }
        }

        public static AssetFileStream Open(String filename, String password)
        {
            var stream = new AssetFileStream();
            stream.OpenFile(filename, password);
            return stream;
        }


        public void ChangePassword(String password)
        {
            this.password = BuildPassword(password);
            this.Save();
        }

        public void Save()
        {
            using (var writer = new BinaryWriter(fileStream, Encoding.UTF8, true))
            {
                this.WriteIndex(writer);
            }
        }





        private static Byte[] BuildPassword(String password)
        {
            if (String.IsNullOrEmpty(password))
            {
                return defaultKey;
            }
            var originKey = Encoding.UTF8.GetBytes(password);
            var keys = originKey.Concat(defaultKey);
            return keys.Take(32).ToArray();
        }



        public void OpenFile(String filename, String password)
        {
            header.Version = new byte[3];
            this.password = BuildPassword(password);
            this.fileStream = File.Open(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            try
            {
                using (var reader = new BinaryReader(fileStream, Encoding.UTF8, true))
                {
                    header.Magic = reader.ReadUInt64();
                    if (header.Magic != MAGIC)
                    {
                        throw new Exception("无效的文件格式");
                    }
                    reader.Read(header.Version);
                    header.CompressOption = (CompressionOption)reader.ReadByte();
                    header.NumberOfFiles = reader.ReadUInt32();
                    header.TableDataAddr = reader.ReadUInt32();
                    header.TableDataSize = reader.ReadUInt32();
                    this.ReadIndex(reader);
                    header.Version = new Byte[] { 1, 0, 2 };
                }
            }
            catch (Exception ex)
            {
                this.fileStream.Close();
                throw ex;
            }
        }


        public IReadOnlyDataBlock GetInfomation(UInt32 index)
        {
            var info = this.Infomations[(Int32)index];
            var node = new DataBlock();
            node.lpType = info.lpType;
            node.lpRenderType = info.lpRenderType;
            node.Unknown1 = info.Unknown1;
            node.Unknown2 = info.Unknown2;
            node.OffsetX = info.OffsetX;
            node.OffsetY = info.OffsetY;
            return node;
        }

        public Boolean Recycle()
        {
            var IsChanged = false;
            while (this.NumberOfFiles > 0)
            {
                var Index = this.NumberOfFiles - 1;
                var info = this.Infomations[(Int32)Index];
                if (info.lpData > 0) break;
                this.Infomations.Remove(info);
                this.header.NumberOfFiles--;
                IsChanged = true;
            }
            if (IsChanged)
            {
                this.Save();
            }
            return IsChanged;
        }





        /// <summary>
        /// 仅读取图片信息不读取数据
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public IReadOnlyLazyInfo? LazyRead(UInt32 index)
        {
            if (index >= this.NumberOfFiles) return null;
            var info = this.Infomations[(Int32)index];
            var node = new LazyDataInfo();
            node.Index = (Int32)index;
            node.lpType = info.lpType;
            node.lpRenderType = info.lpRenderType;
            node.Unknown1 = info.Unknown1;
            node.Unknown2 = info.Unknown2;
            node.OffsetX = info.OffsetX;
            node.OffsetY = info.OffsetY;
            node.Width = info.Width;
            node.Height = info.Height;
            node.ReadData = this.ReadItemData;
            return node;
        }





        internal Byte[]? ReadItemData(Int32 index)
        {
            if (index >= this.NumberOfFiles) return null;
            var info = this.Infomations[index];
            using (var reader = new BinaryReader(fileStream, Encoding.UTF8, true))
            {
                if (info.lpData > 0 && info.lpSize > 0)
                {
                    var data = new Byte[info.lpSize];
                    reader.BaseStream.Position = info.lpData;
                    reader.Read(data);
                    if (info.lpRawSize != info.lpSize)
                    {
                        // 解密 data
                        data = ZLib.Decompress(data, info.lpRawSize);
                    }
                    return data;
                }
                return null;
            }
        }



        /// <summary>
        /// 仅读取图片信息不读取数据
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public IReadOnlyDataInfo? ReadInfo(UInt32 index)
        {
            if (index >= this.NumberOfFiles) return null;
            var info = this.Infomations[(Int32)index];
            var node = new DataBlock();
            node.Index = (Int32)index;
            node.lpType = info.lpType;
            node.lpRenderType = info.lpRenderType;
            node.Unknown1 = info.Unknown1;
            node.Unknown2 = info.Unknown2;
            node.OffsetX = info.OffsetX;
            node.OffsetY = info.OffsetY;
            node.Width = info.Width;
            node.Height = info.Height;
            return node;
        }


        /// <summary>
        /// 读取图片信息及数据
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public IReadOnlyDataBlock? Read(UInt32 index)
        {
            if (index >= this.NumberOfFiles) return null;
            var info = this.Infomations[(Int32)index];
            using (var reader = new BinaryReader(fileStream, Encoding.UTF8, true))
            {
                var node = new DataBlock();
                node.Index = (Int32)index;
                node.lpType = info.lpType;
                node.lpRenderType = info.lpRenderType;
                node.Unknown1 = info.Unknown1;
                node.Unknown2 = info.Unknown2;
                node.OffsetX = info.OffsetX;
                node.OffsetY = info.OffsetY;
                node.Width = info.Width;
                node.Height = info.Height;
                if (info.lpData > 0 && info.lpSize > 0)
                {
                    var data = new Byte[info.lpSize];
                    reader.BaseStream.Position = info.lpData;
                    reader.Read(data);
                    if (info.lpRawSize != info.lpSize)
                    {
                        // 解密 data
                        data = ZLib.Decompress(data, info.lpRawSize);
                    }
                    node.Data = data;
                }
                return node;
            }
        }

        public void UpdateOffset(UInt32 index, Point data)
        {
            var info = this.Infomations[(Int32)index];
            info.OffsetX = (Int16)data.X;
            info.OffsetY = (Int16)data.Y;
            this.Save();
        }


        public void UpdateOffsetNoWrite(UInt32 index, Point data)
        {
            var info = this.Infomations[(Int32)index];
            if (info != null)
            {
                info.OffsetX = (Int16)data.X;
                info.OffsetY = (Int16)data.Y;
            }
        }


        public void UpdateInfo(UInt32 index, DataInfo datainfo)
        {
            var info = this.Infomations[(Int32)index];
            if (info != null)
            {
                info.OffsetX = datainfo.OffsetX;
                info.OffsetY = datainfo.OffsetY;
                info.lpRenderType = datainfo.lpRenderType;
                info.Unknown1 = datainfo.Unknown1;
                info.Unknown2 = datainfo.Unknown2;
            }
            this.Save();
        }






        public void UpdateOffsets(Dictionary<UInt32, Point> datas)
        {
            foreach (var item in datas)
            {
                var info = this.Infomations[(Int32)item.Key];
                info.OffsetX = (Int16)item.Value.X;
                info.OffsetY = (Int16)item.Value.Y;
            }
            this.Save();
        }



        private async Task<Byte[]> Compressing(DataBlock item, Action report = null)
        {
            Byte[] bytes = item.Data;
            if (bytes.Length > 0 && header.CompressOption == CompressionOption.MuchPossibleCompress || header.CompressOption == CompressionOption.MustCompressed)
            {
                bytes = await ZLib.Compress(bytes);
                if (bytes.Length >= item.Data.Length && header.CompressOption == CompressionOption.MuchPossibleCompress)
                {
                    bytes = item.Data;
                }
            }
            if (report != null) report();
            return bytes;
        }

        public UInt32 Import(IReadOnlyList<DataBlock> items, Action<Double> process = null)
        {
            var tasks = new List<Task<Byte[]>>();
            var num = 0;
            var total = items.Count * 2 + 1;
            var report = () =>
            {
                num++;
                if (process != null) process((Double)num / (Double)total * 100.0f);
            };
            foreach (var item in items)
            {
                tasks.Add(Compressing(item, report));
            }
            Task.WaitAll(tasks.ToArray());
            GC.Collect();
            using (var writer = new BinaryWriter(fileStream, Encoding.UTF8, true))
            {
                for (var i = 0; i < tasks.Count; i++)
                {
                    var item = items[i];
                    var bufData = tasks[i].Result;
                    var info = this.Apply(item.Index);
                    info.Width = item.Width;
                    info.Height = item.Height;
                    info.lpType = item.lpType;
                    info.lpRawSize = (UInt32)item.Data.Length;
                    info.OffsetX = item.OffsetX;
                    info.OffsetY = item.OffsetY;
                    info.lpRenderType = item.lpRenderType;
                    if (bufData.Length > 0)
                    {
                        if (info.lpSize < bufData.Length)
                        {
                            info.lpData = header.TableDataAddr;
                            header.TableDataAddr = info.lpData + (UInt32)bufData.Length;
                        }
                        info.lpSize = (UInt32)bufData.Length;
                        writer.Seek((Int32)info.lpData, SeekOrigin.Begin);
                        writer.Write(bufData);
                    }
                    else
                    {
                        info.lpData = 0;
                    }
                    report();
                }
                this.WriteIndex(writer);
                report();
                return header.NumberOfFiles;
            }

        }




        private FileInfomation Apply(Int32 index)
        {
            var info = new FileInfomation();
            if (index >= 0)
            {
                if (index < this.Infomations.Count)
                {
                    info = this.Infomations[index];
                }
                else
                {
                    while (this.Infomations.Count <= index)
                    {
                        this.Infomations.Add(new FileInfomation());
                        header.NumberOfFiles++;
                    }
                    return this.Infomations.Last();
                }
                return info;
            }
            else
            {
                this.Infomations.Add(info);
                header.NumberOfFiles++;
                return info;
            }
        }


        public void Close()
        {
            if (fileStream != null)
            {
                fileStream.Close();
                fileStream.Dispose();
                fileStream = null;
                this.Infomations = null;
                this.password = null;
            }
        }

        public void Dispose()
        {
            this.Close();
        }

        private void ReadIndex(BinaryReader reader)
        {
            Byte[] raw;
            var data = new Byte[header.TableDataSize];
            reader.BaseStream.Position = header.TableDataAddr;
            reader.Read(data);

            try
            {
                raw = AES.Decrypt(data, this.password);
            }
            catch (Exception)
            {
                throw new Exception("无效的密码");
            }
            var stream = InfomationStream.GetReader(this.header.Version);
            using (var ms = new MemoryStream(raw))
            {
                this.Infomations = new List<FileInfomation>();
                using (var msReader = new BinaryReader(ms, Encoding.UTF8, true))
                {
                    for (int i = 0; i < header.NumberOfFiles; i++)
                    {
                        var info = stream.Read(msReader);
                        this.Infomations.Add(info);
                    }
                }
            }
        }






        private void WriteIndex(BinaryWriter writer)
        {
            var stream = InfomationStream.GetReader(this.header.Version);
            using (var ms = new MemoryStream())
            {
                using (var msWriter = new BinaryWriter(ms, Encoding.UTF8, true))
                {
                    for (int i = 0; i < header.NumberOfFiles; i++)
                    {
                        stream.Write(msWriter, Infomations[i]);
                    }
                }
                var tab = AES.Encrypt(ms.ToArray(), this.password);
                header.TableDataSize = (UInt32)tab.Length;
                //writer.Seek(8, SeekOrigin.Begin);
                //writer.Write(this.header.Version);
                writer.Seek(12, SeekOrigin.Begin);
                writer.Write(header.NumberOfFiles);
                writer.Write(header.TableDataAddr);
                writer.Write(header.TableDataSize);
                writer.Seek((Int32)header.TableDataAddr, SeekOrigin.Begin);
                writer.Write(tab);
            }
        }











    }
}
