# StreamMark

> **StreamMark has moved into [TKCode.Arc](https://github.com/AIDotNet/TKCode.Arc).**
> This repository is no longer developed. The code, its tests, the native RaTeX build and the docs now live in
> TKCode.Arc (`src/TKCode.Arc.StreamMark*`, `docs/streammark/`), and the packages ship with every Arc release.
> Open issues and pull requests there.
>
> **StreamMark 已并入 [TKCode.Arc](https://github.com/AIDotNet/TKCode.Arc)，本仓库不再维护。** 源码、测试、RaTeX
> 原生库构建与文档都在 TKCode.Arc（`src/TKCode.Arc.StreamMark*`、`docs/streammark/`），包随 Arc 每次发布一起发布。
> 问题与合并请求请提交到 TKCode.Arc。

High-performance streaming Markdown rendering for [Avalonia](https://avaloniaui.net/), built for AI chat clients:
stable blocks are frozen and parsed once, unfinished syntax is healed while streaming, everything is self-drawn
(no WebView), with LaTeX (RaTeX), Mermaid and TextMate highlighting, and it is NativeAOT-safe.

## Which package to use

### In an Arc app: `TKCode.Arc.Markdown`

Most apps want the Arc controls built on StreamMark: `Markdown` (a document, static or streamed) and `ChatThread` /
`ChatComposer`. They follow Arc's theme (light/dark, accent, fonts) with no extra setup.

```bash
dotnet add package TKCode.Arc.Markdown
dotnet add package TKCode.Arc.Highlighting   # optional: colored fenced code
```

```csharp
using TKCode.Arc;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
              .UsePlatformDetect()
              .UseArc(arc => arc
                  .UseArcControls()
                  .UseArcMarkdown()       // Markdown, ChatThread, ChatComposer
                  .UseArcHighlighting()); // optional
```

```xml
<arc:Markdown Text="# Hello **StreamMark**" />
```

Stream a reply with `markdown.Append(token)` and `markdown.Complete()`.

### StreamMark on its own: `TKCode.Arc.StreamMark`

For a host that does not use Arc's controls, StreamMark is published as standalone packages that depend on Avalonia
(and Markdig), not on Arc: `MarkdownView`, `MarkdownStream`, the virtualizing `TranscriptView` and `MarkdownServices`,
exactly as before.

```bash
dotnet add package TKCode.Arc.StreamMark
dotnet add package TKCode.Arc.StreamMark.TextMate   # optional: syntax highlighting
dotnet add package TKCode.Arc.StreamMark.RaTeX      # optional: LaTeX math (native library for win/linux/osx x64/arm64)
dotnet add package TKCode.Arc.StreamMark.Mermaid    # optional: Mermaid diagrams
```

Usage and theming: [docs/streammark/README.md](https://github.com/AIDotNet/TKCode.Arc/blob/main/docs/streammark/README.md)
in TKCode.Arc.

## Migrating from `TKCode.StreamMark.*`

The namespaces did not change (`using StreamMark.Avalonia;` still works); only the package names and assembly names did.
The old and the new packages contain the same types, so remove every old reference.

| Old package (this repository, last version 0.1.2) | New package (TKCode.Arc) |
|---|---|
| `TKCode.StreamMark.Core` + `TKCode.StreamMark.Avalonia` | `TKCode.Arc.StreamMark` (one package), or `TKCode.Arc.Markdown` for Arc's `Markdown` control |
| `TKCode.StreamMark.Highlighting.TextMate` | `TKCode.Arc.StreamMark.TextMate` (or `TKCode.Arc.Highlighting` in an Arc app) |
| `TKCode.StreamMark.RaTeX` | `TKCode.Arc.StreamMark.RaTeX` |
| `TKCode.StreamMark.Mermaid` | `TKCode.Arc.StreamMark.Mermaid` |
| `TKCode.StreamMark.Theme.AtomUI` | none (see below) |

- XAML that names the assembly: `clr-namespace:StreamMark.Avalonia;assembly=StreamMark.Avalonia` becomes
  `clr-namespace:StreamMark.Avalonia;assembly=TKCode.Arc.StreamMark`.
- The AtomUI theme package has no successor. A host maps the `Md.*` resources onto its own tokens with
  `MarkdownResourceAlias` (after `StreamMarkTheme`); in an Arc app, `TKCode.Arc.Markdown` does this for Arc's tokens.
- The new packages version with TKCode.Arc (`ArcVersion`), starting with 1.3.0-preview.1.

The code that moved is this repository's last release, 0.1.2. Its history stays here; later changes are in
TKCode.Arc's [CHANGELOG.md](https://github.com/AIDotNet/TKCode.Arc/blob/main/CHANGELOG.md).

## License

MIT. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
