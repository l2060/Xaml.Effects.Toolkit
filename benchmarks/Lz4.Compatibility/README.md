> Historical evidence: the executable project targeting the old mutable format has been retired. Current zero-dependency validation and benchmarks are in [GameResource.Package](../../GameResource.Package/README.md). The commands below describe the old version.

# LZ4 源码内嵌兼容性验证

仅用于开发验证的独立程序；它同时引用原版 K4os.Compression.LZ4 1.3.8 与 GameResource.Package 内嵌实现，以检查相互兼容性。生产库和常规测试不依赖原 DLL。

```powershell
dotnet build benchmarks/Lz4.Compatibility/Lz4.Compatibility.csproj -c Release
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    dotnet benchmarks/Lz4.Compatibility/bin/Release/net10.0/GameResource.Package.Lz4Compatibility.dll `
        artifacts/lz4-check/results.json
}
finally { $env:DOTNET_TieredCompilation = $previousTiering }
```

输出路径必须是新文件。可在其后追加多个未加密资源包路径，执行只读 Verify，以检验旧实现写出的资源包。

检查 18 种长度、4 种内容、4 个压缩等级、默认/强制 32 位两种引擎，共 576 组：编码字节相同，旧实现与内嵌实现可以双向解码；同时检查过短目标缓冲区。再比较 2000 个随机损坏块，检查输出区域边界，并执行 256 次并行兼容性检查。强制 32 位是选择算法分支，不等同于在 32 位进程中运行。

性能部分使用 64 KiB 数据块，预热后校准到约 150 ms，每组 7 轮，顺序随机；Windows 仅将当前进程固定到 CPU 0。输入/输出缓冲预先分配，计时不包含磁盘和测试数据生成。分别记录压缩、解压耗时，保留两种实现的程序集哈希。

0=random，1=zeros，2=带少量变化的文本，3=BGRA 风格色块。它们是用于检查内嵌是否产生性能退化的小型合成负载，不替代真实资源性能测试。
