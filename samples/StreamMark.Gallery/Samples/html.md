# HTML 与组件

## 白名单组件

快捷键 <kbd>⌘</kbd>+<kbd>K</kbd>，化学式 H<sub>2</sub>O，平方 x<sup>2</sup>，<mark>高亮文字</mark>，<u>下划线</u>，引用来源<Citation id="1" />，以及 <cite href="https://commonmark.org">CommonMark 规范</cite>。

<details>
<summary>点击展开折叠块</summary>

折叠块里可以写 **Markdown**：

- 列表
- `代码`
- 公式 $a^2 + b^2 = c^2$
</details>

<think>
这是模型的「思考过程」组件：通常显示为可折叠的灰色区域。
</think>

## README 风格的 HTML

<div align="center">
  <h3>居中的 HTML 标题</h3>
  <p>支持 <strong>常用标签</strong>、<span style="color: #1677ff">颜色</span>、<span style="font-size: 18px">字号</span> 与 <a href="https://github.com/AtomUICommunity/StreamMark">链接</a>。</p>
</div>

<div style="margin-top: 8px; padding: 10px 14px; border: 1px solid #8884; border-radius: 8px">

盒子里也可以写 **Markdown**（与 HTML 之间用空行隔开）。

</div>

<table>
  <thead>
    <tr><th>HTML 表格</th><th align="right">数值</th></tr>
  </thead>
  <tbody>
    <tr><td>第一行</td><td align="right">1</td></tr>
    <tr><td bgcolor="#52c41a33">带背景色</td><td align="right">2</td></tr>
  </tbody>
</table>

<dl>
  <dt>术语</dt>
  <dd>定义列表的解释文字。</dd>
</dl>

## 不会执行的内容

未知元素（含 `script` / `style` / `iframe`）按源码显示，永远不会执行：

<script>alert("不会执行")</script>

HTML 注释会被隐藏：<!-- 看不到我 -->到此为止。
