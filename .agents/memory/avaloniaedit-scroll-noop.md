---
name: avaloniaedit-scroll-noop
description: AvaloniaEdit 12.0.0 的 TextEditor.ScrollToVerticalOffset 是空实现，滚动要用 ScrollTo(line, 0, VisualYPosition.LineTop, 偏移, 0)
type: project
updated: 2026-10-19
source: session
evidence: [https://github.com/AvaloniaUI/AvaloniaEdit/blob/86fdebec4cff7affb0e14c7885df71d28edce777/src/AvaloniaEdit/TextEditor.cs#L836-L841, samples/StreamMark.Gallery/Editor/EditorPage.cs:JumpTo, samples/StreamMark.Gallery/SmokeTest.cs:ui.editor.sync]
---
AvaloniaEdit 12.0.0（Gallery 的源码编辑器）里 `TextEditor.ScrollToVerticalOffset(double)` 只调用了 `ApplyTemplate()`，真正滚动的代码被注释掉了；调用不报错，但编辑器不动。`ScrollToHome` / `ScrollToEnd` 正常。要把某行放到视口顶部，用 `ScrollTo(line, 0, VisualYPosition.LineTop, referencedVerticalViewPortOffset, minimumScrollFraction: 0)`。

**Why:** 2026-10-19 写 Gallery 的滚动同步冒烟检查时，编辑器偏移一直是 0，排查到上游源码才发现。
**How to apply:** Gallery 里需要按像素或按行滚动编辑器时都用 `ScrollTo`；升级 AvaloniaEdit 后可以检查上游是否已恢复实现。相关：[[editor-preview-document-path]]。
