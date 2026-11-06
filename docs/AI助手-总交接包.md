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
  没有字符串插值、没有 `?.`、没有 `nameof`、**不能在 catch 里 await**（踩过，见 §6.B.1）。
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
| 取回答 | 解析 SSE | `.ds-markdown.ds-assistant-message-main-content` 第 N 个 | 消息列表里"最后一条有文字的行"（见 §6.D.8~6.19，这里连踩三个坑） |
| 结束判定 | `[DONE]` | 文字 1.5 秒不变 | 同左 |
| 需要 key | 是 | 否（用网页登录态） | 否 |

工厂在 `ChatBackends.Resolve(model, effort)`：先看 `web://` 前缀 → `WebBackend`；否则 HTTP。

### 3.1 图片（只有网页版支持）

**选图流程（2026-11-06 按用户要求重做）**：

```
图片面板右键卡片 →「发送到 AI」
   ↓  不跳转，进入"选图模式"
点卡片 → 右上角按顺序出现号码牌 1 / 2 / 3（再点取消，最多 4 张）
   ↓
点右下角浮条 [发送到 AI]（旁边显示"已选 N 张"，还有 [取消]）
   ↓
这时才切到 AI 面板：图片挂到输入框上方（42×42 缩略图 + × 移除）
   ↓ 当前若是 API 模型，自动切到第一个网页版模型
点发送 → 用户气泡里先摆图片缩略图再摆文字；历史里也带着
```

| 项 | 位置 |
|---|---|
| 选图状态 / 号码牌 / 浮条 | `IslandWindow.Ai.cs`：`BeginAiPick` / `ToggleAiPick` / `UpdateAiPickUi` / `ConfirmAiPick` |
| 卡片上的号码牌 | `IslandWindow.cs` `AddImage`：卡片内容包一层 `Grid`，右上角一个 `Border` 徽章 |
| 点卡片拦截拖拽 | 卡片 `PreviewMouseLeftButtonDown/Up` 在选图模式下 `e.Handled = true` |
| 浮条 | 图片面板 `content` 里的右下角 `aiPickBar` |

真正发出去时：**只有网页版模型能发图片**。做法是 CDP 塞文件：

```
DOM.enable
DOM.getDocument {"depth":0}                 → root nodeId
DOM.querySelector {"nodeId":root,"selector":"input[type=file]"}  → file nodeId
DOM.setFileInputFiles {"nodeId":file,"files":["E:/path/a.png"]}
```

- 两个站点都只有一个隐藏的 `input[type=file]`，`accept` 里都含 png/jpeg/webp。
- 普通 JS 改不了 file input 的值（浏览器安全限制），必须走 CDP。
- 塞完文件等 2.2 秒再打字发送（等缩略图出来）。
- **API 模型带图会被拦住并提示**（`OpenAiCompatBackend` 没有视觉能力）。

**落盘**：用户消息的图片以 `[img]路径` 行写在正文前面（行格式不变，旧数据兼容），
读的时候 `ChatStore.SplitPayload` 再拆回 `ChatMessage.Images`。文件被删了气泡里显示"图片已删除"。

实测（2026-11-06）：DS 网页版和豆包网页版都能对着图片回答识别类问题；3 张图一起发也能正确描述。

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

### `build\data\webthreads.txt`

```
# sessionId|site|url
s20261106191238|doubao|https://www.doubao.com/chat/38452001244968194
s20261106191734|deepseek|https://chat.deepseek.com/a/chat/s/443fdc4b-...
```

- **胶囊会话 ↔ 网页会话的绑定表**。同一个胶囊会话（哪怕关机重启）永远回到同一个网页对话，
  不会在对方账号里堆出一堆碎片会话。
- 绑定发生在第一条消息发出去之后（URL 从 `/chat/` 变成 `/chat/<id>` 时才记）。
- 删除胶囊会话时：先 `Take()` 取出绑定 → 让网页端把那条对话**归档** → 再删本地记录。
- 归档只按"链接 `href` 完全等于这条会话 URL"来匹配，不靠标题猜（猜错会动到别人的记录）。
  找不到归档按钮时（比如站点改版）只提示"需要手动删"，不会乱点。

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
| **胶囊会话绑定一个网页会话**（重启也回同一条） | `WebThreadStore` + `WebChatWindow.EnsureThreadAsync` |
| 删除胶囊会话时**顺手归档网页端那条** | `DeleteAiSession` → `WebBackend.TryArchiveAsync` |

---

## 6. 踩过的坑（按层分组，重做时照这个躲）

**先看这张速查表**，别从第一条读到最后：

| 现象 | 去这组 |
|---|---|
| 启动崩 / 打开是空的 / 回车没反应 / 有灰影 / 窗口偏了 / 滚动作妖 | **6.A 前端** |
| 编译不过（尤其用了 await / 字符串插值） | **6.B 语言层** |
| API 报错、限流 | **6.C 后端** |
| 网页版没反应 / 一直"思考中" / 内容串台 / 隐藏窗口闪 | **6.D 网页版驱动** |

---

### 6.A 前端（WPF / 交互）

#### A.1 XamlReader 崩溃
**现象**：启动就 `XamlParseException: "x" 是未声明的前缀`。
**原因**：`XamlReader.Parse` 的字符串模板必须带 `xmlns:x`（`BuildAiSendButton` / `AiGhostButton` 两个模板被修过）。

#### A.2 第二次打开面板全是黑的
**现象**：第一次正常，第二次打开内容区空的。
**原因**：收起动画把内容层 `Opacity` 停在了 0。修法：从图片面板切过来时把 body 从 0 动画到 1。

#### A.3 回车永远发不出去
**现象**：中文输入法开着，按 Enter 没反应。
**原因**：输入法激活时**所有**按键都以 `Key.ImeProcessed` 到达。要用 `e.ImeProcessedKey`，
并用 `TextCompositionManager` 跟踪 `aiImeComposing`：第一下回车是选词，第二下才发送。

#### A.4 返回图片面板后留一层灰影
**原因**：`DropShadowEffect` 被 WPF 缓存住了。修法：`HideAiSurface()` 里 `Visibility=Collapsed` + `Effect=null`，
每次打开重新 `EnsureAiShadow()`。

#### A.5 窗口不在屏幕正中
**原因**：`PositionOnScreen` 里有个 `CapsuleOffsetX = 150`。已置 0。

#### A.6 动效不能动布局属性
**约定**：`Motion` 只碰 `Opacity` / `RenderTransform`。改 Width/Height/Margin 会掉帧、还会和命中测试打架。
推理等级滑块的填充用的是 `ScaleTransform.ScaleX`，旋钮用的是 `TranslateTransform.X`——就是按这条来的。

#### A.7 点 DS 球还是发给智谱
**现象**：点了 DeepSeek 球，chip 还是"智谱"，发出去还是智谱（正好限流，收到 429）。
**原因**：球和模型是两套状态，`SetAiProvider` 只改了高亮。
**修法**：球 → 找/建 `web://<id>` 模型 → `ApplyAiModel`；高亮反过来由模型推导。
**教训**：凡是"看起来是选择器"的控件，必须和真正发请求用的状态是同一个源。
> 同类的坑：选图模式的号码牌也要和"真正要发的那份列表"同源（`aiPickOrder` 单一真身）。

#### A.8 长回答一直被拽到底部
**现象**：回答很长时，视图一直跳在最底部，想从头看必须一边等一边往上滑。

**两次修法（第一版不够）**：
1. 先加了"只在用户本来就在底部附近才跟随"（`IsAiChatNearBottom`）—— 但刚发完问题用户就在底部，
   所以还是会一路跟到末尾。
2. 最终方案：**流式过程中完全不跟随**。发完把"这条提问"的顶部对齐到可视区顶部
   （`ScrollAiToMessageTop`），回答在下面长，你从头读；到底部靠手动滚或点「↓ 最新」。
   另外 `RenderChat()` 重建气泡时会**保留原滚动偏移**（回答结束会走这里，不保留就会"一跳到底"）。

---

### 6.B 编译 / 语言层（csc 4.x = C# 5）

#### B.1 catch 里不能 await
**现象**：编译报 `CS1985: 无法在 catch 子句中等待`。
**修法**：把异常存到局部变量，出了 catch 再 await 再 throw。

#### B.2 其它 C# 5 限制（写代码前先记住）
没有字符串插值（`$"..."`）、没有 `?.`、没有 `nameof`、没有表达式体成员、没有 out 变量。
`async/await` 和 lambda 是有的。

---

### 6.C 后端 / 配置

#### C.1 智谱 429
免费档限流很猛（`1302 账户速率限制` / `1305 模型访问量过大`），后端会重试 2 次（1.5s / 3.5s）后报错。
**不要**指望它当日常主力；DeepSeek 更稳。

#### C.2 推理等级没生效
`models.txt` 第 7 列没写 `effort` 的话，滑块不会进请求体（这是故意的默认值，最安全）。
智谱那列留空是因为当时整站 429，没法验证它接不接受。

---

### 6.D 网页版驱动（WebBackend）

#### D.1 网页版登录判定永远为真
**现象**：没登录时也"正常"往下走，然后报"找不到输入框"。
**原因**：脚本返回 `"0"`/`"1"` 字符串，代码写成 `!= null` 判断（`"0"` 也是非 null）。
**现在是** `== "1"`，并且在判定前给页面最多 20 秒加载时间（否则会误判、隐藏窗口闪一下）。

#### D.2 豆包回答里混进上一轮的提问（旧方案，已被 D.8 取代）
**现象**：问"你是豆包吗"，回答却是"你是什么模型我是豆包…"。
**原因**：取"消息列表最后一行"——豆包会把被提问的那句在回答里重放一遍，且行结构会变。
**当时的修法**：发送前给整段对话拍快照，之后只取**新增尾巴**；再剥掉开头对提问的回显。
> 快照法在长对话里会失效，最终方案见 **D.8**；但"剥掉开头回显提问"这一步保留着。

#### D.3 豆包回答只出几个字就停（旧方案）
**原因**：文本比对要求"新文本严格以快照开头"，页面在别处插入内容（时间分隔、推荐）就整段丢弃。
**当时的修法**：改成**最长公共前缀**；公共前缀过短（< 快照一半）就先不推，等稳定。
> 同 D.8：最终方案不再做整段前缀比对。

#### D.4 状态行被当成回答
`正在搜索` / `正在思考` / `深度思考中` 这几个整段丢弃（`DbBusyHints`）。
判定放宽成：长度 ≤ 14 且含「正在 / 搜索中 / 思考中」都算状态（不然"正在搜索图片"这种变体会漏）。
另外豆包联网搜索会在正文前塞 `搜索 N 个关键词，参考 N 篇资料` 或 `找到 N 篇资料`，用正则连剥最多 3 层。

#### D.5 推荐尾巴
回答末尾挂的 `相关视频 / 相关推荐 / 相关问题`（连同后面的话题标签）整段切掉（`TrimTailNoise`）。

#### D.6 Esc 之后网页还在生成
**现象**：按 Esc 只停了我们这边的读取，网页继续写；下一条发出去先推"上一条的剩余内容"。
**修法**：
- 用户中断时调用 `StopPageAsync()`，点掉网页的停止按钮；
- 每次发送**之前**也先点一次（生成中就停掉；空闲时输入框是空的，点一下不会发出空消息）。
- 不要用"文本不再变化"判空闲——豆包的文本一直在变，永远判不出来（踩过，白等 15 秒）。

#### D.7 WebView2 不 Show 就不加载
隐藏窗口也必须 `window.Show()`，然后立刻 `DwmSetWindowAttribute(hwnd, 13, 1)` 做 DWM cloak + 挪到 (-4000,-4000)。
登录需要时临时揭盖（`Reveal`），登录完再盖回去。

#### D.8 豆包"思考很久"其实是读不到内容（三个坑叠在一起）

**现象**：发完问题气泡一直「思考中…」，实际豆包早就答完了；
`webdriver.log` 里 25 秒 80 次轮询**全是 `len=0`**，最后报"120 秒没有新内容"。

三个坑，按踩到的顺序：

1. **列表选错**：页面上"推荐气泡"的容器类名也含 `message-list`
   （`suggest-message-list-wrapper-XXXX`）。`querySelector('[class*=message-list]')` 或
   "子元素最多"的挑法，在**空会话**时会被推荐气泡（3 个子元素）赢走 → 一直在读推荐列表。
   **修法**：优先 `[class^="message-list"],[class*=" message-list"]`，抓不到再退回"子元素最多"。
2. **空行干扰**：豆包在消息之间插空行（`height:0` / 只有动作条），
   按"最后一行"取会取到空行 → 读出来是空。
   **修法**：从后往前找**第一条"删掉 action-bar / suggest 后仍有文字"的行**。
3. **门控错了**：豆包是**"一轮一个块"**（提问和回答在同一个块里），
   等"块数 +2"永远等不到。**修法**：发送前记下最后一块的文本，
   之后**只要文本变了**就算新内容，再把开头回显的提问剥掉。

**教训**：网页版"读不到内容"要先用日志区分**三种**情况——
`type=NO-INPUT`（没登录/没加载完）、`send=NO-SEND-BUTTON`（改版）、
`len` 一直是 0（**选择器或取法错了**）。第三种最容易误判成"对方慢"。

#### D.10 网页会话不绑定 → 网页端堆一堆碎片

**现象**：胶囊里明明是同一个会话，但去网页版看，这些问题是**好几条不同的对话**；
用久了网页端累积一堆没人管的历史。

**原因**：隐藏窗口每次启动都停在站点根，豆包/DS 会当成新对话；
我们发完也从没记过"这条消息落在哪个会话 URL 里"。

**修法**：
- 发完之后抓 `CoreWebView2.Source`（`/chat/<id>` 这种带 id 的才算数）→ 存进 `webthreads.txt`
  （`sessionId|site|url`）；
- 下次同一个胶囊会话发送前，先 `Navigate` 回那条 URL，等历史渲染完再发；
- 会话没绑定时，先点站点自己的「新对话 / 开启新对话」再发，避免写进上一条对话里。

**踩过的细节**：刚点完发送那一瞬间 URL 还是 `/chat/`（没有 id），
第一次抓到的就是它 —— 现在用 `LooksLikeThreadUrl()` 只认"最后一段长度 ≥ 6"的地址，
并且在轮询里每 5 次再抓一遍。

#### D.11 删了胶囊会话，网页端还在

**现象**：胶囊里删掉会话，去网页版看那条对话还在。

**修法**：删会话时 `WebThreadStore.Take()` 取出绑定 → 回站点根 →
按 **`href` 完全相等**找到那条会话 → 点它自己的「归档对话」（`aria-label`）→ 再删本地记录。

**为什么用归档而不是删除**：归档可撤销；而且绝不用标题匹配（猜错就动到别人的记录）。
找不到按钮时只提示"需要手动删"，不乱点。
#### D.12 图片上传必须走 CDP
普通 JS 改不了 `<input type=file>` 的值（浏览器安全限制）。必须 `DOM.setFileInputFiles`。
详见 §3.1。

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
13. **图片全流程**：图片面板右键卡片 →「发送到 AI」→ **停在图片面板**、点几张出现 1/2/3 号码牌 →
    点右下角浮条 [发送到 AI] → 切到 AI 面板、图片挂在输入框上方（当前是 API 模型的话会自动切网页版）→
    发送 → 用户气泡里能看到那几张图 → 豆包/DS 网页版回答能描述图片内容
14. **等待提示**：发一句后、第一个字出来之前，气泡里应该有「• 思考中…」，出字后自动消失
15. **滚动**：问题让回答很长，流式中往上滚 → 视图**不被拽回底部**，底部出现「↓ 最新」；
    点它回到底部
16. **豆包取文本不能空转**：问一句短的（比如 "1+1"），看 `build\webdriver.log`，
    应该几秒内出现 `poll N len=` 后面**非 0**；如果 20 秒还全是 `len=0`，按 §6.D.8 查三个坑
17. **网页会话绑定**：用豆包问一句 → `webthreads.txt` 出现一行带 `/chat/<id>` →
    同一会话再问一句，日志里**不应**出现 `new chat`（说明没开新对话）→
    **重启程序**后再问一句，日志里应出现 `restore web thread <同一 URL>`
18. **删除时归档**：删掉上面那条会话 → 日志出现 `archive thread: archived` 或 `not-found`，
    `webthreads.txt` 里那行被清掉
19. **一键清空图片**：图片面板右上角「清空图片」→ 第一下变「确认清空?」→ 3.5 秒内再点 →
    卡片清空、`data\images` 和 `data\screenshots` 里的文件被删、空状态提示出现

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
