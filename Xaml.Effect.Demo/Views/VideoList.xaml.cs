using System.Collections.ObjectModel;
using System.Windows;
using Xaml.Effect.Demo.Models;

namespace Xaml.Effect.Demo.Views
{
    /// <summary>
    /// VideoList.xaml 的交互逻辑
    /// </summary>
    public partial class VideoList : Window
    {
        public VideoListModel Model { get; set; }


        public VideoList(ObservableCollection<VideoInfo> VideoList)
        {

            InitializeComponent();
            this.DataContext = this.Model = new VideoListModel(VlcControl, VideoList);
            this.Model.OnClose += Model_OnClose;

        }

        private void Model_OnClose(object sender, Effects.Toolkit.Model.WindowDestroyArgs e)
        {

            e.Apply(this);
        }
    }
}
