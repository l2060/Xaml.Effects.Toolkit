# GameResource.Package

独立的 .NET 10 单文件资源库。支持区分大小写的完整路径、空目录、空间复用、LZ4、密码加密、并行读取、原生异步 I/O、MessagePack Meta 和应用层懒加载引用。

只包含核心库、测试和基准工具；不依赖 Resource.Package.Assets、WPF 或图片解码器。0.2.x 使用独立磁盘格式 2，不读取实验格式 1，也不兼容现有旧资源包。

## 构建

```powershell
dotnet build GameResource.Package/GameResource.Package.slnx -c Release
dotnet test GameResource.Package/GameResource.Package.slnx -c Release
dotnet pack GameResource.Package/GameResource.Package.csproj -c Release
```

直接 NuGet 依赖仅有 MessagePack 3.1.9（及其传递依赖）。自 0.2.1 起，LZ4 使用内嵌的 K4os.Compression.LZ4 1.3.8 核心源码，直接编译进 GameResource.Package.dll，不需要独立的 K4os.Compression.LZ4.dll 或原生 LZ4 文件。来源和许可证见 [ThirdParty/Lz4/README.md](ThirdParty/Lz4/README.md)。许可证同时嵌入程序集并随 NuGet 包发布。

自 0.2.2 起，文件内容和 Meta 校验使用内部 XXH64 实现，不再依赖 System.IO.Hashing.dll。保持标准 XXH64、种子 0 和原有小端存储，磁盘格式仍为 2，可直接读取此前 0.2.x 写入的包。整块校验无堆分配，流式校验仅保留累计状态和内联的 32 字节尾部缓冲。XXH64 用于检测意外数据损坏；密码派生、SHA-256/HMAC 和 AES-GCM 仍使用 .NET 内置的 System.Security.Cryptography。

## 写入和提交

```csharp
using GameResource.Package;

using var package = ResourcePackage.Create("game.pkg", new()
{
    Password = "your password" // null 表示不加密
});
package.CreateDirectory("game/empty");
using (var source = File.OpenRead("0.png"))
{
    package.WriteResource("game/monster/cat/stand/0.png", source,
        new ResourceProperties(("offsetX", -16), ("offsetY", 8), ("renderType", "Alpha")));
}
package.Save();
```

Create 使用 CreateNew，不覆盖已有包。WriteResource 完整替换内容与 Meta；不传 Meta 的重载会移除原 Meta。UpdateContent 要求文件已存在，保留原 Meta；UpdateMetadata 只替换 Meta；RemoveMetadata 明确删除 Meta。低层 PutFile 支持创建/覆盖内容并保留已有 Meta。

ResourceWriteOptions 设置 Compression、Overwrite 和 Length。写入从输入流当前位置开始，不关闭调用者的流。未知长度的不可定位流通过自动删除的临时文件确定长度。更新、删除和移动立即对当前实例可见；Save 才提交，Dispose 不自动保存。批量修改后统一 Save。

## 合并读取 Meta 和内容

```csharp
using var package = await ResourcePackage.OpenAsync("game.pkg", new()
{
    Password = "your password"
});
using var resource = await package.ReadResourceAsync("package://game/monster/cat/stand/0.png");
int offsetX = resource.Metadata!.GetInt32("offsetX");
ReadOnlyMemory<byte> data = resource.Data;
// 在 resource.Dispose 前完成解码、复制或上传到 GPU。
```

ReadResource / ReadResourceAsync 返回 ResourceData<T>：Info、HasMetadata、Metadata、Data 来自同一读取期间的版本，读取期间禁止修改。结果拥有池化缓冲区，必须 Dispose；Data 及其切片只能在该结果释放前使用，不要在使用同一结果的同时释放它。完整缓冲读取会验证内容 XXH64。

结果可以跨包的修改、Save 和 Dispose 存活。独立的加载结果有独立释放句柄；一方释放不会使另一方失效。默认不缓存完整资源内容。

## 强类型 Meta

默认返回不可变 ResourceProperties；也可使用具有字符串键的 MessagePack DTO：

```csharp
using MessagePack;

[MessagePackObject]
public sealed class SpriteMetadata
{
    [Key("offsetX")] public int OffsetX { get; set; }
    [Key("offsetY")] public int OffsetY { get; set; }
    [Key("renderType")] public string RenderType { get; set; } = "Alpha";
}

// 写入和读取共享相同的字符串键协议。
// package.WriteResource(path, source, new SpriteMetadata { OffsetX = -16 });
// using var sprite = await package.ReadResourceAsync<SpriteMetadata>(path);
// SpriteMetadata? meta = await package.ReadMetadataAsync<SpriteMetadata>(path);
```

Meta 是 MessagePack 的字符串键 map，支持布尔、有/无符号整数、浮点数、字符串、二进制、数组、嵌套 map、null 和扩展值。ResourceProperties 与 DTO 可互读；额外字段可通过 ResourceProperties 保留。DTO 重新写入只保存 DTO 中的字段，不自动保留未建模字段。不使用 Typeless 类型名反序列化。

没有 Meta 时 HasMetadata=false，默认 Properties 返回 Empty；泛型读取返回 default(T)。空 map 是存在的 Meta，可用 HasMetadata 区分。每个文件 Meta 最大 1 MiB，嵌套深度最多 64；禁止非字符串键 map、重复键和非 map 根对象。读取类型不匹配会抛出 InvalidDataException。

Meta 保存在独立数据区，不随索引一起加载、扫描或校验。只有请求 Meta、合并读取或 Verify 时才访问。已编码 Meta 的 LRU 缓存默认上限 4 MiB、4096 项，任一项达到上限即淘汰；可设为 0 禁用。修改成功后清除缓存。解码出的 DTO 由调用者持有，不放入全局对象缓存。

## 应用层懒加载

```csharp
// 仅查索引，无文件内容/Meta I/O，无缓冲区、Task 或读取租约。
var reference = package.GetResource<SpriteMetadata>("game/monster/cat/stand/0.png");
var info = reference.Info;

// 真正显示或使用资源时加载。
using var loaded = await reference.LoadAsync(cancellationToken);
SpriteMetadata? metadata = loaded.Metadata;
ReadOnlyMemory<byte> imageBytes = loaded.Data;

// 也可以仅取 Meta，或者使用流。
var onlyMeta = await reference.GetMetadataAsync(cancellationToken);
using var streamed = await reference.OpenAsync(cancellationToken);
```

同一引用的重叠 LoadAsync 会合并一次实际读取，每个等待者得到独立句柄和独立解码的 Meta 对象。取消一个等待者不影响其他等待者；全部取消才取消底层读取，最后一个等待者会等 I/O 结束再完成取消。失败可重试。加载结束且等待者已取得结果后，引用不保留完整内容。

合并限于同一个引用的异步缓冲加载；独立引用、同步 Load、GetMetadata 和 Open 不合并任务。引用保存路径，不保存物理偏移。替换后下次加载得到新内容；删除或将原路径移走后报 FileNotFoundException；在原路径重新创建则读取新文件。未加载的引用不阻塞编辑，包关闭后无法再次加载。

应用可在模型中保存引用，在可见/需要时加载；已有解码纹理、音频或图片对象的缓存和淘汰属于应用自身。避免一次发起几十万项加载：按使用顺序安排有限并发，通常从 4 或 8 开始实测。库不隐式预取、预解码或永久缓存所有资源。

## 流、局部读取和异步

```csharp
using var resource = await package.OpenResourceAsync<SpriteMetadata>(path, cancellationToken);
Stream content = resource.Data; // 支持 Seek
await content.CopyToAsync(destination, cancellationToken);
// resource.Dispose 释放其内容流和读取租约。
```

OpenResource / OpenResourceAsync 返回 ResourceStream<T>，读取 Meta 后打开独立内容流；内容随 Read/ReadAsync 按需加载，内存不随整个文件长度增长。OpenRead(path) 是不读取 Meta 的内容流；ReadAt/ReadAtAsync 和 CopyTo/CopyToAsync 也仅访问内容。OpenResourceAsync 不提前加载文件内容。

OpenAsync、异步 Meta/合并读取、内容流 ReadAsync 使用 RandomAccess.ReadAsync，没有 Task.Run 包装同步读取。密码派生、校验、解压和反序列化仍是 CPU 工作。编辑打开时的提交头统一和持久化刷新同步完成。写入、Save、Compact 保持同步，支持取消；本版本不提供假异步写入。

小文件和系统缓存命中的读取仍建议保留同步路径，Async 的主要价值是等待磁盘时让出线程及受控并发，不保证单次延迟更低。同步与异步性能见基准报告。

## 并发和生命周期

多个独立读流、读取 API 和枚举可以同时运行；可以在 foreach 枚举中读取内容。写入、Save、移动、删除和包 Dispose 必须等待所有活动读取和枚举结束。发生冲突立即抛 InvalidOperationException，不在库内排队。调用者可在业务层安排读写阶段。

每个内容流只能同时执行一个读取/Seek/Dispose；并发使用不同流。同一个流有异步 I/O 尚未完成时，应先等待或取消并等待该操作，再释放流。流和开始后的枚举持有读取租约；只拿引用或已经缓冲完成的 ResourceData 不持有租约。

只读实例允许其他只读实例；编辑实例排斥其他库实例。加密密钥在打开时派生/认证一次，不在每次文件访问时验证密码；并发读流有独立的 AES-GCM 上下文。

## 路径、目录和操作

package:// 仅为可选前缀，后面的 game 是实际包内目录，没有命名空间和 URI Host 语义，也不解码 %20。路径使用 /，区分大小写，不做 Unicode 归一化，完整路径上限 4096 个 UTF-8 字节。禁止空路径段、.、..、反斜杠、控制字符及 : * ? " < > |。

根目录是空字符串。GetInfo / TryGetInfo 查询文件和目录；Enumerate 枚举直接子项；EnumerateFiles 可递归。索引只存完整路径，不维护 parent、文件 ID 或目录树。非空目录由路径前缀推导，空目录显式保留一条标记；删除/移动最后一个子项时保留原父目录。目录前缀匹配包含 /，a/b 不会匹配 a/bb。

Move、DeleteFile、DeleteDirectory、Recompress 保留原有能力。Recompress 的 Auto 按 64 KiB 块尝试 LZ4，仅编码后更小时采用压缩，每块存储实际编码类型。未加密的 None 内容保持连续原始字节。Compression 表示写入策略，不代表每块都已压缩。

## 空间、内存和完整性

文件和 Meta 分别使用连续空间段，按 8 字节对齐。空闲区按地址排列、首次适配分配，支持切分与合并。替换写到新空间；旧区域在双头提交成功后才重用。Save 重写当前索引，但不重写全部文件或 Meta；索引成本仍随记录数增长。只有 Compact 会整理重写全包。

读取常驻索引约为 `64 × 记录数 + 全部路径 UTF-8 字节数`，另加空闲列表和对象开销。打开时校验布局有临时分配，首次编辑还会构建可修改的有序集合。Meta 缓存单独受限制。MaxBufferedResourceBytes 默认 32 MiB，是每次完整加载的上限，超出时使用流；它不是所有并发加载的总内存上限。池化缓冲区可能向上取整，每个活动压缩/加密流还需几个 64 KiB 缓冲区。

PackageOpenOptions 默认限制 200 万条记录和 256 MiB 基础索引；空闲列表另受同一内存上限约束。GetStatistics 返回索引、空闲/待释放空间、当前逻辑长度和 Meta 缓存统计。

AES-256-GCM 先压缩后加密，目录索引、Meta 和内容均加密。每块随机 nonce，认证绑定物理位置、块序号、长度、编码及数据类别。Meta 读取验证独立 XXH64/GCM；缓冲合并读取验证内容 XXH64。流式或局部读取只验证实际读取块的结构和 GCM，完整内容 XXH64 由 Verify 检查；XXH64 检测意外损坏，不提供防篡改认证。

```csharp
using (var package = ResourcePackage.OpenRead("game.pkg", options)) package.Verify();
// 关闭该包全部实例后调用。
ResourcePackage.Compact("game.pkg", options);
```

Verify 包括所有内容和 Meta，绕过 Meta 缓存。Compact 逐文件复制到临时包，保留原始 Meta 编码字节、压缩策略与空目录，验证后替换原包。失败保留原文件。

恢复选择校验有效的最高提交序号。编辑打开先统一两个头，随后才允许复用旧空间；发布文件头期间失败的实例只能关闭重开。Save 在发布头部之后不再响应取消。持久化保证取决于系统和存储设备正确执行刷新；中断注入测试不等同于硬件断电测试。

磁盘布局见 [FORMAT.md](FORMAT.md)，性能数据见 [基准目录](../benchmarks/GameResource.Package.Benchmarks/README.md)。
