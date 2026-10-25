# Zlib / LZ4 解压基准

独立的 .NET 10 Windows 基准程序，不修改资源库的压缩实现。比较内置 `System.IO.Compression.ZLibStream` 的 Fastest / Optimal 与资源库使用的 `K4os.Compression.LZ4 1.3.8`、L00_FAST。

## 运行

从仓库根目录执行，输出文件必须不存在：

```powershell
dotnet build benchmarks/Compression.Benchmarks/Compression.Benchmarks.csproj -c Release
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    dotnet benchmarks/Compression.Benchmarks/bin/Release/net10.0-windows/Compression.Benchmarks.dll `
        . artifacts/compression-benchmark/results.json 7 200
}
finally { $env:DOTNET_TieredCompilation = $previousTiering }
```

参数依次为仓库根目录、JSON 输出文件、轮数、每项每轮目标毫秒数。目标时长通过预热校准为固定迭代数，并非强制终止计时。默认 7 轮、200 ms，所有项目逐轮随机排序；只给当前进程设置 CPU 0 亲和性，不修改机器配置。基准采用单线程和 Workstation GC。

## 样本

- 当前仓库 GameResource.Package、AssetsEditor、src 下的 C# / XAML 文件：各读取一次，换行连接，不复制填充；原文件清单及哈希存入结果。
- 固定种子 20261025 生成的 8192 条 JSON 资源配置，带不同编号、属性和路径，格式化输出。
- 仓库内实际的图标和两张背景 PNG，在测量前解码为不预乘 Alpha 的 BGRA32。
- 1024 × 1024 的合成透明精灵图集：调色板色块、透明空白和半透明边缘。
- 1024 × 1024 的合成 RGB 噪声渐变纹理，Alpha=255；用于覆盖较难压缩的像素数据。

图片测量对象是原始 32 位 BGRA 像素，行紧密排列，没有 PNG 文件头、行滤波或其他像素预处理；PNG 解码完全不计时。合成图不冒充真实游戏素材。像素顺序明确为 BGRA，和 RGBA 一样每像素 4 字节。

## 计时方法

所有输入已驻留内存，输出缓冲区预先分配且重复使用。计时不包含压缩、磁盘 I/O、PNG 解码、路径查询、加密或全文件哈希，仅包含完成一次解压所需的公开 API 调用：

- LZ4：原始块解码并检查返回长度。
- Zlib：为每个压缩单元创建 MemoryStream、ZLibStream，ReadExactly，检查流结束，Dispose。包含 Zlib 自身的校验和/流生命周期成本；不人为给 LZ4 添加同等校验。
- 每轮结束均逐字节对比原文/原像素，在计时外完成。

每个样本有两种布局：整个样本作为一个独立压缩单元，以及每 64 KiB 独立压缩。后者与当前资源库的块大小一致。

64 KiB 布局再测两种策略：`always_decode` 强制解码每个块，即使压缩变大；`auto_fallback` 在压缩长度大于等于原长度时直接存原始块，读取时内存复制。后者能模拟当前包的回退规则；对不可压缩内容，不把原始块的复制速度误报成算法解压速度。

压缩率包含 Zlib 头部和校验值，不包含任何算法的资源包块描述表。流对象的托管分配量计入结果；不包括输出数组的创建，也不等于原生库的全部内存消耗。整样本压缩的文本连接及跨行字典有额外上下文，不能当作逐个小资源的压缩率。

结果记录每轮原始值、每操作耗时、中位数/最小值/最大值、MiB/s、分配量、GC、样本及程序集哈希。吞吐以**解压后的字节数**计算。小图标的流初始化成本很显著，不应将小图吞吐外推到大图。

实测结果见 [2026-10-25 报告](results/2026-10-25/REPORT.md)。
