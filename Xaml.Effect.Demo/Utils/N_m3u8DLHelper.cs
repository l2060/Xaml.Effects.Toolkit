﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Xaml.Effect.Demo.Utils
{
    internal static class N_m3u8DLHelper
    {

        // 
        /*
             --tmp-dir <tmp-dir>                      设置临时文件存储目录
              --save-dir <save-dir>                    设置输出目录
              --save-name <save-name>                  设置保存文件名
              --base-url <base-url>                    设置BaseURL
              --thread-count <number>                  设置下载线程数 [default: 16]
              --download-retry-count <number>          每个分片下载异常时的重试次数 [default: 3]
              --auto-select                            自动选择所有类型的最佳轨道 [default: False]
              --skip-merge                             跳过合并分片 [default: False]
              --skip-download                          跳过下载 [default: False]
              --check-segments-count                   检测实际下载的分片数量和预期数量是否匹配 [default: True]
              --binary-merge                           二进制合并 [default: False]
              --del-after-done                         完成后删除临时文件 [default: True]
              --no-date-info                           混流时不写入日期信息 [default: False]
              --write-meta-json                        解析后的信息是否输出json文件 [default: False]
              --append-url-params                      将输入Url的Params添加至分片, 对某些网站很有用, 例如 kakao.com [default: False]
              -mt, --concurrent-download               并发下载已选择的音频、视频和字幕 [default: False]
              -H, --header <header>                    为HTTP请求设置特定的请求头, 例如:
                                                       -H "Cookie: mycookie" -H "User-Agent: iOS"
              --sub-only                               只选取字幕轨道 [default: False]
              --sub-format <SRT|VTT>                   字幕输出类型 [default: SRT]
              --auto-subtitle-fix                      自动修正字幕 [default: True]
              --ffmpeg-binary-path <PATH>              ffmpeg可执行程序全路径, 例如 C:\Tools\ffmpeg.exe
              --log-level <DEBUG|ERROR|INFO|OFF|WARN>  设置日志级别 [default: INFO]
              --ui-language <en-US|zh-CN|zh-TW>        设置UI语言
              --urlprocessor-args <urlprocessor-args>  此字符串将直接传递给URL Processor
              --key <key>                              设置解密密钥, 程序调用mp4decrpyt/shaka-packager进行解密. 格式:
                                                       --key KID1:KEY1 --key KID2:KEY2
              --key-text-file <key-text-file>          设置密钥文件,程序将从文件中按KID搜寻KEY以解密.(不建议使用特大文件)
              --decryption-binary-path <PATH>          MP4解密所用工具的全路径, 例如 C:\Tools\mp4decrypt.exe
              --use-shaka-packager                     解密时使用shaka-packager替代mp4decrypt [default: False]
              --mp4-real-time-decryption               实时解密MP4分片 [default: False]
              -M, --mux-after-done <OPTIONS>           所有工作完成时尝试混流分离的音视频. 输入 "--morehelp mux-after-done" 以查看详细信息
              --custom-hls-method <METHOD>             指定HLS加密方式
                                                       (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)
              --custom-hls-key <FILE|HEX|BASE64>       指定HLS解密KEY. 可以是文件, HEX或Base64
              --custom-hls-iv <FILE|HEX|BASE64>        指定HLS解密IV. 可以是文件, HEX或Base64
              --use-system-proxy                       使用系统默认代理 [default: True]
              --custom-proxy <URL>                     设置请求代理, 如 http://127.0.0.1:8888
              --task-start-at <yyyyMMddHHmmss>         在此时间之前不会开始执行任务
              --live-perform-as-vod                    以点播方式下载直播流 [default: False]
              --live-real-time-merge                   录制直播时实时合并 [default: False]
              --live-keep-segments                     录制直播并开启实时合并时依然保留分片 [default: True]
              --live-pipe-mux                          录制直播并开启实时合并时通过管道+ffmpeg实时混流到TS文件 [default: False]
              --live-record-limit <HH:mm:ss>           录制直播时的录制时长限制
              --live-wait-time <SEC>                   手动设置直播列表刷新间隔
              --mux-import <OPTIONS>                   混流时引入外部媒体文件. 输入 "--morehelp mux-import" 以查看详细信息
              -sv, --select-video <OPTIONS>            通过正则表达式选择符合要求的视频流. 输入 "--morehelp select-video" 以查看详细信息
              -sa, --select-audio <OPTIONS>            通过正则表达式选择符合要求的音频流. 输入 "--morehelp select-audio" 以查看详细信息
              -ss, --select-subtitle <OPTIONS>         通过正则表达式选择符合要求的字幕流. 输入 "--morehelp select-subtitle" 以查看 详细信息
              -dv, --drop-video <OPTIONS>              通过正则表达式去除符合要求的视频流.
              -da, --drop-audio <OPTIONS>              通过正则表达式去除符合要求的音频流.
              -ds, --drop-subtitle <OPTIONS>           通过正则表达式去除符合要求的字幕流.
              --morehelp <OPTION>                      查看某个选项的详细帮助信息
              --version                                Show version information
              -?, -h, --help                           Show help and usage information
         */

        //private String saveDir = "V:\\steam\\resource\\m3u8\\Downloads";// "V:\\steam\\resource\\m3u8";
        public static async Task Download(String url, String saveDir, String saveAs)
        {

            var proc = new Process();
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
            proc.StartInfo.WorkingDirectory = System.IO.Path.Combine(Environment.CurrentDirectory, "m3u8-tools");
            proc.StartInfo.FileName = System.IO.Path.Combine(Environment.CurrentDirectory, "m3u8-tools\\N_m3u8DL-RE.exe");

            proc.StartInfo.ArgumentList.Add(url);
            // 设置启动进程时的窗口状态为最小化
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
            proc.StartInfo.ArgumentList.Add($"--save-dir");
            proc.StartInfo.ArgumentList.Add($"{saveDir}");

            if (saveAs != null && saveAs.Length > 0)
            {
                proc.StartInfo.ArgumentList.Add($"--save-name");
                proc.StartInfo.ArgumentList.Add($"{saveAs}");
            }



            proc.StartInfo.ArgumentList.Add("--auto-select");
            proc.StartInfo.ArgumentList.Add("--concurrent-download");

            proc.StartInfo.ArgumentList.Add("--thread-count");
            proc.StartInfo.ArgumentList.Add("16");
            proc.Start();

            await proc.WaitForExitAsync();
            Console.WriteLine(proc.ExitCode);
        }
    }
}
