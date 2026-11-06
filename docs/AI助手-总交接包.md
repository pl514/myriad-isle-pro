# AI 助手面板 —— 总交接包

> 写于 2026-11-06 收盘。前情：`docs/AI前端-交接包.md`（界面规格）、`docs/AI后端-交接包.md`（接口与配置）、
> `docs/AI网页驱动-交接包.md`（网页版驱动）。本文件是**站在今天的状态上重写的一份总览**，
> 三份旧文件里过时的部分以本文件为准。

---

## 0. 一句话现状

胶囊里那套 AI 面板已经能用：**多会话 + 落盘 + 删除**、**两路 API（智谱免费 / DeepSeek）**、
**两路网页版驱动（DeepSeek / 豆包，实验性）**、**推理等级滑块（对 DeepSeek 真实生效）**、
**流式 + 中断 + 错误气泡**。编译通过，三个后端都端到端实测过。

**代码干净**：`git status` 应无输出；当前 HEAD `3858c55`（本文件那次提交），里程碑标签 `v2.0-ai`。

---

## 1. 编译与运行

```powershell
# 编译（csc 直接编译，无 csproj / 无 NuGet）
cd E:\yingyong\deepseekgongzuoqv\IslandShot-pro
.\Build.ps1

# 运行
.\build\Capsule image Pro.exe
```

- 编译器是 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，**C# 5**。
  没有字符串插值、没有 `?.`、没有 `nameof`、**不能在 catch 里 await**（踩过，见 §6.9）。
  `async/await` 和 lambda 是有的。
- 新增引用要改 `Build.ps1` 的 `$refs`（现在有 `System.Net.Http`、`System.Security`、vendor 的 WebView2）。
- `csc` 的临时目录被 `Build.ps1` 指到项目内 `.tmp`，避免沙箱 `%TEMP%` 不可写。

---

## 2. 文件地图

> 行数是 `v2.0-ai` 时刻的**粗略值**，只用来判断“这个文件大概多大”，别拿来当定位依据；定位请搜符号名。

| 文件 | 行数 | 职责 |
|---|---|---|
| `src\IslandWindow.Ai.cs` | 1933 | **AI 面板全部 UI**：构建、会话、渲染、发送、chip 弹层、模型表单、注意事项卡片 |
| `src\IslandWindow.cs` | 2042 | 主窗口：展开/收起、Esc 分发、热键、右键菜单、窗口定位 |
| `src\IslandWindow.Motion.cs` | 858 | 展开/收起动画（`SetExpandedAnimated`）、胶囊形变 |
| `src\IslandWindow.Orbs.cs` | 392 | 图片面板顶部那排小球（音乐 / AI / 抖音），点 AI 球 → `OpenAiPanel()` |
| `src\WebBackend.cs` | 563 | **网页版驱动**：隐藏 WebView2 窗口 + 站点表 + 取回答 |
| `src\OpenAiCompatBackend.cs` | 307 | OpenAI 兼容流式后端 + `ChatBackends.Resolve` 工厂 |
| `src\AiConfig.cs` | 218 | models.txt / keys.txt 读写（DPAPI 加密） |
| `src\ChatStore.cs` | 138 | chats.txt 落盘（追加 + 整体重写删除） |
| `src\ChatMessage.cs` | 33 | `ChatMessage` / `ChatSession` 数据类 |
| `src\ChatBackend.cs` | 51 | `IChatBackend` 接口（**不要改**） |
| `src\Motion.cs` | 326 | 动效工具（只动 Opacity / RenderTransform） |

配置文件（都在 `build\data\`）：`models.txt`、`keys.txt`、`chats.txt`、`settings.txt`。

---

## 3. 三个后端

`IChatBackend` 只有一句话：`Task SendAsync(string text, Action<string> onDelta, CancellationToken token)`。
增量推送、正常结束直接返回、出错抛异常（前端显示成错误气泡）。三个实现：

| | `OpenAiCompatBackend` | 网页版 DeepSeek | 网页版豆包 |
|---|---|---|---|
| 触发条件 | `baseUrl` 是 http(s) | `models.txt` 的 baseUrl 写 `web://deepseek` | `web://doubao` |
| 输入 | HTTP 请求 | `textarea` + 原生 setter + `InputEvent` | `div.tiptap.ProseMirror` + `execCommand('insertText')` |
| 取回答 | 解析 SSE | `.ds-markdown.ds-assistant-message-main-content` 第 N 个 | 整段对话快照 + 取新增尾巴 |
| 结束判定 | `[DONE]` | 文字 1.5 秒不变 | 同左 |
| 需要 key | 是 | 否（用网页登录态） | 否 |

工厂在 `ChatBackends.Resolve(model, effort)`：先看 `web://` 前缀 → `WebBackend`；否则 HTTP。

**节流**：同一站点两次发送之间至少 3 秒（`WebBackend.LastSendAt`），且全进程单窗口。

---

## 4. 配置格式（改这几个文件就能改行为）

### `build\data\models.txt`

```
# id|label|baseUrl|model|keyRef|vision|reasoning
zhipu|智谱 GLM-4.7-Flash（免费）|https://open.bigmodel.cn/api/paas/v4|glm-4.7-flash|zhipu|false|
deepseek|DeepSeek Flash|https://api.deepseek.com|deepseek-flash|deepseek|true|effort
deepseek-web|DeepSeek 网页版（实验）|web://deepseek|deepseek||false|
doubao-web|豆包网页版（实验）|web://doubao|doubao||false|
```

- 第 6 列 `vision`：给以后的多模态留的，现在没用到。
- **第 7 列 `reasoning`**：`effort` = 请求体里带 `reasoning_effort`；空 = 不传（默认）。
  五档滑块映射 `minimal / low / medium / high / xhigh`。
- `id` 不能重复；`keyRef` 空 = 这个模型不需要 key（网页版）。
- 第一次点网页版小球时，会自动往这里补一条 `*-web` 记录。

### `build\data\keys.txt`

```
zhipu|AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA...   ← DPAPI 密文（CurrentUser）
deepseek|plain:sk-xxxx                       ← 调试用明文
```

- 密文用 `ProtectedData.Protect(..., CurrentUser)`，换用户/换机器解不开。
- 面板里 chip → 编辑当前模型 可以改 key（写入时自动加密）。

### `build\data\chats.txt`

```
# sessionId|title|role|base64(text)|timestamp
s20261106132436da7b|撒盐apple|user|5pKk55uQYXBwbGU=|1793945076
s20261106132436da7b|撒盐apple|ai|6JCo...|1793945076
```

- 一条回答**结束**后，把这轮未落盘的消息按顺序追加（`ChatMessage.Persisted` 防重复）。
- 删除会话走 `SaveAll()` 整体重写，盘上不留孤儿行。
- 上限：50 会话 × 每会话 200 条，超出从最旧的丢。
- **被 Esc 中断的半截回答也会落盘**（它是真实消息）。

---

## 5. 行为规则（都已经实现，别改回去）

| 规则 | 位置 |
|---|---|
| AI 面板展开时，**点空白处不收起**，只有 × / Esc 才收 | `IslandWindow.PreviewMouseLeftButtonUp` 里 `if (aiVisible) return` |
| 收起再展开，**回到上次的界面**（AI 还是图片）；图片/文字面板仍默认回图片 | `aiLastSurface` |
| 点网页版小球 = **直接切模型**（chip 立刻变），不是只改高亮 | `SetAiProvider` |
| 球的高亮完全由"当前模型是不是 web://"推导 | `UpdateAiOrbSelection` |
| Esc 优先级：注意事项 > 加入模型表单 > chip 弹层 > 停止生成 > 收起面板 | `IslandWindow.KeyDown` |
| 历史会话右键 → 删除 | `BuildAiSessionMenu` |
| 输入法开着时，第一次回车是选词、第二次才发送 | `aiImeComposing` + `TextCompositionManager` |
| 流式中再点发送 = 先停再发 | `SubmitAiInput` |

---

## 6. 踩过的坑（按"现象 → 原因"写，重做时照这个躲）

### 6.1 XamlReader 崩溃
**现象**：启动就 `XamlParseException: "x" 是未声明的前缀`。
**原因**：`XamlReader.Parse` 的字符串模板必须带 `xmlns:x`（`BuildAiSendButton` / `AiGhostButton` 两个模板被修过）。

### 6.2 第二次打开面板全是黑的
**现象**：第一次正常，第二次打开内容区空的。
**原因**：收起动画把内容层 `Opacity` 停在了 0。修法：从图片面板切过来时把 body 从 0 动画到 1。

### 6.3 回车永远发不出去
**现象**：中文输入法开着，按 Enter 没反应。
**原因**：输入法激活时**所有**按键都以 `Key.ImeProcessed` 到达。要用 `e.ImeProcessedKey`，
并用 `TextCompositionManager` 跟踪 `aiImeComposing`：第一下回车是选词，第二下才发送。

### 6.4 返回图片面板后留一层灰影
**原因**：`DropShadowEffect` 被 WPF 缓存住了。修法：`HideAiSurface()` 里 `Visibility=Collapsed` + `Effect=null`，每次打开重新 `EnsureAiShadow()`。

### 6.5 窗口不在屏幕正中
**原因**：`PositionOnScreen` 里有个 `CapsuleOffsetX = 150`。已置 0。

### 6.6 动效不能动布局属性
**约定**：`Motion` 只碰 `Opacity` / `RenderTransform`。改 Width/Height/Margin 会掉帧、还会和命中测试打架。
推理等级滑块的填充用的是 `ScaleTransform.ScaleX`，旋钮用的是 `TranslateTransform.X`——就是按这条来的。

### 6.7 点 DS 球还是发给智谱
**现象**：点了 DeepSeek 球，chip 还是"智谱"，发出去还是智谱（正好限流，收到 429）。
**原因**：球和模型是两套状态，`SetAiProvider` 只改了高亮。
**修法**：球 → 找/建 `web://<id>` 模型 → `ApplyAiModel`；高亮反过来由模型推导。
**教训**：凡是"看起来是选择器"的控件，必须和真正发请求用的状态是同一个源。

### 6.8 网页版登录判定永远为真
**现象**：没登录时也"正常"往下走，然后报"找不到输入框"。
**原因**：脚本返回 `"0"`/`"1"` 字符串，代码写成 `!= null` 判断（`"0"` 也是非 null）。
**现在是** `== "1"`，并且在判定前给页面最多 20 秒加载时间（否则会误判、隐藏窗口闪一下）。

### 6.9 catch 里不能 await
**现象**：编译报 `CS1985: 无法在 catch 子句中等待`。
**修法**：把异常存到局部变量，出了 catch 再 await 再 throw。

### 6.10 豆包回答里混进上一轮的提问
**现象**：问"你是豆包吗"，回答却是"你是什么模型我是豆包…"。
**原因**：取"消息列表最后一行"——豆包会把被提问的那句在回答里重放一遍，且行结构会变。
**修法**：发送前给整段对话拍快照，之后只取**新增尾巴**；再剥掉开头对提问的回显。

### 6.11 豆包回答只出几个字就停
**原因**：文本比对要求"新文本严格以快照开头"，页面在别处插入内容（时间分隔、推荐）就整段丢弃。
**修法**：改成**最长公共前缀**；公共前缀过短（< 快照一半）就先不推，等稳定。

### 6.12 状态行被当成回答
`正在搜索` / `正在思考` / `深度思考中` 这几个整段丢弃（`DbBusyHints`）。
另外豆包联网搜索会在正文前塞 `搜索 N 个关键词，参考 N 篇资料` 或 `找到 N 篇资料`，用正则连剥最多 3 层。

### 6.13 推荐尾巴
回答末尾挂的 `相关视频 / 相关推荐 / 相关问题`（连同后面的话题标签）整段切掉（`TrimTailNoise`）。

### 6.14 Esc 之后网页还在生成
**现象**：按 Esc 只停了我们这边的读取，网页继续写；下一条发出去先推"上一条的剩余内容"。
**修法**：
- 用户中断时调用 `StopPageAsync()`，点掉网页的停止按钮；
- 每次发送**之前**也先点一次（生成中就停掉；空闲时输入框是空的，点一下不会发出空消息）。
- 不要用"文本不再变化"判空闲——豆包的文本一直在变，永远判不出来（踩过，白等 15 秒）。

### 6.15 WebView2 不 Show 就不加载
隐藏窗口也必须 `window.Show()`，然后立刻 `DwmSetWindowAttribute(hwnd, 13, 1)` 做 DWM cloak + 挪到 (-4000,-4000)。
登录需要时临时揭盖（`Reveal`），登录完再盖回去。

### 6.16 429
智谱免费档限流很猛（`1302 账户速率限制` / `1305 模型访问量过大`），后端会重试 2 次（1.5s / 3.5s）后报错。
**不要**指望它当日常主力；DeepSeek 更稳。

---

## 7. 验收清单（改完代码照着跑一遍）

1. 启动 → 点胶囊 → 图片面板正常，顶部 `图片 / 文字 / ⋯` 正常
2. 点 `⋯` → 三个球展开 → 点 ✦（AI）→ AI 面板打开
3. 发一句 → 有流式回答；回答结束后 `data\chats.txt` 多两行
4. 关闭程序重开 → 历史会话还在（点开能看到上一条对话）
5. 右键历史项 → 删除 → 界面回空会话、`chats.txt` 只剩表头
6. 点 chip → 弹层里拖滑块 → 标签跟着变（低/中/高/极高/最高）
7. chip → 模型 → 列表里当前项有紫点、没 key 的标橙字
8. 点 D 球 → chip 变「DeepSeek 网页版（实验）」→ 发一句 → 有回答（隐藏窗口不可见）
9. 点豆包球 → 同上；回答里不应出现"正在搜索 / 找到 N 篇资料 / 相关视频"
10. 回答中途按 Esc → 停止；再问一句 → 新回答不串旧内容
11. 点 `!` 球 → 注意事项卡片；Esc 关卡片、面板保留
12. AI 面板展开时点面板外 → **不收起**

---

## 8. 已知限制（别当成 bug 报）

- 网页版是**灰色地带**：自动化操作网页，可能被风控。默认只在用户主动点球时启用，节流 3 秒。
- 网页改版就废：类名一变 → 报"网页版没找到发送按钮（可能改版了）"，不会崩。
- 豆包 Esc 后网页端还会忙一阵；现在用"先点停止"绕过，最多多等 0.5 秒。
- 豆包联网搜索的引用（`抖音百科` 之类）会混在正文里，没做精细清理。
- 推理等级对**智谱**没验证（限流挡着），所以 `models.txt` 里智谱那列是空的。
- 网页版回答不区分来源，落盘后和 API 回答长得一样（`ChatMessage` 里没记后端类型）。

---

## 9. 建议的下一步（按性价比排）

1. **实测几天再动**：现在功能是全的，继续加东西之前先让用户用一用，把"实际别扭的地方"攒出来。
2. 消息里记后端类型（`api / web:deepseek / web:doubao`），历史里能看出这条是哪来的。
3. 豆包引用/话题标签的精细清理（现在是整段切）。
4. 推理等级对智谱做一次验证（等它不限流），确认 `thinking` 还是 `reasoning_effort`。
5. 网页版登录态过期的引导（现在只会揭盖 + 等 3 分钟）。
6. **不建议**做：自动重试失败的网页版请求（会放大风控）。

---

## 10. 日志与诊断

| 文件 | 内容 |
|---|---|
| `build\webdriver.log` | 网页版每次请求：站点、快照长度、发送结果、每次轮询拿到的字数、停止动作 |
| `%TEMP%\Low\CapsuleImagePro-boot.log` | 启动流程 |
| `build\CapsuleImagePro-error.log` | HTTP 失败、重试、异常链 |
| `E:\deepseek1\aiweb-probe\FINDINGS.md` | 网页版探针的原始实测记录（项目外，参考用） |

网页版出问题的第一诊断步骤：看 `webdriver.log` 里
`type=` / `send=` / `poll N len=` 三个值——
- `type=NO-INPUT` → 没登录或页面没加载完；
- `send=NO-SEND-BUTTON` → 改版了；
- `len` 一直是 0 → 选择器废了或者被限流。

---

## 11. 不要动的东西

- `src\DouyinPlayerWindow.cs`、`src\IslandWindow.Douyin.cs`、`src\MorphShellWindow.cs`（抖音那套，独立功能）
- `src\ChatBackend.cs` 的 `IChatBackend` 接口（三个后端共用，改了要一起改）
- `src\Motion.cs` 的语义（只动 Opacity / RenderTransform）
- `OpenAiCompatBackend` 里**不能出现 `ConfigureAwait(false)`**（onDelta 必须在 UI 线程上，跨线程改 TextBlock 会崩）
