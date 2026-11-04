# Capsule image Pro

独立胶囊截图/图片工具。当前主程序源码在 `src`，构建产物固定为 `build\Capsule image Pro.exe`。

## 运行

双击 `启动 Capsule image Pro.cmd`，或直接双击 `build\Capsule image Pro.exe`。
**用普通用户身份运行即可，不要选「以管理员身份运行」。**

开机自启动在托盘菜单里开关（写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，
普通权限、不弹 UAC、不用计划任务）。

需要重新编译时双击 `构建.cmd`（已带 ExecutionPolicy Bypass，不需要改系统策略）。

> 从终端、沙箱或自动化工具里启动时，进程会继承那个环境的**低权限**令牌，
> 此时托盘图标和拖拽会被 Windows 拒绝。程序会弹一个说明窗告诉你原因；
> 请在桌面上双击启动。

## 首次使用（建议做一次，需要管理员）

旧版留了一个“最高权限”的开机自启动计划任务 `CapsuleImagePro`，登录时会以管理员身份
拉起旧版程序（还会占住单实例互斥体，让新副本提示“已有一个管理员实例在运行”）。
本版启动时会尝试自动删掉它，但**删除它需要管理员权限，普通权限删不掉**。
请在管理员 PowerShell / CMD 里执行一次：

```powershell
schtasks /Delete /TN "CapsuleImagePro" /F
```

> 只是想临时用一次也可以不删：登录后先在托盘右键退出那个管理员实例，再双击本副本即可。

## 与标准版 Capsule image 共存

这一版是独立的第二个应用，可以和标准版同时运行：

| 项目 | 标准版 | Pro 版 |
| --- | --- | --- |
| 进程名 | `Capsule image.exe` | `Capsule image Pro.exe` |
| 单实例互斥体 | `IslandShot.SingleInstance` | `CapsuleImagePro.SingleInstance` |
| 默认快捷键 | `Ctrl+Alt+Space` 等 | `Ctrl+Alt+Shift+Space` 等 |
| 开机自启动 Run 值 | `CapsuleImage` | `CapsuleImagePro` |
| 数据目录 | `build\data`（各自独立） | `build\data`（各自独立） |
| 胶囊位置 | 屏幕顶部正中 | 顶部正中向右 150px |

两个应用互不影响：各自的图片库、文字记录、快捷键、开机自启动都是分开的。
## 环境要求

- Windows 10 及以上
- 构建需要 .NET Framework 4.8 自带的 `csc.exe`（系统自带，无需安装 SDK）

## 为什么以前“必须用管理员运行”？

不是代码要求的，是**文件夹的完整性标签**。

2026-11-04 排查确认：`E:\yingyong\deepseekgongzuoqv` 这个目录被加上了
**低完整性标签（Low Mandatory Level）**，里面的每个文件（包括 exe）都继承它。
Windows 会用低完整性创建这种 exe 的进程，于是：

- `Shell_NotifyIcon` 被静默拒绝 → **托盘图标不出现**（返回 FALSE，不报错）；
- OLE 拖放被 UIPI 拦截 → **拖到微信 `effect=0`**，怎么放都放不进。

以管理员启动是 High 完整性，所以“看起来正常”。实证：

| exe 所在目录 | 进程完整性 | 托盘图标 |
| --- | --- | --- |
| `...\deepseekgongzuoqv\...`（带 Low 标签） | Low | 无 |
| `E:\yingyong\IslandShot-normal`（无标签） | 普通用户 | 有 |

解决办法二选一：

1. **把程序放在没有 Low 标签的目录**（本副本就在 `E:\yingyong\IslandShot-normal`，已验证普通权限下托盘正常）；
2. 或者用**管理员** PowerShell 一次性把标签改回 Medium：

   ```powershell
   icacls "E:\yingyong\deepseekgongzuoqv" /setintegritylevel (OI)(CI)M /T
   ```

   之后那个目录里的 exe 双击就是普通权限，重新编译出来的 exe 也不会再变 Low。

## 已知问题：游戏加加（GamePP）冲突
GamePP 会把 `GPP64.dll` 注入到 IslandShot。Windows 事件记录显示崩溃模块就是 `GPP64.dll`，不是岛窗自己的代码。
请先从托盘退出游戏加加（并关闭其开机启动），否则点击收起/展开可能整个进程崩溃。

## 当前可用

- 启动后在屏幕顶部显示胶囊，点击展开图片面板。
- 面板外任意位置点击、面板左上角 ×、`Ctrl+Alt+Space`、双击托盘图标都可收起回胶囊。
- 截图：`Ctrl+Alt+2` 当前窗口、`Ctrl+Alt+3` 全屏；截图自动添加到胶囊。区域截图仍未稳定，暂未接入。
- `添加图片`；把图片拖到胶囊附近并停留约 0.75 秒，胶囊展开后才允许放下，添加完自动收起；面板内 `Ctrl+V` 粘贴剪贴板图片。
- 卡片横向排列，滚轮横向滚动。
- 卡片右键菜单 `从胶囊移除`。
- 托盘图标：双击打开/收起，右键打开/退出。
- 导入的图片会复制到 `build\data\images\`，路径保存在 `build\data\images.txt`，重启后自动恢复。

## 新增：系统原生跨应用拖拽导出

在展开的面板里，鼠标按住任意卡片拖动（图片或视频），走的是 Windows 自带 OLE 拖放
（`ole32!DoDragDrop`），不是模拟鼠标/点击脚本。

- 拖到任意接受文件/图片的窗口（微信聊天框、画图、浏览器、Word/WPS、资源管理器……）松开即可放置。
- 视频卡片同样能拖：发的是真实文件路径（CF_HDROP），微信等程序会按视频文件接收，不会当成图片。
- 拖动过程中有图片缩略图跟随鼠标。
- 目标窗口可以接收时，缩略图出现绿色描边和「松开放置」提示。
- 按 `Esc`、或松手在不能放置的位置 = 取消：缩略图消失、卡片恢复原样，胶囊状态不变。
- 只发送「复制」效果（`DROPEFFECT_COPY`），目标端不会移动/删除胶囊里的源文件。
- 权限相关（重要）：托盘图标、拖到微信/资源管理器**都不需要管理员**。
  普通用户双击启动时进程是 Medium，这两个功能本来就正常。
  Windows 用 UIPI 隔离完整性级别：如果程序是被**低完整性**环境（沙箱、自动化工具、
  远程桌面里由受控进程拉起）启动的，`Shell_NotifyIcon` 会被静默拒绝 —— 表现为
  **托盘图标不出现**；拖放也会 `effect=0` —— 表现为**怎么都放不进微信**。
  这种情况下程序会弹一个不依赖托盘的说明窗，照提示在桌面上双击
  `启动 Capsule image Pro.cmd` 即可。反向同理：以管理员运行时，拖到同样以管理员运行
  的目标程序也会被拦，所以普通权限才是最省事的用法。

### 两套手势分开

| 位置 | 手势 | 行为 |
| --- | --- | --- |
| 胶囊本体 | 原地按下再松开（位移 < 4px） | 展开图片面板 |
| 胶囊本体 | 按住拖动（位移 ≥ 4px） | 移动胶囊窗口（`DragMove`，位置记到 `build\data\window.txt`） |
| 面板卡片 | 按住拖动（超过系统拖拽阈值） | 把卡片内容作为原生拖拽数据包导出（图片：CF_HDROP + DIB/PNG；视频：只用 CF_HDROP 文件路径） |
| 面板卡片 | 单击 / 右键 | 不触发导出；右键照旧弹出菜单 |

### 拖拽数据包里有什么

由 `src\NativeImageDrag.cs` 里的自定义 `IDataObject` 提供，全部走 HGLOBAL，可跨进程：

| 格式 | 内容 | 典型接收方 |
| --- | --- | --- |
| `CF_HDROP` | 胶囊内图片的真实文件路径 | 微信、资源管理器、浏览器文件框、Word/WPS |
| `CF_DIB` | 24 位位图（BITMAPINFOHEADER + BGR 像素） | 画图、Office、WPS |
| `PNG` / `image/png` | PNG 原始字节 | Chromium 系浏览器、支持图片剪贴板的程序 |
| `Preferred DropEffect` | `DROPEFFECT_COPY` | 资源管理器（显示为复制而不是移动） |

`IDropSource` 负责跟随系统拖拽循环：`GiveFeedback` 收到目标允许的效果时点亮缩略图，
`QueryContinueDrag` 处理松手 / `Esc` 取消。

### 验证

仓库内 `test\` 目录带三个自测工具：

```powershell
# 1) 数据包格式与字节自检（不弹窗）
& .\test\PayloadTest.exe .\build\data\images\sample.png

# 2) 独立进程 OLE 接收探针，再从胶囊卡片拖进它的窗口
Start-Process .\test\DropProbe.exe

# 3) 浏览器接收测试页（用 Chrome/Edge 打开后把卡片拖进页面）
#    test\drop-target.html
```

完整验收项见 [`docs\验收清单.md`](docs/验收清单.md)，实现细节见
[`docs\技术实现说明.md`](docs/技术实现说明.md)。

拖拽诊断日志：`%TEMP%\CapsuleImagePro-drag.log`（记录拖拽开始/结束、effect 与失败原因）。

## 下一阶段

- 截图：区域、窗口、全屏快捷键。
- 全屏 MP4 录制。
- 桌面钉图、悬浮相框、独立显示/隐藏热键。
- 图片编辑器。
- 托盘、开机启动、热键设置、应用内更新。
- 自动吸附工作区、多屏、贴边自动收起。

## 动效（WPF 原生）

胶囊、面板、页签、卡片、托盘菜单和滚轮滚动的动效都是 WPF 自带动画
（`DoubleAnimation` / `ColorAnimation` / 关键帧 + 缓动函数），没有引入任何前端库或 WebView。

- 只改 `Opacity`、`RenderTransform`、画笔颜色，不动布局属性，所以拖拽、右键拖出、
  剪贴板同步、热键、查看器的行为都不变。
- 动画结束会把终值写回基础值并清除动画，后续代码里直接赋值依旧生效。
- 想整体关掉：把 `src\Motion.cs` 里的 `allowed` 改成 `false`。

节奏、缓动、实现细节和验证方法见 [`docs\动效说明.md`](docs/动效说明.md)。

## 来源与许可

本项目在产品和交互设计上参考了 PicRope（图绳）：
https://github.com/Renly1994/PicRope

PicRope 原版权：Copyright (c) 2026 Alejandro Buján，MIT License。
上游 Tendedero：https://github.com/alejandrobujan/tendedero

完整第三方声明见 [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt)，
PicRope 的 MIT 许可全文见 [`licenses\PicRope-MIT.txt`](licenses/PicRope-MIT.txt)。

本项目使用独立名称、独立图标和独立 WPF 实现；没有使用 PicRope 的
Filerobot 编辑器、WebView2 容器或图标素材。