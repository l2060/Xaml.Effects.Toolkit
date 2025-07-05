using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Xaml.Effect.Demo.Views;
using Xaml.Effects.Toolkit;
using Xaml.Effects.Toolkit.Model;

namespace Xaml.Effect.Demo.Models
{
    // https://blog.csdn.net/sinat_31608641/article/details/107255094
    public class MainWindowModel : DialogModel
    {

        string script = @"
                function findParent (parent){
                    var count = 0;
                    while(parent.parentElement != null && count < 8){
                        if(parent.parentElement.className.startsWith('itemCol')) return parent.parentElement;
                        parent = parent.parentElement;
                        count++;
                    }
                    return null;
                }
                document.addEventListener('click', function (event)
                {
                      var root = findParent(event.srcElement)
                        console.log(root);
                      if(root){
                          var titleElement = root.querySelector('div[class^=""title__""]');
                          console.log(titleElement);
                          if(titleElement){
                               window.chrome.webview.postMessage('@'+titleElement.innerText);
                          }
                      }
                });

                

                document.body.style.opacity = '0.1';
                console.log('监视中');
";






        private WebView2 WebCore { get; set; }

        /// <summary>
        /// 应用,需要时在派生类中重写
        /// </summary>
        public ICommand VideoListCommand { get; protected set; }


        /// <summary>
        /// 应用,需要时在派生类中重写
        /// </summary>
        public ICommand ThemesCommand { get; protected set; }


        public ICommand SettingCommand { get; protected set; }





        public ICommand HomeCommand { get; protected set; }

        public ICommand RefreshCommand { get; protected set; }

        public ICommand GoBackCommand { get; protected set; }




        public ObservableCollection<VideoInfo> VideoList { get; protected set; }




        public MainWindowModel()
        {
            Trace.WriteLine("do");
            this.SettingCommand = new RelayCommand(Settings_Click);
            this.ThemesCommand = new RelayCommand(Themes_Click);
            this.VideoListCommand = new RelayCommand(VideoList_Click);
            this.HomeCommand = new RelayCommand(Home_Click);
            this.GoBackCommand = new RelayCommand(Goback_Click);
            this.RefreshCommand = new RelayCommand(Refresh_Click);
            this.VideoList = new ObservableCollection<VideoInfo>();
            this.webOpacity = 100;
            this.Title = "Console";
        }



        public async Task InitWebView(WebView2 WebCore)
        {
            this.WebCore = WebCore;
            var webView2Environment = await CoreWebView2Environment.CreateAsync(null, Environment.CurrentDirectory);
            await WebCore.EnsureCoreWebView2Async(webView2Environment);
            WebCore.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            WebCore.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
            WebCore.CoreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;
            WebCore.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            WebCore.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
            WebCore.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;

            WebCore.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            WebCore.CoreWebView2.ScriptDialogOpening += CoreWebView2_ScriptDialogOpening;

            WebCore.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);

            WebCore.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
            WebCore.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;




            await WebCore.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            WebCore.CoreWebView2.Navigate("https://5721004.xyz/bu/?/3.25/");
        }

        private void CoreWebView2_ScriptDialogOpening(object sender, CoreWebView2ScriptDialogOpeningEventArgs e)
        {
            //e.Message
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            Trace.WriteLine(e.Uri.ToString());
            e.Handled = true;
        }

        private void CoreWebView2_DocumentTitleChanged(object sender, object e)
        {
            this.Title = WebCore.CoreWebView2.DocumentTitle;
        }

        private async void CoreWebView2_WebResourceResponseReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            try
            {
                var response = e.Response;
                var s = response.Headers.FirstOrDefault(e => e.Key == "content-length");
                if (s.Value == "0")
                {
                    return;
                }

                Stream content = await response.GetContentAsync();
                string jsonText = new StreamReader(content).ReadToEnd();
                if (response.StatusCode == 200 && jsonText.StartsWith("#EXTM3U"))
                {
                    Trace.WriteLine(e.Request.Uri);
                    //NewVideo(e.Request.Uri);
                    if (VideoName == null || VideoName.Length == 0)
                    {
                        var arry = e.Request.Uri.Split('/');
                        var lastSegment = arry[arry.Length - 1];
                        VideoName = lastSegment.Split(new char[] { '?', '&', '=' })[0];
                    }
                    var urls = WebCore.CoreWebView2.Source.Split("/", StringSplitOptions.None);
                    var domains = urls[0..3];
                    var domain = String.Join("/", domains);
                    var video = new VideoInfo()
                    {
                        VideoUrl = e.Request.Uri,
                        Title = VideoName,
                        Domain = domain,
                        Url = WebCore.CoreWebView2.Source
                    };
                    if (!this.VideoList.Any(x => x.VideoUrl == e.Request.Uri))
                    {
                        this.VideoList.Insert(0, video);
                    }
                    VideoName = "";
                }
            }
            catch (Exception ex)
            {

            }
        }



        private void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            //if (e.Request.Uri.EndsWith(".png") || e.Request.Uri.EndsWith(".jpg") || e.Request.Uri.EndsWith(".webp"))
            //{
            //    var response = WebCore.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not found", null);
            //    e.Response = response;
            //}

            //    Trace.WriteLine(e.Request.Uri);


        }


        private String VideoName { get; set; }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var text = e.WebMessageAsJson.Trim('"');
            if (text.StartsWith("@"))
            {
                VideoName = text.Substring(1);
            }
        }




        private void Themes_Click()
        {
            // /Xaml.Effect.Demo;component/Assets/Themes/background.png
            if (ThemeManager.CurrentTheme == "Default")
            {
                ThemeManager.LoadThemeFromResource("/Xaml.Effect.Demo;component/Assets/Themes/White.xaml");
            }
            else if (ThemeManager.CurrentTheme == "White")
            {
                ThemeManager.LoadThemeFromResource("/Xaml.Effect.Demo;component/Assets/Themes/Black.xaml");
            }
            else if (ThemeManager.CurrentTheme == "Black")
            {
                ThemeManager.LoadThemeFromResource("/Xaml.Effect.Demo;component/Assets/Themes/Image.xaml");
            }
            else
            {
                ThemeManager.LoadThemeFromResource("/Xaml.Effect.Demo;component/Assets/Themes/White.xaml");
            }
            //Console.WriteLine(ThemeManager.CurrentTheme);
        }




        private void Settings_Click()
        {

        }

        private void Goback_Click()
        {
            WebCore.CoreWebView2.GoBack();
        }

        private void Refresh_Click()
        {
            WebCore.CoreWebView2.Navigate("https://twitter.com/home");
        }


        private void Home_Click()
        {
            // https://www.nnu77.com/favoriteList
            // https://hotleak.vip/little_sula
            WebCore.CoreWebView2.Navigate("https://hotleak.vip/anaimiya/video");
        }


        private void VideoList_Click()
        {
            if (this.VideoListWindow != null)
            {
                this.VideoListWindow.Close();
                return;
            }
            this.VideoListWindow = new VideoList(this.VideoList);
            this.VideoListWindow.Closed += Video_Closed;

            this.VideoListWindow.Show();

        }


        private VideoList VideoListWindow { get; set; }


        private void Video_Closed(object sender, EventArgs e)
        {
            this.VideoListWindow = null;
        }

        public Int32 WebOpacity
        {
            get
            {
                return this.webOpacity;
            }
            set
            {
                WebCore.CoreWebView2.ExecuteScriptAsync($"document.body.style.opacity = '{value / 100.0f}';");
                base.SetProperty(ref this.webOpacity, value);
            }
        }

        private Int32 webOpacity;

    }
}
