---
module: avalonia-images
sources: ["src/StreamMark.Avalonia/Images/**", "src/StreamMark.Avalonia/Rendering/ImageSlot.cs", "src/StreamMark.Avalonia/Rendering/ImageViewer.cs"]
commit: 8d344a6
updated: 2026-10-18
---
# avalonia-images（图片：策略 / 获取器 / 两级缓存 / 缩略图 / 解码 / 布局槽位 / 查看器）

按"规范化地址 → 内存缓存 → 同一请求合并 → 磁盘缩略图（命中就只读它，不解码原图）→ 磁盘缓存（新鲜直接用，过期条件请求重验证）→ 获取器取原始字节 → 后台按宽度档位解码 → 写回两级缓存并在后台补写缩略图"的管线加载一张图片；视图侧用 `ImageSlot` 持有句柄、用 `ImageRun` 参与 `TextLayout` 排版（占位骨架 / 失败重试 / 需要同意标签），点击已加载图片在全屏 `ImageViewer` 里查看（文档内是缩小解码，查看器按自然尺寸打开、加载原图、关闭时把原图移出缓存）。
**不负责：** Markdown 与 HTML 的解析、`InlineObject(Image)` 的产生（`core-parsing-and-model`，`MarkdigMapper.cs:460`、`HtmlMapper.cs:426`）；`DrawableTextRun` 基类、基线对齐与保留行高的重排（`avalonia-text-rendering`，`Rendering/InlineRuns.cs:69`、`SegmentLayout.cs:155`）；视图回收与 `OnDetachedFromVisualTree` 的调用时机（`avalonia-transcript`，`TranscriptPanel.cs`）；流式未写完图片地址的占位符替换（`core-streaming` 对应的 `StreamMark.Core/Remend`，`Remend.cs:87` 定义 `IncompleteImageUrl`）；相对地址的基准来源（宿主附加属性，`Extensions/MarkdownServices.cs:61`）；公钥固定的宿主实现 `SvgImageRasterizer`（`StreamMark.Mermaid`，`SvgImageRasterizer.cs:13`，本页只描述接口约定）。

## Entry points
| Symbol | File | Role |
|---|---|---|
| `ImagePipeline` | `src/StreamMark.Avalonia/Images/ImagePipeline.cs:76` | 唯一的对外入口：构造即建内存缓存（有目录时再建磁盘缓存），`LoadAsync` / `TryGetCached` / `TryGetPixelSize` / `Policy` / `DecodeDensity` / `Approve` / `ForgetFailure` / `Dispose` |
| `ImagePipelineOptions` | `src/StreamMark.Avalonia/Images/ImagePipeline.cs:7` | 宿主配置：`Policy`、`MemoryBudgetBytes`、`NegativeCacheDuration`、`DiskCacheDirectory`（null 即不启用磁盘）、`DiskCacheMaxBytes`、`Fetchers`、`HttpHandler`、`SvgRasterizer`、`TimeProvider`、`DecodeDensity`（文档内解码密度上限，`ImagePipeline.DecodeDensity` 可运行时修改）、`DiskThumbnails` / `ThumbnailEncoder`（磁盘缩略图，默认开启；编码器为 null 时用 Avalonia 存 PNG，`StreamMark.Mermaid.WebPImageEncoder` 存 WebP） |
| `IImageFetcher` | `src/StreamMark.Avalonia/Images/IImageFetcher.cs:8` | 协议扩展点：`CanFetch(scheme)` / `IsRemote` / `FetchAsync`；内置 `DataUriFetcher`、`HttpsFetcher`、`FileFetcher`，宿主注册的排在前面（`ImagePipeline.cs:110`） |
| `IImageEncoder` | `src/StreamMark.Avalonia/Images/ImagePipeline.cs:55` | 缩略图编码扩展点：`Encode(Bitmap)` 返回可存盘的字节，失败返回 null（该缩略图不落盘）；在线程池上调用，必须线程安全 |
| `ImagePolicy` | `src/StreamMark.Avalonia/Images/ImagePolicy.cs:11` | 不可变策略记录：`CheckUri`（协议 + 域名）、`RequiresConsent`、`AllowedFileRoots`、`MaxBytes` / `Timeout` / `MaxRedirects` / `UserAgent` |
| `ImageMemoryCache` | `src/StreamMark.Avalonia/Images/ImageMemoryCache.cs:56` | 已解码位图的内存缓存（LRU + 字节预算 + 负缓存 + 记住原始像素尺寸） |
| `ImageDiskCache` | `src/StreamMark.Avalonia/Images/ImageDiskCache.cs:63` | 原始字节 + 元数据的两文件磁盘缓存，容量 LRU 清理，可多进程共用；缩略图是同一目录里的普通条目 |
| `ImageSlot` | `src/StreamMark.Avalonia/Rendering/ImageSlot.cs:12` | 一个视图中一张图的加载状态与句柄；`Ensure` / `AcquireLease` / `IsDownscaled` / `Retry` / `Approve` / `Dispose` |
| `ImageRun` | `src/StreamMark.Avalonia/Rendering/InlineRuns.cs:69` | 行内图片 run：按自然尺寸 / `width` / `height` 属性算尺寸，加载中画骨架占位，其它状态画标签 |
| `ImageViewer` | `src/StreamMark.Avalonia/Rendering/ImageViewer.cs:14` | 全屏查看器（`Show` 静态入口，覆盖层显示，缩放 / 平移 / Esc 关闭 / 点背景关闭 / `Closed` 事件） |

## Key types and functions
| Symbol | File | Role |
|---|---|---|
| `ImagePipeline.LoadAsync(url, decodeWidth)` | `ImagePipeline.cs:230` | 全流程；返回的 `Loaded` 结果持有句柄，调用方负责释放；取消只影响本次等待，底层获取会跑完并写缓存（第 227 行注释、`GetBytesAsync` 第 529 行） |
| `ImagePipeline.TryGetCached` / `TryGetPixelSize` | `ImagePipeline.cs:177,195` | 同步、不联网：前者查内存命中（渲染首帧直接显示），后者按"内存记录 → `data:` 文件头 → 磁盘元数据"给占位宽高比（第 202-222 行） |
| `ImagePipeline.GetDecodeBucket` / `NormalizeUrl` | `ImagePipeline.cs:166,172` | 宽度档位（256px 向上取整，0 = 原始）；地址规范化（去片段、小写、去默认端口，`data:` 原样保留，`TryNormalize` 第 638 行） |
| `ImagePipeline.DecodeDensity` | `ImagePipeline.cs:118` | 文档内解码密度上限；`0` 内部表示未设置（第 89 行），设成非有限值或 ≤ 0 会抛 `ArgumentOutOfRangeException`（第 121-122 行） |
| `HttpsFetcher` | `Images/HttpsFetcher.cs:12` | 手动跟重定向并逐跳 `CheckUri`、`Content-Type` 必须 `image/*`、边读边限长、条件请求、`IDisposable`（持有 `HttpClient`） |
| `FileFetcher` | `Images/FileFetcher.cs:7` | 只读 `AllowedFileRoots` 下（逐级解析符号链接后）的文件，拒绝远程共享；`ResolveAllowedPath` 第 57 行、`ContentTypeFromExtension` 第 125 行 |
| `DataUriFetcher` | `Images/DataUriFetcher.cs:4` | 纯内存解析 `data:image/*[;base64],…`，先按长度粗判上限再解码 |
| `ImageDecoder.Sniff` / `TryReadSize` / `Decode` / `Bucket` | `Images/ImageDecoder.cs:26,71,182,176` | 头部识别格式、只读文件头取像素尺寸（PNG/JPEG/GIF/WebP/BMP/ICO，SVG 只看 `Content-Type` 或前 512 字节文本）、按档位 `Bitmap.DecodeToWidth` |
| `ImageFetchResult` / `ImageCacheHeaders` / `ImageFetchStatus` | `Images/IImageFetcher.cs:112,56,43` | 获取结果四态（`Ok` / `NotModified` / `Failed` / `Blocked`）与缓存头（`ExplicitFreshness` 第 83 行算新鲜期） |
| `ImageLease` | `Images/ImageMemoryCache.cs:10` | 引用计数句柄：`Image` / `PixelSize` / `DecodedSize` / `AddRef` / `Evict`；最后一个句柄释放且条目已淘汰时才真正释放位图 |
| `ImageDiskCacheEntry` | `Images/ImageDiskCache.cs:9` | `<sha256>.json` 元数据：`Url` / `ETag` / `LastModified` / `ExpiresUtc` / `FreshnessSeconds` / `MustRevalidate` / `PixelWidth/Height` / `LastAccessUtc` / `Size` / `SourceVersion`（缩略图专用，第 43 行） |
| `ImagePipeline.TryLoadThumbnailAsync` / `WriteThumbnailAsync` / `ThumbnailKey` / `SourceVersion` | `ImagePipeline.cs:466,493,434,440` | 缩略图的读 / 写 / 键 / 失效依据（详见下面 State it owns） |
| `ImageSlot.Ensure` / `Load` / `AcquireLease` / `Release` | `Rendering/ImageSlot.cs:49,74,44,134` | 地址 / 档位未变则直接返回；否则先同步查缓存，未命中就异步加载并用 `_generation` 丢弃过期结果 |
| `TextSegmentView.ShowViewer` | `Rendering/TextSegmentView.cs:479` | 文档内图片的默认查看器：取缩小图的一个额外句柄、按自然尺寸开 `ImageViewer`、`IsDownscaled` 时后台 `LoadAsync(url, 0)` 换原图、`Closed` 时 `Evict` 原图 |
| `ImageViewer.Show` / `Attach` / `UpdateSize` / `Close` / `Closed` | `Rendering/ImageViewer.cs:40,47,65,86,100,98` | 两个 `Show` 重载（`IImage` / 任意 `Size` + 绘制委托）；尺寸跟随覆盖层与窗口客户区；`Close` 幂等并只触发一次 `Closed` |

## Dependencies
- Uses: `core-parsing-and-model` via `InlineObject(InlineObjectKind.Image, src, alt, title)` 与 `Attributes`（`width` / `height` 已由解析层从 `style` 规范化）— 消费点 `src/StreamMark.Avalonia/Rendering/TextSegmentView.cs:1183-1192`，`DeclaredSize` 第 1207 行；模型定义 `src/StreamMark.Core/Model/InlineContent.cs:89`；HTML 侧规范化 `src/StreamMark.Core/Html/HtmlMapper.cs:426-439`
- Uses: `core-parsing-and-model` via `Remend.IncompleteImageUrl`（流式中地址未写完 → 传 `null` slot 的骨架占位）— `Rendering/TextSegmentView.cs:1186`，常量 `src/StreamMark.Core/Remend/Remend.cs:87`
- Uses: `avalonia-text-rendering` via `DrawableTextRun`（`Size` / `Baseline` / `Draw`，基类在 Avalonia 的 `Avalonia.Media.TextFormatting`）— `Rendering/InlineRuns.cs:69`；`RenderContext.ImageBaseUri` 每帧传入 `SlotFor` — `Rendering/RenderContext.cs:33`、`TextSegmentView.cs:459`
- Uses: `avalonia-text-rendering` 的宽度相关失效：可用宽度变化时图片 run 整体失效（`HasWidthDependentObjects` 第 420 行、`TextSegmentView.cs:331-338`）
- Uses: `avalonia-transcript` 无关方向反之：`ImageSlot` 由视图持有，视图被回收即 `Dispose` — `Rendering/TextSegmentView.cs:268-288`
- Uses: `MarkdownServices`（附加属性承载管线与基准） via `Services.Images` / `ResolveImageUrl` — `Extensions/MarkdownServices.cs:55,69`
- Uses: `StreamMark.Mermaid` via `ISvgRasterizer`（接口在 `ImagePipeline.cs:62`，实现 `src/StreamMark.Mermaid/SvgImageRasterizer.cs:13`；不注入时 SVG 直接失败 `ImagePipeline.cs:391-395`）与 `IImageEncoder`（实现 `src/StreamMark.Mermaid/WebPImageEncoder.cs:14`，不注入时退回 Avalonia 的 PNG 编码 `ImagePipeline.cs:506-511`）
- Used by: `avalonia-text-rendering` via `ImageRun` 作为 `ITextSource` 的嵌入对象 — `Rendering/TextSegmentView.cs:1189,1192`
- Used by: `avalonia-text-rendering` 的图表全屏 via `ImageViewer.Show(anchor, Size, draw, contentBackground)`（图表不是图片，但共用查看器） — `Rendering/DiagramBlockView.cs:261-268`
- Used by: `tests` via `ImagePipeline` / `ImageMemoryCache` / `ImageDiskCache` / `ImageSlot` / `TextSegmentView` 诊断计数 — `tests/StreamMark.Avalonia.Tests/Images/ImagePipelineTests.cs:73,88`、`Rendering/TextSegmentView.cs:446,449,451,453`

## State it owns
- **内存缓存键与生命周期**（`ImageMemoryCache.cs:51-55` 注释）：键 =（规范化地址，解码宽度档位），同地址可有多个档位条目；字节按 `宽×高×4` 估算（`Entry.Bytes` 第 431 行），超预算时从 LRU 尾部淘汰**未被句柄引用**的条目（`TrimLocked` 第 333 行），默认 96MB（第 59 行）。`TryAcquire` 允许"原始尺寸或更大档位"满足更小请求（第 115-150 行，优先最接近的档位）。
- **负缓存**：失败地址记 `(原因, 到期时间)`，默认 5 分钟（第 67、78 行）；`ImagePipeline.LoadCoreAsync` 只在 `ImageFetchStatus.Failed` 且原因非 `canceled` 时写入（`ImagePipeline.cs:379-382`），`Blocked` 不进负缓存（`IImageFetcher.cs:49-52`）。
- **记忆的原始像素尺寸**：`_sizes`（上限 8192，满了整体清空，`RememberSize` 第 391 行）；`Clear()` 会一并清掉。
- **磁盘缩略图**：只在档位 > 0（即按显示宽度缩小解码，含按该档位栅格化的 SVG）且原图版本可知时生成。键 = `thumbnail:<档位>:<规范化地址>`（`ThumbnailKey` 第 434 行），磁盘文件名仍是 `SHA256(该键)`（`ImageDiskCache.PathsFor` 第 296 行），元数据 `SourceVersion` 记录生成时的原图版本，`PixelWidth/Height` 记**原图**的自然尺寸（第 514 行）。读：`LoadCoreAsync` 在取原始字节**之前**先查缩略图（第 372-375 行 → `TryLoadThumbnailAsync` 第 466 行），`SourceVersion` 不一致、缺像素尺寸或字节解不开（解不开会顺手删掉该条目，第 483-487 行）都视为未命中；命中时不发网络请求、不解码原图（`ThumbnailHits` 计数第 488 行，测试断言 `Decodes == 0`）。写：解码成功后不阻塞显示地起一个后台任务（第 425-430 行），编码期间用 `lease.AddRef()` 保住位图，写完释放（`WriteThumbnailAsync` 第 493-526 行），任何失败都被吞掉。
- **缩略图的失效依据**（`SourceVersion` 第 440-464 行）：本地文件 = `file:<大小>:<LastWriteTimeUtc.Ticks>`，且路径先经 `FileFetcher.ResolveAllowedPath` 按策略解析（所以缩略图不会绕过 `AllowedFileRoots`，测试断言被拒时 `ThumbnailHits == 0`）；远程图片 = 原图磁盘条目存在且 `IsFresh` 时的 `remote:<Size>:<ETag>:<LastModified.UtcTicks>`（过期时先走重新验证，不使用缩略图）；`data:` 与宿主自定义获取器一律返回 null（不用缩略图）。整体由 `_thumbnails = options.DiskThumbnails && Disk is not null` 开关（第 112 行）。
- **磁盘缓存**：文件名主干 = `SHA256(地址)` 小写十六进制（`HashKey` 第 105 行），`<hash>.bin` + `<hash>.json`；写入先临时文件再覆盖改名（`WriteAtomicAsync` 第 350 行），元数据版本或地址不符即删掉整对文件（`ReadMetadata` 第 303-341 行）；超过容量（默认 512MB，第 66 行）在后台按 `LastAccessUtc` LRU 清理，构造时也清一次（第 95 行），并清理 10 分钟以上的 `.tmp` 与 1 分钟以上的孤立 `.bin`（第 456-474 行）。缩略图与原图共用同一目录、容量上限与 LRU 清理。它有**自己的时钟**（`ImagePipelineOptions.TimeProvider` 注入）。
- **进程共享默认管线**：`MarkdownServices.s_defaultImages`（`MarkdownServices.cs:31,55`）是静态字段，`Default` 服务的 `Images` 惰性创建并全进程共用——**只有内存缓存**（构造时不传 `DiskCacheDirectory`），没有所有者、不释放，也不会随宿主设置页变化。宿主自建 `ImagePipeline` 时由宿主负责 `Dispose`（注释见 `MarkdownServices.cs:51-54`）。
- **视图侧句柄**：`ImageSlot._lease`（`ImageSlot.cs:16`）在 `Release` 时归还（第 134-139 行）；`TextSegmentView` 的 `_images` 字典按解析后的地址缓存 slot，`OnDetachedFromVisualTree` 里全部 `Dispose` 并清空（`TextSegmentView.cs:268-288`），重新挂载时重新取得（一般命中内存缓存）；`_usedImages` + `ReleaseUnusedImages`（第 455、532 行）只在整轮重建时释放本轮没用到的地址。
- **查看器的临时句柄**：`ShowViewer` 先用 `slot.AcquireLease()`（`ImageSlot.cs:44`）拿缩小图的一个独立句柄，再按 `slot.PixelSize`（自然尺寸）开查看器；`IsDownscaled`（`ImageSlot.cs:41`）为真时才后台 `LoadAsync(url, 0)` 取原图，成功后替换绘制用的位图并 `InvalidateVisual`（`TextSegmentView.cs:505-529`）。关闭时 `Closed` 处理器释放缩小图句柄并 `original.Evict()` + `Dispose()`（第 492-498 行）——`Evict` 把原图条目移出内存缓存（`ImageMemoryCache.cs:295-306`），句柄本身仍在，位图到最后一个句柄释放才释放；查看器在加载完成前就被关掉时，回调里补一次 `Evict`（第 520-525 行）。
- **派生副本**：`ImageSlot.PixelSize` 是"缓存元数据里已知的自然尺寸"（`ImageSlot.cs:33-34`、`Ensure` 第 70 行），可与 `Lease.PixelSize` 不同（占位时先有、加载后以 lease 为准，第 102 行）；`IsDownscaled` 比较的是 `DecodedSize.Width < PixelSize.Width`（第 41 行）；`ImagePipeline` 的 7 个 `internal` 计数器（`ImagePipeline.cs:138-144`）与 `ImageDiskCache._approxBytes`（第 76 行）都是估算 / 诊断值。
- **释放责任**：`ImageLoadResult` 与 `ImageLease` 都 `IDisposable`（`ImageLoadResult.cs:54`、`ImageMemoryCache.cs:42`），成功路径的句柄归调用方；管线内部只在等待者归零后释放自己那份 master 引用（`ImagePipeline.cs:299-312`）。

## Contracts it exposes
- **`IImageFetcher`**（`IImageFetcher.cs:8`）：实现必须线程安全；`FetchAsync` 失败返回 `Failed` / 策略拒绝返回 `Blocked`，**不抛**（调用方取消才抛 `OperationCanceledException`，第 19-20 行）；`IsRemote` 同时决定"是否受同意约束"和"是否落磁盘缓存"——`ImagePipeline.FetchBytesAsync` 里 `var disk = fetcher.IsRemote ? Disk : null`（`ImagePipeline.cs:565`，注释也写在 `IImageFetcher.cs:13-16`）。它**不影响**缩略图：`SourceVersion` 只认 `FileFetcher` 与 `IsRemote` 两条路径（`ImagePipeline.cs:448-458`）。
- **`IImageEncoder`**（`ImagePipeline.cs:55`）：`Encode` 返回 null 或抛异常都只是"这次缩略图不落盘"，不影响显示（`WriteThumbnailAsync` 第 495-521 行）；编码在线程池上跑，实现必须线程安全。默认编码器是 Avalonia 的 PNG（第 506-511 行），`WebPImageEncoder` 走 Skia 有损 WebP。
- **`ImagePolicy` 允许的协议与文件根**：`CheckUri`（`ImagePolicy.cs:50`）拒绝相对地址、默认拒绝 `http`、`file:` 直接放行（协议层面），域名按 `BlockedHosts` 优先、`AllowedHosts` 通配（`*.x.com` / `.x.com` 只匹配子域，第 104-130 行）；本地文件**必须**由载荷再检查一次：`FileFetcher.ResolveAllowedPath` + `ImagePipeline.LoadAsync` 里的 `AllowedFileRoots.Count == 0 → Blocked("file access not allowed")`（`ImagePipeline.cs:248-251`）。
- **`MarkdownServices.ImageBaseUri` 与 `ImagePipeline` 的关系**：`ImageBaseUri` 只做**地址解析**，相对地址在进入管线之前由 `MarkdownServices.ResolveImageUrl` 拼成绝对地址（rooted 路径用 `new Uri(Path.GetFullPath(...))` 转 `file:`；没有基准时原样返回相对地址，随后被管线判 `invalid url` 而失败）。它**不会**放行本地文件——放行只能靠 `ImagePolicy.AllowedFileRoots`，这一点写在宿主机面向的注释里（`MarkdownServices.cs:57-60`）。
- **尺寸与对齐约定**：显示尺寸像浏览器一样 1 像素 = 1 DIP；**尺寸取 `slot.PixelSize`（自然尺寸）或 `width`/`height` 属性，解码出的位图只用于绘制**——它可能更小（按显示宽度解码）或更大（高分屏），因此缩小解码不会让占位尺寸或行高变小（`ImageRun` 构造函数注释 `InlineRuns.cs:83-84`）。只给宽度时按自然宽高比推高；未加载但已知自然尺寸时**先按同样尺寸画骨架**，加载完成不跳动；两个方向都被限制在可用宽度内（`maxWidth`，第 91 行）。`Baseline` 在有标签时取标签首行基线，否则取整个高度（第 110 行），因此图片整体坐在基线上、行高随之增长；`SegmentLayout` 只在"行内有更高的 `DrawableTextRun`"时丢掉固定行高重排（`SegmentLayout.cs:108-117,155-165`）。
- **解码宽度**：`SlotFor` 用 `显示宽度 × TopLevel.RenderScaling` 作为物理像素宽度（无声明宽度、无限宽时兜底 1024），再向上取 256 的档位（`TextSegmentView.cs:466-473`）；`ImagePipeline.DecodeDensity` 有值时渲染缩放取 `min(RenderScaling, DecodeDensity)`（宿主压缩画质 / 内存用，只影响文档内显示）。点击图片的默认查看器（`TextSegmentView.ShowViewer`）按**原始尺寸**打开：先画缩小图并持有它的一个句柄，`LoadAsync(url, 0)` 换成原图（档位 0 因此永不命中缩略图），关闭时 `ImageLease.Evict()` 把原图移出内存缓存，不让它常驻。

## How to extend
- To add 一种协议 / 获取器：写一个 `IImageFetcher`（`CanFetch` 返回小写协议名、`IsRemote` 明确本地还是远程）→ 通过 `ImagePipelineOptions.Fetchers` 注册（`ImagePipeline.cs:25,110`，注册项排在 `data:` / `https` / `file` 之前）→ 若要求同意或落盘，必须把 `IsRemote` 设为 `true`，否则 `ImagePipeline.FetchBytesAsync` 不会给它磁盘缓存（`ImagePipeline.cs:565`），`RequiresConsent` 也不会触发（`ImagePipeline.cs:262`）；这类来源也**不会**有缩略图（`SourceVersion` 第 448-458 行只认 `FileFetcher` 与远程）。漏掉注册的表现是 `LoadAsync` 直接返回 `Blocked("unsupported scheme: …")`（`ImagePipeline.cs:238-242`）。参考测试 `ImagePipelineTests.Custom_scheme_fetcher_is_used`（`tests/StreamMark.Avalonia.Tests/Images/ImagePipelineTests.cs:704`）——它用一个测试内私有的 `LambdaFetcher("app", …)`（`ImagePipelineTests.cs:738`）。
- To add 一种解码格式：先在 `ImageFormat` 加枚举值并在 `Sniff` 里加头部判定（`ImageDecoder.cs:8,26`）——这一步决定磁盘缓存是否愿意落盘（`ImagePipeline.cs:611` 对 `Unknown` / `Svg` 不建条目）→ 再在 `TryReadSize` 加分支（第 71 行，占位宽高比依赖它，失败只是占位不准）→ 最后确认 `ImageDecoder.Decode` 能解（第 182 行，走 `Bitmap` / `Bitmap.DecodeToWidth`，解码失败会 `AddFailure("decode failed")` 并删掉磁盘副本，`ImagePipeline.cs:406-414`）。漏掉第 2 步的表现是：图能显示，但加载前没有正确占位、也没有磁盘缓存；矢量 / 需要专用栅格化的格式参考 SVG 的走法（`ISvgRasterizer`，`ImagePipeline.cs:62,389-401`）。
- To 换缩略图格式：实现 `IImageEncoder` 并传 `ThumbnailEncoder`（例如 `StreamMark.Mermaid.WebPImageEncoder`）；注意编码器对像素格式有要求时会返回 null（`WebPImageEncoder.cs:20-36`），表现为"能显示但没有缩略图"而不是报错。关掉缩略图是 `DiskThumbnails = false`（`ImagePipeline.cs:112`）。
- To add 宿主接入带磁盘缓存 + 缩略图的管线（三步）：`new ImagePipeline(new ImagePipelineOptions { DiskCacheDirectory = …, Policy = ImagePolicy.Default with { AllowedFileRoots = [工作区] }, SvgRasterizer = …, DecodeDensity = 1, ThumbnailEncoder = new WebPImageEncoder() })`（`ImagePipeline.cs:99`）→ 放进 `new MarkdownServices { Images = pipeline }` 并 `MarkdownServices.SetServices(root, services)`（`MarkdownServices.cs:55`）→ 在**同一个 root** 上 `MarkdownServices.SetImageBaseUri(root, 工作区)`（`MarkdownServices.cs:61-66`）。漏掉第 3 步的表现是相对路径原样进管线 → `invalid url` → 整片图片显示失败标签（测试 `RelativeImage_WithoutBaseUri_FailsInsteadOfHanging`，`ImageRenderingTests.cs:167`）；另外宿主必须自己 `using` / `Dispose` 这个 pipeline（`ImagePipeline.Dispose` 会取消在途请求并释放内存缓存与内置 `HttpsFetcher`，`ImagePipeline.cs:315-324`），并可用 `Disk.ClearAsync` / `Disk.GetSizeAsync` 做设置页（第 135 行注释）。

## Invariants and traps
- **默认管线只有内存缓存**：`MarkdownServices` 的共享默认实例不带磁盘缓存、没人释放；要磁盘缓存与本地文件必须宿主自建并负责释放 — `MarkdownServices.cs:31,55`、`ImagePipeline.cs:105-108,315`。
- **本地文件必须显式放行**：`AllowedFileRoots` 为空时一切 `file:` 都被拒绝（哪怕 `CheckUri` 对 `file:` 返回 null），且路径要先规范化再逐级解析符号链接比较前缀，因此"允许工作区"不会顺带允许工作区外的软链接目标 — `ImagePipeline.cs:248-251`、`FileFetcher.cs:57-95`。
- **缩略图必须先过策略**：本地缩略图的 `SourceVersion` 走 `FileFetcher.ResolveAllowedPath`，解析不出来就返回 null（不用缩略图），所以缓存里残留的缩略图不会被越权目录读到 — `ImagePipeline.cs:448-453`，测试 `ImagePipelineTests.Local_file_thumbnail_is_reused_until_the_file_changes` 末段（`ImagePipelineTests.cs:106-115`）。
- **缩略图只在档位 > 0 且原图版本可知时生效**：`LoadCoreAsync` 的入口条件是 `bucket > 0`（第 372 行），`SourceVersion` 返回 null 就不用；远程图片还要求原图磁盘条目存在且新鲜（第 455 行）。所以"刚清过缓存"或"只有内存缓存"的管线一定重新解码原图——这不是 bug，而是避免拿旧缩略图配新原图。
- **缩略图元数据里的 `PixelWidth/Height` 是原图尺寸**（第 514 行，测试 `ImagePipelineTests.cs:75` 断言 1600），命中时把它当 `natural` 传给内存缓存（第 489-490 行），占位宽高比因此与原图一致；不要改成缩略图自身的尺寸。
- **`ImageBaseUri` 是 `inherits: true` 的附加属性**：值从窗口 / 会话视图继承到 `TextSegmentView`，并在成员变化时作废渲染上下文（`MarkdownView` 的类处理器 `MarkdownView.cs:45`、`TranscriptPanel.ContextFor` 比较后重建 `TranscriptPanel.cs:359-367`）；不设它时相对地址不会被解析，直接失败。
- **流式过程中的占位尺寸决定行高与滚动**：`IncompleteImageUrl` 或尚未取到尺寸时画的是标签 / 骨架（尺寸未知 → 由标签或 alt 决定），一旦 `TryGetPixelSize` 能给出自然尺寸（磁盘元数据 / `data:` 文件头）就按宽高比占位，`Load` 完成后再 `_changed()` → `OnResourceLoaded` → `_runCache.Invalidate()` + 全量重排（`TextSegmentView.cs:433-439`）；这段重排会改变行高，虚拟化下表现为滚动位置 / 滚动条长度变化（`avalonia-transcript` 的锚定机制负责兜住，见 `TranscriptPanel.cs:463,480`）。
- **`[![…](…)](…)` 的点击行为分两层**：解析层给图片的占位字符多加一个 `InlineStyle.Link` 区间（`MarkdigMapper.cs:620-624`，HTML 侧 `HtmlMapper.cs:451-454`），渲染层在 `OnPointerReleased` 里按**状态**分派——`Loaded` 且 `LinkAt(position) >= 0` 时发 `LinkClicked` 打开链接，其余成功情况发 `ImageClicked` 并在未被标记 `Handled` 时 `ShowViewer`；失败 / 需要同意时分别 `Retry` / `Approve`（`TextSegmentView.cs:896-924`）。因此"徽章图片点开链接"不需要图片子系统做任何事，反而是图片自己的查看器被让位。
- **点击要落到 `OnPointerReleased`，依赖选区不抢指针**：`SelectionController` 只在指针移动超过 3px（`DragThreshold` 第 54 行）后才捕获指针，且按在宿主内容控件上时直接返回（第 77、115 行）。改回"按下即捕获"会让图片 / 链接点击被吞掉（这正是 `9b0e08b` 与 `InteractionRegressionTests.Selection_IgnoresPressesOnHostContent` 记录的问题）。
- **查看器不能固定为打开时的尺寸**：`OverlayLayer` 是 `Canvas`，不会拉伸子控件，而且第一次使用前可能是 0×0（AtomUI 窗口），固定成 `layer.Bounds` 会让查看器不可见；`Attach` + `UpdateSize` 因此同时监听覆盖层 `Bounds` 与 `TopLevel.ClientSize`（`ImageViewer.cs:63-95`），回归测试 `ImageViewer_FillsOverlay_AndFollowsResize`（`tests/StreamMark.Avalonia.Tests/Rendering/InteractionRegressionTests.cs:17`）。
- **`Close()` 幂等、`Closed` 只触发一次**：`_closed` 守卫（`ImageViewer.cs:100-120`）保证 `ShowViewer` 的 `Closed` 处理器只跑一次，重复 `Close` 不会重复 `Evict`；点背景关闭也走同一个 `Close`（第 226 行）。宿主自己 `Show` 查看器时若不订阅 `Closed`，就要自己负责句柄生命周期。
- **`Show(anchor, IImage, caption)` 只是重载的包装**：它用 `image.Size` 与 `DrawImage` 委托调用 `Show(anchor, Size, draw, contentBackground, caption)`（`ImageViewer.cs:40-41`）；图表全屏用同一个重载但传不透明底色，因为深色遮罩上主题色内容会失去对比度（`DiagramBlockView.cs:261-268`）。`size` 任一维 ≤ 0 时 `Show` 返回 null（第 49 行）。
- **句柄不在手时位图可能已被释放**：`ImageMemoryCache` 只保证"被 `ImageLease` 引用"的条目不被释放，`Clear()` / `Dispose()` 之后仍持有的 lease 才让位图活到最后一次 `Dispose`（第 264-284 行）；`ImageSlot.Image` 在 `Release` 之后仍是旧引用，所以 `Dispose` 后不应再用（`ImageMemoryCache.cs:20` 注释、`ImageSlot.cs:134-147`）。
- **取消不取消共享获取**：同一地址（同档位）的并发 `LoadAsync` 合并到一次请求；某个等待者取消只让它自己收到异常，底层获取继续完成并写缓存 — `ImagePipeline.cs:227,271-312`，测试 `Cancelled_waiter_does_not_cancel_shared_fetch`（`ImagePipelineTests.cs:329`）。不同档位也会共享同一次字节获取（`_bytesInflight`，第 529-559 行）。
- **`ImageSlot.Ensure` 的短路条件容易误读**：`Status is not null && Status != Loaded` 表示"已失败 / 需同意 / 被拒绝"时，只要 pipeline 与地址不变就直接返回，不重新请求——重试必须走 `Retry()` 或 `Approve()`（`ImageSlot.cs:52,109,122`）。
- **`decodeWidth` 是物理像素**：传逻辑宽度会得到过小的档位，在高分屏上表现为图片发虚；反向传过大会浪费内存 — `TextSegmentView.cs:466-473`、`ImagePipeline.GetDecodeBucket` 文档注释 `ImagePipeline.cs:165`。`DecodeDensity` 是**上限**（取 `min`），不是放大倍率。
- **SVG 只在注入栅格化器时可用**，且 `ImageDecoder.Sniff` 靠 `Content-Type` 或前 512 字节文本识别（`ImageDecoder.cs:52-66`）；未注入时返回 `Failed("svg not supported")` 并进负缓存（`ImagePipeline.cs:391-395`），磁盘缓存也不为 SVG 建条目（第 611 行），但栅格化后的 SVG 可以写缩略图（第 425 行起）。
- **磁盘缓存的新鲜度默认 7 天**：响应没有任何缓存头时用 `DefaultFreshness`（`ImageDiskCache.cs:69,260`）；过期后用 ETag / Last-Modified 条件请求，304 时刷新元数据，重新验证失败且没有 `must-revalidate` 时继续用旧内容（`ImagePipeline.cs:591-635`）。
- **缩略图写入是后台且静默的**：`WriteThumbnailAsync` 吞掉所有异常，所以"缩略图没写出来"没有任何界面症状，只能靠 `Disk.TryGetEntry("thumbnail:<档位>:<地址>")` 或 `ThumbnailHits` 观察（测试用 `WaitForThumbnailAsync` 轮询，`ImagePipelineTests.cs:38-50`）。

## Tests
- 位置：`tests/StreamMark.Avalonia.Tests/Images/`，4 个文件：`ImagePipelineTests.cs`（24 个 `[Fact]` + 1 个 `[Theory]` 的 7 个 `FailureCases`：网络→内存→磁盘、本地 / 远程磁盘缩略图与失效、HTTP 缓存语义、请求合并、策略与同意、失败负缓存、档位解码、data: URI、SVG 不支持、自定义协议）、`ImageCacheTests.cs`（10 个 `[Fact]` + 1 个 `[Theory]` 的 5 个 `[InlineData]`：LRU 与租约保护、负缓存过期、磁盘清理与损坏恢复、`FileFetcher` 越界、头部尺寸嗅探）、`ImageRenderingTests.cs`（4 个 `[Fact]`：`ResolveImageUrl`、工作区相对路径 PNG+SVG 渲染、解码密度 + 查看器换原图并在关闭时逐出、无基准地址时失败而不挂起）、`ImageTestSupport.cs`（`TestPng.Create` 自造可解码 PNG、`TestImageServer`、`FakeImage`、`TempDir`、`ManualTimeProvider`）。查看器的尺寸回归在 `tests/StreamMark.Avalonia.Tests/Rendering/InteractionRegressionTests.cs:17`。
- 命名模式：类名 `<主题>Tests`，方法用"行为_期望"的下划线句子（如 `Lease_outlives_eviction_from_pipeline_memory`、`Local_file_thumbnail_is_reused_until_the_file_changes`），方法体包在 `await HeadlessApp.Run(async () => { … })` 里在界面线程跑（`tests/StreamMark.Avalonia.Tests/HeadlessApp.cs:55`），渲染类测试用 `HeadlessApp.Settle()`（第 65 行）推进帧并可用 `HeadlessApp.Save(window, "…")`（第 44 行）落截图。
- 只跑一个类 / 文件：`dotnet test StreamMark.slnx --filter "FullyQualifiedName~Images.ImagePipelineTests"`（同理替换 `ImageCacheTests` / `ImageRenderingTests`）；整个模块 `--filter "FullyQualifiedName~Tests.Images"`。仓库既有命令是 `dotnet test StreamMark.slnx`（`README.md:139`）。未运行验证：(unverified)。

## Related flows
- [raw-html-render](../flows/raw-html-render.md)
- [streaming-token-to-view](../flows/streaming-token-to-view.md)
- [transcript-virtualization-scroll](../flows/transcript-virtualization-scroll.md)
