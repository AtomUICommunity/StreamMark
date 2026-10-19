---
module: theme-and-styles
sources: ["src/StreamMark.Avalonia/MarkdownResourceKeys.cs", "src/StreamMark.Avalonia/MarkdownResourceAlias.cs", "src/StreamMark.Avalonia/Themes/**", "src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs", "src/StreamMark.Theme.AtomUI/**"]
commit: 8d344a6
updated: 2026-10-18
---
# 主题与样式解析（`Md.*` 资源 → `MarkdownStyle` 快照 → AtomUI 令牌）

StreamMark 的全部外观（颜色、字体、行高、间距、圆角、明暗）都走一套 Avalonia 资源键 `Md.*`：默认值写在 `StreamMarkTheme` 的 Light / Dark 主题字典里，宿主（如 `StreamMark.Theme.AtomUI`）用同名键或 `MarkdownResourceAlias` 覆盖，`MarkdownStyle.Resolve(control)` 在控件可见的资源作用域内一次性读成不可变快照，由 `MarkdownView` / `TranscriptPanel` 下发给每个块视图。

快照时刻说明：本页写入时 `git rev-parse HEAD` = `8d344a6`；写之前工作树已有 4 个模块页（`avalonia-extensions` / `avalonia-plugin-system` / `avalonia-text-rendering` / `avalonia-transcript`）处于未提交状态，本页是随后单独重写的。与上一版 front matter 的 `e01b99a` 相比，本模块的 5 个源只有 2 个文件变了：`MarkdownResourceKeys.cs`（+2 个键常量）与 `Rendering/MarkdownStyle.cs`（+2 个 `required` 属性与对应解析）。`git diff e01b99a..8d344a6 -- src/StreamMark.Avalonia/Themes/StreamMarkTheme.axaml` 与 `-- src/StreamMark.Theme.AtomUI/AtomUIMarkdownTheme.cs` **均为空输出**（逐字节相同），所以本页对这两个文件的引用在 `e01b99a` 与 `8d344a6` 下同样成立。

**不负责：** 快照如何被消费到排版与绘制（`avalonia-text-rendering`，`src/StreamMark.Avalonia/Rendering/TextSegmentView.cs:346`、`CodeBlockView.cs:341`）；`RenderContext` 的身份语义与视图重建（`avalonia-text-rendering`，`src/StreamMark.Avalonia/Rendering/RenderContext.cs:12`、`BlockView.cs:27`）；查找模型与高亮区间本身（`avalonia-selection`，`src/StreamMark.Avalonia/Selection/SearchModel.cs:8`、`SelectionModel.cs:76`，本页只管“这些区间用哪个画刷”）；会话虚拟化的行高估算（`avalonia-transcript`，`src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:702`）；`MarkdownServices` 附加属性本身（`avalonia-plugin-system`，`src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:100`）；TextMate 主题内部（`avalonia-extensions`，`src/StreamMark.Highlighting.TextMate/ThemeContext.cs:31`）；`IDiagramRenderer` 的 `DiagramRenderContext` 定义（`src/StreamMark.Avalonia/Extensions/IDiagramRenderer.cs:10`，本页只到“谁把 `style.IsDark` 传进去”为止）。

## Entry points
| Symbol | File | Role |
|---|---|---|
| `MarkdownResourceKeys` | `src/StreamMark.Avalonia/MarkdownResourceKeys.cs:7` | 公开契约：22 个 `Md.*` 键常量（:9–35），宿主覆盖这些键即可换肤 |
| `MarkdownResourceAlias` | `src/StreamMark.Avalonia/MarkdownResourceAlias.cs:8` | `record MarkdownResourceAlias(object Key)`：把一个 `Md.*` 键指向宿主另一个资源键（不写死值） |
| `StreamMarkTheme` | `src/StreamMark.Avalonia/Themes/StreamMarkTheme.axaml:1`、`.axaml.cs:7` | 默认主题：`Styles.Resources` 里的 Light / Dark 主题字典 + 3 个不随明暗变化的键 |
| `MarkdownStyle.Resolve` | `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:70` | 唯一的解析入口：从控件可见资源 → 不可变 `MarkdownStyle`（含全部硬编码回退值） |
| `AtomUIMarkdownTheme` | `src/StreamMark.Theme.AtomUI/AtomUIMarkdownTheme.cs:15` | 代码主题：13 个 `Md.*` 键 → `MarkdownResourceAlias(SharedTokenKind.*)` |

## Key types and functions
| Symbol | File | Role |
|---|---|---|
| `MarkdownStyle` | `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:15` | 不可变快照：16 个 `IBrush` + `FontFamily` / `MonospaceFontFamily` / `FontSize` / `LineHeight` / `BlockSpacing` / `CornerRadius` / `IsDark`，全部 `required … { get; init; }`（:17–41） |
| `MarkdownStyle.Resolve` 内的局部 `Get<T>(key, fallback)` | `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:74` | 单个键的解析：`TryFindResource(key, variant)` → 解别名 → `is T` 命中 / `Color` 转 `ImmutableSolidColorBrush` → 否则返回 `fallback`（:76–96） |
| `MarkdownStyle.CodeFontSize` / `CodeLineHeight` | `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:43` / `:45` | 派生值：`round(FontSize * 0.9)` / `round(CodeFontSize * 1.55)`（不是资源键） |
| `MarkdownStyle.HeadingFontSize` | `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:50` | 标题字号倍数：1 → 1.75，2 → 1.5，3 → 1.25，4 → 1.1，5/6 → 1.0 |
| `MarkdownStyle.AlertBrush` | `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:60` | 提示块强调色：TIP / IMPORTANT / WARNING / CAUTION 硬编码（按 `IsDark` 分两套），其它 kind 返回 `LinkForeground` |
| `MarkdownStyle.HighlightTheme` | `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:47` | `IsDark ? Dark : Light` 的 `HighlightThemeKind`，是高亮器 / Mermaid 的明暗入口 |
| `StreamMarkTheme`（XAML 资源） | `src/StreamMark.Avalonia/Themes/StreamMarkTheme.axaml:9` / `:22` / `:36` | 每个主题字典 11 个 `SolidColorBrush`；主题字典之外声明 `Md.MonospaceFontFamily`（:36）、`Md.BlockSpacing`（:37）、`Md.CornerRadius`（:38） |
| `StreamMarkTheme`（代码） | `src/StreamMark.Avalonia/Themes/StreamMarkTheme.axaml.cs:11` | `AvaloniaXamlLoader.Load(this)`，以 `x:Class` 编译加载（NativeAOT 安全，不用运行时 `StyleInclude`，见 :6 注释） |
| `AtomUIMarkdownTheme.Map` | `src/StreamMark.Theme.AtomUI/AtomUIMarkdownTheme.cs:34` | `Resources[key] = new MarkdownResourceAlias(token)`；13 次调用在 :19–31 |

## Dependencies
- Uses: `core-highlighting` via `HighlightThemeKind`（`MarkdownStyle.HighlightTheme` 的返回类型）— `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:7`、`:47`
- Used by: `avalonia-text-rendering` via 读取快照属性（`Style.BlockSpacing` / `Style.LineHeight` / `Style.CodeBackground` / `Style.SelectionBackground` / `Style.SearchCurrentBackground`…）— `src/StreamMark.Avalonia/Rendering/BlockView.cs:75`、`src/StreamMark.Avalonia/Rendering/TextSegmentView.cs:346`、`:577`、`:587`、`src/StreamMark.Avalonia/Rendering/CodeBlockView.cs:341`、`:651`、`src/StreamMark.Avalonia/Rendering/DiffBlockView.cs:823`、`src/StreamMark.Avalonia/Rendering/TableBlockView.cs:176`
- Used by: `avalonia-transcript` via `MarkdownStyle.Resolve(this)` 与 `_style` 缓存 / 失效 — `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:361`、`:534`、`:872`、`:348`、`:770`
- Used by: `avalonia-extensions` via `style.HighlightTheme` 选高亮主题、`style.IsDark` / `style.Foreground` / `style.CodeBackground` / `style.LinkForeground` 组 `DiagramRenderContext` — `src/StreamMark.Avalonia/Rendering/CodeBlockState.cs:51`、`src/StreamMark.Avalonia/Rendering/DiffBlockView.cs:952`、`src/StreamMark.Avalonia/Rendering/DiagramBlockView.cs:124`、`src/StreamMark.Mermaid/MermaidDiagramRenderer.cs:138`
- Used by: `avalonia-plugin-system` via 把 `MarkdownStyle` 交给插件（`ChipRun(…, context.Style.LinkForeground, context.Style.InlineCodeBackground, …)`、`InlineComponentStyle(style.LinkForeground, …)`、`InlineComponentContext(MarkdownStyle Style, …)`）— `src/StreamMark.Avalonia/Plugins/BuiltInComponentsPlugin.cs:233`、`:244`、`src/StreamMark.Avalonia/Plugins/IMarkdownPlugin.cs:124`
- Used by: 宿主 / 示例 / 测试 via `Styles.Add(new StreamMarkTheme())`（`AtomUIMarkdownTheme` 在其后加入）— `README.md:27`、`samples/StreamMark.Gallery/Program.cs:61`、`tests/StreamMark.Avalonia.Tests/HeadlessApp.cs:20`
- Used by: `tests` via `ThemeSnapshotTests` / `StyleKeysTests` 直接调 `MarkdownStyle.Resolve` 并读 `MarkdownResourceKeys`，`DiagramBlockViewTests` 往 `w.Resources[MarkdownResourceKeys.*]` 写覆盖值再断言渲染结果 — `tests/StreamMark.Avalonia.Tests/ThemeSnapshotTests.cs:20`、`tests/StreamMark.Avalonia.Tests/Rendering/StyleKeysTests.cs:20`、`tests/StreamMark.Avalonia.Tests/Rendering/DiagramBlockViewTests.cs:108`、`:134`

## State it owns
- `MarkdownStyle` 实例本身是**不可变、无缓存**的：`Resolve` 每次都 `new` 一份（`MarkdownStyle.cs:104`）。唯一持有处是调用方：`MarkdownView._context`（`MarkdownView.cs:34`，`_context ??= CreateContext()`，:167；`CreateContext` 在 :172–173 里调 `Resolve`）与 `TranscriptPanel._style`（`TranscriptPanel.cs:75`，`_style ??= …`，:361 / :534 / :872）。
- 重新解析的触发点（置空缓存 + 请求下一帧）：`MarkdownView` 在构造时挂 `ResourcesChanged` / `ActualThemeVariantChanged` → `InvalidateContext()`（`MarkdownView.cs:66`、`:67`、`:175`），`MarkdownServices.ServicesProperty` / `ImageBaseUriProperty` 变化同样（:44–45），进入可视树时也置空（:189）；`TranscriptPanel` 挂同样的两个事件 → `InvalidateStyle()`（`TranscriptPanel.cs:219`、`:220`、`:348`），`MarkdownServices.ServicesProperty` 变化（:225）、`OnAttachedToVisualTree`（:1016）也走它，`InvalidateStyle` 还会把每个 entry 的 `Context` 置空（:350–353）；`ImageBaseUri` 在 `ContextFor` 里逐次比较（:364）。
- 别名的作用域规则：`MarkdownResourceAlias` 只存目标键；解析时**从 Markdown 控件本身**再查一次（`control.TryFindResource(alias.Key, variant, …)`，`MarkdownStyle.cs:83`），因此能找到只在窗口 / 主题作用域注册的资源；链长上限 4（:81）。
- 从宿主控件继承的值：`Md.FontFamily` / `Md.FontSize` 缺失时取 `control.GetValue(TextElement.FontFamilyProperty / FontSizeProperty)`（`MarkdownStyle.cs:102`、`:103`）；`Md.LineHeight` 缺失时是 `round(fontSize * 1.6)`——用的是**解析后**（可能继承来的）字号（:125）；`IsDark` 来自 `control.ActualThemeVariant`（:72–73、:128）；`Md.BlockSpacing` 缺失时 12（:126）；`Md.CornerRadius` 缺失时 `new CornerRadius(6)`（:127）。
- 无状态的部分：`AlertBrush`（:60）与 `HeadingFontSize`（:50）每次调用现算，`TIP` 等用 `Brushes.*` 静态画刷；`MarkBackground`（:110）、`SearchHighlightBackground`（:111）、`SearchCurrentBackground`（:112）的默认值是每次解析新建的画刷，非资源值。

## Contracts it exposes
- `Md.*` 键清单（`MarkdownResourceKeys.cs:9–35`，共 22 个），按类别：
  - 文字 / 交互色（9）：`Foreground`、`SecondaryForeground`、`LinkForeground`、`SelectionBackground`、`HoverBackground`、`ListMarkerForeground`（默认 = `SecondaryForeground`，:109）、`MarkBackground`、`SearchHighlightBackground`（:28）、`SearchCurrentBackground`（:30）。
  - 代码与图表容器（8）：`CodeBackground`、`CodeBorderBrush`、`InlineCodeBackground`、`InlineCodeForeground`（默认 = `Foreground`，:108）、`QuoteBorderBrush`、`TableBorderBrush`、`TableHeaderBackground`、`CornerRadius`。
  - 字体与度量（5）：`FontFamily`、`MonospaceFontFamily`、`FontSize`、`LineHeight`、`BlockSpacing`。
  覆盖范围说明：`Md.TableBorderBrush` 被表格外框（`TableBlockView.cs:176`）、H1/H2 与分隔线下方（`BlockView.cs:220`、`:244`）、行内代码边框（`ContainerViews.cs:94`）、脚注分隔线（`ContainerViews.cs:320`）共用；`Md.CodeBackground` / `Md.CodeBorderBrush` 同时用于代码块（`CodeBlockView.cs:341`）、图表块（`DiagramBlockView.cs:344`）与引用来源卡片（`SourceCard.cs:86`）。
- `MarkdownResourceAlias` 的宿主契约：值可以是任意宿主资源键；目标值类型要与取值处一致（`IBrush` 或 `Color`）；`Color` 仅在 `Get<IBrush>` 时自动转 `ImmutableSolidColorBrush`（`MarkdownStyle.cs:92`）；找不到目标键**不报错**，直接走 `fallback`（:85）。
- `AtomUIMarkdownTheme` 的映射表（`AtomUIMarkdownTheme.cs:19–31`，13 条）：`Foreground→ColorText`、`SecondaryForeground→ColorTextSecondary`、`LinkForeground→ColorLink`、`SelectionBackground→ColorPrimaryBgHover`、`CodeBackground→ColorFillQuaternary`、`CodeBorderBrush→ColorBorderSecondary`、`InlineCodeBackground→ColorFillTertiary`、`InlineCodeForeground→ColorText`、`ListMarkerForeground→ColorTextSecondary`、`QuoteBorderBrush→ColorBorder`、`TableBorderBrush→ColorBorderSecondary`、`TableHeaderBackground→ColorFillQuaternary`、`HoverBackground→ColorFillSecondary`。**未映射的 9 个**：`FontFamily` / `MonospaceFontFamily` / `FontSize` / `LineHeight` / `BlockSpacing` / `CornerRadius` / `MarkBackground` / `SearchHighlightBackground` / `SearchCurrentBackground`（字体字号故意留给控件继承，`AtomUIMarkdownTheme.cs:13`）。
- 查找高亮的两个新键：`Md.SearchHighlightBackground`（非当前结果）与 `Md.SearchCurrentBackground`（当前结果），README 明确点名（`README.md:101`）。三处会显示文字的视图各画一遍：`TextSegmentView.cs:577`、`CodeBlockView.cs:651`、`DiffBlockView.cs:823`，都按 `Selection.GetHighlights(Key, 长度)` 的 `current` 标志二选一。
- 默认字体 / 行高约定：默认字典故意**不设** `Md.FontFamily` / `Md.FontSize` / `Md.LineHeight`（`StreamMarkTheme.axaml:4–5` 注释、`docs/decisions.md:28`），字体字号沿用控件继承的 `TextElement` 值，行高 = 字号 × 1.6（`MarkdownStyle.cs:125`）；等宽字体默认 `Menlo, Consolas, "DejaVu Sans Mono", monospace`（`StreamMarkTheme.axaml:36`，回退值 `Menlo, Consolas, monospace`，`MarkdownStyle.cs:123`）。
- 22 个键中只有 14 个在默认字典里有条目（Light / Dark 各 11 个画刷 + 3 个非主题键），其余 **8** 个（`FontFamily`、`FontSize`、`LineHeight`、`InlineCodeForeground`、`ListMarkerForeground`、`MarkBackground`、`SearchHighlightBackground`、`SearchCurrentBackground`）故意依赖 `Resolve` 的 fallback。

## How to extend
- To add 一个新的 `Md.*` 键（必须同步的位置，缺一处都有症状）：
  1. `MarkdownResourceKeys` 加 `public const string X = "Md.X";`（`MarkdownResourceKeys.cs:9`）——缺了后面无从引用。
  2. `MarkdownStyle` 加 `required` 属性（`MarkdownStyle.cs:17–41`）并在 `Resolve` 的对象初始化器里 `Get<T>(MarkdownResourceKeys.X, 默认值)`（:104–129）——漏掉属性会直接编译失败（`required` 成员未赋值）。需要宿主写 `Color` 也能用时把 `T` 取成 `IBrush`（走 :92 的转换）。
  3. 在消费点（`src/StreamMark.Avalonia/Rendering/**`）真的读它；只加键不读等于没加。
  4. 默认值写进 `StreamMarkTheme.axaml`：随明暗变化的放 Light（:9）/ Dark（:22）两个主题字典各一条，不随明暗变化的放主题字典之外（参照 `Md.BlockSpacing`，:37）。**漏掉这一处的症状**：`TryFindResource` 返回 false，静默回落到 `Resolve` 里的硬编码 fallback（`MarkdownStyle.cs:76`），宿主即使覆盖了该键也不生效（键从未被任何字典定义时不会报错，只是看起来“没换肤”）。**本仓库就有现成例子**：`Md.SearchHighlightBackground` / `Md.SearchCurrentBackground` 只做了第 1、2 步（键 + 属性 + 回退值），两个主题字典里都没有条目——它们靠 `isDark` 分岔的硬编码回退值随明暗变化（`MarkdownStyle.cs:111–112`），但不跟随任何宿主 token。
  5. 可选：在 `AtomUIMarkdownTheme.Map`（`AtomUIMarkdownTheme.cs:19–31`）把键指向 AtomUI token。**漏掉这一处的症状**：AtomUI 宿主下该键仍取 `StreamMarkTheme` 的默认值（别名缺失 → 键直接命中前面的默认主题），若默认字典里也没有就是 `Resolve` 的硬编码值——两个查找键正是这种状态。
  6. 测试模板：`tests/StreamMark.Avalonia.Tests/Rendering/StyleKeysTests.cs:12`（同时断言 fallback 与覆盖生效）；覆盖值真的影响渲染的断言可参照 `tests/StreamMark.Avalonia.Tests/Rendering/DiagramBlockViewTests.cs:129`（改 `Resources[MarkdownResourceKeys.LinkForeground]` 后必须重渲染，不吃缓存）。
- To add 一套 XAML 主题字典：复制 `Themes/StreamMarkTheme.axaml` 的结构（`Styles` + `Styles.Resources` + `ThemeDictionaries`），代码侧同样用 `x:Class` + `AvaloniaXamlLoader.Load`（`StreamMarkTheme.axaml.cs:11`，保持 NativeAOT 安全），宿主按 `Styles.Add(new StreamMarkTheme()); Styles.Add(new MyTheme());` 的顺序加入（`README.md:27–28`）——**必须后加**，否则被默认值盖住（Avalonia 资源按后加入者优先）。
- To add 一套代码主题（AtomUI 那种）：新建项目引用宿主 token 包 + `StreamMark.Avalonia`（`src/StreamMark.Theme.AtomUI/StreamMark.Theme.AtomUI.csproj:7–11`），类继承 `Styles`，在构造函数里往 `Resources[key]` 写 `new MarkdownResourceAlias(宿主键)`（`AtomUIMarkdownTheme.cs:34`）。不要写 `DynamicResource` 画刷（见下条陷阱）。

## Invariants and traps
- AtomUI 主题用**资源别名而不是画刷**：资源字典里画刷上的动态资源从字典所在位置向上查找，找不到 AtomUI 注册在窗口 / 主题作用域内的 token，文字会变成透明；别名由 StreamMark 从 Markdown 控件本身解析 — `docs/decisions.md:27`，实现见 `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:81`、`MarkdownResourceAlias.cs:4`（注释），背景见 `.agents/memory/atomui-resource-alias.md`。
- `MarkdownStyle.Resolve` 只读一次并下发：`Resolve` 每次返回新实例，消费方靠 `RenderContext` 的**实例身份**判断“样式变了”，原地改快照不会触发任何重排 — `src/StreamMark.Avalonia/Rendering/RenderContext.cs:25`、`src/StreamMark.Avalonia/Rendering/BlockView.cs:27`、`src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:11`（注释）。
- 主题变体 / 资源变化后必须重新解析：`ResourcesChanged`、`ActualThemeVariantChanged`、`Services`、`ImageBaseUri`、进入可视树，都会置空缓存并重建 — `src/StreamMark.Avalonia/MarkdownView.cs:66`、`:67`、`:44`、`:45`、`:189`、`src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:219`、`:220`、`:225`、`:1016`。反过来说，直接改宿主资源而宿主控件不在同一资源作用域内不会自动生效。
- 未设置 `Md.FontFamily` / `Md.FontSize` 时沿用控件继承的 `TextElement` 字体，行高为字号 1.6 倍 — `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:102`、`:103`、`:125`；默认字典也刻意不设这三个键（`src/StreamMark.Avalonia/Themes/StreamMarkTheme.axaml:4–5`）。注意 `LineHeight` 依赖解析后的字号，宿主改窗口字号会连带改行高。
- 别名链上限是 4 层：第 5 层仍为别名时循环退出，值不是 `T`，直接 `return fallback`（**不报错**）— `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:81`、`:96`。
- 解析失败一律静默回退：`TryFindResource` 未命中返回 `fallback`（:76–78），别名目标未命中同样返回 `fallback`（:83–86）。因此键名写错只会表现为“颜色不对”，不会有异常。
- `InlineCodeForeground` / `ListMarkerForeground` 的默认值是 `Foreground` / `SecondaryForeground` 的**同一实例**，覆盖后者会连带改变前者（这也是 `StyleKeysTests` 断言的 `Assert.Same`）— `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:108–109`、`tests/StreamMark.Avalonia.Tests/Rendering/StyleKeysTests.cs:21`。
- 两个查找键的默认值不受 `StreamMarkTheme` 控制：`MarkdownStyle.cs:111–112` 在解析时按 `isDark` 现算（亮色偏黄 `#66FFD24D` / 当前项偏橙 `#CCFF9632`，暗色对应琥珀 / 橙），而 `StreamMarkTheme.axaml` 与 `AtomUIMarkdownTheme` 都没有这两个键。所以 AtomUI 宿主下的查找高亮**不会**跟随设计 token（这是新增键时漏掉第 4、5 步的活样本）；宿主必须自己在窗口 / 主题作用域写这两个键才能换色。
- 新增会显示文字的视图要自己画查找高亮：三处视图各画一遍（`TextSegmentView.cs:577`、`CodeBlockView.cs:651`、`DiffBlockView.cs:823`），漏掉则查找结果在该视图上不可见 — 细节见 `avalonia-text-rendering`。
- `StreamMarkTheme` 必须保持 `x:Class` 编译加载（NativeAOT：不用运行时 `StyleInclude`）— `src/StreamMark.Avalonia/Themes/StreamMarkTheme.axaml.cs:6`。
- `AtomUIMarkdownTheme` 只映射 13 个键，其余 9 个（`MarkBackground` / `SearchHighlightBackground` / `SearchCurrentBackground` / `CornerRadius` / `BlockSpacing` / `MonospaceFontFamily` 等）仍取 `StreamMarkTheme` 默认值或 `Resolve` 硬编码值，AtomUI 宿主下这些不会跟随设计 token — `src/StreamMark.Theme.AtomUI/AtomUIMarkdownTheme.cs:19–31`。
- `AlertBrush` / `HeadingFontSize` 不受资源键控制（提示块颜色、标题倍数都是硬编码），要换肤只能改 `MarkdownStyle` 本身 — `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs:50`、`:60`。
- 无头测试宿主只加载 `StreamMarkTheme`（`tests/StreamMark.Avalonia.Tests/HeadlessApp.cs:20`），所以主题回归测试跑的是默认字典，不是 AtomUI 别名路径。

## Tests
- 位置与命名：xUnit v3 + `[Fact]`，测试在 `tests/StreamMark.Avalonia.Tests/`。本模块直接相关的是根下的 `ThemeSnapshotTests.cs`（类名 = 被验证主题场景）与 `Rendering/StyleKeysTests.cs`（类名 = 被测资源键族，方法名描述断言），两者都经 `HeadlessApp.Run` / `HeadlessApp.Session.Dispatch` 在界面线程执行（`tests/StreamMark.Avalonia.Tests/HeadlessApp.cs:55`、`:34`）。
- `ThemeSnapshotTests.DefaultTheme_ResolvesResources_AndRenders`（`tests/StreamMark.Avalonia.Tests/ThemeSnapshotTests.cs:12`）：断言 `window.TryFindResource(Md.Foreground, window.ActualThemeVariant)` 命中且是 `IBrush`（:20–21）、`Md.BlockSpacing` == 12（:22–23）。
- `StyleKeysTests.InlineCodeAndListMarkerColors_FallBack_AndCanBeOverridden`（`tests/StreamMark.Avalonia.Tests/Rendering/StyleKeysTests.cs:12`）：先在裸 `MarkdownView` 上断言两条 `Assert.Same` 回退（:21–22），再往 `view.Resources[...]` 写覆盖值并用 `MarkdownStyle.Resolve(view)` 验证取到覆盖值（:27–36）。
- 与主题有关的相邻断言（不在本模块命名下，但依赖 `Md.*` 解析）：`DiagramBlockViewTests.SemiTransparentThemeColors_AreFlattenedOntoTheRealBackdrop`（`tests/StreamMark.Avalonia.Tests/Rendering/DiagramBlockViewTests.cs:96`，往窗口资源写半透明的 `Foreground` / `CodeBackground` 并断言叠到真实底色后不透明）与 `ThemeColorChange_RendersAgain_NotServedFromTheCache`（`:129`，改 `LinkForeground` 后必须重渲染）；`InteractionTests.Search_FindsAcrossEntries_HighlightsAndScrolls`（`tests/StreamMark.Avalonia.Tests/Rendering/InteractionTests.cs:96`）只断言 `Selection.GetHighlights` 的 `Current` 标志，**不断言**用的是哪个画刷。
- 只跑一个文件：仓库口径是 `dotnet test StreamMark.slnx`（`README.md:139`）。单文件可用 `dotnet test tests/StreamMark.Avalonia.Tests/StreamMark.Avalonia.Tests.csproj --filter "FullyQualifiedName~StyleKeysTests"`——仓库脚本与 CI 里没有这种写法（unverified，本次未运行任何测试或构建）。
- 没有被测覆盖的部分：没有任何测试加载 `AtomUIMarkdownTheme`（全仓库引用只有 `README.md:28`、`docs/architecture.md:84` 与该文件自身）；别名链、`Color→IBrush` 转换、4 层上限都没有测试；两个查找键既没有字典条目也没有渲染断言（`SearchHighlightBackground` / `SearchCurrentBackground` 的引用只出现在 `MarkdownResourceKeys.cs:28`、`:30`、`MarkdownStyle.cs:28`、`:30`、`:111`、`:112`、三处 `Render` 与 `README.md:101`）。

## Related flows
- [theme-resolution](../flows/theme-resolution.md)
- [streaming-token-to-view](../flows/streaming-token-to-view.md)
