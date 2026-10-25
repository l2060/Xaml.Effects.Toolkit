# Internal XXH64 compatibility

独立校验工具，以 System.IO.Hashing 10.0.0 为参考实现，验证内部 XXH64 的数值和流式行为。该依赖仅存在于此工具，不进入核心库或常规测试项目。

覆盖空输入、32 字节条带边界、64 KiB 块边界、不对齐切片、随机/全零/全 255/递增输入、随机分段、所有短输入切分、重复快照、快照后继续追加、空追加、并行独立实例，以及超过 4 GiB 的累计输入。大输入复用 64 KiB 缓冲区，不生成大文件。

可传入已有资源包，使用新库的 Verify 检查全部文件内容和 Meta。旧包只读打开，不修改。性能对比覆盖一次性校验及 64 KiB 分段累计校验，报告每次耗时与托管分配。

```powershell
dotnet build benchmarks/XxHash64.Compatibility/XxHash64.Compatibility.csproj -c Release
$env:DOTNET_TieredCompilation = "0"
dotnet run --project benchmarks/XxHash64.Compatibility/XxHash64.Compatibility.csproj -c Release --no-build -- artifacts/xxh64-internal/results.json artifacts/package-benchmark-v02-release-500k/none.pkg artifacts/package-benchmark-v02-release-500k/lz4.pkg
Remove-Item Env:DOTNET_TieredCompilation
```

输出路径必须尚不存在。性能部分在 Windows 上绑定单核，禁用分层编译以减少测量期间 JIT 状态变化；每项校准到约 100 ms，7 轮随机交错执行。输入复用，不包含磁盘读取、解压或 Meta 解码。流式测量包含每次创建校验对象的成本；它是局部哈希基准，不代表资源加载端到端性能。
