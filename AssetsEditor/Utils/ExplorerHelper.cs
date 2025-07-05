using System.Diagnostics;

namespace Assets.Editor.Utils
{
    public static class ExplorerHelper
    {


        public static void ExploreFile(string filePath)
        {
            Process proc = new Process();
            proc.StartInfo.FileName = "explorer";
            //打开资源管理器
            proc.StartInfo.Arguments = $"/select,\"{filePath}\"";
            //选中"notepad.exe"这个程序,即记事本
            proc.Start();

        }

    }
}
