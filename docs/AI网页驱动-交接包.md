# 交接包：Capsule image Pro —— 网页版驱动（WebBackend，实验功能）

> 写于 2026-11-06。前置阅读：`docs/AI前端-交接包.md`、`docs/AI后端-交接包.md`。
>
> **一句话**：让"豆包 / DeepSeek 网页版"这两个球真正能回答问题——后台开一个**看不见的 WebView2**，
> 用 JS 把问题打进网页、再把回答抓回来，伪装成和 `ApiBackend` 一样的流式输出。
>
> **策略**：先在项目外做一个**独立探针**（`E:\deepseek1\aiweb-probe\`），实测选择器，
> 验证通过后再照抖音那套搬进项目。**探针没跑通，一行都不要写进项目。**

---

## 0. 为什么它最脆弱（先认清代价）

| | API | 网页版驱动 |
|---|---|---|
| 依赖 | HTTP 协议（几年不变） | 对方网页的 DOM 结构 |
| 改版影响 | 无 | **改一次类名就可能全废** |
| 登录 | key 长期有效 | cookie 会过期，要重扫 |
| 条款 | 正常使用 | 自动化操作网页，**属于灰色地带** |
| 风控 | 无 | 可能被识别、限制 |

所以：**默认关闭，放在"实验"开关后面**；坏了不能影响 API 那条路。

---

## 1. 目标站点与差异

| 站点 | 地址 | 登录方式 | 备注 |
|---|---|---|---|
| DeepSeek 网页版 | `https://chat.deepseek.com/` | 手机号 / 邮箱验证码 | 先做这个（用户已在用） |
| 豆包网页版 | `https://www.doubao.com/chat/` | 手机号 | 结构不同，第二个做 |

**一份 `WebBackend`，两套配置**（每个站点一份选择器），不要写两个类。

---

## 2. 必须先实测的四件事

在写 `WebBackend` 之前，探针里要拿到这些答案：

| # | 问题 | 为什么关键 |
|---|---|---|
| 1 | 输入框是 `<textarea>` 还是 `contenteditable` | 决定怎么写入文字 |
| 2 | 发送按钮怎么定位（类名 / aria-label / 文字） | 决定怎么点 |
| 3 | 回答容器是哪个元素 | 决定从哪里抓文字 |
| 4 | **怎么判定"生成结束"** | 判错就抓半截回答 |

第 4 条三种常见判法，按可靠性排：

1. **发送按钮变回"发送"态**（最可靠，但要确认有这种状态切换）；
2. 回答文字停止变化超过 1.5 秒（通用兜底）；
3. 出现"停止生成"按钮 → 消失（不少站点是这样）。

建议 1 + 2 组合。

---

## 3. 探针（第一步，先做这个）

位置：`E:\deepseek1\aiweb-probe\`（项目外，跟抖音那个 spike 一样）

```
AiWebProbe.exe          正常窗口，WebView2 占满
  [DeepSeek] [豆包]     切换站点
  [导出结构]            把候选元素写进 probe.log
  [测试输入]            找到输入框 → 写入"你好" → 看发送按钮是否变可用
```

`导出结构` 会 dump：

- 所有 `textarea` / `[contenteditable=true]` 的：标签、类名、位置、尺寸、placeholder；
- 所有 `button` / `[role=button]` 里"文字或 aria-label 含 发送/send/submit"的；
- 所有类名含 `message` / `markdown` / `chat` 的容器（取前 10 个）。

日志：`E:\deepseek1\aiweb-probe\probe.log`（UTF-8，直接读）。

**验收标准**：能明确写出"输入框选择器 / 发送按钮选择器 / 回答容器选择器 / 结束判定"，四样齐全才能进项目。

---

## 4. 集成方案（探针通过后再看）

### 4.1 窗口

和抖音反过来：**平时完全看不见**。

```csharp
var window = new Window { ShowInTaskbar = false, WindowStyle = None,
                          Width = 900, Height = 700, Left = -4000, Top = -4000 };
window.Show();               // 必须 Show，否则 WebView2 不加载
Cloak(true);                 // DwmSetWindowAttribute(hwnd, 13, 1)  —— 注册表都看不见
```

- 位置挪到屏幕外 + DWM cloak 双保险；
- 首次使用要**临时露出来**（去掉 cloak）让用户扫码登录；
- 登录态存 `data\webview2-ai\`，和抖音的分开。

### 4.2 输入

```js
// React 受控组件必须用原生 setter，否则值改了但状态没更新
var el = document.querySelector(INPUT_SELECTOR);
var proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, text);
el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: text }));
```

`contenteditable` 的情况改用：

```js
el.focus();
document.execCommand('insertText', false, text);   // 仍然有效，且会触发 input 事件
```

### 4.3 发送

```js
document.querySelector(SEND_SELECTOR).click();
```

**不要**合成 `keydown`(Enter)——大多数框架只认"真实按键"，合成事件会被忽略。

### 4.4 收回答

```js
var target = document.querySelector(ANSWER_SELECTOR);   // 最后一条 AI 消息
var last = target.innerText;
new MutationObserver(function(){
    var now = target.innerText;
    if (now.length > last.length) { push(now.slice(last.length)); last = now; }
}).observe(target, { childList: true, subtree: true, characterData: true });
```

- 用 **差分**（只推新增部分），和 `IChatBackend.onDelta` 的约定一致；
- 结束判定：发送按钮恢复可点，或 1.5 秒无变化 → 回填 `[DONE]`。

### 4.5 桥接

WebView2 → C# 用 `WebMessageReceived`：

```csharp
core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
{
    onDelta(e.TryGetWebMessageAsString());
};
core.AddScriptToExecuteOnDocumentCreatedAsync(@"
    window.__dsh = function(t){ window.chrome.webview.postMessage(t); };");
```

JS 里把增量通过 `window.chrome.webview.postMessage(delta)` 发回来。

---

## 5. 失败模式（都要有明确提示，不能静默）

| 情况 | 提示 |
|---|---|
| 没登录（出现登录框） | "网页版还没登录，点这里扫码" → 临时揭开窗口 |
| cookie 过期 | 同上 |
| 找不到输入框 | "网页改版了，这个站点暂时不可用，先用 API" |
| 发送按钮一直不可点 | "网页版没接受这条消息" |
| 60 秒没有新文字 | "网页版没有响应，可能被限流" |
| 站点弹出验证码 | 同上，并临时揭开窗口让用户处理 |

**任何一种失败，都必须能一键切回 API**——不能把用户困在坏掉的后端里。

---

## 6. 里程碑

| # | 内容 | 验收 |
|---|---|---|
| W1 | 探针跑通 DeepSeek，拿到四样选择器 | `probe.log` 里四样齐全 |
| W2 | 探针里能完整跑一轮「输入 → 发送 → 抓完整回答」 | 回答文字完整、不多不少 |
| W3 | 豆包同样跑通 | 同上 |
| W4 | 搬进项目：隐藏窗口 + 登录流程 + WebBackend | 点豆包球能真回答 |
| W5 | 失败提示 + 一键切 API | 断开登录态时提示正确 |

**W1/W2 没通就别做 W4**——这是我这次唯一坚持的顺序。

---

## 7. 已知风险与底线

- 不要绕过登录、不要抓 cookie 发给第三方、不要模拟验证码；
- 不要高频调用（两次提问间隔 < 2 秒容易触发风控）；
- 结果默认**不落盘**（除非用户开"保存会话"），避免把网页内容写进聊天记录；
- 这个功能永远标"实验"，且默认关闭。

---

## 8. 给下一个对话的开场

> 读本文件 + `docs/AI后端-交接包.md` §4（OpenAI 兼容后端）。
> **第一步只做探针**（`E:\deepseek1\aiweb-probe\`），拿到四个选择器之前不要碰项目源码。
> 探针通过后按 §4 集成；`IChatBackend` 接口不要改，`WebBackend` 只是它的第三个实现。
> 抖音三个文件（`DouyinPlayerWindow.cs` / `IslandWindow.Douyin.cs` / `MorphShellWindow.cs`）不要动。

---

## 9. 进度（2026-11-06 实测）

### 9.1 探针结果（`E:\deepseek1\aiweb-probe\`，项目外）

| 站点 | 输入 | 发送 | 回答容器 | 实测 |
|---|---|---|---|---|
| DeepSeek | `textarea`（原生 setter） | `div.ds-button--primary.ds-button--filled`，输入框右下 | `.ds-markdown.ds-assistant-message-main-content` | ✅ 两轮完整 |
| 豆包 | `div.tiptap.ProseMirror`（execCommand） | 36×36 图标，**输入文字后才出现** | `.message-list-*` 最后一条 | ✅ 一轮完整 |

**通用经验**：发送按钮一律"输入框下方横带里取最右边那个"，按中心距离找会点到旁边的「快速」胶囊。

### 9.2 W4 已落地（DeepSeek）

| 文件 | 内容 |
|---|---|
| **新增 `src\WebBackend.cs`** | `WebSiteConfig` + `WebBackend`（节流 ≥3s）+ `WebChatWindow`（屏幕外 + DWM cloak、懒创建、轮询差分） |
| `src\OpenAiCompatBackend.cs` | `ChatBackends.Resolve` 里加一条：`baseUrl` 以 `web://` 开头 → 走 `WebBackend`，不需要 key |

启用方式（**默认关闭**，opt-in）：`build\data\models.txt` 里去掉这行的 `#`

```
# deepseekweb|DeepSeek 网页版（实验）|web://deepseek|deepseek-web||false
```

实测（2026-11-06）：选「DeepSeek 网页版（实验）」→ 输入 hi → 面板里收到 `Hello! How can I help you today?`，
全程没有可见窗口，流式进入气泡。

### 9.3 豆包也接上了（2026-11-06 追加）

`WebSiteConfig.Kind` 区分两种站点形态，`WebSites.Find` 里各写一份：

| | `ds`（DeepSeek） | `db`（豆包） |
|---|---|---|
| 输入 | `textarea` + 原生 setter | `div.tiptap.ProseMirror` + `execCommand('insertText')` |
| 回答定位 | 固定选择器 `.ds-markdown.ds-assistant-message-main-content`，取第 `before` 个 | `[class*=message-list]` 里**高度 > 40 的最后一行** |
| 取文本 | 直接 `innerText` | 克隆该行 → 删掉 `[class*=action-bar]`、`[class*=suggest]` → `innerText.trim()` |
| 避免读到旧回答 | 索引天然隔离 | 发送前先记下"上一条回答"，内容没变就当还没开始 |

实测：点豆包球 → chip 变「豆包网页版（实验）」→ 发 hi / ok → 收到「hi!hi～有什么我可以帮你的吗?」「ok」。

**踩到并修掉的两个坑**：

1. 登录检测写成 `!= null`，`"0"` 也被当成已登录 → 改成 `== "1"`；
2. 页面首次加载时 DOM 还没出来，被误判成"没登录"导致隐藏窗口闪一下 →
   判定前先给页面最多 20 秒。

另外去掉了一个 30 秒的空等：原来要等"行数 +2"才轮询，现在直接轮询、
用"是否等于上一条回答"来排除旧内容。

### 9.4 还没做

- **登录揭盖**路径只写了没实测（当前用的是从探针复制过来的已登录 profile）；
  代码逻辑：20 秒内找不到输入框 → 窗口揭盖 + 置顶 3 分钟，等登录后自动盖回去；
- 正式的"实验开关"UI（现在是"点球自动写 models.txt"，够用但不够显眼）；
- 失败提示里"一键切回 API"目前是提示用户自己切 chip，没做自动回退。

### 9.5 已知限制

- 结尾判定用"文字 1.5 秒不变"，超长回答里如果有 1.5 秒的停顿会被判早结束（概率低）；
- 豆包/DeepSeek 改版后类名会变 → 报"网页版没找到发送按钮（可能改版了）"，不会崩；
- 同一时刻只允许一个网页请求（节流 + 单窗口）；
- 诊断日志：`build\webdriver.log`（谁在什么时候发了什么、轮询到了多少字）。
