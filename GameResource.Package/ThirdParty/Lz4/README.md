# Embedded LZ4 core

本目录包含 K4os.Compression.LZ4 **1.3.8** 的 C# 核心源码，直接编译进 Game.Asset.Package.dll。没有对 K4os.Compression.LZ4.dll 或原生 LZ4 二进制的运行时依赖。

上游仓库：https://github.com/MiloszKrajewski/K4os.Compression.LZ4

固定提交：`f5a25b7d72e2e41550fe20662597169ff11c3b60`，tag `1.3.8`。

## 来源和范围

引入 `src/K4os.Compression.LZ4` 下的 22 个源文件：LZ4Codec、LZ4Level、32/64 位核心算法和所需内存辅助代码。保留核心引擎的原有快速/高压缩实现；资源库仍使用 L00_FAST，按需将未缩小的块存为原始数据。

未引入流/链式编码器封装、Pickler、上游公开测试桥接 Pubternal、未被使用的 UnmanagedResources 以及上游项目/构建系统。所有所引入文件均核对了上游 Git blob 哈希；UPSTREAM.json 记录原文件及本地文件的 SHA-256。

本地调整仅为：

1. 将命名空间及 using 别名从 K4os.Compression.LZ4 改为 Game.Asset.Package.Internal.Lz4。
2. 将类型声明收为 internal，避免暴露第三方 API 或与使用者引用的原库冲突。
3. 将原先位于 System 命名空间的 Extensions 移到本地 Lz4 命名空间。
4. 文件前加入来源注释和 `#nullable disable`，匹配上游项目默认设置；文件内原有 `#nullable enable` 继续生效。

没有修改压缩/解压算法、数据结构、编译分支或块编码格式。构建目标为 .NET 10，启用 AllowUnsafeBlocks。所使用的 Unsafe、ArrayPool、Marshal 等由 .NET 运行时提供。

## 许可证

- [LICENSE.K4os.txt](LICENSE.K4os.txt)：K4os 的 MIT License。
- [LICENSE.LZ4.txt](LICENSE.LZ4.txt)：原始 LZ4 的 BSD 2-Clause 条款。

保留两份版权及许可文本。它们作为资源嵌入 Game.Asset.Package.dll，同时打包到 NuGet 的 `licenses/Lz4/` 目录。后续同步源码时也必须保留这些声明。

## 验证与维护

当前控制台验证程序不引用原 K4os 包，覆盖资源包行为、缓冲区边界、损坏块拒绝、程序集依赖和许可证内嵌：`dotnet run --project tests/Game.Asset.Package.Tests.csproj -c Release`。

引入源码时曾通过旧独立兼容性工具与上游1.3.8进行双向解码和编码对比。本次重构仅更改命名空间，算法内容保持一致，UPSTREAM.json 更新本地文件 SHA-256。旧兼容性项目不属于新的解决方案或发布产物。

更新时固定新的上游提交，核对 UPSTREAM.json 和本地差异，重新执行兼容性与资源包测试。不要因减少源文件数量而修改核心算法。
