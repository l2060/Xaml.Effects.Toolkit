# 读取后端验证

使用标准库的可复现控制台基准，无 BenchmarkDotNet 或其他 NuGet 依赖。从 GameResource.Package 目录运行：

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project benchmarks/Game.Asset.Package.Benchmarks.csproj -c Release -- benchmarks/results/read-backends.json
```

结果保存到指定 JSON；源文件和临时包在系统临时目录创建，完成后清理。计时包括路径解析、流/结果生命周期、实际读取、必要的解压解密；完整内容读取还包括 SHA-256 及 Meta。数值 Meta getter 微基准单独记录，不混同为资源加载吞吐。

## 数据与测量边界

2026-10-26，i7-13700KF（24逻辑处理器）、Windows 10.0.28000、.NET 10.0.12，Release，关闭分层编译。

- 3000个小资源：80%为1KiB、15%为4KiB、5%为64KiB；一半随机、一半可压缩数据，各有四字段Meta。
- 一个16MiB随机数据资源，用于大资源局部和顺序读取。
- 四种配置：原始、LZ4、AES-GCM、LZ4+AES-GCM；默认块大小256KiB。
- 小资源每轮12000次随机读取；大资源每轮1500次随机4KiB读取。
- 同步1/4线程，异步1/4/8/16个worker，各3轮。后端顺序在不同配置间交替。
- 每个后端先预热内容和Meta；**结果属于热系统缓存，不是冷盘带宽**。没有清空操作系统缓存，没有修改系统电源计划。
- 映射后端的 ReadAsync 可以同步完成，异步worker可能顺序执行；不能将其“8个worker”解读为8个CPU线程。同步4线程场景使用 Parallel.For，原始数据也保存在JSON中。

## 结果与选择

以下为12000次随机范围读取（最多4KiB）的三轮耗时中位数，单位ms：

| 配置 | RandomAccess 同步1 | 映射 同步1 | RandomAccess 异步8 | 映射 异步8调用 |
|---|---:|---:|---:|---:|
| 原始 | 90.86 | 11.11 | 49.37 | 10.65 |
| LZ4 | 42.87 | 16.22 | 23.22 | 17.40 |
| AES-GCM | 228.67 | 179.70 | 64.38 | 181.26 |
| LZ4 + AES-GCM | 206.60 | 178.87 | 61.92 | 179.89 |

映射在本机热缓存的未加密小读中有明显收益；加密资源的异步并发读取使用 RandomAccess 更快。默认保留 RandomAccess，提供 `AssetReadOptions.ReadMode = AssetReadMode.MemoryMapped` 显式选择，不根据这组热缓存结果自动启用映射。

1500次大资源内随机4KiB读取，同步单线程中位数：

| 配置 | RandomAccess | 映射 |
|---|---:|---:|
| 原始 | 9.54 ms | 1.96 ms |
| LZ4 | 26.74 ms | 15.14 ms |
| AES-GCM | 81.80 ms | 68.87 ms |
| LZ4 + AES-GCM | 82.45 ms | 68.19 ms |

压缩或加密时，4KiB范围仍可能需要处理完整256KiB块，不能将返回的字节数解释为底层处理字节数。

两百万次数值/布尔 Meta getter 调用耗时34.9–37.1ms，测得额外托管分配0字节。测量对象是已加载的Meta，排除了首次解析、字符串解码和磁盘I/O。

原始数据包含全部轮次、P95/P99、CPU、分配量、工作集及程序集摘要：[read-backends.json](results/read-backends.json)。测量程序集 SHA-256：`AC6D331B70EC0AB3657A54AAF90BF27C88C7B718E900F978E346B04C68267ED6`。工作集包含测试程序和运行时；同进程首次打开时间包含初始化因素，不作为冷启动结论。

冷盘、多盘型和生产资源集的收益仍需在目标环境测量。对于映射模式，还需考虑页错误在调用线程上发生，异步接口不能消除该等待。
