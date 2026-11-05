# 交接包：Capsule image Pro —— AI 面板（第一阶段：只做前端）

> 写于 2026-11-05。给下一个对话用：读完这份就能直接开工，不需要翻旧聊天记录。
>
> **一句话**：在胶囊窗口里做一个**原生 WPF** 的 AI 对话面板——点 AI 球 → 面板从胶囊长出 → 输入问题 → 流式回答。**这一阶段只做界面和假数据，不接任何真实模型。**

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
- AI 球现在是占位：`LaunchAiChat()` 只弹一个托盘气泡；
- 每个球有运行态绿点：`SetToolRunning(tool, true/false)`。

**这一阶段的入口改造只有一行**：把 `LaunchAiChat()` 换成打开 AI 面板。

### 当前 git 状态（回退用）

```
6a0c3fd  fix: 隐藏播放器右侧播上一条/下一条黑条     ← 最新
v0.7-no-right-strip / v0.6-hide-right-strip / v0.5-layout-modes
v0.4-no-crop / v0.3-no-white-flash / v0.2-douyin-morph / v0.1-douyin-working
```

回退：`git -C "E:\yingyong\deepseekgongzuoqv\IslandShot-pro" reset --hard v0.7-no-right-strip`

---

## 2. 这一阶段的目标（严格限定范围）

### 要做
1. 点 AI 球 → 面板从胶囊形变展开、输入框自动获得焦点；
2. 输入文字 → `Enter` 发送；
3. **假后端**（`FakeBackend`）：把固定文本按 20~40ms 一个字的节奏流式吐出来，用来验证渲染和动画；
4. 流式过程中输入框变成「停止」按钮；
5. 消息列表：用户气泡靠右、AI 气泡靠左，可滚动、自动滚到底；
6. `Esc` / 点面板外 / 面板右上 `×` → 收起面板，高度还原；
7. 会话存在内存里（这一阶段不落盘也可以）。

### 明确不做（下一阶段的活）
- ❌ 真实 API、key、模型选择
- ❌ 网页版驱动（隐形 WebView2 + DOM 注入）
- ❌ 图片问答 / 上传
- ❌ 多会话、历史搜索

---

## 3. 关键工程决策：窗口高度

**问题**：现在的窗口是 `1000 × 260`，面板只有 `960 × 236`。一个能用的聊天面板至少要 700×420，**装不下**。

**方案（推荐）**：按需改窗口高度，但**不做动画**。

```
收起态：窗口 1000×260   （现状）
AI 态 ：窗口 1000×620
```

进入 AI 模式时：
1. **先瞬间**把窗口 `Height` 改成 620（多出来的区域是全透明的，视觉上看不出来）；
2. 再让 AI 面板在里面做「从胶囊长出来」的动画（复用 `Motion.cs` 的缓动）；
3. 退出时反过来：先收面板动画，动画结束再把窗口高度还原成 260。

要点：

- `Width` **不要动**。`PositionOnScreen()` 用窗口宽高算居中，宽度变了一切定位逻辑都要重算。
- 高度变化后要 `SetWindowPos` 保证置顶不被破坏。
- 多出来的透明区域**不要**设 `Background = Brushes.Transparent`（会吃掉点击），根元素留 `null` 或只给真正的内容上色。
- **备选方案 B**：窗口永远开成 1000×620，只画顶部需要的部分。透明区仍然点击穿透。如果方案 A 有抖动就退到 B。

---

## 4. 界面规格

```
┌──────────────────────────────────────────────┐
│  AI 助手                    [新对话] [×]      │  ← 顶栏 40，可拖动？不用，固定
├──────────────────────────────────────────────┤
│                                              │
│   ┌────────────────────────────┐             │
│   │ 用户消息（右侧，紫底）       │             │
│   └────────────────────────────┘             │
│   ┌──────────────────────────────┐           │
│   │ AI 消息（左侧，深底 + 边框）   │           │
│   └──────────────────────────────┘           │
│                                              │
├──────────────────────────────────────────────┤
│  [ 输入你的问题…            ]  [发送 / 停止]  │  ← 底栏 56
└──────────────────────────────────────────────┘
```

| 项 | 值 |
|---|---|
| 面板尺寸 | 宽 760 × 高 420（窗口 1000×620，居中，`Margin.Top = 8`） |
| 圆角 | 30（和现有图片面板一致） |
| 背景 | 复用图片面板的渐变：`#262C33` → `#1B2129`，透明度 240/242 |
| 字体 | `Microsoft YaHei UI`，正文 13，用户消息 13，代码 12 等宽 |
| 气泡圆角 | 14，内边距 12×8，最大宽度 86% |
| 动画 | 展开 320ms（`Motion.Enter`），收起 200ms（`Motion.Exit`） |

颜色沿用现有体系：主色 `#7C6CF2`，正文 `#E6E9F0`，次要文字 `#A7A0C4`。

---

## 5. 代码结构建议

```
src/ChatBackend.cs        IChatBackend 接口 + FakeBackend 实现
src/ChatMessage.cs        Role(用户/AI) + Text + 状态
src/IslandWindow.Ai.cs    partial：面板构建、状态机、流式渲染
```

### 后端接口（这一阶段只有假实现）

```csharp
internal interface IChatBackend
{
    // 逐段回调；结束时 onDone；出错时 onError
    void Send(string text, Action<string> onDelta, Action onDone, Action<string> onError, CancellationToken token);
    void Cancel();
}
```

`FakeBackend` 用一个 `DispatcherTimer` 每 30ms 吐 1~2 个字，文本写死一段带代码块的回答（顺便验证代码块渲染）。

> 下一阶段换成 `ApiBackend`（OpenAI 兼容）和 `WebBackend`（隐形 WebView2）时，前端一行不用改——这正是现在就要把接口定下来的原因。

### 状态机

```
IDLE ──(回车)──► STREAMING ──(done)──► IDLE
                    │
                    └──(点停止/再按 Esc 一次)──► IDLE
```

---

## 6. 交互细节（照做，别自由发挥）

| 情况 | 行为 |
|---|---|
| 面板打开 | 输入框 `Focus()`，光标在末尾 |
| `Enter` | 发送；输入框清空 |
| `Shift+Enter` | 换行（用 `AcceptsReturn`） |
| 流式中按 `Enter` | 忽略 |
| 流式中按 `Esc` | **先停止流式**，不收起面板 |
| 非流式中按 `Esc` | 收起面板 |
| 点面板外 | 收起（沿用现有 `PreviewMouseLeftButtonUp` 逻辑，注意别和工具坞的球冲突） |
| 消息为空时发送 | 忽略 |
| 流式出错误 | AI 气泡里显示错误文案 + 「重试」按钮 |
| 回答完成 | 气泡下方出现「复制」按钮（`Clipboard.SetText`） |

---

## 7. 验收清单

- [ ] 点 AI 球，面板从胶囊位置长出，输入框已聚焦
- [ ] 输入一段中文，回车后逐字出现回答，滚动条自动跟到底
- [ ] 流式中输入框变成「停止」，点击后立刻停住，已输出的文字保留
- [ ] `Esc` 第一次停止、第二次收起
- [ ] 收起后窗口高度**精确**回到 260，胶囊位置不变
- [ ] 再次打开，上一轮对话还在（内存即可）
- [ ] 面板外的桌面区域点击正常穿透（不挡其它程序）
- [ ] 抖音球、图片页签、文字页签都**没有被这次改动弄坏**

---

## 8. 已经踩过的坑（别重复）

| 坑 | 结论 |
|---|---|
| 分层窗口里塞 WebView2 | 不可能，一点画面都没有。AI 前端必须原生 WPF |
| 用 `Brushes.Transparent` 当面板根背景 | 会吃掉点击，桌面点不动。用 `null` 或不设 |
| 动画改布局属性（Width/Height/Margin） | 现有的 `Motion` 只改 `Opacity` / `RenderTransform`，继续遵守 |
| 窗口 Resize 带 WebView2 | 会闪。AI 面板没有 WebView2，所以这里反而安全 |
| 窗口高度变化后位置漂移 | 只改 `Height` 不改 `Width`，`Top` 保持，居中逻辑不用动 |
| 工具坞的球被"点外面收起"误伤 | 球群和 AI 面板都算"可交互"，判断里要放行 |

---

## 9. 构建与调试

```powershell
# 编译
& "E:\yingyong\deepseekgongzuoqv\IslandShot-pro\Build.ps1"

# 跑
start "E:\yingyong\deepseekgongzuoqv\IslandShot-pro\build\Capsule image Pro.exe"
```

- 编译只用系统自带 `csc.exe`，**不要引入 NuGet / csproj**；
- 新增 `.cs` 文件放到 `src\` 即可，`Build.ps1` 自动收集；
- **别在自动化沙箱里启动 exe**（进程令牌受限，托盘图标会失效），请用户自己双击。

---

## 10. 下一阶段（不在本包范围，先别做）

1. `ApiBackend`：OpenAI 兼容协议（DeepSeek / 智谱 / 本机 Ollama），支持流式；
2. `WebBackend`：隐形 WebView2 驱动网页版 AI（免 key）——DOM 注入 + `MutationObserver` 抓回答，**脆弱，需单独设计**；
3. 图片问答：把胶囊里的图片一起发出去；
4. 设置面板：模型选择、温度、提示词。

---

## 11. 给下一个对话的开场

> 读 `E:\yingyong\deepseekgongzuoqv\IslandShot-pro\src\IslandWindow.cs`（窗口结构：87–229、面板 679–838、展开 839–843）、`IslandWindow.Motion.cs`（399–466 展开动画）、`IslandWindow.Orbs.cs`（工具坞与 `LaunchAiChat`），然后按本文件第 5 节的三个新文件开始做 AI 面板前端。
> 完工标准看第 7 节验收清单。**不要碰抖音相关文件**（`DouyinPlayerWindow.cs` / `IslandWindow.Douyin.cs` / `MorphShellWindow.cs`）。
