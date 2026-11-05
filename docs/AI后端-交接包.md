# 交接包：Capsule image Pro —— AI 后端（第二阶段：接真模型）

> 写于 2026-11-05。**v2（同日复核）**：订正 5 处会卡住/会出怪字的地方（见 §11；其中 R3 已作废）。
> **v3（同日，按用户决策）**：**不做本地模型**，后端只走 API。默认智谱免费档，DeepSeek 作质量选项；
> 本机没有 ollama / 没有 .gguf，也不需要它来验收。`tools\mock-sse.ps1` 降级成"还没拿到 key 时的兜底"。
> 上一阶段（前端）见 `docs/AI前端-交接包.md`，那份的 §13 是复核结论。
>
> **一句话**：把 `FakeBackend` 换成真后端，**前端一行不改**。只写一个 `OpenAiCompatBackend`，
> 换服务商 = 改 `models.txt` 一行。

---

## 0. 核心结论：不要写两个后端

主流服务商**用的是同一套协议**——OpenAI 兼容：

| 目标 | baseUrl | model | 要 key | 定位 |
|---|---|---|---|---|
| 智谱 | `https://open.bigmodel.cn/api/paas/v4` | `glm-4.7-flash`（以控制台实际 id 为准） | 是（需实名） | **默认**（免费档） |
| DeepSeek | `https://api.deepseek.com` | `deepseek-flash` | 是 | 备选（质量档，几块钱/月） |
| 本机 Ollama | `http://127.0.0.1:11434/v1` | 用户自装 | 否 | **不做**（见下） |

所以只写 **一个 `OpenAiCompatBackend`**，差别全在配置里。

> **本机模型不做了**（用户 2026-11-05 决策）：本机没有 ollama、没有 .gguf；8B 打不过云端几百亿参数，
> 还要吃 5GB 显存；唯一不可替代的价值是"离线/隐私"，暂时不需要。
> **代码里不要把 Ollama 当默认**，M1~M3 也不需要它来验收；本地方案只作为"以后可选"留在配置格式里。
>
> ⚠️ 别为任何一家写专门实现。它们的 `/v1/chat/completions` 都是标准格式，写两套只会让错误处理分叉。

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
zhipu|智谱 GLM-4.7-Flash（免费）|https://open.bigmodel.cn/api/paas/v4|glm-4.7-flash|zhipu|false
deepseek|DeepSeek Flash|https://api.deepseek.com|deepseek-flash|deepseek|true
```

- `keyRef` 为空 = 不需要 key（只有本地方案才这样）；
- `vision` 为后续图片问答预留，先不管（DeepSeek 的 `deepseek-flash` 支持图像理解，到时改这一列）；
- 文件不存在时**自动写入上面这两条默认**，用户改这一份就行；
- 想用本机模型（不做，仅留格式）就自己加一行：`ollama|x|http://127.0.0.1:11434/v1|你的模型||false`。
- 读取时按 `|` split，行首 `#` 跳过，容错到"坏一行不影响其它行"。

### Key 的保存

**不要明文**。用 DPAPI：

```csharp
byte[] cipher = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
```

- 需要 `/r:System.Security.dll`（`Build.ps1` 的 `$refs` 里补一行）；
- **还要 `/r:System.Net.Http.dll`**——`HttpClient` / `HttpRequestMessage` 在这个程序集里，不在 `System.dll`。
  不补的话 M2 第一行就 `CS0246: 找不到类型 HttpClient`。

`Build.ps1` 的 `$refs` 补两行即可（两个文件都在 `Framework64\v4.0.30319\` 下，已确认存在）：

```powershell
    "/r:$framework\System.Net.Http.dll",
    "/r:$framework\System.Security.dll",
```
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
    // 不要用 EnsureSuccessStatusCode()：.NET Framework 的 HttpRequestException 拿不到 StatusCode，
    // §5 那张错误映射表会全废。先自己判，再按数字映射。
    if (!response.IsSuccessStatusCode)
    {
        int status = (int)response.StatusCode;
        string body = "";
        try { body = await response.Content.ReadAsStringAsync(); } catch (Exception) { }
        throw BuildHttpError(status, body);   // 把 §5 那张表实现成一个方法
    }
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
    // 关键：不能直接找下一个引号。遇到 "content":null 时会一路找到 "finish_reason" 的引号，
    // 把那个字段名当成正文吐出来（表现为回答里突然冒出 finish_reason 字样）。
    int cursor = colon + 1;
    while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++;
    if (cursor >= json.Length || json[cursor] != '"') return null;
    int quote = cursor;
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

### 4.6 线程上下文：**后端里一处 `ConfigureAwait(false)` 都不能写**

前端是这样调用的：

```csharp
await aiBackend.SendAsync(prompt, delegate(string delta) { AppendDelta(msg, delta); }, token);
```

`SendAsync` 从 UI 线程发起，WPF 的 `SynchronizationContext` 会跟着 `await` 一路回到 UI 线程，
所以 `onDelta` 里可以直接 `message.Text += delta`、直接操作 `TextBlock`。

**一旦你在后端内部写了任何 `ConfigureAwait(false)`，续体就跑到线程池上**，`onDelta` 会在非 UI 线程
执行 → `InvalidOperationException: 调用线程无法访问此对象，因为另一个线程拥有该对象` → 直接崩。

- 后端内部**不要**写 `ConfigureAwait(false)`；
- 如果哪天真需要（比如为了性能），前端 `onDelta` 必须改成
  `Dispatcher.BeginInvoke(new Action(delegate { ... }))`——但那是两处改动，不划算。

---

## 5. 错误映射（前端已有错误气泡，这里只负责"错误信息要说人话"）

| 情况 | 判定 | 抛出的消息 |
|---|---|---|
| 没配 key | `keyRef != null` 但 keys.txt 里没有 | "这个模型需要 API Key，去设置里填一下" |
| key 错 | 401 | "API Key 无效" |
| 余额/额度 | 402 或响应含 `balance` / `quota` | "余额不足，换本机模型试试？" |
| 限流 | 429 | "请求太频繁，等两秒再试" |
| 模型不存在 | 404 或含 `model not found` | "服务端没有这个模型：{model}" |
| 连不上本机（可选） | `HttpRequestException` 且 baseUrl 是 127.0.0.1 | "本机模型服务没启动"（只有用户自己配了本地模型才会走到） |
| 断网 | `HttpRequestException` 其它 | "网络不通" |
| 超时 | 上面三层 | "等太久了，已取消" |

**中文文案统一在 backend 里拼**，前端只显示 `exception.Message`。

> 这张表能生效的**前提**是 §4.1 里没有用 `EnsureSuccessStatusCode()`——先拿到数字状态码再映射。
> 另外 `HttpRequestException` 在 .NET Framework 里没有 `StatusCode` 属性，别指望事后补。

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
| M2 | `OpenAiCompatBackend` + SSE 解析 | 用**真实 key**（智谱或 DeepSeek）流式逐字出来；没拿到 key 时用 `tools\mock-sse.ps1` 兜底 |
| M3 | 超时 + 取消 + 错误映射 | key 改错 → 提示"API Key 无效"；`mock -Mode 401` / `-Mode slow` 各跑一次；中途点停止 → 立即停且保留已输出 |
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
| 智谱免费档要实名 | 拿 key 需实名认证；免费档有并发/额度限制，超了会 429 |
| 免费模型随时可能变 | `glm-4.7-flash` 的免费策略和模型 id 以控制台为准，改了一行配置就能换 |
| 免费档与付费档质量差距 | 日常问答够用；复杂任务可以切 DeepSeek（`deepseek-flash`，几块钱/月） |
| DeepSeek 要花钱 | 但极便宜：一次图文问答约 0.4 分钱，一个月几块钱 |
| 本机 8B 模型 vs 云端的差距 | 日常问答够用，复杂推理差一截——所以配置里要保留云端选项 |
| `ExtractDelta` 的简陋解析 | 厂商改字段名就会失效；换了服务商要重新验证 |
| 会话文件会变大 | 已定"50 会话 × 200 条"上限，别再放宽 |

---

## 10. 给下一个对话的开场

> 读 `docs/AI前端-交接包.md`（尤其 §13 复核结论）和本文件，**再看一眼 §11 的订正（R3 已作废）**。
> **v3 决策：不做本地模型，只走 API**——默认智谱 `glm-4.7-flash`（免费），DeepSeek `deepseek-flash` 作备选。
> M1 里记得给 `keys.txt` 加 `plain:` 前缀支持（用户要能手改，见 §12）。
> 从 M1 开始：先把 `models.txt` / `keys.txt` 的读写做出来，接口 `IChatBackend` **不要动**。
> 编译用 `Build.ps1`（csc + C# 5，无 NuGet）；**别动抖音的三个文件**（`DouyinPlayerWindow.cs` / `IslandWindow.Douyin.cs` / `MorphShellWindow.cs`）。
> 每完成一个里程碑就 `git commit`，别用 `git add -A` 把别的会话的改动一起卷进来。

---

## 11. 复核订正 v2（对着源码和本机环境实测出来的）

### 11.1 五条订正（**R3 已作废**）

| # | 原文档写的 | 实际会怎样 | 已改在 |
|---|---|---|---|
| R1 | 只补 `/r:System.Security.dll` | `HttpClient` 在 `System.Net.Http.dll` 里，不补引用 **M2 第一行就 CS0246** | §2「Key 的保存」下方 |
| R2 | 流式请求里 `response.EnsureSuccessStatusCode()` | .NET Framework 的 `HttpRequestException` **没有** `StatusCode` 属性，先 Ensure 就等于把 401/402/429 全糊成一句话，§5 那张表全废 | §4.1 代码块 |
| ~~R3~~ | ~~`deepseek-flash` 不是官方模型名~~ | **这条是我错了，作废**。2026-11-05 官方文档原文：「模型名请使用 `deepseek-flash`」；在售只有 `deepseek-flash` 和 `deepseek-v4-pro`，`deepseek-chat` / `deepseek-reasoner` 是 V3/R1 时代旧名，已下线。**配置保持 `deepseek-flash`** | 无（§0 / §2 保持原样） |
| R4 | `int quote = json.IndexOf('"', colon + 1);` | 遇到 `"content":null` 会一路找到 `"finish_reason"` 的引号，把字段名当正文吐出来（回答里突然冒出 `finish_reason`） | §4.3 `ExtractDelta` |
| R5 | 没提线程上下文 | 后端里任何一处 `ConfigureAwait(false)` 都会让 `onDelta` 跑到线程池 → 前端跨线程改 `TextBlock` 直接崩 | 新增 §4.6 |

> 教训：涉及具体服务商的**模型名、计费、限流**，一律以官方文档为准，别凭"我记得"下结论。
> 我这次的 R3 就是拿旧知识去否定用户，属于错的。核对地址：<https://api-docs.deepseek.com/zh-cn/quick_start/pricing>

### 11.2 本机环境实测（2026-11-05）

```
System.Net.Http.dll  : 存在（Framework64\v4.0.30319\）
System.Security.dll  : 存在
ollama               : 未安装（PATH 里没有）
本机模型文件          : 没有 .gguf
11434 端口           : 没有监听
```

结论（v3）：**不做本地方案**。M2 的唯一验收手段是**真实 API key**（智谱或 DeepSeek）；
`mock` 只在"还没拿到 key"时兜底，不能当成唯一验收。

### 11.3 还没拿到 key 时的兜底：mock SSE 服务

**已经写好了：`tools\mock-sse.ps1`**（只用于验证，不参与发布），用 pwsh 跑，不需要管理员权限：

```powershell
pwsh -NoProfile -File tools\mock-sse.ps1                # 正常流式
pwsh -NoProfile -File tools\mock-sse.ps1 -Mode 401      # 验错误映射
pwsh -NoProfile -File tools\mock-sse.ps1 -Mode slow     # 验首字超时（先睡 40s 再回）
```

脚本内容（已实测：返回 `HTTP/1.1 200` + 逐字 `data: {"choices":[{"delta":{"content":"你"}}]}`）：

```powershell
# tools/mock-sse.ps1 —— 模拟 OpenAI 兼容的流式接口，给 M2/M3 验收用
# 用 TcpListener 而不是 HttpListener：后者绑 127.0.0.1 需要 URL ACL（管理员）
param([int]$Port = 11435, [string]$Mode = "ok")
$server = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$server.Start()
Write-Host "mock SSE on http://127.0.0.1:$Port/v1  (mode=$Mode)"
while ($true) {
    $client = $server.AcceptTcpClient()
    $stream = $client.GetStream()
    $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII)
    while ($true) { $line = $reader.ReadLine(); if ([string]::IsNullOrEmpty($line)) { break } }
    if ($Mode -eq "401") {
        $msg = "HTTP/1.1 401 Unauthorized`r`nContent-Length: 0`r`nConnection: close`r`n`r`n"
        $b = [Text.Encoding]::ASCII.GetBytes($msg); $stream.Write($b, 0, $b.Length); $stream.Flush()
        $client.Close(); continue
    }
    if ($Mode -eq "slow") { Start-Sleep -Seconds 40 }
    # 没有 Content-Length 也没有 chunked：HttpClient 会一直读到连接关闭，正好适合流式
    $head = "HTTP/1.1 200 OK`r`nContent-Type: text/event-stream`r`nConnection: close`r`n`r`n"
    $b = [Text.Encoding]::ASCII.GetBytes($head); $stream.Write($b, 0, $b.Length); $stream.Flush()
    foreach ($ch in "你好，这是 mock 后端的流式回答。".ToCharArray()) {
        $json = 'data: {"choices":[{"delta":{"content":"' + $ch + '"}}]}' + "`n`n"
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $stream.Write($bytes, 0, $bytes.Length); $stream.Flush()
        Start-Sleep -Milliseconds 40
    }
    # 故意发一个 content:null 的收尾块，用来验证 R4 的修复
    $tail = 'data: {"choices":[{"delta":{"content":null},"finish_reason":"stop"}]}' + "`n`n"
    $tb = [Text.Encoding]::UTF8.GetBytes($tail); $stream.Write($tb, 0, $tb.Length)
    $done = [Text.Encoding]::UTF8.GetBytes("data: [DONE]`n`n")
    $stream.Write($done, 0, $done.Length); $stream.Flush()
    $client.Close()
}
```

`models.txt` 里加一条指向它：

```
mock|Mock 流式|http://127.0.0.1:11435/v1|mock||false
```

三种模式对应三个里程碑验收：

| 模式 | 验什么 |
|---|---|
| `ok` | R4（`content:null` 不能冒字）、M2 流式逐字、M3 正常结束 |
| `slow` | M3 首字超时（25s 应触发"等太久了"） |
| `401` | M3 错误映射（应显示"API Key 无效"，而不是英文异常） |

> 换模式直接加 `-Mode` 参数重启脚本即可；不需要改 App 的任何代码，只改 `models.txt`。

### 11.4 开工顺序（建议）

1. **M1**：`Build.ps1` 补两个引用 → `models.txt` / `keys.txt` 读写 → chip 显示当前 label；
   *顺手把前端 `readonly IChatBackend aiBackend = new FakeBackend();` 改成可解析的字段*（现在是内联 readonly，换后端必须先改它）。
2. **M2**：`OpenAiCompatBackend` → 先用 `mock -Mode ok` 验通协议 → 拿到 key 后改用智谱/DeepSeek 真跑。
3. **M3**：超时 / 取消 / 错误映射 → `mock -Mode slow` / `-Mode 401` 各跑一次，再用错 key 跑一次真接口。
4. M4 之后再动 UI（模型管理、加入模型真正落盘）。

> **Ollama 自动探测不进本轮**（用户决定不做本地模型）。配置格式仍然兼容它，但默认值、错误文案、
> 验收路径里都不再出现"本机模型"。

### 11.5 提交纪律

- 前端那三个未提交文件（`docs/AI前端-交接包.md`、`src/IslandWindow.Ai.cs`、`src/IslandWindow.Motion.cs`）**建议先单独 commit**，
  再开后端的活；否则后端的 diff 会混着前端的改动，回退时分不开。
- 一次 commit 只做一个里程碑。

---

## 12. 用户侧最短清单（注册 → 填 key → 自测）

> 这一节是写给用户看的，不是给 AI 看的；每一步都能独立完成。

### 第 1 步：拿 key（和写代码无关，现在就能做）

| 服务商 | 去哪 | 要做什么 | 拿到什么 |
|---|---|---|---|
| **智谱（默认，免费）** | <https://open.bigmodel.cn> | 注册 → 实名认证 → 控制台「API Keys」新建 | 形如 `xxxxxxxx.yyyyyyyy` 的一串 |
| DeepSeek（可选，几块钱/月） | <https://platform.deepseek.com> | 注册 → 充值（最低额度即可）→ 新建 API key | 形如 `sk-...` |

**只用选一个**。智谱那条 0 成本，先跑它；觉得质量不够再补 DeepSeek。

### 第 2 步：填 key（二选一）

**方式 A（推荐，不用碰文件）**：打开 AI 面板 → 底栏 chip → 「**编辑当前模型**」→ 填 API Key → 保存。
（这是给**已有**模型填 Key 的入口；「＋ 加入模型」是新增一个服务商。也可以**右键**模型列表里的任意一条直接编辑。）
保存后程序用 DPAPI 加密写进 `build\data\keys.txt`。

> 当前模型没 Key 时，chip 弹层里的模型名会显示成橙色的「（缺 Key）」——点「编辑当前模型」补上即可。

**方式 B（手改，方便调试）**：在 `build\data\keys.txt` 里加一行：

```
zhipu|plain:你的key原样贴这里
```

`plain:` 前缀表示"不加密，直接读"——只在本机调试时用，别把这个文件发给别人。
（这条是我在 M1 里额外加的便利开关，官方设计仍然是 DPAPI。）

### 第 3 步：自测一次（3 个动作）

| 动作 | 期望 |
|---|---|
| 打开 AI 面板，确认底栏 chip 显示「智谱 GLM-4.7-Flash（免费）」 | 名字对得上 `models.txt` 里的 `label` |
| 输入「你好」→ 回车 | 回答**逐字**冒出来（不是一次性整段） |
| 回答到一半点「停止」 | 立刻停住，已出的字保留 |

想顺带验错误态：把 `keys.txt` 里的 key 改错一位 → 再发一句 → 应该显示「API Key 无效」，
而不是英文异常或空白。

### 第 4 步（可选）：换服务商

把 chip 切到「DeepSeek Flash」就行——前提是 `keys.txt` 里也有一行 `deepseek|plain:sk-...`。
两个都在 `models.txt` 里，改一行就切换，不用重编译。

---

## 13. 进度：M1 / M2 已完成（2026-11-05）

### 13.1 落了什么

| 项 | 文件 | 说明 |
|---|---|---|
| 编译引用 | `Build.ps1` | 补了 `/r:System.Net.Http.dll`、`/r:System.Security.dll` |
| 配置层 | **新增 `src\AiConfig.cs`** | `AiModelConfig` + `AiConfigStore`：models.txt 读写、keys.txt DPAPI 加密、`plain:` 明文直读、默认两条自动生成、坏行跳过、全坏时兜底一条 |
| chip 接配置 | `IslandWindow.Ai.cs` | 启动读 `models.txt`，「模型」弹层按配置动态生成，选中项写回 `aiModelId` |
| 加入模型 | `IslandWindow.Ai.cs` | 表单改成 4 项（显示名称 / 接口地址 / **模型 ID** / API Key），保存真写 `models.txt` + `keys.txt` |
| 解耦 | `IslandWindow.Ai.cs` | **两个球（网页版）不再改 chip 文案**——球 = 网页入口（M6 才接），chip = API 模型，两个维度分开 |

### 13.2 验收记录（实测）

| 验收项 | 结果 |
|---|---|
| 删掉 `models.txt` 重启 → 自动生成两条默认 | ✅ 内容与 §2 一致 |
| 改 `models.txt` 里的 label → 重启后 chip 跟着变 | ✅ 改成「智谱【改名验证】」后 chip + 弹层都变，验完已改回 |
| 模型弹层列出配置里的全部模型 | ✅ 智谱 / DeepSeek 两条都在，点一下能切换 |
| 加入模型表单 4 个字段 | ✅ 截图确认 |
| DPAPI 加密写读 / `plain:` 手改 / 缺失 key 返回 null | ✅ 用独立控制台 harness 跑通（见下） |

配置层的单测输出（`csc` 直接编译 `AiConfig.cs` + 一个 `Main`）：

```
[1] 首次加载（文件不存在应自动生成） models=2
[2] 加密写读  save=True  after=sk-test-123
[3] 手改 plain:  plain=sk-plain-123  missing=<null>
[4] 加一条模型并重读  models=3 last=测试模型
```

### 13.3 M2 已完成（同日）

新增 `src\OpenAiCompatBackend.cs`（含 `ChatBackends` 工厂）：

| 能力 | 实现 |
|---|---|
| 流式 | `HttpCompletionOption.ResponseHeadersRead` + `StreamReader.ReadLineAsync` 逐行解析 `data:` |
| 首字超时 | 25s（`CancellationTokenSource.CancelAfter`，只包住"等响应头"那一段） |
| 总时长 | 180s（外层 linked CTS，在读取循环里检查） |
| 取消 | 用户点停止 → `OperationCanceledException` 直接上抛，前端静默处理、已输出文字保留 |
| 错误映射 | 401 / 402 / 403 / 404 / 429 + 网络异常，全部翻成中文可操作文案 |
| `content:null` | `ExtractDelta` 里判"冒号后必须是引号"，不会把 `finish_reason` 当正文 |
| 线程 | **没有一处 `ConfigureAwait(false)`**，`onDelta` 保证在 UI 线程 |

接入点只有一处（`IslandWindow.Ai.cs` 的 `RunAiStream`）：

```csharp
IChatBackend backend = ChatBackends.Resolve(CurrentAiModel());
await backend.SendAsync(prompt, delegate(string delta) { AppendDelta(message, delta); }, token);
```

配置不全时 `Resolve` 抛"人话"异常，前端统一显示成错误气泡 + 重试按钮。

**验收记录（全部实测）**：

| 用例 | 结果 |
|---|---|
| `mock -Mode ok` | ✅ 逐字流式出「你好，这是 mock 后端的流式回答。」，结尾的 `content:null` 块没有污染文字 |
| `mock -Mode 401` | ✅ 气泡显示「API Key 无效（401）。检查 data\keys.txt 里那一行。」 |
| `mock -Mode slow` | ✅ 25 秒后显示「等太久了（25 秒没有响应），已取消」 |
| 模型要 key 但 `keys.txt` 没有 | ✅ 显示「这个模型需要 API Key。在 data\keys.txt 里加一行：zhipu\|plain:你的key」 |
| 真实服务商 | ⏳ 等用户的 key |

### 13.4 还没做

- **真实 key 的验收**（用户侧，拿到 key 按 §12 走一遍）；
- 真实的流中「停止」——代码路径与 mock 一致，但没用真服务商压过；
- 推理等级（High/Medium/Low）目前只改 chip 文字，还没进请求参数；
- M4 模型管理打磨、M5 会话落盘、M6 WebBackend。

### 13.5 一个副作用要知道

`FakeBackend` 还留在 `src\ChatBackend.cs` 里，但 `models.txt` 没有指向它的条目，所以**当前没有任何路径会用到它**。
留着它是当"离线演示/协议对照"用的；嫌它碍眼可以删掉这个类，接口定义在同文件里。不用删。
