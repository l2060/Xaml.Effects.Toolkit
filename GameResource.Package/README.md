# Game.Asset.Package

不可变的 .NET 10 资源包。发布两个包：`Game.Asset.Package` 负责读取，`asset-cli` 负责构建。两个发布项目、测试和基准程序均无第三方 NuGet 依赖；LZ4 核心源码内嵌，许可证随程序集和 NuGet 发布。

这是全新格式及 API，不兼容原 `GameResource.Package` 格式。资源包不提供写入、删除、移动、保存或空间回收 API。

## 构建和验证

```powershell
dotnet build Game.Asset.Package.slnx -c Release
dotnet run --project tests/Game.Asset.Package.Tests.csproj -c Release
dotnet run --project tests/Game.Asset.Package.Tests.csproj -c Release -- --large
dotnet pack src/Game.Asset.Package/Game.Asset.Package.csproj -c Release -o artifacts/packages
dotnet pack src/asset-cli/asset-cli.csproj -c Release -o artifacts/packages
```

测试使用标准库控制台验证器，失败返回非零退出码。`--large` 会创建逻辑长度超过 4 GiB 的稀疏源文件，实际执行扫描、压缩、可选加密、构建完整校验和跨 4 GiB 读取；它不是只验证大偏移的测试。

## 项目配置

所有相对路径以 `asset-project.yml` 所在目录为基准。源目录可以在项目目录之外。`prefix` 支持 `common/data` 等多级相对路径，也可以是空字符串。

```yaml
version: 1
output: ./dist
compression: true

encryption:
  enabled: false
  key-environment: GAME_ASSET_KEY

packages:
  - name: common
    sources:
      - directory: ./assets/common
        prefix: common/data
      - directory: ../shared/config
        prefix: config
  - name: textures
    compression: true
    sources:
      - directory: ./assets/images
        prefix: game/textures
        include:
          - "**/*.png"
        exclude:
          - "**/draft/**"
```

压缩仅有 true/false，开启后使用独立 LZ4 块，压缩无收益的块保存原始数据。不同分包可以覆盖全局同一路径；同一分包内重复映射报错。

YAML 支持两空格缩进的块映射、块序列、普通/单引号/双引号标量和注释。不支持锚点、别名、自定义标签、多行标量或流式集合，未知设置和不支持的语法会报错。没有 `state` 或 `metadata.format` 设置。

可直接运行 [示例项目](examples/asset-project.yml)：

```powershell
dotnet run --project src/asset-cli -c Release -- build --project examples/asset-project.yml
```

## 固定 property=value Meta

`player.png` 对应 `player.png.meta`。没有 sidecar 表示没有 Meta，空 sidecar 表示存在空 Meta。

```text
name=player
offsetX=-16
scale=1.0
visible=true
tags=["hero","blue"]
frames=[1,2,3]
weights=[0.25,0.75]
enabled=[true,false]
code:string=001
emptyIds:int64[]=[]
```

只支持 string、int64、double、bool 及其各自数组。每行一个属性，使用第一个 `=` 分隔；空行和以 `#` 开始的整行注释被忽略。值内的 `=`、`#` 保留。字符串可以不加引号；双引号字符串和数组里的字符串使用标准 JSON 字符串转义，但不接受 JSON 对象作为另一种 Meta 文件格式。

未标注的整数是 int64，小数和指数形式是 double，true/false 是 bool，其余普通文本是 string。显式类型使用 `name:type=value`。未标注空数组是 string[]，其他空数组必须标注类型。数值数组提升为 double[] 时不允许整数精度丢失。禁止重复键、非有限浮点数、对象、嵌套数组、混合类型数组和 null 元素。字符串 `null` 本身可以作为普通文本。

源 sidecar 和编码后的 Meta 分别限制为 1 MiB，键名最多 1024 UTF-8 字节。编码按 UTF-8 键名排序，调整排版或行顺序不会产生补丁。运行时使用专用二进制字段表，不依赖序列化器或反射。

## .assetignore

项目根目录的 `.assetignore` 应用于每个源目录的相对路径；源目录及子目录中的规则追加生效。支持 `*`、`?`、`**`、`!`、根锚定 `/`、目录后缀 `/`、整行注释及开头的 `\#` / `\!` 转义。

```gitignore
**/*.tmp
**/*.psd
!textures/keep.psd
draft/
```

已排除的目录直接剪枝；重新包含子文件需要先包含其父目录。规则区分大小写。当前项目的 `.asset/`、输出目录、项目配置和规则文件不作为资源。符号链接和 junction 不参与扫描。忽略资源时其 Meta 一并忽略；单独忽略 sidecar 等价于本次源没有 Meta。未被忽略的孤立 sidecar 报错，扫描/权限错误使构建失败。

## Tag 和增量构建

```powershell
asset-cli build --project ./asset-project.yml
asset-cli build --project ./asset-project.yml --base-tag <TagID>
asset-cli diff --project ./asset-project.yml --base-tag <TagID>
asset-cli tags --project ./asset-project.yml
asset-cli inspect ./dist
asset-cli verify ./dist --password-env GAME_ASSET_KEY
```

每次成功构建生成 GUID TagId。指定 `--base-tag` 时，以该 Tag 的有效资源清单比较内容 SHA-256、规范化 Meta 摘要、分包位置和编码策略。删除源文件不生成删除记录；仅删除 sidecar 不移除旧 Meta。完整内容替换同时替换该资源的 Meta 状态。Meta-only 记录替换整份 Meta，不合并字段。编码策略或加密凭据变化会重新写入相关内容。

增量是文件级的；大文件内容变化会重新写入整个文件。扫描默认重新计算内容摘要，不仅依赖时间戳。实际打包再次计算摘要，源内容变化时失败。发布构建应使用稳定的源目录；各文件的读取检查不等同于文件系统原子快照。

构建状态固定在配置文件旁的 `.asset/`：

```text
.asset/
  tags/<TagId>/tag.json       构建记录
  tags/<TagId>/files.idx      完整有效清单
  temp/                      临时索引
  key.salt                   加密凭据指纹的派生盐（加密项目）
  build.lock                 项目构建互斥
dist/<TagId>/<name>.asset
```

Tag 记录保存继承后有效的资源状态，不建立运行时依赖图。同一构建的分包共享 TagId 和 UTC 时间。本地时间比已有 Tag 小时递增到最新 Tag 之后；跨机器构建应保持系统时钟一致。

包先写入临时文件，完成校验、头部及持久化刷新后重命名。分包在暂存目录构建完毕后发布整个 Tag 输出目录，最后提交本地 Tag。失败不发布 Tag；若输出已完成但 Tag 提交失败，CLI 报告保留的完整产物位置。无变化构建仍创建 Tag，但不输出空补丁。

## 目录加载与读取

```csharp
using Game.Asset.Package;

using var assets = await AssetPackageSet.OpenDirectoryAsync("./dist",
    new AssetReadOptions { Password = password }, cancellationToken);

AssetInfo info = assets.GetInfo("common/data/player.png");
AssetMetadata meta = await assets.ReadMetadataAsync(info.Path, cancellationToken);
long offsetX = meta.GetInt64("offsetX");

using AssetData loaded = await assets.ReadAsync(info.Path, cancellationToken);
ReadOnlyMemory<byte> bytes = loaded.Data;

// 大文件：流或范围读取，长度和偏移均为 long。
using Stream stream = assets.OpenRead("video/intro.mp4");
int count = await assets.ReadAtAsync("video/intro.mp4", offset, destination, cancellationToken);
```

默认递归扫描 `.asset` 文件；跳过 `.asset` 状态目录、以 `.tmp` 结尾的暂存目录和符号链接。`Recursive=false` 可限制为当前目录。正式包损坏、缺密钥或不可读会使打开失败，并报告具体文件。

排序先按包头时间，再按相对文件路径的 Ordinal 顺序。较晚内容记录覆盖内容与 Meta，较晚 Meta-only 记录只覆盖 Meta。找不到任何内容的孤立 Meta-only 路径不会成为可读资源。枚举返回有效文件，目录由路径前缀查询，不维护空目录记录。

未出现在补丁中的路径保持已加载的状态。例如先加载 A(a=1)、B(a=2)，再加载基于 A 生成且省略 a 的 C，结果仍为 a=2。运行库不会推断应移除哪些包；连续发布通常以上次发布 Tag 为比较基准。

同一个集合支持多线程和原生异步读取。各流位置独立；同一流禁止重叠 Read/Seek/Dispose。关闭集合后拒绝新操作，已经打开的流/枚举继续持有底层文件租约。`AssetData` 使用池化缓冲，Dispose 后 Data 失效；Meta 为独立不可变对象，可以继续使用。

Meta 的 Get/TryGet 为明确类型访问。数值及布尔数组返回只读 Span，字符串数组返回只读集合。数值字段访问不装箱、不分配；字符串按需解码并缓存在该 Meta 对象内。

`MaxBufferedAssetBytes` 默认 32 MiB，是单次完整加载的限制，不是并发总内存限制。索引常驻上限默认每包 64 MiB，超过则分页；页缓存每包默认 8 MiB，Meta 缓存整个集合默认 4 MiB 编码字节，解码字符串和调用者持有的结果另占内存。

完整缓冲读取检查内容 SHA-256。局部/流读取验证块结构及加密认证，完整内容校验使用 `Verify/VerifyAsync`，它会检查所有物理包，包括被后续包覆盖的内容。

## 加密与路径保护

资源内容可选 AES-256-GCM。密码只在打开每个物理包时通过 PBKDF2-SHA256（600000 次）派生；各包使用独立随机盐。内容、索引和 Meta 使用独立域和分组子密钥。不同包密码可通过 `PasswordProvider(AssetPackageInfo)` 提供。

未加密时，路径、字段名和 Meta 值仍通过公开参数派生的 AES-GCM 密钥编码，避免磁盘区域直接出现明文。这个公开模式是可逆保护，不是保密或发布者认证。持有读取器的人可以恢复这些数据。真正保密需要启用加密并提供外部密码；公开 TagId、时间及必要布局字段用于加载。

## 并行构建与性能

`--jobs` 控制压缩/加密工作线程；`--io-concurrency` 控制源文件并发；`--write-concurrency` 默认 1，可测试增加写入并发。单个大文件也会分块进入共同的工作队列。`--memory-mib` 限制工作缓冲配置，默认 128 MiB；资源清单、路径和源 Meta 的内存随记录数增长，另行计算。

`--block-size` 支持 65536、131072、262144、524288、1048576，默认 262144。数据偏移和块表均支持 64 位；当前实现支持 little-endian .NET 平台，超大包建议 64 位进程。

读取后端基准和结论见 [benchmarks/README.md](benchmarks/README.md)，字节布局见 [FORMAT.md](FORMAT.md)。

默认 `ReadMode = AssetReadMode.RandomAccess`。实测热缓存下未加密随机读取获益明显的场景可显式使用 `AssetReadMode.MemoryMapped`。映射按需由操作系统调页，资源内容仍通过受控缓冲访问；映射后端的异步读取可能同步完成，页错误也可能阻塞调用线程。加密并发与冷盘场景应按实际负载选择。
