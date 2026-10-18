# 欢迎使用 StreamMark Gallery

这是一个用 **Avalonia + StreamMark** 写成的原生 Markdown 编辑器：左侧编辑源码，右侧实时预览，全部自绘、**NativeAOT** 发布、不依赖 WebView。

## 快速上手

1. 直接在左侧输入，右侧会随输入增量刷新。
2. 工具栏或快捷键插入格式，选中文字后再按会包裹选区。
3. 左侧「大纲」点击标题，编辑器与预览一起跳转。
4. 「示例」菜单里有每类语法的演示文档。
5. 点「流式回放」，把当前文档当成 AI 回答重新流式输出一遍。

## 常用快捷键

| 操作 | macOS | Windows / Linux |
|:-----|:-----:|:---------------:|
| 粗体 / 斜体 | <kbd>⌘</kbd>+<kbd>B</kbd> / <kbd>⌘</kbd>+<kbd>I</kbd> | <kbd>Ctrl</kbd>+<kbd>B</kbd> / <kbd>Ctrl</kbd>+<kbd>I</kbd> |
| 链接 | <kbd>⌘</kbd>+<kbd>K</kbd> | <kbd>Ctrl</kbd>+<kbd>K</kbd> |
| 行内代码 | <kbd>⌘</kbd>+<kbd>E</kbd> | <kbd>Ctrl</kbd>+<kbd>E</kbd> |
| 标题 1–6 / 正文 | <kbd>⌘</kbd>+<kbd>1</kbd>…<kbd>6</kbd> / <kbd>⌘</kbd>+<kbd>0</kbd> | <kbd>Ctrl</kbd>+<kbd>1</kbd>…<kbd>6</kbd> / <kbd>Ctrl</kbd>+<kbd>0</kbd> |
| 删除线 / 代码块 | <kbd>⌘</kbd>+<kbd>Shift</kbd>+<kbd>X</kbd> / <kbd>K</kbd> | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>X</kbd> / <kbd>K</kbd> |
| 编辑器内查找替换 | <kbd>⌘</kbd>+<kbd>F</kbd> | <kbd>Ctrl</kbd>+<kbd>F</kbd> |
| 预览内查找 | <kbd>⌘</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd> | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd> |
| 放大 / 缩小字号 | <kbd>⌘</kbd>+<kbd>=</kbd> / <kbd>-</kbd> | <kbd>Ctrl</kbd>+<kbd>=</kbd> / <kbd>-</kbd> |
| 切换视图 | <kbd>⌘</kbd>+<kbd>\\</kbd> | <kbd>Ctrl</kbd>+<kbd>\\</kbd> |
| 新建 / 打开 / 保存 | <kbd>⌘</kbd>+<kbd>N</kbd> / <kbd>O</kbd> / <kbd>S</kbd> | <kbd>Ctrl</kbd>+<kbd>N</kbd> / <kbd>O</kbd> / <kbd>S</kbd> |

列表里按回车会自动续写下一项，空项再按回车退出列表；<kbd>Tab</kbd> / <kbd>Shift</kbd>+<kbd>Tab</kbd> 调整缩进。

## 预览里能做什么

- [x] 公式 $e^{i\pi} + 1 = 0$、Mermaid 图表、代码高亮与 diff
- [x] 任意跨块选中，⌘C 复制纯文本，右键「复制为 Markdown」
- [x] 链接在浏览器打开，文内锚点（例如 [跳到末尾](#关于)）直接跳转
- [x] 表格与图表可以全屏查看

> [!TIP]
> 把 `.md` 文件拖进窗口即可打开；文件里的相对路径图片按文件所在目录解析。

## 关于

StreamMark 是面向 AI 对话场景的流式 Markdown 渲染库。这个 Gallery 同时是它的 NativeAOT 冒烟测试。
