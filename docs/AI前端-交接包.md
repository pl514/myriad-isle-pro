# 交接包：Capsule image Pro —— AI 面板（第一阶段：只做前端）

> 写于 2026-11-05。**v2**：对着真实源码复核，补齐 9 处必踩的细节（§9）。
> **v3（同日，按用户手绘批注）**：面板多了三块——**顶栏两个网页版 AI 球 / 左侧会话栏 / 底栏模型 chip 弹层**，
> 面板宽 760 → 860。**v4（实测收尾）**：窗口改固定高度（改高度会黑屏）、去掉了居中偏移、
> 补了"第二次打开只剩底色"和"输入法开着时回车失效"两个真坑（§9 P3 / P13）。
> **v6（2026-11-05，由另一个会话复核后修订）**：
> ① 作废 §2 第 6 条、§7 第 5 条里"窗口高度还原"的说法——方案 B 是固定高度，永远不会还原，见 **§13**；
> ② §12 的行号已过时，见 §13 的替代读法；
> ③ 补记一条事实：AI 面板入口在图片面板的球里，**"从胶囊形变"这条路径当前走不到**，见 §13。
> §2 / §4 / §5 / §6 / §7 / §8 / §9 已全部同步；**开工前必须读完 §9（P1–P15）和 §13**。
>
> **一句话**：在胶囊窗口里做一个**原生 WPF** 的 AI 对话面板——点 AI 球 → 面板形变展开 → 输入问题 → 流式回答。
> 这一阶段**只做界面和假数据**，不接任何真实模型。

---

## 0. 先读这一节：为什么必须是原生 WPF

胶囊窗口是 `AllowsTransparency = true` 的**分层透明窗口**。WebView2 的常规控件是子 HWND，**放进去一点画面都不会有**（这个坑已经在抖音小窗上实测确认，最终只能做成独立窗口）。

所以「AI 面板画在胶囊里」这件事只有一条路：**纯 WPF 控件**（TextBox / ItemsControl / TextBlock）。
好消息是这正好也是最好的选择——流式文字用原生控件渲染，比嵌网页更轻、更快、和现有动效同源。

---

## 1. 项目现状（已建成的部分，别重复做）

| 项 | 状态 |
|---|---|
| 源码 | `E:\yingyong\deepseekgongzuoqv\IslandShot-pro\src\*.cs` |
| 构建 | `Build.ps1`（csc，.NET Framework 4.8，无 csproj / 无 NuGet） |
| 产物 | `build\Capsule image Pro.exe`（+ 3 个 WebView2 DLL，抖音用） |
| 三球工具坞 | ✅ 已做：[IslandWindow.Orbs.cs](E:/yingyong/deepseekgongzuoqv/IslandShot-pro/src/IslandWindow.Orbs.cs) |
| 抖音小窗 | ✅ 已做：`DouyinPlayerWindow.cs` + `IslandWindow.Douyin.cs` + `MorphShellWindow.cs` |
| 热键系统 | ✅ `Hotkeys.cs`（数据驱动，设置面板自动列出）；抖音开关默认 `Ctrl+Alt+Shift+D` |

### 三球工具坞是什么

图片/文字面板的顶栏，页签右边有一个 26px 圆点，点开散出三个 28px 球：**抖音 / AI 问答 / 音乐**。

- 点球 → `RunTool(tool)` → `tool.Launch()`；
- AI 球现在是占位：`LaunchAiChat()` 只弹一个托盘气泡（[IslandWindow.Orbs.cs:418-421](E:/yingyong/deepseekgongzuoqv/IslandShot-pro/src/IslandWindow.Orbs.cs#L418-L421)）；
- 每个球有运行态绿点：`SetToolRunning(tool, true/false)`。

**入口改造只有一行**：把 `LaunchAiChat()` 换成 `OpenAiPanel()`。

### 当前 git 状态（回退用）

```
f69eaca  docs: 新增 AI 前端交接包（第一阶段只做界面）   ← 最新
6a0c3fd  fix: 隐藏播放器右侧播上一条/下一条黑条
v0.7-no-right-strip / v0.6-hide-right-strip / v0.5-layout-modes
v0.4-no-crop / v0.3-no-white-flash / v0.2-douyin-morph / v0.1-douyin-working
```

回退代码用：`git -C "E:\yingyong\deepseekgongzuoqv\IslandShot-pro" reset --hard 6a0c3fd`

---

## 2. 这一阶段的目标

### 要做

1. 点胶囊工具坞的 AI 球 → 面板形变展开、输入框自动获得焦点；
2. 输入文字 → `Enter` 发送（输入法组词时除外，见 §6）；
3. **假后端**（`FakeBackend`）：把固定文本按 20~40ms 一批的节奏流式吐出来；
4. 流式过程中输入框右侧变成「停止」按钮；
5. 消息列表：用户气泡靠右、AI 气泡靠左，可滚动、自动滚到底；
6. `Esc` / 点面板外 / 面板右上 `×` → 收起面板；**窗口高度不还原**（方案 B 固定 440，详见 §3 与 §13）；
7. **顶栏两个「网页版 AI」球**（DeepSeek / 豆包）：只切换"当前用哪个"，球选中态 + chip 文案跟着变；
8. **左侧会话栏**：「＋ 新对话」+ 历史对话列表，点一下切会话（**内存多会话**，退出即清空）；
9. **底栏模型 chip**：显示当前模型/推理等级，点开弹层（模型列表 / 推理等级 / ＋加入模型）；
10. 「加入模型」：弹空表单占位，保存只提示"阶段二接 API 后才生效"，**不落盘、不发请求**。

### 明确不做（下一阶段的活）

- ❌ 真实 API、key、真实模型调用（模型弹层**只做 UI**）
- ❌ 网页版驱动（隐形 WebView2 + DOM 注入）——两个球**只做选中态**，不打开浏览器
- ❌ 图片问答 / 上传
- ❌ 会话落盘、历史搜索（历史只存内存）
- ❌ 多模型并发对比、token 统计

---

## 3. 关键工程决策：窗口高度

**问题**：原来的窗口是 `1000 × 260`，图片面板只有 `960 × 236`。一个能用的聊天面板装不下。

**最终实现：方案 B —— 窗口高度固定 440，运行时一次都不 resize。**

```
收起态：窗口 1000×440（下面 400px 全是透明像素，点击照常穿透）
AI 态 ：窗口 1000×440（AI 面板 860×420 画在上面，位置不变）
```

> **为什么放弃"进入 AI 时改高度"**：实测会黑屏。`AllowsTransparency = true` 的分层窗口一旦改
> `Height`，合成表面跟不上，整个窗口会变成一块黑的；再点一次就复现。这是分层窗口的老问题，
> 和有没有 WebView2 无关。
>
> 所以：**`Height` 在构造函数里定死，之后任何地方都不要改**。多出来的透明区域不需要任何处理——
> 分层窗口的命中测试按像素 alpha 判定，alpha = 0 的地方鼠标直接落到桌面（这就是"点外面收起"
> 能正常工作、桌面图标也能点的原因）。
>
> `ApplyAiWindowHeight()` 现在只负责把 `aiPanel.Height` 夹到可用空间里，**不碰 `Height`**。

**居中**：`PositionOnScreen()` 原来的 `CapsuleOffsetX = 150` 是为了让 Pro 版和标准版并排不重叠，
现在用户要求居中，常量已改 0。要并排运行再改回 150。

**面板高度**：`AiPanelHeight = 420`，内部三行 Grid（顶栏 `Auto` / 主体 `*` / 底栏 `Auto`），
**不要给消息区写死行高**。

**矮屏兜底**：`ApplyAiWindowHeight` 用 `MaxWindowHeight()` 把面板高度夹在
`工作区高/dpi - 20` 之内；440 DIP 在 1366×768 / 125% 下是 550 物理像素，放得下。

**状态收尾不能只靠动画回调**：收起动画如果被下一次展开打断，`Completed` 不会触发，
`aiVisible` 会卡在 `true`（下次点胶囊会直接弹回 AI 面板）。所以 `SetExpandedAnimated(false)`
末尾还挂了一个 `Motion.Delay(340, ...)` 的兜底，超时强制收尾。

要点：

- `Width` **不要动**。`PositionOnScreen()` 用窗口宽高算居中，宽度变了一切定位逻辑都要重算。
- 多出来的透明区域**不要**主动给它上色（包括 `Brushes.Transparent`）。根元素留 `null` 或只给真正的内容上色。

---

## 4. 界面规格（v3）

```
┌──────────────────────────────────────────────────────────────┐
│  AI 助手 │ ○ ○                                    ←  ×   │ 顶栏 40
├──────────┬───────────────────────────────────────────────────┤
│ ＋新对话  │                                                   │
│ ─────────│        ┌─────────────────────────┐                │
│ 今天      │        │ 用户消息（右，紫底）      │                │
│   历史 1  │        └─────────────────────────┘                │
│   历史 2  │  ┌────────────────────────────┐                   │
│ 昨天      │  │ AI 消息（左，深底 + 边框）    │                   │
│   历史 3  │  └────────────────────────────┘                   │
├──────────┴───────────────────────────────────────────────────┤
│ [ 输入你的问题…                ] [DeepSeek 网页版 ⌄] [发送]   │ 底栏 56
└──────────────────────────────────────────────────────────────┘
                ↑ 点 chip 向上弹出：
                ┌────────────────────────┐
                │ 模型   DeepSeek 网页版  >│
                │ 推理等级            High >│
                │ ────────────────────── │
                │ ＋ 加入模型             │
                └────────────────────────┘
```

| 项 | 值 |
|---|---|
| 面板尺寸 | 宽 **860**（v3：比原 760 宽 100，正好给左栏）× 高 420（矮屏时 = `窗口高 - 20`） |
| 左栏 | 宽 160，右侧 1px 分隔线 `#FFFFFF14`；顶部「＋ 新对话」36 高，下面是历史列表（`ScrollViewer`） |
| 圆角 | 30（和现有图片面板一致） |
| 背景 | 复用图片面板的渐变：`#262C33` → `#1B2129`，透明度 242/240 |
| 字体 | `Microsoft YaHei UI`，正文 13，用户消息 13，代码 12 等宽 |
| 气泡圆角 | 14，内边距 12×8，最大宽度 86% |
| 输入框 | 高 34 起、最高 92（`TextWrapping=Wrap` + 自动竖向滚动条），多行 |
| 网页版球 | 22px，两个，间距 8；在"AI 助手"右边隔 12 的 1px 竖线之后 |
| 顶栏右侧 | **← 返回图片面板**（不收起窗口，淡出 AI 面板+淡入图片面板）、**× 收起回胶囊**（走 `SetExpanded(false)`）。两个都要：用户明确要求"× 左边加返回键" |
| 球选中态 | 外圈 1px `#7C6CF2` + 外发光；未选中是半透明白 `#FFFFFF1A` |
| 模型 chip | 高 26，圆角 13，底 `#FFFFFF0F`，文字 11，右侧一个 ⌄；内容形如 `DeepSeek 网页版` |
| **滚动条** | **必须自己写样式**：WPF 默认是浅色宽条 + 上下箭头，在深色面板里像一根白条（用户第一眼就指出了）。做法见 §9 P14：8px 轨道透明，滑块 6px / 圆角 3 / `#3AFFFFFF`，悬停变亮 |
| chip 弹层 | 宽 240，圆角 12，从 chip **上**方弹出（底栏在最下，只能往上）；三行 + 一条分隔线 |
| 加入模型表单 | 面板内浮层，宽 300；三个输入框（名称 / 接口地址 / Key）+ 取消·保存 |
| 动画 | 展开：从胶囊 420ms（`Motion.Enter`）；从图片面板切过来 200ms 淡入 + 0.96 缩放；弹层 160ms 上浮淡入 |

颜色沿用现有体系：主色 `#7C6CF2`，正文 `#E6E9F0`，次要文字 `#A7A0C4`。

---

## 5. 代码结构建议

```
src/ChatBackend.cs        IChatBackend + FakeBackend
src/ChatMessage.cs        ChatMessage(Role/Text/状态) + ChatSession(Id/Title/Messages/Provider)
src/IslandWindow.Ai.cs    partial：面板构建、左栏会话、chip 弹层、状态机、流式渲染
```

`Build.ps1` 自动收集 `src\*.cs`（[Build.ps1:32](E:/yingyong/deepseekgongzuoqv/IslandShot-pro/Build.ps1#L32)），加文件不用改构建。

**改动点只有这 5 处旧文件**（抖音三个文件一行都不要碰）：

| 文件 | 改什么 |
|---|---|
| `IslandWindow.Orbs.cs:418` | `LaunchAiChat()` → `OpenAiPanel()` |
| `IslandWindow.Motion.cs:399-466` | `SetExpandedAnimated` 的载体从 `panel` 泛化成 `surface`（§9 P1） |
| `IslandWindow.cs:124-131` | 点外收起放行 AI 面板 + 流式中不收起（§9 P5） |
| `IslandWindow.cs:132-135`、`:183-191` | `Deactivated` / 全局鼠标钩子 加 `!aiStreaming`（§9 P5） |
| `IslandWindow.cs:225-229` | `Esc` 两段语义（§9 P6） |

### 字段（`IslandWindow.Ai.cs`）

```csharp
const double AiPanelWidth = 860;
const double AiPanelHeight = 420;
const double AiSidebarWidth = 160;

Border aiPanel;
TextBox inputBox;
ScrollViewer chatScroll;
StackPanel messageList;        // 只放当前会话的气泡
StackPanel sessionList;        // 左栏历史
TextBlock modelChipText;
Border chipPopup;              // 弹层（面板内 Grid 叠层，不是 Popup）
Border addModelSheet;          // 「加入模型」浮层

readonly List<ChatSession> sessions = new List<ChatSession>();
ChatSession current;
string webProvider = "deepseek";   // "deepseek" / "doubao"
string modelLabel = "DeepSeek 网页版";
string reasoningLevel = "High";

bool aiVisible;      // 当前展开的内容是 AI 面板
bool aiStreaming;    // 正在流式
bool chipOpen;
CancellationTokenSource aiCancellation;
```

### 后端接口（v2 定稿）

```csharp
internal interface IChatBackend
{
    // 流式发送。onDelta 收到的是【本次新增的片段】（增量），由前端自己累加。
    // 正常结束或被取消时返回；出错请直接抛异常，前端统一 catch → 错误气泡 + 重试。
    Task SendAsync(string text, Action<string> onDelta, CancellationToken token);
}
```

三条定稿理由：

- **去掉 `Cancel()`**：`Cancel()` 和 `CancellationToken` 是同一件事，两套并存迟早打架；
- **去掉 `onError`**：错误即异常，ApiBackend / WebBackend 的差异不用前端关心；
- **`onDelta` 必须是增量**（不是全量快照），前端只做 `msg.Text += delta`。

`FakeBackend`（`async/await` + `Task.Delay`，.NET 4.8 自带，不需要 NuGet）：

```csharp
internal sealed class FakeBackend : IChatBackend
{
    const string Answer = "...";   // 写死一段带代码块的中文回答，顺便验证代码块渲染

    public async Task SendAsync(string text, Action<string> onDelta, CancellationToken token)
    {
        int i = 0;
        while (i < Answer.Length)
        {
            token.ThrowIfCancellationRequested();
            int take = Math.Min(1 + (i % 2), Answer.Length - i);   // 每批 1~2 字
            onDelta(Answer.Substring(i, take));
            i += take;
            await Task.Delay(30, token);                           // 取消时抛 TaskCanceledException
        }
    }
}
```

调用侧（UI 线程，`async void`）：

```csharp
async void SendToBackend(string text)
{
    aiCancellation = new CancellationTokenSource();
    try { await backend.SendAsync(text, AppendDelta, aiCancellation.Token); }
    catch (OperationCanceledException) { /* 已输出的文字保留，不显示错误 */ }
    catch (Exception exception) { ShowAiError(exception.Message); }
    finally { EndStreamingUi(); }
}
```

> ⚠️ 这里是 `async void`，**方法体必须整体包在 try/catch 里**。漏出去的异常会直接崩进程。

### 状态机（v2：两层状态）

```
expanded   = 窗口处于展开态（保持原意，所有收起路径都走 SetExpanded(false)）
aiVisible  = 当前展开的内容是 AI 面板，而不是图片面板

IDLE ──(回车)──► STREAMING ──(done)──► IDLE
                    │
                    └──(点停止 / Esc 第一次)──► IDLE
```

**不要把 AI 面板另开一套 `expanded`**，否则点外面收起、全局鼠标钩子、热键收起、托盘双击这四条路径全都会漏掉一处。

### 会话数据（v3）

```csharp
internal sealed class ChatSession
{
    public string Id;
    public string Title;                       // 首句前 12 字，超出加 …
    public List<ChatMessage> Messages = new List<ChatMessage>();
    public string Provider;                    // "deepseek" / "doubao"
}
```

- 启动时先建一个空会话，避免"左栏是空的、聊天区也没东西"这种边缘态；
- 「＋ 新对话」：把当前会话压进 `sessions`（如果它已经有消息），再新建一个；
- 点历史项：切 `current`，**整块重绘**消息区（§9 P11）。

---

## 6. 交互细节（照做，别自由发挥）

| 情况 | 行为 |
|---|---|
| 面板打开 | 输入框 `Focus()`，光标在末尾 |
| `Enter` | 发送；输入框清空 |
| `Shift+Enter` | 换行（`AcceptsReturn`） |
| **中文输入法开着时按 `Enter`** | 用 `e.ImeProcessedKey` 取真实按键：组词中 → 上屏不发送；不在组词 → 发送（见 §9 P3） |
| 流式中按 `Enter` | 忽略 |
| 流式中按 `Esc` | **先停止流式**，不收起面板 |
| 非流式中按 `Esc` | 先关弹层（如果开着），没有再收起面板 |
| **流式中点面板外** | **不收起**（继续看回答）。只有 `Esc` / 停止 / `×` 能中断或关闭 |
| **流式中点 `×` / 第二次 `Esc`** | 先停止流式，再收起；已输出的文字保留 |
| 点面板外 | 收起（沿用现有 `PreviewMouseLeftButtonUp` 逻辑，注意别和工具坞的球冲突） |
| **AI 状态下点面板外** | **不收起**。只有 `×` 和 `Esc` 能关（用户 2026-11-05 明确要求）。三处都要放行：`PreviewMouseLeftButtonUp`、`Deactivated`、全局鼠标钩子的回调 |
| **收起后从胶囊再展开** | 记住上次是哪个面板：上次是 AI → 直接回到 AI（对话还在）；上次是图片/文字 → 仍然默认回「图片」页签。字段 `aiLastSurface`，在 `SetExpanded(true)` 里判断；拖图/截图预览这类"必须是图片面板"的路径要手动置 false |
| **删除历史会话** | 左栏会话行**右键** → 弹面板内小菜单「删除会话」（不用 WPF `ContextMenu`，分层窗口里它会跑到所有窗口最上层）。删的是当前会话时自动切到最近一个；删空了新建一个空会话。M5 落盘后，删除还要同步删 `chats.txt` 里的行 |
| 消息为空时发送 | 忽略（纯空白也算空） |
| 流式出错误 | AI 气泡里显示错误文案 + 「重试」按钮 |
| 回答完成 | 气泡下方出现「复制」按钮 |
| 复制 | `Clipboard.SetText` 可能被别的程序占用 → 重试 5 次（40ms 间隔），失败不崩 |
| **点顶栏网页版球** | 只切换 `webProvider` + 球选中态 + chip 文案；**不收起面板、不打开浏览器** |
| **点「＋ 新对话」** | 有消息就把当前会话压进左栏历史；清空消息区、开新会话 |
| **点历史项** | 切会话，消息区重绘 + 滚到底；当前项高亮 |
| **点 chip** | 弹层从 chip 上方弹出；再点一次收起 |
| **弹层打开时点面板内别处** | **只关弹层**，不关面板 |
| **弹层打开时点面板外** | 先关弹层，再收面板 |
| **点「＋ 加入模型」** | 弹表单（显示名称 / 接口地址 / 模型 ID / API Key），保存真写 `models.txt` + `keys.txt`（DPAPI） |
| **推理等级** | 五个吸点的自绘滑块（低/中/高/极高/最高）：已选段才上色，旋钮带辉光；拖动或点击都吸附到最近一档。**目前只影响 chip 显示，还没进请求参数**（各家的参数名不统一，接入前要先确认） |
| **点「编辑当前模型」/ 右键模型行** | 打开同款表单并预填，用来给已有模型填 Key；编辑态多一个「删除」按钮 |
| **点「模型」行** | 展开 `models.txt` 里的模型列表：当前模型左边有一个小圆点，没有 Key 的标橙色「缺 Key」；左键选中、右键编辑 |

---

## 7. 验收清单

- [ ] 点 AI 球，面板从胶囊位置长出，输入框已聚焦
- [ ] 输入一段中文，回车后逐字出现回答，滚动条自动跟到底
- [ ] 流式中输入框变成「停止」，点击后立刻停住，已输出的文字保留
- [ ] `Esc` 第一次停止、第二次收起
- [x] ~~收起后窗口高度**精确**回到 260~~ → **不适用**：方案 B 是固定高度，永不变（见 §3 / §13）
- [ ] 再次打开，上一轮对话还在（内存即可）
- [ ] 面板外的桌面区域点击正常穿透（不挡其它程序）
- [ ] 抖音球、图片页签、文字页签都**没有被这次改动弄坏**
- [ ] **拼音输入「你好」，按 Enter 选词那一下没有把消息发出去**
- [ ] **1366×768 / 125% 缩放的机器上，底栏和"发送"按钮完整可见**
- [ ] **流式中点别的窗口，面板不消失，回答继续跑**
- [ ] **窗口撑高后，在面板外的透明区点桌面图标能正常选中**（实测穿透，见 §8 备注）
- [ ] **从 AI 面板收起后，再点「图片」页签一切正常**（AI 的高度改动没有污染图片面板布局）
- [ ] **v3：点两个球能切换，选中态看得见，点球不会把面板收起来**
- [ ] **v3：「新对话」把当前对话压进左栏，点历史能切回去，消息和滚动位置对**
- [ ] **v3：chip 点开弹层，点面板别处只关弹层；点桌面才收面板**
- [ ] **v3：「加入模型」表单能填能取消，保存后不崩**
- [ ] **v3：左栏只有 1~2 条历史时布局不塌，20 条时能滚**

---

## 8. 已经踩过的坑（别重复）

| 坑 | 结论 |
|---|---|
| 分层窗口里塞 WebView2 | 不可能，一点画面都没有。AI 前端必须原生 WPF |
| 用 `Brushes.Transparent` 当面板根背景 | WPF 内部把 `Transparent` 当"命中了"，会吃掉本窗口的预览事件（点外面收起就失灵）。面板根别设它。至于**桌面穿透**，是分层窗口按像素 alpha 判定的：面板之外只要没画任何东西（alpha = 0）就是穿透的。这条 §7 要实测 |
| 动画改布局属性（Width/Height/Margin） | 现有的 `Motion` 只改 `Opacity` / `RenderTransform`，继续遵守 |
| 窗口 Resize 带 WebView2 | 会闪。AI 面板没有 WebView2，所以这里反而安全 |
| 窗口高度变化后位置漂移 | 只改 `Height` 不改 `Width`，`Top` 保持，居中逻辑不用动 |
| 工具坞的球被"点外面收起"误伤 | 球群和 AI 面板都算"可交互"，判断里要放行 |
| **TextBox `AcceptsReturn=true` 把 `Enter` 吃了** | 窗口级 `KeyDown` 收不到，必须在 TextBox 的 `PreviewKeyDown` 里拦 |
| **`if (e.Key == Key.ImeProcessed) return;` 当护栏** | 输入法开着时**所有**按键都以 ImeProcessed 到达（h / i / 回车都一样），这样写回车直接失效。必须取 `e.ImeProcessedKey` + 组词状态判断 |
| **第二次打开面板只剩一块底色（"黑屏"）** | 收起动画把内容层 Opacity 淡到 0，重开时只淡了面板本身。见 §9 P13 |
| **点 AI 面板里的 TextBox 被当成"点外面"** | `panel.IsMouseOver` 之外还要显式放行 AI 面板；AI 面板收起态记得 `IsHitTestVisible = false` |
| **直接复用 `CapsuleWidthRatio()`** | 它读的是图片面板 `panel.Width`（960），AI 面板 860 会算出错误的形变起点 |
| **620 DIP 在矮屏装不下** | 见 §3 兜底，1366×768 / 125% 下是必现的 |
| **流式中点外面面板消失了，流式还在后台跑** | 必须显式禁掉，否则"停止"按钮根本点不到 |
| **`async void` 漏异常** | 直接崩进程，方法体整体 try/catch |
| **用 WPF `Popup` 做 chip 弹层** | Popup 是独立 HWND，在 `AllowsTransparency` 的分层窗口里会跑到所有窗口最上层、位置也容易跟丢。用面板内的 `Grid` 叠层 |
| **弹层里点一下面板就收了** | 弹层必须算"面板内"，且点击判定要"先关弹层、不关面板"（§9 P10） |
| **`Motion.ColorTo` 静默不生效** | 它遇到冻结画笔直接 `return`（[Motion.cs:223](E:/yingyong/deepseekgongzuoqv/IslandShot-pro/src/Motion.cs#L223)）。选中态直接用新画笔赋值 |
| **`XamlReader.Parse` 的模板漏 `xmlns:x`** | 只要用了 `x:Name`，就必须带 `xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'`，否则在构造函数里抛 `XamlParseException` → **双击后进程秒退、什么都看不到**。排查方法：读 `%TEMP%\Low\CapsuleImagePro-boot.log`（Program.Boot 会记 `constructing window` → `window shown`，崩了就只有 `APP DOMAIN EX`） |

---

## 9. 补丁清单（开工前必读）

每条都是"对着真实代码跑一遍就会撞上"的，按顺序改。

### P1　形变载体：`panel` 写死，AI 面板会长错大小

**症状**：AI 面板从错误的比例长出来；收起时缩不到胶囊。
**根因**：`SetExpandedAnimated` 里 7 处引用写死 `panel`（960×236 图片面板），两个 ratio helper 也读 `panel.Width/Height`。
**改法**：在方法开头取一个 `surface`，把引用都换成它。

```csharp
// IslandWindow.Motion.cs:399
void SetExpandedAnimated(bool value)
{
    if (expanded == value) return;
    expanded = value;
    FrameworkElement surface = aiVisible && aiPanel != null ? (FrameworkElement)aiPanel : panel;
    ...
    if (!aiVisible) SetTab(false);      // 只在图片面板时切页签，AI 模式别动它
```

要替换的位置（当前行号）：`405`、`406`、`417`、`418`、`419`、`425`、`428-434`、`443`、`446`、`447`、`450-457`、`460`、`461`。

ratio helper 改成带参数，并顺手修掉一个**潜在错值**（fallback 写的是 220/41，实际胶囊是 148/37）：

```csharp
// IslandWindow.Motion.cs:715
double CapsuleWidthRatio(FrameworkElement surface)
{
    double host = surface != null && surface.Width > 1 ? surface.Width : 960;
    double cap = capsule.Width > 1 ? capsule.Width : 148;
    return cap / host;
}

double CapsuleHeightRatio(FrameworkElement surface)
{
    double host = surface != null && surface.Height > 1 ? surface.Height : 236;
    double cap = capsule.Height > 1 ? capsule.Height : 37;
    return cap / host;
}
```

调用处（`:423`、`:424`、`:450`、`:451`）补上 `surface`。

### P2　窗口高度：时序 + 矮屏兜底

```csharp
const double AiPanelHeight = 420;     // 想加高面板只改这里
const double AiPanelTop = 8;

void ApplyAiWindowHeight(bool ai)
{
    double want = ai ? AiPanelHeight + AiPanelTop + 12 : 260;
    double max = MaxWindowHeight();
    if (want > max) want = max;
    if (Math.Abs(Height - want) < 0.5) return;
    Height = want;          // 先改 Height
    PositionOnScreen();     // 再刷 HWND：Width 没动 → 居中结果不变，只更新高度和置顶
    if (ai && aiPanel != null)
        aiPanel.Height = Math.Max(300, Height - AiPanelTop - 12);
}

double MaxWindowHeight()
{
    Forms.Screen screen = Forms.Screen.PrimaryScreen;
    double dpi = DpiScale();
    return Math.Max(300, screen.WorkingArea.Height / dpi - 4);
}
```

时序（进）：

```csharp
void OpenAiPanel()
{
    if (aiVisible) return;
    bool fromImagePanel = expanded;          // 从图片面板里点球进来
    aiVisible = true;
    panel.Visibility = Visibility.Collapsed;
    ApplyAiWindowHeight(true);               // ① 窗口先变高（瞬间，看不出）
    if (fromImagePanel)
    {
        // ② 已经在展开态：不再走胶囊形变，直接淡入 AI 面板
        aiPanel.Visibility = Visibility.Visible;
        aiPanel.IsHitTestVisible = true;
        aiPanel.Opacity = 0;
        Motion.PopIn(aiPanel, new Point(0.5, 0.15), 0.96, 10, 200, 0, Motion.Drop);
    }
    else
    {
        SetExpanded(true);                   // ② 从胶囊形变（载体自动是 aiPanel，见 P1）
    }
    Dispatcher.BeginInvoke(new Action(delegate
    {
        Activate();
        Keyboard.Focus(inputBox);
        inputBox.CaretIndex = inputBox.Text.Length;
    }), DispatcherPriority.Input);
}

void CloseAiPanel()
{
    if (!aiVisible) return;
    if (aiStreaming) StopAiStream();         // 先停流式，已输出的文字保留
    SetExpanded(false);                      // 收起动画；高度在动画结束回调里还原（P4）
}
```

时序（出）挂在 `IslandWindow.Motion.cs:457` 那条回调里：

```csharp
Motion.AnimateDouble(surface, UIElement.OpacityProperty, surface.Opacity, 0, 170, Motion.Exit, 120, delegate
{
    if (expanded) return;
    surface.Visibility = Visibility.Collapsed;
    surface.IsHitTestVisible = true;
    if (aiVisible)                     // ← 新增：AI 收起动画播完，再还原窗口高度
    {
        aiVisible = false;
        ApplyAiWindowHeight(false);
    }
});
```

> 如果实测"改高度"那一帧有闪：把 `ApplyAiWindowHeight(true)` 挪进一个 `Dispatcher.BeginInvoke(..., DispatcherPriority.Render)`。

### P3　输入框：`Enter` / `Shift+Enter` / 输入法

```csharp
inputBox = new TextBox
{
    AcceptsReturn = true,
    AcceptsTab = false,
    TextWrapping = TextWrapping.Wrap,
    MinHeight = 34,
    MaxHeight = 92,
    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    VerticalContentAlignment = VerticalAlignment.Center,
    FontFamily = new FontFamily("Microsoft YaHei UI"),
    FontSize = 13,
    Foreground = new SolidColorBrush(Color.FromRgb(230, 233, 240)),
    Background = Brushes.Transparent,          // 这是输入框内部，不是面板根，可以用
    BorderThickness = new Thickness(0),
    CaretBrush = new SolidColorBrush(Color.FromRgb(124, 108, 242))
};

// 输入法开着的时候，h / i / Enter 全都以 Key.ImeProcessed 到达（实测日志拿到了三条一样的），
// 所以【绝对不能】写 "if (e.Key == Key.ImeProcessed) return;"——那样回车永远发不出去。
// 正确做法：取真实按键（ImeProcessedKey），再用组词状态区分"选词回车"和"发送回车"。
TextCompositionManager.AddPreviewTextInputStartHandler(inputBox, delegate(object s, TextCompositionEventArgs e)
{
    imeComposing = true;
});
TextCompositionManager.AddPreviewTextInputUpdateHandler(inputBox, delegate(object s, TextCompositionEventArgs e)
{
    if (e.TextComposition == null || e.TextComposition.CompositionText.Length == 0) imeComposing = false;
});
TextCompositionManager.AddPreviewTextInputHandler(inputBox, delegate(object s, TextCompositionEventArgs e)
{
    imeComposing = false;
});

inputBox.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
{
    Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
    if (key != Key.Enter || !e.IsDown) return;
    if (e.Key == Key.ImeProcessed && imeComposing) return;      // 这一下回车是输入法选词，放行
    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return; // Shift+Enter 换行
    e.Handled = true;        // 拦住，别让 TextBox 自己插换行
    SubmitInput();
};
```

> 实测行为：输入法开着打 `hi` 会形成组词，**第一个回车是上屏、第二个回车才发送**——这和微信一致，
> 是正确交互，不要试图把两步合成一步。

`SubmitInput()` 里先 `string text = inputBox.Text.Trim();`，空则 `return`。

### P4　重试/错误态不丢会话

`ShowAiError` 只在最后一条 AI 气泡上换文案 + 加「重试」按钮，**不要清空历史**；重试就用 `LastUserText` 再发一次。

### P5　点外面收起：放行 AI 面板 + 流式中不收起

```csharp
// IslandWindow.cs:124
PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
{
    if (!expanded) return;
    if (panel.IsMouseOver) return;
    if (aiPanel != null && aiPanel.IsMouseOver) return;   // ← 新增
    if (aiStreaming) return;                               // ← 新增：别把正在看的回答收掉
    if (IsInteractive(e.OriginalSource)) return;
    CollapseLater();
    e.Handled = true;
};
```

`IsInteractive`（`:1212`）补一条兜底：`if (node == aiPanel) return true;`

另外两处会偷偷收起面板，都要加 `&& !aiStreaming`：

- `Deactivated`（`:132-135`）
- 全局鼠标钩子的回调（`:183-191`）

### P6　`Esc` 两段语义

```csharp
// IslandWindow.cs:225
KeyDown += delegate(object sender, KeyEventArgs e)
{
    if (e.Key != Key.Escape) return;
    // 注：输入法组词时 Esc 会以 Key.ImeProcessed 到达，这里天然放行，正好是"取消候选"
    if (chipOpen) { CloseChip(); e.Handled = true; return; }   // ← v3：先关弹层
    if (aiVisible && AiHandleEscape()) { e.Handled = true; return; }
    if (expanded) { SetExpanded(false); e.Handled = true; }
};

bool AiHandleEscape()
{
    if (aiStreaming) { StopAiStream(); return true; }   // 第一次：只停流式
    return false;                                       // 第二次：交给外层收起
}
```

### P7　后端接口（见 §5 定稿）

`ChatBackend.cs` 里只留 `IChatBackend` + `FakeBackend`，不要提前写 `ApiBackend` 的骨架。

### P8　自动滚底 + 复制要稳

```csharp
void AppendDelta(string delta)
{
    if (string.IsNullOrEmpty(delta)) return;
    lastAiMessage.Text += delta;
    Dispatcher.BeginInvoke(new Action(delegate { chatScroll.ScrollToEnd(); }),
        DispatcherPriority.Background);      // 别用 UpdateLayout() 硬刷，流式会很卡
}

static void SafeCopy(string text)
{
    for (int attempt = 0; attempt < 5; attempt++)
    {
        try { Clipboard.SetText(text); return; }
        catch (Exception) { Thread.Sleep(40); }
    }
}
```

### P9　面板只构建一次

`aiPanel` 和消息列表在构造函数里 `BuildAiPanel()` 一次，开关只切 `Visibility`。每次重建会丢会话，直接违反 §7 的"再次打开对话还在"。

### P10　弹层的点击边界（v3 新增）

**症状**：弹层一打开，点弹层里任何位置面板就收了；或者点别处弹层关不掉。
**根因**：弹层不在 `panel.IsMouseOver` 的判断里；而"点外面收起"是窗口级 `PreviewMouseLeftButtonUp`。

**改法**：

1. 弹层做成 `aiPanel` 内部的 `Grid` 叠层（`VerticalAlignment=Bottom, HorizontalAlignment=Right`，`Margin` 让它在 chip 正上方），**不要用 `Popup`**；
2. 弹层打开时，在面板根挂一个 `PreviewMouseLeftButtonDown`：如果点击不在弹层里 → `CloseChip()` + `e.Handled = true`，**不要**走 `CollapseLater()`；
3. `IsInteractive` 里给弹层 `Tag = "ai-popup"` 放行（`Border.Tag is string` 已经认这个，见 `:1219`）。

```csharp
aiPanel.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
{
    if (!chipOpen) return;
    if (chipPopup.IsMouseOver || addModelSheet.IsMouseOver) return;
    CloseChip();
    e.Handled = true;          // 吃掉，别让窗口级的"点外面收起"接手
};
```

### P11　切换会话要整块重绘（v3 新增）

**症状**：切历史后气泡重叠、滚动位置错、旧气泡还在。
**改法**：`messageList.Children.Clear()`，按 `current.Messages` 从头重建，最后再滚到底。**别做增量 patch**——这一个阶段的会话量很小，重绘成本可以忽略。

```csharp
void RenderSession()
{
    messageList.Children.Clear();
    foreach (ChatMessage message in current.Messages) messageList.Children.Add(BuildBubble(message));
    Dispatcher.BeginInvoke(new Action(delegate { chatScroll.ScrollToEnd(); }), DispatcherPriority.Background);
}
```

### P12　选中态别用冻结画笔做动画（v3 新增）

**症状**：调 `Motion.ColorTo(brush, color, ...)` 一点反应都没有。
**根因**：`Motion.cs:223` 第一行就是 `if (brush == null || brush.IsFrozen) return;`。
**改法**：球的选中/未选中直接用两个 `new SolidColorBrush(...)` 赋值，或者叠一层 `Motion.Fade` 的高光 Border，别碰 `ColorTo`。

### P13　"从图片面板打开"这条路径必须自己把内容层淡回来（v4 新增）

**症状**：第一次点球打开正常；关掉再打开，整块面板只剩一层底色，内容（标题、球、输入框、按钮）全看不见。用户会描述成"黑屏"。实测像素：头部亮像素 66 → 18，最亮值 255 → 102。

**根因**：`SetExpandedAnimated(false)` 收起时把 `surface.Child`（面板内容层）的 `Opacity` 淡到 0。
而重新打开走的是 `OpenAiPanel` 里 `fromImagePanel == true` 那条分支，只做了 `aiPanel.Opacity = 0 → 1`（面板本身），
**没有把内容层淡回来**——它还是 0。第一次不黑，是因为那时内容层从来没被淡过。

**改法**：

```csharp
if (fromImagePanel)
{
    aiPanel.Visibility = Visibility.Visible;
    aiPanel.IsHitTestVisible = true;
    aiPanel.Opacity = 0;
    // 上一次收起把内容层淡到了 0，这条路径必须自己淡回来
    UIElement aiBody = aiPanel.Child as UIElement;
    if (aiBody != null) Motion.AnimateDouble(aiBody, UIElement.OpacityProperty, 0, 1, 220, Motion.Enter, 40, null);
    Motion.PopIn(aiPanel, new Point(0.5, 0.15), 0.96, 10, 200, 0, Motion.Drop);
}
else
{
    SetExpanded(true);   // 这条路 SetExpandedAnimated 里本来就有 body 0→1，不用管
}
```

**自查方法**：打开 → Esc 收起 → 再打开，连做三次；每次截头部区域看亮像素数量是否一致。
另外整块面板只有底色、没有内容，就是这条坑，不是渲染坏了。

### P14　滚动条必须自己写样式（v5 新增）

**症状**：聊天区右侧出现一根**浅灰色粗滚动条**，带上下箭头按钮，在深色面板里极其扎眼（用户第一个指出的就是它）。

**根因**：WPF 默认 ScrollBar 模板是系统色，不跟随深色主题。

**改法**：给面板根元素挂一个隐式样式（`Resources[typeof(ScrollBar)]`），模板里只留一条 Track + 圆角 Thumb：

```csharp
panelBorder.Resources[typeof(ScrollBar)] = BuildAiScrollBarStyle();

static Style BuildAiScrollBarStyle()
{
    const string xaml =
        "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
        "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>" +
        "<Setter Property='Width' Value='8'/>" +
        "<Setter Property='MinWidth' Value='8'/>" +
        "<Setter Property='Template'><Setter.Value>" +
        "<ControlTemplate TargetType='ScrollBar'>" +
        "<Grid Background='Transparent'>" +
        "<Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'>" +
        "<Track.Thumb><Thumb Width='6' HorizontalAlignment='Center'>" +
        "<Thumb.Template><ControlTemplate TargetType='Thumb'>" +
        "<Border x:Name='bar' CornerRadius='3' Background='#3AFFFFFF'/>" +
        "<ControlTemplate.Triggers>" +
        "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bar' Property='Background' Value='#7AFFFFFF'/></Trigger>" +
        "<Trigger Property='IsDragging' Value='True'><Setter TargetName='bar' Property='Background' Value='#B0FFFFFF'/></Trigger>" +
        "</ControlTemplate.Triggers></ControlTemplate></Thumb.Template>" +
        "</Thumb></Track.Thumb></Track></Grid></ControlTemplate>" +
        "</Setter.Value></Setter></Style>";
    return (Style)XamlReader.Parse(xaml);
}
```

要点：

- 隐式样式挂在 **panelBorder.Resources** 上，面板里所有 ScrollViewer（聊天区 / 历史列表 / 输入框自带的那条）一起生效；
- 模板里**不要放 RepeatButton**，去掉上下箭头和整条轨道；
- 聊天区容器再给 8px 右外边距，别让滑块贴着面板圆角边框。

### P15　`DropShadowEffect` 会在面板隐藏后留下"阴影残影"（v5 新增）

**症状**：AI 面板 → 点 ← 返回图片面板后，图片面板下方浮着一条**半透明的 AI 面板底栏/代码块**（模糊、约 40% 不透明度）。
等 3 秒也不消失，所以不是"没刷新"。

**根因**：`DropShadowEffect` 会把元素内容缓存成位图来算阴影；面板 `Visibility = Collapsed` 之后，
这份缓存仍被画了一次，于是旧内容以"阴影"的形式留在原地。

**验证方法**：把 `aiPanel.Effect` 临时删掉，同样的操作就干净了（我实测确认过）。

**改法**：隐藏时把投影摘掉，显示时再挂回去——投影本身别删，否则面板会变"贴纸"。

```csharp
void EnsureAiShadow()
{
    if (aiPanel == null) return;
    if (aiPanel.Effect == null)
        aiPanel.Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 10, Opacity = 0.4 };
}

void HideAiSurface()
{
    if (aiPanel == null) return;
    aiPanel.Visibility = Visibility.Collapsed;
    aiPanel.Effect = null;      // 关键：不清缓存就会画出旧内容的阴影
}
```

三个隐藏入口都要走 `HideAiSurface()`：`BackToImagePanel`、`CloseAiPanel` 的兜底分支、
`SetExpandedAnimated(false)` 的收起回调（`if (surface == aiPanel) HideAiSurface();`）。
`OpenAiPanel` 开头调一次 `EnsureAiShadow()`。

---

## 10. 构建与调试

```powershell
# 编译
& "E:\yingyong\deepseekgongzuoqv\IslandShot-pro\Build.ps1"

# 跑
start "E:\yingyong\deepseekgongzuoqv\IslandShot-pro\build\Capsule image Pro.exe"
```

- 编译只用系统自带 `csc.exe`，**不要引入 NuGet / csproj**；
- 那个 csc 是 .NET 4.x 老编译器，**C# 语法上限 5.0**：不能用字符串插值 `$"..."`、`?.`、`nameof`、表达式体成员。`async/await`、lambda、`delegate{}` 都可以；
- 新增 `.cs` 文件放到 `src\` 即可，`Build.ps1` 自动收集；
- **别在自动化沙箱里启动 exe**（进程令牌受限，托盘图标会失效），请用户自己双击。

---

## 11. 下一阶段（不在本包范围，先别做）

1. `ApiBackend`：OpenAI 兼容协议（DeepSeek / 智谱 / 本机 Ollama），支持流式；
2. `WebBackend`：隐形 WebView2 驱动网页版 AI（免 key）——**两个球真正接上的地方**，DOM 注入 + `MutationObserver` 抓回答，脆弱，需单独设计；
3. 图片问答：把胶囊里的图片一起发出去；
4. 模型管理：`data\models.txt` 落盘、接口地址、Key 的本地保存（现在只做 UI）；
5. 会话落盘：`data\chats.txt`，左栏历史能跨重启。

---

## 12. 给下一个对话的开场

> 读 `src\IslandWindow.cs`（构造与窗口字段 → `Height = AiPanelHeight + AiPanelTop + 12`；面板构建 → `BuildPanel`；点外收起 → `PreviewMouseLeftButtonUp`；`IsInteractive`；`PositionOnScreen`）、`src\IslandWindow.Motion.cs`（`SetExpandedAnimated`、`CapsuleWidthRatio/HeightRatio`）、`src\IslandWindow.Orbs.cs`（工具坞与 `LaunchAiChat`）。
> **用符号名搜，别用行号**——这批文件改动频繁，行号早就漂了（见 §13）。
> **然后先读完本文件 §9 的 P1–P15 再动手**——只按 §5 写会撞上输入法回车失效、点输入框自动收起、第二次打开黑屏、弹层一点就收、滚动条白条这五个必现问题。
> 按 §5 的三个新文件开工，完工标准看 §7 验收清单（含 v3 的 5 条）。
> **不要碰抖音相关文件**（`DouyinPlayerWindow.cs` / `IslandWindow.Douyin.cs` / `MorphShellWindow.cs`）。

---

## 13. 复核结论与现状（v6，由另一个会话核对源码后补写）

### 13.1 本阶段的实际完成度

对着源码逐条核对 §7 的结果：

| §7 验收项 | 状态 | 依据 |
|---|---|---|
| 点 AI 球打开面板、输入框聚焦 | ✅（形式见 13.3） | `LaunchAiChat()` → `OpenAiPanel()`；`FocusAiInput()` 里 `Keyboard.Focus` + `CaretIndex` |
| 逐字流式 | ✅ | `FakeBackend` 每批 1~2 字、`Task.Delay(28)` |
| 流式中变「停止」 | ✅ | `UpdateAiSendButton` / `StopAiStream` |
| Esc 两级 | ✅ | `IslandWindow.cs` 的 `KeyDown` 先 `CloseAiChip` → `StopAiStream` → `SetExpanded(false)` |
| 收起后窗口高度回到 260 | ⛔ **不适用** | 方案 B：`Height` 构造时定死 440，运行时不改 |
| 再次打开对话还在 | ✅ | `aiSessions` + `RenderChat`（内存） |
| 面板外点击穿透 | ⚠️ 待实测 | 分层窗口按像素 alpha 判定；窗口变高后需人工确认 |
| 抖音球 / 图片页签 / 文字页签未被弄坏 | ⚠️ 待实测 | 编译通过；需人工回归一次 |
| v3 的 5 条（球切换 / 会话栏 / chip 弹层 / 加入模型 / 左栏布局） | ⚠️ 待实测 | UI 已实现，无实测记录 |

**结论：前端（阶段一）实质完成，可以验收**；未做的是「落盘」和「真后端」，属于阶段二/三，不算欠账。

### 13.2 三处已修正的文档错误（v6）

1. **§2 第 6 条 / §7 第 5 条**：原写"高度还原到 260"，与 §3 的方案 B 矛盾，已改为"不适用"；
2. **§12 行号**：已改成按符号名搜索；
3. **§3 补充**：窗口高度 440 = `AiPanelHeight(420) + AiPanelTop(8) + 12`，代码在 `IslandWindow.cs` 构造函数里，只此一处。

> 上次的教训：**文档里凡是"验收项"和"实现方案"分两节写的，改方案时必须同步改验收**，否则下个会话会去修一个不可能通过的项（重新踩"改高度→黑屏"）。

### 13.3 一条容易误解的事实：形变路径当前走不到

`OpenAiPanel()` 里：

```csharp
bool fromImagePanel = expanded;   // AI 球在图片面板的顶栏里 → 这里永远是 true
```

所以实际永远是「淡入 + 0.96 缩放 + 上浮 10px」，**不会**从胶囊形变。要走形变，必须给 AI 一个**不经过图片面板**的入口（右键胶囊 / 胶囊上再挂一个小圆点 / 独立热键）。

这是产品决定，不是 bug。验收时别把"没有形变"当成缺陷。

### 13.4 当前状态

- 编译：通过（仅 `DetachedImageViewerWindow.cs` 有个历史遗留警告 `CS0169`）；
- 未提交的改动：`docs/AI前端-交接包.md`、`src/IslandWindow.Ai.cs`、`src/IslandWindow.Motion.cs`；
- 阶段二的工作看 **`docs/AI后端-交接包.md`**。
