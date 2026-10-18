# 代码与差异

代码块由 TextMate 语法增量高亮，右上角可以复制或下载。

## 多种语言

```csharp
public sealed record Message(string Role, string Content);

public static async IAsyncEnumerable<string> StreamAsync(HttpClient http, [EnumeratorCancellation] CancellationToken ct = default)
{
    using var response = await http.GetAsync("/chat", HttpCompletionOption.ResponseHeadersRead, ct);
    await foreach (var line in ReadLinesAsync(response, ct))
    {
        yield return line;
    }
}
```

```typescript
interface Snapshot { blocks: Block[]; complete: boolean }

export function diff(prev: Snapshot | null, next: Snapshot): Op[] {
  return next.blocks.map((b, i) => ({ kind: prev?.blocks[i] === b ? "keep" : "update", key: b.key }));
}
```

```python
from dataclasses import dataclass

@dataclass(frozen=True)
class Block:
    key: int
    text: str

def frozen_prefix(blocks: list[Block], safe: int) -> list[Block]:
    return [b for b in blocks if b.key < safe]
```

```rust
#[no_mangle]
pub extern "C" fn ratex_layout(src: *const c_char, display: bool) -> *mut DisplayList {
    let source = unsafe { CStr::from_ptr(src) }.to_string_lossy();
    Box::into_raw(Box::new(layout(&source, display)))
}
```

```json
{ "name": "StreamMark", "aot": true, "rids": ["win-x64", "linux-x64", "osx-arm64"] }
```

```bash
dotnet publish samples/StreamMark.Gallery -c Release -r osx-arm64
./StreamMark.Gallery --smoke
```

## 行号与起始行

围栏信息里写路径和起始行（`#L10`）时，行号从 10 开始：

```csharp src/Example.cs#L10
var document = new StreamingDocument();
document.SnapshotReady += _ => RequestFrame();
document.Append(token);
document.Complete();
```

## 差异（diff）

默认是 GitHub 风格；点击代码块标题栏的按钮切换为并排视图。

```diff
diff --git a/src/Pacer.cs b/src/Pacer.cs
--- a/src/Pacer.cs
+++ b/src/Pacer.cs
@@ -12,7 +12,8 @@ public sealed class StreamPacer
     public string Tick(TimeSpan now)
     {
-        var budget = (int)(now - _last).TotalMilliseconds / 16;
+        var elapsed = now - _last;
+        var budget = Math.Max(1, (int)(elapsed.TotalMilliseconds * _rate / 1000));
         _last = now;
         return Take(budget);
     }
```

## 长代码折叠

超过 40 行的代码块默认折叠（流式输出的代码块不折叠）：

```python
# 1
# 2
# 3
# 4
# 5
# 6
# 7
# 8
# 9
# 10
# 11
# 12
# 13
# 14
# 15
# 16
# 17
# 18
# 19
# 20
# 21
# 22
# 23
# 24
# 25
# 26
# 27
# 28
# 29
# 30
# 31
# 32
# 33
# 34
# 35
# 36
# 37
# 38
# 39
# 40
# 41
# 42
# 43
# 44
# 45
print("done")
```
