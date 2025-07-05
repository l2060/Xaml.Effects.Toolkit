using System;

namespace Xaml.Effect.Demo.Models
{
    public class VideoInfo
    {
        public VideoInfo()
        {
            this.Domain = "";
            this.Url = "";
            this.Title = "";
            this.VideoUrl = "";

        }
        public Int32 Index { get; set; }
        public String VideoUrl { get; set; }
        public String Title { get; set; }

        public String Domain { get; set; }

        public String Url { get; set; }


    }
}

