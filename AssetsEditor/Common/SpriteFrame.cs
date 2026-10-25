using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Assets.Editor.Common
{


    public class SpriteFrameResult
    {
        public Bitmap Image { get; set; }
        public List<SpriteFrame> Frames = new List<SpriteFrame>();
    }


    public class SpriteFrame
    {
        [JsonPropertyName("x")]
        public Int32 X { get; set; }
        [JsonPropertyName("y")]
        public Int32 Y { get; set; }
        [JsonPropertyName("w")]
        public Int32 Width { get; set; }
        [JsonPropertyName("h")]
        public Int32 Height { get; set; }
        [JsonPropertyName("ox")]
        public Int32 OffsetX { get; set; }
        [JsonPropertyName("oy")]
        public Int32 OffsetY { get; set; }
    }
}
