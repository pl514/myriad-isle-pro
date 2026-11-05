# 交接包：Capsule image Pro —— AI 后端（第二阶段：接真模型）

> 写于 2026-11-05。上一阶段（前端）见 `docs/AI前端-交接包.md`，那份的第 13 节是复核结论。
>
> **一句话**：把 `FakeBackend` 换成真后端，**前端一行不改**。先用免费的本机 Ollama 跑通整条链路，
> DeepSeek 只是同一份实现的另一种配置。

---

## 0. 核心结论：不要写两个后端

Ollama 和 DeepSeek **用的是同一套协议**——OpenAI 兼容：

| 目标 | baseUrl | model | 要 key |
|---|---|---|---|
| 本机 Ollama | `http://127.0.0.1:11434/v1` | `qwen2.5:7b`（用户装的） | 否 |
| DeepSeek | `https://api.deepseek.com` | `deepseek-flash` | 是 |
| 智谱（备选） | `https://open.bigmodel.cn/api/paas/v4` | `glm-4.6v-flashx` | 是 |

所以只写 **一个 `OpenAiCompatBackend`**，差别全在配置里。

> ⚠️ 别为 Ollama 写专门的实现。它的 `/v1/chat/completions` 就是标准格式，写两套只会让错误处理分叉。

---

## 1. 接口（已经定稿，不要改）

```csharp
internal interface IChatBackend
{
    Task SendAsync(string text, Action<string> onDelta, CancellationToken token);
}
```

三条约定（前端已经依赖它们）：

1. `onDelta` 收到的是**增量片段**，前端自己 `msg.Text += delta`；
2. 正常结束或被取消时**正常返回**，不抛异常；
3. 出错**直接抛异常**，前端统一 catch → 错误气泡 + 重试按钮。

**不要加 `Cancel()`**（和 token 重复），**不要加 `onError`**（异常即错误）。

---

## 2. 配置文件：`build\data\models.txt`

**不要引入 Newtonsoft / 任何 JSON 库**（csc 直编，没有 NuGet）。用简单行格式，字段用 `|` 分隔：

```
# id|label|baseUrl|model|keyRef|vision
ollama-qwen|Qwen2.5 7B（本机）|http://127.0.0.1:11434/v1|qwen2.5:7b||false
deepseek|DeepSeek Chat|https://api.deepseek.com|deepseek-flash|deepseek|true
```

- `keyRef` 为空 = 不需要 key（本机）；
- `vision` 为后续图片问答预留，先不管；
- 文件不存在时**自动写入两条默认**（上面这两条），用户改这一份就行；
- 读取时按 `|` split，行首 `#` 跳过，容错到"坏一行不影响其它行"。

### Key 的保存

**不要明文**。用 DPAPI：

```csharp
byte[] cipher = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
```

- 需要 `/r:System.Security.dll`（`Build.ps1` 的 `$refs` 里补一行）；
- 存在 `build\data\keys.txt`，格式 `keyRef|base64(cipher)`；
- 拷到别的机器解不开——这是特性，不是 bug；
- DPAPI 抛异常时退回明文并在设置界面显式警告。

---

## 3. 会话落盘：`build\data\chats.txt`

行格式，一行一条消息：

```
# sessionId|title|role|base64(utf8 text)|timestamp
s1|新对话|user|5L2g5aW9|1762311111
s1|新对话|ai|5oiR5piv5Yqg5Ymn|1762311115
```

- `title` 只用第一行那条的标题（首句前 12 字）；
- 文本用 **base64**，避免换行/竖线破坏格式；
- 上限：最多保留 50 个会话、每会话 200 条，超了从旧删；
- 写入时机：流式**结束后**整条追加（不要在流式过程中反复重写整个文件）。

---

## 4. 实现要点（.NET Framework 4.8 + C# 5 的限制）

### 4.1 流式请求

```csharp
var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
request.Content = new StringContent(json, Encoding.UTF8, "application/json");

using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
{
    response.EnsureSuccessStatusCode();
    using (var stream = await response.Content.ReadAsStreamAsync())
    using (var reader = new StreamReader(stream, Encoding.UTF8))
    {
        string line;
        bool sawAny = false;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            token.ThrowIfCancellationRequested();
            if (line.Length == 0) continue;
            if (!line.StartsWith("data:")) continue;
            string payload = line.Substring(5).Trim();
            if (payload == "[DONE]") break;
            string delta = ExtractDelta(payload);   // 见 4.3
            if (string.IsNullOrEmpty(delta)) continue;
            sawAny = true;
            onDelta(delta);
        }
        if (!sawAny) throw new InvalidOperationException("服务返回了空内容，检查模型名是否正确。");
    }
}
```

**必须用 `ResponseHeadersRead`**，否则 `SendAsync` 会等整个响应体，流式就变成"一次性"了。

### 4.2 C# 5 红线（这个编译器不支持新语法）

| 不能用 | 替代 |
|---|---|
| `$"..."` 字符串插值 | `"..." + x + "..."` |
| `?.` / `??=` | 显式判空 |
| `nameof(x)` | 写字面量 |
| 表达式体成员 `=> x` | `{ return x; }` |
| `using var` | `using (...) { }` |
| `await foreach` / `IAsyncEnumerable` | 上面的 `StreamReader` 循环 |
| 元组 / 解构 | 用多个变量或小类 |

`async/await`、`Task.Delay`、lambda、`delegate {}` 都可以。

### 4.3 解析一段 SSE 数据

自己拼字符串解析，不要引 JSON 库。回答文本在 `choices[0].delta.content`：

```csharp
static string ExtractDelta(string json)
{
    int choice = json.IndexOf("\"delta\"");
    if (choice < 0) return null;
    int content = json.IndexOf("\"content\"", choice);
    if (content < 0) return null;
    int colon = json.IndexOf(':', content);
    if (colon < 0) return null;
    int quote = json.IndexOf('"', colon + 1);
    if (quote < 0) return null;
    var sb = new StringBuilder();
    for (int i = quote + 1; i < json.Length; i++)
    {
        char c = json[i];
        if (c == '\\' && i + 1 < json.Length)
        {
            char n = json[++i];
            if (n == 'n') sb.Append('\n');
            else if (n == 't') sb.Append('\t');
            else if (n == 'r') sb.Append('\r');
            else if (n == '"') sb.Append('"');
            else if (n == '\\') sb.Append('\\');
            else if (n == 'u' && i + 4 < json.Length)
            {
                int code;
                if (int.TryParse(json.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                {
                    sb.Append((char)code);
                    i += 4;
                }
            }
            continue;
        }
        if (c == '"') break;
        sb.Append(c);
    }
    return sb.ToString();
}
```

> 这是**够用的简陋解析**，不是通用 JSON 解析器。它只处理 `delta.content` 这一种情况，
> 遇到 `reasoning_content`（思维链）、工具调用会自然跳过——正好是我们要的行为。

### 4.4 取消

用户点「停止」→ 前端 `CancellationTokenSource.Cancel()` → `SendAsync` 里抛 `OperationCanceledException` → 前端已有 `catch (OperationCanceledException)`，**已输出的文字保留**。

要注意：`ReadLineAsync()` **不接收 token**。所以循环里手写 `token.ThrowIfCancellationRequested()`（上面 4.1 已经有），取消延迟最多一行数据的间隔，可接受。

### 4.5 超时

```csharp
client.Timeout = Timeout.InfiniteTimeSpan;   // 整体超时自己控制
```

不要用 `HttpClient.Timeout` 管流式（它会把长回答截断）。用三层：

| 层 | 值 | 实现 |
|---|---|---|
| 连接 | 10s | `CancellationTokenSource` + `Task.WhenAny` |
| 首字 | 25s | 循环里记 `Stopwatch`，第一次 `onDelta` 前超时就抛 |
| 总时长 | 180s | 同上 |

---

## 5. 错误映射（前端已有错误气泡，这里只负责"错误信息要说人话"）

| 情况 | 判定 | 抛出的消息 |
|---|---|---|
| 没配 key | `keyRef != null` 但 keys.txt 里没有 | "这个模型需要 API Key，去设置里填一下" |
| key 错 | 401 | "API Key 无效" |
| 余额/额度 | 402 或响应含 `balance` / `quota` | "余额不足，换本机模型试试？" |
| 限流 | 429 | "请求太频繁，等两秒再试" |
| 模型不存在 | 404 或含 `model not found` | "服务端没有这个模型：{model}" |
| 连不上本机 | `HttpRequestException` 且 baseUrl 是 127.0.0.1 | "Ollama 没启动，先运行 `ollama serve`" |
| 断网 | `HttpRequestException` 其它 | "网络不通" |
| 超时 | 上面三层 | "等太久了，已取消" |

**中文文案统一在 backend 里拼**，前端只显示 `exception.Message`。

---

## 6. 与前端对接的三处

| 位置 | 现在 | 改成 |
|---|---|---|
| `IslandWindow.Ai.cs` 里 `RunAiStream` 用的 `FakeBackend` | `new FakeBackend()` | `ChatBackends.Resolve(currentModelId)` |
| 底栏 chip 显示的模型名 | 写死文案 | 当前配置的 `label` |
| 「加入模型」表单保存 | 只提示 | 写进 `models.txt`（key 走 DPAPI） |

> 前端拿到的是 `IChatBackend`，所以这三处都是"换数据源"，不涉及流式渲染逻辑。

---

## 7. 里程碑（每一步都能独立验收）

| # | 内容 | 验收 |
|---|---|---|
| M1 | `models.txt` + `keys.txt` 读写 + 默认两条 | 删掉文件重启，自动生成；改模型名后 chip 跟着变 |
| M2 | `OpenAiCompatBackend` + SSE 解析 | 连本机 Ollama，流式逐字出来 |
| M3 | 超时 + 取消 + 错误映射 | 停掉 Ollama → 提示"没启动"；中途点停止 → 立即停且保留已输出 |
| M4 | 模型切换 UI 接上配置 | 切到 DeepSeek（填 key）能跑 |
| M5 | 会话落盘 | 重启后左栏历史还在 |
| M6（可选） | `WebBackend` 网页版驱动 | 单独设计，别和上面混做 |

**先做 M1+M2**，两步之内就能看到真模型的回答；M3 再补健壮性。

---

## 8. 前端遗留的三个待测项（做后端时顺手回归）

1. 窗口变高后，面板外的透明区域点击是否穿透到桌面；
2. 抖音球 / 图片页签 / 文字页签是否正常（回归一次）；
3. v3 的五条 UI（球切换、会话栏、chip 弹层、加入模型、左栏布局）。

---

## 9. 已知风险

| 风险 | 说明 |
|---|---|
| Ollama 要用户自己装 | 几个 GB 的模型下载；没有显卡的机器只能跑小模型（1.7B 级别），质量会明显下降 |
| DeepSeek 要花钱 | 但极便宜：一次图文问答约 0.4 分钱，一个月几块钱 |
| 本机 8B 模型 vs 云端的差距 | 日常问答够用，复杂推理差一截——所以配置里要保留云端选项 |
| `ExtractDelta` 的简陋解析 | 厂商改字段名就会失效；换了服务商要重新验证 |
| 会话文件会变大 | 已定"50 会话 × 200 条"上限，别再放宽 |

---

## 10. 给下一个对话的开场

> 读 `docs/AI前端-交接包.md`（尤其 §13 复核结论）和本文件。
> 从 M1 开始：先把 `models.txt` / `keys.txt` 的读写做出来，接口 `IChatBackend` **不要动**。
> 编译用 `Build.ps1`（csc + C# 5，无 NuGet）；**别动抖音的三个文件**（`DouyinPlayerWindow.cs` / `IslandWindow.Douyin.cs` / `MorphShellWindow.cs`）。
> 每完成一个里程碑就 `git commit`，别用 `git add -A` 把别的会话的改动一起卷进来。
