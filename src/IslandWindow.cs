using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace IslandShot
{
    internal sealed partial class IslandWindow : Window
    {
        const int WM_HOTKEY = 0x0312;
        const int WM_CLIPBOARDUPDATE = 0x031D;
        const int HotkeyToggle = 0x4A53;
        const int HotkeyRegion = 0x4A54;
        const int HotkeyWindow = 0x4A55;
        const int HotkeyScreen = 0x4A56;
        const int HotkeyImages = 0x4A57;
        const int HotkeyRecord = 0x4A58;
        const int HotkeyStopRecord = 0x4A59;
        const int HotkeyDouyin = 0x4A5A;
        const int HotkeyScreenTranslate = 0x4A5B;
        const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModNoRepeat = 0x4000;
        HotkeyBinding[] hotkeys;
        const uint VkSpace = 0x20, VkA = 0x41, Vk2 = 0x32, Vk3 = 0x33;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        readonly Border capsule;
        readonly Border panel;
        StackPanel cards;
        ScrollViewer scroller;
        TextBlock empty;
        ScrollViewer textScroller;
        StackPanel textList;
        TextBlock textEmpty;
        Button imageTab, textTab, textClear, imageClear;
        Button translateTab;
        bool showingTranslate;
        bool forceImageSurface;
        bool showingText;
        bool capsuleClosed;
        string lastClipboardHash = "";
        string lastClipboardText = "";
        readonly Forms.NotifyIcon tray;
        readonly string storePath;
        readonly string placementPath;
        readonly string textsPath;
        readonly List<string> clipboardTexts = new List<string>();

        bool expanded;
        bool capturing;
        bool suppressDeactivate;
        bool dragOut;
        bool exporting;
        bool dropArmed;
        bool lowPrivilegeWarned;
        Point capsuleStart;
        double capsuleDragLeft;
        double capsuleDragCursorX;
        bool capsulePressed;
        bool capsuleDoubleClick;
        readonly List<DependencyObject> capsuleMiniButtons = new List<DependencyObject>();
        bool capsuleMoved;
        DispatcherTimer dragTimer;
        DispatcherTimer collapseTimer;
        DispatcherTimer clipboardTimer;
        IntPtr handle;
        HwndSource source;
        TrayMenuWindow trayMenuWindow;
        MouseHookService mouseHook;
        ScreenRecorder recorder;
        readonly List<DetachedImageViewerWindow> detachedWindows = new List<DetachedImageViewerWindow>();

        public IslandWindow()
        {
            Width = 1000;
            // 方案 B（见交接包 §3）：窗口高度固定，运行时不再 resize。
            // 分层透明窗口改高度会让合成表面跟不上，表现为整块黑屏。
            // 多出来的部分是全透明像素，鼠标点击照常穿透到桌面。
            Height = AiPanelHeight + AiPanelTop + 12;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
#if OUTSIDE_CLICK_TEST
            Title = AppInfo.TitleWithVersion + " TEST";
#else
            Title = AppInfo.Title;
#endif
            AllowDrop = true;

            storePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "images.txt");
            placementPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "window.txt");
            textsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "texts.txt");
            AppSettings.Init(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"));
            LoadTextHistory();
            hotkeys = HotkeyStore.Load();

            var root = new Grid();
            capsule = BuildCapsule();
            panel = BuildPanel();
            aiPanel = BuildAiPanel();
            musicPanel = BuildMusicPanel();
            SetTab(false);
            RenderTextHistory();
            InitAiSessions();
            root.Children.Add(capsule);
            root.Children.Add(panel);
            root.Children.Add(aiPanel);
            root.Children.Add(musicPanel);
            Content = root;

            AttachCapsuleGestures();
            // 提示点击：窗口级隧道拦截，保证不会先被胶囊手势拿去展开面板
            PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!capsuleNoticeActive) return;
                MessengerLog("notice click -> " + capsuleNoticeApp);
                string app = capsuleNoticeApp;
                e.Handled = true;
                OpenMessengerApp(app);
                SuppressApp(app, 4000);
                ClearUnreadNotice();
            };

            PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!expanded) return;
                // AI / 音乐状态下点空白处不收起：只有 × 或 Esc 能关（用户明确要求）
                if (aiVisible || musicVisible) return;
                if (capsule.IsMouseOver) return;   // 点胶囊本身不收起
                if (panel.IsMouseOver) return;
                if (aiPanel != null && aiPanel.IsMouseOver) return;
                if (aiStreaming) return;
                if (IsInteractive(e.OriginalSource)) return;
                if (aiChipOpen) CloseAiChip();
                CollapseLater();
                e.Handled = true;
            };
            Deactivated += delegate
            {
                if (expanded && !aiVisible && !musicVisible && !aiStreaming && !capturing && !suppressDeactivate && !dragOut) SetExpanded(false);
            };
            PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (!expanded) return;
                if (aiVisible || musicVisible) return;   // 聊天区 / 歌单自己滚，别被图片面板的平滑滚动接管
                if (showingTranslate) return;            // 翻译页的输入框 / 译文 / 历史各自滚动
                if (showingText && textScroller != null)
                {
                    GlideText(-e.Delta);
                    e.Handled = true;
                    return;
                }
                GlideImages(-e.Delta);
                e.Handled = true;
            };
            DragEnter += HandleDragOver;
            DragOver += HandleDragOver;
            DragLeave += delegate
            {
                if (!expanded && !dropArmed) CancelDragTimer();
            };
            Drop += delegate(object sender, DragEventArgs e)
            {
                CancelDragTimer();
                if (dragOut) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { dropArmed = false; return; }
                if (!expanded && !dropArmed) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
                string[] dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
                Point dropAt = e.GetPosition(this);
                for (int i = 0; i < dropped.Length; i++) AcceptDroppedImage(dropped[i], dropAt);
                dropArmed = false;
                e.Handled = true;
                CollapseShortly(950);
            };

            // 托盘图标先不挂，等窗口真正显示出来（Loaded）再挂：
            // 登录自启动时资源管理器可能还没准备好，太早调用 Shell_NotifyIcon 会失败。
            tray = new Forms.NotifyIcon { Icon = LoadApplicationIcon(), Text = AppInfo.TitleWithVersion, Visible = false };
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs e)
            {
                if (e.Button == Forms.MouseButtons.Right) ShowTrayMenu();
            };
            tray.DoubleClick += delegate { SetExpanded(!expanded); };

            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                source = HwndSource.FromHwnd(handle);
                source.AddHook(WndProc);
                AddClipboardFormatListener(handle);
                mouseHook = new MouseHookService(
                    delegate { return handle; },
                    delegate
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (expanded && !dragOut && !aiVisible && !musicVisible && !aiStreaming) SetExpanded(false);
                        }));
                    });
                mouseHook.Start();
                int style = GetWindowLong(handle, -20);
                SetWindowLong(handle, -20, style | 0x80);
                PositionOnScreen();
                RegisterHotkeys();
            };
            Loaded += delegate
            {
                PositionOnScreen();
                SetExpanded(false);
                LoadImages();
                HandleClipboardUpdate();
                AttachTrayIcon();
                // 命令行直接打开配对二维码：MyriadIslePro.exe --pair
                try
                {
                    string[] startArgs = Environment.GetCommandLineArgs();
                    for (int a = 0; a < startArgs.Length; a++)
                    {
                        if (string.Equals(startArgs[a], "--pair", StringComparison.OrdinalIgnoreCase))
                        {
                            Dispatcher.BeginInvoke(new Action(delegate { OpenPhonePair(); }), DispatcherPriority.Background);
                            break;
                        }
                    }
                }
                catch (Exception) { }
                if (RuntimeAccess.IsLow) ShowLowPrivilegeNotice();
                // 顺手清掉旧版本留下的“最高权限”计划任务（普通权限下自启动就够了）。
                var cleanup = new Thread(delegate() { RuntimeAccess.RemoveLegacyStartupTask(); });
                cleanup.IsBackground = true;
                cleanup.Name = "MyriadIslePro.LegacyStartupCleanup";
                cleanup.Start();
            };
            Closed += delegate
            {
                if (handle != IntPtr.Zero)
                {
                    UnregisterHotkeys();
                    UnregisterStopRecording();
                    RemoveClipboardFormatListener(handle);
                    if (mouseHook != null) mouseHook.Dispose();
                }
                if (source != null) source.RemoveHook(WndProc);
                if (recorder != null && recorder.Recording) recorder.Stop();
                if (musicWebStageWindow != null) { try { musicWebStageWindow.Close(); } catch (Exception) { } }
                if (musicStageWindow != null) { try { musicStageWindow.Close(); } catch (Exception) { } }
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                MusicDebug("esc key=" + e.Key + " expanded=" + expanded + " music=" + musicVisible + " ai=" + aiVisible);
                if (e.Key != Key.Escape) return;
                if (musicStageWindow != null || musicWebStageWindow != null)
                {
                    CloseMusicStage();
                    e.Handled = true;
                    return;
                }
                if (aiVisible)
                {
                    if (AiPickMode) { CancelAiPick(); e.Handled = true; return; }
                    if (aiRulesSheet != null && aiRulesSheet.Visibility == Visibility.Visible) { CloseAiRules(); e.Handled = true; return; }
                    if (aiAddSheet != null && aiAddSheet.Visibility == Visibility.Visible) { CloseAiAddSheet(); e.Handled = true; return; }
                    if (aiChipOpen) { CloseAiChip(); e.Handled = true; return; }
                    if (aiStreaming) { StopAiStream(); e.Handled = true; return; }
                }
                if (musicVisible)
                {
                    // 已登录时那张卡只是"换平台"，Esc 先退卡；没登录时它就是主体，Esc 直接收面板
                    if (musicLoginOverlay != null && musicLoginOverlay.Visibility == Visibility.Visible
                        && MusicPlatforms.Find(musicPlatformId) != null)
                    {
                        HideMusicLoginPicker();
                        e.Handled = true;
                        return;
                    }
                    CloseMusicPanel();
                    e.Handled = true;
                    return;
                }
                if (expanded) { SetExpanded(false); e.Handled = true; }
            };
        }

        IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HotkeyToggle) ToggleCapsule();
                else if (id == HotkeyRegion) CaptureRegionAsync();
                else if (id == HotkeyWindow) CaptureWindowAsync();
                else if (id == HotkeyScreen) CaptureScreenAsync();
                else if (id == HotkeyImages) ToggleDetachedImages();
                else if (id == HotkeyRecord) ToggleRecording();
                else if (id == HotkeyStopRecord) StopRecording();
                else if (id == HotkeyDouyin) ToggleDouyinWindow();
                else if (id == HotkeyScreenTranslate) StartScreenTranslate();
                handled = true;
            }
            else if (message == WM_CLIPBOARDUPDATE)
            {
                HandleClipboardUpdate();
                handled = true;
            }
            return IntPtr.Zero;
        }

        static BitmapSource ToOpaque(BitmapSource source)
        {
            if (source == null) return null;
            try
            {
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
                converted.Freeze();
                return converted;
            }
            catch (Exception) { return source; }
        }

        static BitmapSource NormalizeCopy(BitmapSource source)
        {
            BitmapSource opaque = ToOpaque(source);
            if (opaque == null) return null;
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(opaque));
                using (var buffer = new MemoryStream())
                {
                    encoder.Save(buffer);
                    buffer.Position = 0;
                    var decoder = new PngBitmapDecoder(buffer, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapSource frame = decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
            }
            catch (Exception) { return opaque; }
        }

        static BitmapSource ReadClipboardImage(out string formatList)
        {
            formatList = "";
            Exception last = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var data = Clipboard.GetDataObject();
                    if (data == null) { Thread.Sleep(60); continue; }
                    foreach (string name in new[] { "PNG", "image/png" })
                    {
                        object value = null;
                        try { value = data.GetData(name); } catch (Exception) { }
                        var asBitmap = value as BitmapSource;
                        if (asBitmap != null) { asBitmap.Freeze(); return asBitmap; }
                        var stream = value as Stream;
                        var raw = value as byte[];
                        if (stream == null && raw == null) continue;
                        try
                        {
                            using (var memory = new MemoryStream())
                            {
                                if (stream != null)
                                {
                                    if (stream.CanSeek) stream.Position = 0;
                                    stream.CopyTo(memory);
                                }
                                else
                                {
                                    memory.Write(raw, 0, raw.Length);
                                }
                                memory.Position = 0;
                                var decoder = new PngBitmapDecoder(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                                BitmapSource frame = decoder.Frames[0];
                                frame.Freeze();
                                return frame;
                            }
                        }
                        catch (Exception) { }
                    }
                    bool hasBitmap = false;
                    try { hasBitmap = data.GetDataPresent(DataFormats.Bitmap); } catch (Exception) { }
                    if (hasBitmap || Clipboard.ContainsImage())
                    {
                        BitmapSource image = Clipboard.GetImage();
                        if (image != null) { image.Freeze(); return image; }
                    }
                    try { formatList = string.Join(", ", data.GetFormats()); } catch (Exception) { }
                    bool looksImage = formatList.IndexOf("PNG", StringComparison.OrdinalIgnoreCase) >= 0
                        || formatList.IndexOf("Bitmap", StringComparison.OrdinalIgnoreCase) >= 0
                        || formatList.IndexOf("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (looksImage && attempt < 4) { Thread.Sleep(80); continue; }
                    return null;
                }
                catch (Exception exception)
                {
                    last = exception;
                    Thread.Sleep(80);
                }
            }
            if (last != null) DragNative.Log("clipboard read failed: " + last.Message);
            return null;
        }

        // 剪贴板哈希落盘：否则每次重启都把剪贴板里那张旧图当成新图再存一遍
        static string ClipboardHashPath
        {
            get { return Path.Combine(AiConfigStore.DataDir, "clipboard-hash.txt"); }
        }

        static bool IsKnownClipboardHash(string hash)
        {
            try
            {
                if (string.IsNullOrEmpty(hash)) return false;
                string path = ClipboardHashPath;
                if (!File.Exists(path)) return false;
                foreach (string line in File.ReadAllLines(path))
                {
                    string text = line == null ? "" : line.Trim();
                    // 保留最近若干条，避免文件无限长
                    if (text.Length > 0 && string.Equals(text, hash, StringComparison.Ordinal)) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        static void SaveClipboardHash(string hash)
        {
            try
            {
                if (string.IsNullOrEmpty(hash)) return;
                string path = ClipboardHashPath;
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var lines = new List<string>();
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string text = line == null ? "" : line.Trim();
                        if (text.Length > 0 && lines.Count < 40) lines.Add(text);
                    }
                }
                lines.Insert(0, hash);
                File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        void HandleClipboardUpdate()
        {
            if (clipboardTimer == null)
            {
                clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                clipboardTimer.Tick += delegate
                {
                    clipboardTimer.Stop();
                    ProcessClipboard();
                };
            }
            clipboardTimer.Stop();
            clipboardTimer.Start();
        }

        void ProcessClipboard()
        {
            try
            {
                string formats;
                BitmapSource clipboardImage = ReadClipboardImage(out formats);
                if (clipboardImage != null)
                {
                    BitmapSource image = NormalizeCopy(clipboardImage);
                    if (image == null) return;
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    byte[] bytes;
                    using (var stream = new MemoryStream())
                    {
                        encoder.Save(stream);
                        bytes = stream.ToArray();
                    }
                    string hash;
                    using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(bytes));
                    if (hash == lastClipboardHash) return;
                    // 同一张图以前存过（换个会话/重启后也算）：不再重复落盘
                    if (IsKnownClipboardHash(hash)) return;
                    lastClipboardHash = hash;
                    SaveClipboardHash(hash);
                    string folder = Path.Combine(Path.GetDirectoryName(storePath), "screenshots");
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "剪贴板_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                    File.WriteAllBytes(path, bytes);
                    AcceptClipboardImage(path);
                    return;
                }
                if (Clipboard.ContainsFileDropList())
                {
                    bool added = false;
                    foreach (string file in Clipboard.GetFileDropList())
                    {
                        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) continue;
                        if (IsVideoFile(file)) { AddVideo(file); added = true; }
                        else { AddImage(file); added = true; }
                    }
                    if (added) return;
                }
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    lastClipboardText = text;
                    AddClipboardText(text);
                    return;
                }
                DragNative.Log("clipboard ignored formats: " + formats);
            }
            catch (Exception exception) { DragNative.Log("clipboard failed: " + exception.Message); }
        }

        void AddClipboardText(string text)
        {
            if (textList == null) return;
            text = text == null ? "" : text.Trim();
            if (text.Length == 0) return;
            clipboardTexts.RemoveAll(item => string.Equals(item, text, StringComparison.Ordinal));
            clipboardTexts.Insert(0, text);
            while (clipboardTexts.Count > 50) clipboardTexts.RemoveAt(clipboardTexts.Count - 1);
            SaveTextHistory();
            RenderTextHistory();
        }

        void RenderTextHistory()
        {
            if (textList == null) return;
            textList.Children.Clear();
            foreach (string raw in clipboardTexts)
            {
                string block = raw;
                if (block.Length > 2000) block = block.Substring(0, 2000) + "…";
                var content = new TextBlock
                {
                    Text = block,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(225, 221, 245)),
                    FontFamily = new FontFamily("Microsoft YaHei UI"),
                    FontSize = 12,
                    LineHeight = 19
                };
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(31, 27, 46)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(58, 49, 84)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 0, 0, 7),
                    Child = content,
                    Cursor = Cursors.Hand,
                    ToolTip = "点击复制这段文字"
                };
                string copy = raw;
                card.MouseLeftButtonUp += delegate { try { Clipboard.SetText(copy); } catch (Exception) { } };
                textList.Children.Add(card);
            }
            if (textEmpty != null) textEmpty.Visibility = showingText && textList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void LoadTextHistory()
        {
            try
            {
                if (!File.Exists(textsPath)) return;
                foreach (string line in File.ReadAllLines(textsPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try { clipboardTexts.Add(Encoding.UTF8.GetString(Convert.FromBase64String(line))); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        void SaveTextHistory()
        {
            try
            {
                string folder = Path.GetDirectoryName(textsPath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var lines = new List<string>();
                foreach (string text in clipboardTexts) lines.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));
                File.WriteAllLines(textsPath, lines);
            }
            catch (Exception) { }
        }

        void ClearClipboardText()
        {
            clipboardTexts.Clear();
            SaveTextHistory();
            RenderTextHistory();
        }

        // 胶囊上的手势分两种：
        //   - 原地按下再松开（位移小于 4px）→ 展开面板，保持原有点击行为；
        //   - 按住拖动（位移超过 4px）→ 移动胶囊窗口本身（DragMove 走系统非客户区拖动），
        //     与面板里"按住图片拖出去"完全分开，互不抢占。
        void AttachCapsuleGestures()
        {
            capsule.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                // 点在迷你播放器的按钮上：交给按钮处理，别展开面板
                if (IsCapsuleMiniButtonSource(e.OriginalSource)) return;
                // 点在消息提示上：让它自己跳转客户端，别展开面板
                if (IsCapsuleNoticeSource(e.OriginalSource)) return;
                capsuleDoubleClick = capsuleMiniActive && e.ClickCount >= 2;
                capsuleStart = e.GetPosition(this);
                capsuleDragLeft = Left;
                capsuleDragCursorX = System.Windows.Forms.Cursor.Position.X / DpiScale();
                capsulePressed = true;
                capsuleMoved = false;
                capsule.CaptureMouse();
            };
            capsule.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!capsulePressed) return;
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    capsulePressed = false;
                    capsuleMoved = false;
                    Mouse.OverrideCursor = null;
                    try { capsule.ReleaseMouseCapture(); } catch (Exception) { }
                    return;
                }
                Point now = e.GetPosition(this);
                if (!capsuleMoved)
                {
                    if (Math.Abs(now.X - capsuleStart.X) < 4 && Math.Abs(now.Y - capsuleStart.Y) < 4) return;
                    capsuleMoved = true;
                    Mouse.OverrideCursor = Cursors.SizeWE;
                }
                double delta = System.Windows.Forms.Cursor.Position.X / DpiScale() - capsuleDragCursorX;
                double minLeft = SystemParameters.VirtualScreenLeft;
                double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width;
                Left = Math.Max(minLeft, Math.Min(maxLeft, capsuleDragLeft + delta));
                Top = SystemParameters.WorkArea.Top;
                e.Handled = true;
            };
            capsule.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!capsulePressed) return;
                capsulePressed = false;
                Mouse.OverrideCursor = null;
                try { if (capsule.IsMouseCaptured) capsule.ReleaseMouseCapture(); } catch (Exception) { }
                if (capsuleMoved)
                {
                    capsuleMoved = false;
                    return;
                }
                // 迷你播放器按钮上的点击不触发展开；迷你态下要双击才展开
                if (IsCapsuleMiniButtonSource(e.OriginalSource)) return;
                // 点在消息提示上：让它自己跳转客户端，别展开面板
                if (IsCapsuleNoticeSource(e.OriginalSource)) return;
                if (capsuleMiniActive && !capsuleDoubleClick) return;
                SetExpanded(true);
            };
        }

        // 点在迷你播放器的三个按钮上时，胶囊不处理（交给按钮自己）
        bool IsCapsuleMiniButtonSource(object source)
        {
            // 来消息提示是独立一层：点它应该"打开微信"，不是展开面板
            if (IsCapsuleNoticeSource(source)) return true;
            if (!capsuleMiniActive || capsuleMiniButtons.Count == 0) return false;
            DependencyObject node = source as DependencyObject;
            int guard = 0;
            while (node != null && guard++ < 32)
            {
                if (capsuleMiniButtons.Contains(node)) return true;
                if (node == capsule) return false;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        void SavePlacement()
        {
            try
            {
                string folder = Path.GetDirectoryName(placementPath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(placementPath, ((int)Math.Round(Left)) + "," + ((int)Math.Round(Top)));
            }
            catch (Exception) { }
        }

        void RestorePlacement()
        {
            try
            {
                if (!File.Exists(placementPath)) return;
                string[] parts = File.ReadAllText(placementPath).Split(',');
                if (parts.Length != 2) return;
                double left, top;
                if (!double.TryParse(parts[0], out left) || !double.TryParse(parts[1], out top)) return;
                double minLeft = SystemParameters.VirtualScreenLeft;
                double minTop = SystemParameters.VirtualScreenTop;
                double maxLeft = minLeft + SystemParameters.VirtualScreenWidth - Width;
                double maxTop = minTop + SystemParameters.VirtualScreenHeight - Height;
                if (left < minLeft || left > maxLeft || top < minTop || top > maxTop) return;
                Left = left;
                Top = top;
                if (handle != IntPtr.Zero)
                {
                    PresentationSource presentation = PresentationSource.FromVisual(this);
                    double dpi = presentation == null ? 1.0 : presentation.CompositionTarget.TransformToDevice.M11;
                    SetWindowPos(handle, new IntPtr(-1),
                        (int)Math.Round(left * dpi), (int)Math.Round(top * dpi),
                        (int)Math.Round(Width * dpi), (int)Math.Round(Height * dpi), 0x0010);
                }
            }
            catch (Exception) { }
        }

        Border BuildCapsule()
        {
            // 胶囊上不再显示标题文字：换成一双会眨眼的卡通眼睛（EyeBlinkControl）。
            var built = new Border
            {
                Width = 148,
                Height = 37,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(0, 0, 20, 20),
                BorderBrush = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(
                    Color.FromArgb(232, 44, 50, 57),
                    Color.FromArgb(232, 31, 37, 45),
                    new Point(0, 0), new Point(0, 1)),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.38 },
                Cursor = Cursors.Hand,
                ToolTip = "打开 万象岛 Pro"
            };
            return DecorateCapsule(built);
        }

        static Button TabButton(string text, Action click)
        {
            var button = new Button
            {
                Content = text,
                Width = 58,
                Height = 26,
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(138, 143, 152)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='6'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Opacity' Value='0.86'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");
            button.Click += delegate { click(); };
            return button;
        }

        void SetTab(bool text)
        {
            SetTabMode(text ? 1 : 0);
        }

        void SetTabTranslate()
        {
            SetTabMode(2);
        }

        void SetTabMode(int mode)
        {
            bool text = mode == 1;
            bool translate = mode == 2;
            showingText = text;
            showingTranslate = translate;

            if (scroller != null) scroller.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (textScroller != null) textScroller.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
            if (translateView != null) translateView.Visibility = translate ? Visibility.Visible : Visibility.Collapsed;

            if (empty != null) empty.Visibility = mode == 0 && (cards == null || cards.Children.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            if (textEmpty != null) textEmpty.Visibility = text && (textList == null || textList.Children.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

            var idle = new SolidColorBrush(Color.FromRgb(167, 159, 196));
            if (imageTab != null)
            {
                imageTab.Background = TabIdleWash;
                imageTab.Foreground = mode == 0 ? Brushes.White : idle;
            }
            if (textTab != null)
            {
                textTab.Background = TabIdleWash;
                textTab.Foreground = text ? Brushes.White : idle;
            }
            if (translateTab != null)
            {
                translateTab.Background = TabIdleWash;
                translateTab.Foreground = translate ? Brushes.White : idle;
            }

            if (textClear != null) textClear.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
            if (imageClear != null) imageClear.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;

            MoveTabPill(mode, IsLoaded);
            FadeEmptyStates();
            if (text) AnimateTextCards();
            if (mode == 0) ScrollImagesToEnd();
            if (translate && translateInput != null) translateInput.Focus();
        }

        void ScrollImagesToEnd()
        {
            StopImageGlide();
            if (scroller == null) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                UpdateLayout();
                scroller.ScrollToRightEnd();
            }), DispatcherPriority.Background);
        }

        Border BuildPanel()
        {
            var header = new Grid { Height = 38, Margin = new Thickness(18, 8, 14, 0) };
            var tabHost = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(23, 19, 31)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(52, 42, 79)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            imageTab = TabButton("图片", delegate { SetTab(false); });
            textTab = TabButton("文字", delegate { SetTab(true); });
            translateTab = TabButton("翻译", delegate { SetTabTranslate(); });
            tabs.Children.Add(imageTab);
            tabs.Children.Add(textTab);
            tabs.Children.Add(translateTab);
            tabHost.Child = BuildTabHost(tabs);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            textClear = ToolButton("清空", 10);
            textClear.Visibility = Visibility.Collapsed;
            textClear.Click += delegate { ClearClipboardText(); };
            imageClear = ToolButton("清空图片", 10);
            imageClear.Click += delegate { ConfirmClearImages(); };
            var add = ToolButton("添加图片", 12);
            add.Click += delegate { AddImagesDialog(); };
            var close = new Button
            {
                Content = "×",
                Width = 22,
                Height = 22,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 210, 255)),
                Background = new SolidColorBrush(Color.FromArgb(30, 124, 108, 242)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 124, 108, 242)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            close.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='11'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Background' Value='#3D2F66'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");
            close.Click += delegate { CollapseLater(); };
            actions.Children.Add(textClear);
            actions.Children.Add(imageClear);
            actions.Children.Add(add);
            actions.Children.Add(close);
            // 页签 + 工具坞放在同一行左侧，动作按钮仍靠右
            var headerLeft = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerLeft.Children.Add(tabHost);
            headerLeft.Children.Add(BuildOrbHub());
            header.Children.Add(headerLeft);
            header.Children.Add(actions);

            cardBadges = new Dictionary<string, Border>();

            // 选图模式下的底部条：显示已选张数 + 取消 + 发送到 AI（点了才切面板）
            aiPickCountText = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 210, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 10, 0)
            };
            var pickCancel = ToolButton("取消", 10);
            pickCancel.Height = 24;
            pickCancel.Click += delegate { CancelAiPick(); };
            aiPickSendButton = ToolButton("发送到 AI", 12);
            aiPickSendButton.Height = 24;
            aiPickSendButton.Click += delegate { ConfirmAiPick(); };
            var pickRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            pickRow.Children.Add(aiPickCountText);
            pickRow.Children.Add(pickCancel);
            pickRow.Children.Add(aiPickSendButton);
            aiPickBar = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 18, 12),
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(236, 30, 34, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 124, 108, 242)),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed,
                Child = pickRow
            };
            cards = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
            scroller = new ScrollViewer
            {
                Content = cards,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(16, 0, 16, 0),
                Background = Brushes.Transparent
            };
            scroller.Resources[typeof(ScrollBar)] = BuildTranslateScrollBarStyle();
            scroller.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                GlideImages(-e.Delta);
                e.Handled = true;
            };
            empty = new TextBlock
            {
                Text = "暂无图片\n点击「添加图片」或把图片拖进来",
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 242, 245, 250)),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            textList = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
            textScroller = new ScrollViewer
            {
                Content = textList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(18, 4, 18, 4),
                Background = Brushes.Transparent,
                Visibility = Visibility.Collapsed
            };
            textScroller.Resources[typeof(ScrollBar)] = BuildTranslateScrollBarStyle();
            textScroller.PreviewMouseWheel += TranslateWheel;
            textEmpty = new TextBlock
            {
                Text = "暂无文字\n复制文字后会自动同步到这里",
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 242, 245, 250)),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(header, 0);
            body.Children.Add(header);
            var content = new Grid();
            content.Children.Add(scroller);
            content.Children.Add(empty);
            content.Children.Add(textScroller);
            content.Children.Add(textEmpty);
            content.Children.Add(BuildTranslateView());
            content.Children.Add(aiPickBar);
            Grid.SetRow(content, 1);
            body.Children.Add(content);

            var border = new Border
            {
                Width = 960,
                Height = 236,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0),
                CornerRadius = new CornerRadius(30),
                BorderBrush = new SolidColorBrush(Color.FromArgb(52, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(
                    Color.FromArgb(242, 38, 44, 51),
                    Color.FromArgb(240, 27, 33, 41),
                    new Point(0, 0), new Point(0, 1)),
                Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 10, Opacity = 0.4 },
                Child = body,
                Visibility = Visibility.Collapsed
            };
            return border;
        }

        static Button ToolButton(string text, double horizontalPadding)
        {
            var button = new Button
            {
                Content = text,
                Height = 26,
                Padding = new Thickness(horizontalPadding, 0, horizontalPadding, 0),
                Margin = new Thickness(4, 0, 4, 0),
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                Foreground = new SolidColorBrush(Color.FromRgb(217, 210, 255)),
                Background = new SolidColorBrush(Color.FromArgb(30, 124, 108, 242)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 124, 108, 242)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Focusable = false
            };
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='bg' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>" +
                "<ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bg' Property='Background' Value='#3D2F66'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate>");
            return button;
        }

        void SetExpanded(bool value)
        {
            // 休眠中卸掉的缩略图 / AI 气泡在真正展开面板时才懒加载回来。
            if (value)
            {
                ReloadCardImages();
                if (aiLastSurface || aiVisible) RestoreAiChatVisuals();
            }
            // 新增图片时强制回到图片面板（照片要飞进去），但不动"上次是哪个面板"的记忆
            if (value && forceImageSurface)
            {
                forceImageSurface = false;
                if (aiVisible) BackToImagePanel();
                if (musicVisible) BackToImagePanelFromMusic();
                SetTab(false);
                if (mouseHook != null) mouseHook.Enabled = value;
                SetExpandedAnimated(value);
                UpdateCapsuleMusicMode();
                return;
            }
            // 上次关的是 AI 面板 → 从胶囊再展开时直接回到 AI 面板。
            // （图片/文字面板不受影响：它们本来就默认回到"图片"页签）
            if (value && aiLastSurface && !aiVisible && aiPanel != null)
            {
                aiVisible = true;
                if (panel != null) panel.Visibility = Visibility.Collapsed;
            }
            // 上次关的是音乐面板 → 下次从胶囊展开直接回到音乐面板
            if (value && musicLastSurface && !musicVisible && musicPanel != null)
            {
                musicVisible = true;
                aiVisible = false;
                if (panel != null) panel.Visibility = Visibility.Collapsed;
            }
            if (mouseHook != null) mouseHook.Enabled = value;
            SetExpandedAnimated(value);
            if (!value) ResetOrbHubInstant();
            if (value) RefreshPanelNoticeChip();   // 只按当前未读刷新，不重新标记
            if (!value) HidePanelNoticeChip();
            // 收起后如果正在放歌：胶囊变宽成迷你播放器（等形变播完再变，避免两段动画打架）
            if (value) UpdateCapsuleMusicMode();
            else Motion.Delay(380, delegate { UpdateCapsuleMusicMode(); });
        }

        void ToggleCapsule()
        {
            if (!capsuleClosed)
            {
                SetExpanded(false);
                SetCapsuleVisible(false);
                capsuleClosed = true;
                ScheduleSleep();
            }
            else
            {
                SetExpanded(false);
                SetCapsuleVisible(true);
                capsuleClosed = false;
                WakeFromSleep();
                CapsuleReturnJump();
                Activate();
            }
        }
        void SetCapsuleVisible(bool visible)
        {
            if (handle == IntPtr.Zero)
            {
                if (visible) Show(); else Hide();
                return;
            }
            int cloak = visible ? 0 : 1;
            int result = DwmSetWindowAttribute(handle, 13, ref cloak, sizeof(int));
            if (result != 0)
            {
                if (visible) Show(); else Hide();
            }
        }
        static bool IsVideoFile(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv" || ext == ".webm" || ext == ".m4v";
        }

        void ToggleRecording()
        {
            try
            {
                if (recorder == null)
                {
                    recorder = new ScreenRecorder();
                    recorder.Finished += delegate(string path)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            UnregisterStopRecording();
                            if (string.IsNullOrEmpty(path))
                            {
                                tray.ShowBalloonTip(5000, "录屏失败", recorder.Error ?? "没有生成有效的视频文件。", Forms.ToolTipIcon.Warning);
                                return;
                            }
                            AddVideo(path);
                            tray.ShowBalloonTip(4000, "录屏完成", "视频已挂到胶囊里。", Forms.ToolTipIcon.Info);
                        }));
                    };
                }
                if (recorder.Recording)
                {
                    tray.ShowBalloonTip(3000, "正在录屏", "按 Esc 停止录屏。", Forms.ToolTipIcon.Info);
                }
                else if (recorder.Start())
                {
                    RegisterStopRecording();
                    tray.ShowBalloonTip(4000, "开始录屏", "按 Esc 停止录屏。", Forms.ToolTipIcon.Info);
                }
                else
                {
                    tray.ShowBalloonTip(6000, "无法开始录屏", recorder.Error, Forms.ToolTipIcon.Warning);
                }
            }
            catch (Exception exception)
            {
                DragNative.Log("recording failed: " + exception.Message);
            }
        }

        void RegisterStopRecording()
        {
            if (handle == IntPtr.Zero) return;
            if (!RegisterHotKey(handle, HotkeyStopRecord, ModNoRepeat, 0x1B))
                DragNative.Log("record stop hotkey registration failed");
        }

        void UnregisterStopRecording()
        {
            if (handle != IntPtr.Zero) UnregisterHotKey(handle, HotkeyStopRecord);
        }

        void StopRecording()
        {
            if (recorder == null || !recorder.Recording) return;
            UnregisterStopRecording();
            recorder.Stop();
            tray.ShowBalloonTip(4000, "正在结束录屏", "视频处理完成后会自动挂到胶囊。", Forms.ToolTipIcon.Info);
        }

        void OpenVideo(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                new VideoPlayerWindow(path).Show();
            }
            catch (Exception exception)
            {
                DragNative.Log("video open failed: " + exception.Message);
                MessageBox.Show("无法打开视频：" + exception.Message, "万象岛 Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void AddVideo(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try { path = LocalCopy(path); } catch (Exception) { return; }
            foreach (UIElement child in cards.Children)
            {
                var existing = child as Border;
                if (existing != null && string.Equals(existing.Tag as string, path, StringComparison.OrdinalIgnoreCase)) return;
            }
            var cover = new Image { Width = 160, Height = 110, Stretch = Stretch.UniformToFill };
            cover.Clip = new RectangleGeometry(new Rect(0, 0, 160, 110), 8, 8);
            var playBadge = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(21),
                Background = new SolidColorBrush(Color.FromArgb(205, 12, 15, 22)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "▶",
                    Foreground = Brushes.White,
                    FontSize = 19,
                    Margin = new Thickness(3, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var coverHost = new Grid { Width = 160, Height = 110, IsHitTestVisible = false };
            coverHost.Children.Add(cover);
            coverHost.Children.Add(playBadge);
            var preview = new Border
            {
                Width = 160,
                Height = 110,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "▶",
                            Foreground = new SolidColorBrush(Color.FromRgb(167, 159, 196)),
                            FontSize = 34,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "视频",
                            Foreground = new SolidColorBrush(Color.FromRgb(167, 159, 196)),
                            FontSize = 11,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };
            string coverPath = null;
            Task.Run(delegate
            {
                string generated = CreateVideoCover(path);
                if (string.IsNullOrEmpty(generated)) return;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(generated, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();
                        cover.Source = bitmap;
                        preview.Child = coverHost;
                        coverPath = generated;
                    }
                    catch (Exception) { }
                }));
            });
            var label = new TextBlock
            {
                Text = System.IO.Path.GetFileName(path),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromArgb(190, 242, 245, 250)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10,
                Margin = new Thickness(2, 6, 2, 0)
            };
            var stack = new StackPanel();
            stack.Children.Add(preview);
            stack.Children.Add(label);

            // 选图模式的号码牌（默认隐藏，进选图状态后按选择顺序显示 1/2/3…）
            var badge = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromRgb(124, 108, 242)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "1",
                    FontFamily = new FontFamily("Microsoft YaHei UI"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var cardContent = new Grid();
            cardContent.Children.Add(stack);
            cardContent.Children.Add(badge);

            var card = new Border
            {
                Width = 172,
                Height = 150,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(5),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 124, 108, 242)),
                BorderThickness = new Thickness(1),
                Child = cardContent,
                Tag = path,
                ToolTip = path,
                Cursor = Cursors.Hand
            };
            if (cardBadges != null) cardBadges[path] = badge;

            // 选图模式：左键点卡片 = 勾选/取消（拦在拖拽和菜单前面）
            card.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!AiPickMode) return;
                ToggleAiPick(path);
                e.Handled = true;
            };
            // 视频卡片：单击看封面、双击播放、按住拖动 = 把视频文件原生拖到微信等程序。
            bool videoPressed = false;
            System.Windows.Point videoDragStart = new System.Windows.Point();
            var videoClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            videoClickTimer.Tick += delegate
            {
                videoClickTimer.Stop();
                if (!string.IsNullOrEmpty(coverPath)) ShowCardZoom(card, coverPath);
            };
            card.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (e.ClickCount == 2)
                {
                    videoClickTimer.Stop();
                    videoPressed = false;
                    try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch (Exception) { }
                    OpenVideo(path);
                    e.Handled = true;
                    return;
                }
                videoDragStart = e.GetPosition(this);
                videoPressed = true;
                videoClickTimer.Stop();
                try { card.CaptureMouse(); } catch (Exception) { }
            };
            card.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!videoPressed) return;
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    videoPressed = false;
                    try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch (Exception) { }
                    return;
                }
                System.Windows.Point now = e.GetPosition(this);
                if (Math.Abs(now.X - videoDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(now.Y - videoDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                videoPressed = false;
                videoClickTimer.Stop();
                try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch (Exception) { }
                StartImageDrag(card, path);
                e.Handled = true;
            };
            card.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!videoPressed) return;
                videoPressed = false;
                try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch (Exception) { }
                videoClickTimer.Stop();
                videoClickTimer.Start();
            };
            var menu = new ContextMenu();
            var play = new MenuItem { Header = "播放视频" };
            play.Click += delegate { OpenVideo(path); };
            var locate = new MenuItem { Header = "打开位置" };
            locate.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch (Exception) { } };
            var remove = new MenuItem { Header = "从胶囊移除" };
            remove.Click += delegate { cards.Children.Remove(card); SaveImages(); empty.Visibility = !showingText && cards.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed; };
            menu.Items.Add(play);
            menu.Items.Add(locate);
            menu.Items.Add(remove);
            card.ContextMenu = menu;
            cards.Children.Add(card);
            OnCardAdded(card);
            if (!showingText) ScrollImagesToEnd();
            empty.Visibility = Visibility.Collapsed;
            SaveImages();
        }

        static string CreateVideoCover(string video)
        {
            try
            {
                string ffmpeg = ScreenRecorder.FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpeg)) return null;
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "covers");
                Directory.CreateDirectory(folder);
                string cover = Path.Combine(folder, Path.GetFileNameWithoutExtension(video) + "_" + DateTime.Now.ToString("HHmmssfff") + ".jpg");
                var info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-y -hide_banner -loglevel error -ss 0 -i \"" + video + "\" -frames:v 1 -vf \"scale=480:-2\" \"" + cover + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    if (process == null) return null;
                    process.WaitForExit(8000);
                }
                return File.Exists(cover) && new FileInfo(cover).Length > 0 ? cover : null;
            }
            catch (Exception) { return null; }
        }

        void ToggleDetachedImages()
        {
            bool anyVisible = false;
            foreach (var window in detachedWindows)
            {
                if (window.IsVisible) { anyVisible = true; break; }
            }
            foreach (var window in detachedWindows)
            {
                if (anyVisible) window.Hide();
                else window.Show();
            }
        }

        void CollapseLater()
        {
            suppressDeactivate = true;
            if (collapseTimer == null)
            {
                collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                collapseTimer.Tick += delegate
                {
                    collapseTimer.Stop();
                    suppressDeactivate = false;
                    SetExpanded(false);
                };
            }
            collapseTimer.Stop();
            collapseTimer.Start();
        }

        bool IsInteractive(object source)
        {
            DependencyObject node = source as DependencyObject;
            while (node != null && node != this)
            {
                if (node == aiPanel) return true;
                if (node is System.Windows.Controls.Primitives.ButtonBase) return true;
                Border border = node as Border;
                if (border != null && border.Tag is string) return true;
                if (node is System.Windows.Controls.Primitives.ScrollBar) return true;
                if (node is System.Windows.Controls.Primitives.Thumb) return true;
                node = VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        // 原先为了让 Pro 版和标准版 万象岛 同时运行不重叠，默认向右错开 150。
        // 用户要求胶囊/面板居中，改成 0；以后需要并排运行再调回 150。
        const double CapsuleOffsetX = 0;

        void PositionOnScreen()
        {
            Forms.Screen screen = Forms.Screen.PrimaryScreen;
            PresentationSource presentation = PresentationSource.FromVisual(this);
            double dpi = presentation == null ? 1.0 : presentation.CompositionTarget.TransformToDevice.M11;
            int width = (int)Math.Round(Width * dpi);
            int height = (int)Math.Round(Height * dpi);
            int left = screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2 + (int)Math.Round(CapsuleOffsetX * dpi);
            left = Math.Min(left, screen.WorkingArea.Right - width);
            int top = screen.WorkingArea.Top;
            Left = left / dpi;
            Top = top / dpi;
            if (handle != IntPtr.Zero) SetWindowPos(handle, new IntPtr(-1), left, top, width, height, 0x0010);
        }

        void AddImagesDialog()
        {
            suppressDeactivate = true;
            try
            {
                var dialog = new Forms.OpenFileDialog
                {
                    Title = "添加图片或视频",
                    Multiselect = true,
                    Filter = "图片和视频|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.mp4;*.mov;*.avi;*.mkv;*.webm;*.m4v|图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|视频|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.m4v|所有文件|*.*"
                };
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                foreach (string path in dialog.FileNames) AddImage(path);
            }
            finally
            {
                suppressDeactivate = false;
                Activate();
            }
        }

        // 同一个源文件只复制一次：否则每次点击都会生成新副本，去重永远失效（会出现重复卡片）
        static readonly Dictionary<string, string> localCopyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string LocalCopy(string path)
        {
            string dataRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            string full = Path.GetFullPath(path);
            if (full.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase)) return full;
            string folder = Path.Combine(dataRoot, "images");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string key = full;
            try
            {
                var info = new FileInfo(full);
                key = full + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            }
            catch (Exception) { }

            string cached;
            if (localCopyCache.TryGetValue(key, out cached) && File.Exists(cached)) return cached;
            if (localCopyCache.Count > 300) localCopyCache.Clear();

            string destination = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "_" + Path.GetFileName(path));
            File.Copy(path, destination, true);
            localCopyCache[key] = destination;
            return destination;
        }

        void AddImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            if (IsVideoFile(path)) { AddVideo(path); return; }
            try { path = LocalCopy(path); } catch (Exception) { return; }
            foreach (UIElement child in cards.Children)
            {
                var existing = child as Border;
                if (existing != null && string.Equals(existing.Tag as string, path, StringComparison.OrdinalIgnoreCase)) return;
            }
            // 休眠期间不解码缩略图：卡片照常加进来，等展开面板时再统一回填。
            BitmapSource image = sleeping ? null : DecodeCardThumbnail(path);
            if (!sleeping && image == null) return;

            var picture = new Image { Source = image, Stretch = Stretch.Uniform, Width = 160, Height = 110 };
            picture.Clip = new RectangleGeometry(new Rect(0, 0, 160, 110), 8, 8);
            var label = new TextBlock
            {
                Text = Path.GetFileName(path),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new SolidColorBrush(Color.FromArgb(190, 242, 245, 250)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10,
                Margin = new Thickness(2, 6, 2, 0)
            };
            var stack = new StackPanel();
            stack.Children.Add(picture);
            stack.Children.Add(label);

            // 选图模式的号码牌（默认隐藏，进选图状态后按选择顺序显示 1/2/3…）
            var badge = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromRgb(124, 108, 242)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = "1",
                    FontFamily = new FontFamily("Microsoft YaHei UI"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var cardContent = new Grid();
            cardContent.Children.Add(stack);
            cardContent.Children.Add(badge);

            var card = new Border
            {
                Width = 172,
                Height = 150,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(5),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = cardContent,
                Tag = path,
                ToolTip = path
            };
            if (cardBadges != null) cardBadges[path] = badge;

            // 选图模式：左键点卡片 = 勾选/取消，并且把按下/抬起都吃掉，别触发拖拽或打开预览
            card.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!AiPickMode) return;
                ToggleAiPick(path);
                e.Handled = true;
            };
            card.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!AiPickMode) return;
                e.Handled = true;
            };
            card.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!AiPickMode) return;
                e.Handled = true;
            };

            var menu = new ContextMenu();
            var export = new MenuItem { Header = "复制到桌面" };
            export.Click += delegate
            {
                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string target = Path.Combine(desktop, Path.GetFileName(path));
                    if (File.Exists(target)) target = Path.Combine(desktop, Path.GetFileNameWithoutExtension(path) + "-" + DateTime.Now.ToString("HHmmss") + Path.GetExtension(path));
                    File.Copy(path, target, true);
                }
                catch (Exception) { }
            };
            var remove = new MenuItem { Header = "从胶囊移除" };
            remove.Click += delegate { RemoveCardAnimated(card); };
            var editItem = new MenuItem { Header = "图片编辑" };
            editItem.Click += delegate
            {
                try
                {
                    var editor = new ImageEditorWindow(path, delegate(string edited) { AddImage(edited); });
                    editor.Show();
                }
                catch (Exception exception) { DragNative.Log("editor failed: " + exception.Message); }
            };
            var previewItem = new MenuItem { Header = "打开预览" };
            var translateImageItem = new MenuItem { Header = "翻译图片" };
            translateImageItem.Click += delegate { TranslateImageAsync(path); };
            previewItem.Click += delegate { ShowCardZoom(card, path); };
            var fullItem = new MenuItem { Header = "大图查看" };
            fullItem.Click += delegate { ShowFullImage(path); };
            var pinItem = new MenuItem { Header = "钉在桌面" };
            pinItem.Click += delegate
            {
                try
                {
                    Rect home = CapsuleScreenRectPhysical();
                    var pinned = new DetachedImageViewerWindow(path, home, delegate { if (!expanded) CapsuleCatch(); });
                    pinned.Show();
                }
                catch (Exception) { }
            };
            var aiItem = new MenuItem { Header = "发送到 AI" };
            aiItem.Click += delegate { BeginAiPick(path); };
            menu.Items.Add(aiItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(editItem);
            menu.Items.Add(translateImageItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(previewItem);
            menu.Items.Add(fullItem);
            menu.Items.Add(pinItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(export);
            menu.Items.Add(remove);
            card.ContextMenu = menu;

            card.Cursor = Cursors.Hand;
            card.ToolTip = path + "\n按住拖到微信 / 画图 / 浏览器等窗口即可导出";

            System.Windows.Point dragStart = new System.Windows.Point();
            bool pressed = false;
            var clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            clickTimer.Tick += delegate
            {
                clickTimer.Stop();
                ShowCardZoom(card, path);
            };
            card.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (e.ClickCount == 2)
                {
                    clickTimer.Stop();
                    pressed = false;
                    try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch (Exception) { }
                    ShowFullImage(path);
                    e.Handled = true;
                    return;
                }
                dragStart = e.GetPosition(this);
                pressed = true;
                clickTimer.Stop();
                try { card.CaptureMouse(); } catch (Exception) { }
            };
            card.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!pressed) return;
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    pressed = false;
                    try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch (Exception) { }
                    return;
                }
                Point now = e.GetPosition(this);
                if (Math.Abs(now.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(now.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                pressed = false;
                clickTimer.Stop();
                try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch (Exception) { }
                StartImageDrag(card, path);
                e.Handled = true;
            };
            card.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!pressed) return;
                pressed = false;
                try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch (Exception) { }
                clickTimer.Stop();
                clickTimer.Start();
            };
            System.Windows.Point rightStart = new System.Windows.Point();
            bool rightPressed = false;
            bool rightMoved = false;
            bool rightSuppressMenu = false;
            DragPreviewWindow rightPreview = null;
            card.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                rightStart = e.GetPosition(this);
                rightPressed = true;
                rightMoved = false;
                try { card.CaptureMouse(); } catch (Exception) { }
            };
            card.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!rightPressed || e.RightButton != MouseButtonState.Pressed) return;
                Point now = e.GetPosition(this);
                if (!rightMoved && Math.Abs(now.X - rightStart.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(now.Y - rightStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                rightMoved = true;
                Mouse.OverrideCursor = Cursors.Hand;
                Point screen = PointToScreen(now);
                if (rightPreview == null)
                {
                    try
                    {
                        rightPreview = new DragPreviewWindow(ViewerImageLoader.Load(path));
                        rightPreview.Show();
                    }
                    catch (Exception) { }
                }
                if (rightPreview != null) rightPreview.MoveTo((int)screen.X, (int)screen.Y);
                e.Handled = true;
            };
            card.PreviewMouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!rightPressed) return;
                rightPressed = false;
                Mouse.OverrideCursor = null;
                try { if (card.IsMouseCaptured) card.ReleaseMouseCapture(); } catch (Exception) { }
                if (rightPreview != null)
                {
                    try { rightPreview.Close(); } catch (Exception) { }
                    rightPreview = null;
                }
                if (!rightMoved) return;
                rightSuppressMenu = true;
                rightMoved = false;
                int returnIndex = cards.Children.IndexOf(card);
                try
                {
                    var detached = new DetachedImageViewerWindow(path, CapsuleScreenRectPhysical(), ReturnToCapsule);
                    detached.Closed += delegate
                    {
                        detachedWindows.Remove(detached);
                        if (IsLoaded)
                        {
                            AddImage(path);
                            if (cards.Children.Count > 0 && returnIndex >= 0)
                            {
                                UIElement added = cards.Children[cards.Children.Count - 1];
                                cards.Children.Remove(added);
                                cards.Children.Insert(Math.Min(returnIndex, cards.Children.Count), added);
                                SaveImages();
                            }
                            empty.Visibility = !showingText && cards.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                            if (capsuleClosed)
                            {
                                SetCapsuleVisible(true);
                                capsuleClosed = false;
                                WakeFromSleep();
                            }
                        }
                    };
                    detachedWindows.Add(detached);
                    detached.Show();
                }
                catch (Exception) { }
                cards.Children.Remove(card);
                empty.Visibility = !showingText && cards.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                SaveImages();
                SetExpanded(false);
                e.Handled = true;
            };
            card.ContextMenuOpening += delegate(object sender, ContextMenuEventArgs e)
            {
                if (rightSuppressMenu)
                {
                    rightSuppressMenu = false;
                    e.Handled = true;
                }
            };

            cards.Children.Add(card);
            OnCardAdded(card);
            if (!showingText) ScrollImagesToEnd();
            empty.Visibility = Visibility.Collapsed;
            SaveImages();
        }

        // 取出的图片关闭后：直接收回胶囊，不再把面板重新展开一下。
        // 预览窗口关闭后：把面板重新展开回来（点空白关掉大图 → 面板又出来）
        // 预览窗口关闭后：把面板重新展开回来（点空白关掉大图 → 面板又出来）
        void ReturnToCapsule()
        {
            suppressDeactivate = false;
            peekOwned = false;
            if (capsule != null) capsule.Visibility = Visibility.Visible;
            if (!expanded) SetExpanded(true);
            else CapsuleCatch();
        }
        void ShowCardZoom(Border card, string path)
        {
            try
            {
                suppressDeactivate = true;
                Point origin = card.PointToScreen(new Point(0, 0));
                Rect home = CapsuleScreenRectPhysical();
                SetExpanded(false);
                var window = new CardZoomWindow(path, new Rect(origin, new Size(card.ActualWidth, card.ActualHeight)), ReturnToCapsule, home);
                window.Show();
            }
            catch (Exception exception)
            {
                suppressDeactivate = false;
                DragNative.Log("preview failed: " + exception.Message);
            }
        }

        void ShowFullImage(string path)
        {
            try
            {
                suppressDeactivate = true;
                Rect home = CapsuleScreenRectPhysical();
                SetExpanded(false);
                var window = new FullImageViewerWindow(path, ReturnToCapsule, home);
                window.Show();
            }
            catch (Exception exception)
            {
                suppressDeactivate = false;
                DragNative.Log("viewer failed: " + exception.Message);
            }
        }

        // 卡片上的左键拖动 = 把图片交给系统 OLE 拖放，跨进程导出到任意接受图片的窗口。
        // 这里不模拟鼠标事件：真正的拖拽循环由 ole32!DoDragDrop 拥有，目标窗口也由系统解析。
        void StartImageDrag(Border card, string path)
        {
            if (exporting) return;
            exporting = true;
            dragOut = true;
            double previousOpacity = card.Opacity;
            card.Opacity = 0.45;
            CardDragVisual(card, true);
            Mouse.OverrideCursor = null;
            try
            {
                bool video = IsVideoFile(path);
                BitmapSource preview = null;
                StackPanel stack = card.Child as StackPanel;
                if (stack != null && stack.Children.Count > 0)
                {
                    Image picture = stack.Children[0] as Image;
                    if (picture != null) preview = picture.Source as BitmapSource;
                    else preview = FindImageSource(stack.Children[0]);
                }

                // 视频只发 CF_HDROP（真实文件路径），不带 DIB/PNG：
                // 微信这类目标拿到图片格式会当成图片粘贴，拿到文件路径才会当视频文件处理。
                BitmapSource full = null;
                if (!video)
                {
                    try { full = LoadFullImage(path); }
                    catch (Exception exception) { DragNative.Log("full image load failed: " + exception.Message); }
                }

                DragNative.Log("drag begin | integrity=" + RuntimeAccess.IntegrityName);
                DragOutcome outcome = NativeImageDrag.Run(path, full, preview);
                if (outcome.Failed) DragNative.Log("export failed: " + outcome.Error);
                if (outcome.Effect == 0) ExplainRejectedDrop();
            }
            catch (Exception exception)
            {
                DragNative.Log("export exception: " + exception);
            }
            finally
            {
                card.Opacity = previousOpacity;
                CardDragVisual(card, false);
                dragOut = false;
                exporting = false;
            }
        }

        // 拖放被系统拒绝时给出可操作的提示（低权限 / 目标提权是最常见原因）。
        void ExplainRejectedDrop()
        {
            if (lowPrivilegeWarned) return;
            lowPrivilegeWarned = true;
            if (RuntimeAccess.IsLow)
            {
                ShowBalloon(8000, "拖放被 Windows 阻止",
                    "当前是低权限（沙箱）环境，系统不允许把图片拖到微信、资源管理器等普通程序。\n请双击「启动 万象岛 Pro.cmd」以普通用户身份重新打开（不需要管理员）。",
                    Forms.ToolTipIcon.Warning);
            }
            else if (RuntimeAccess.IsElevated)
            {
                ShowBalloon(8000, "拖放被 Windows 阻止",
                    "万象岛 Pro 正以管理员身份运行，而目标程序是普通权限，Windows 不允许从高权限拖到低权限。\n请在托盘菜单选择「以普通权限重新启动」。",
                    Forms.ToolTipIcon.Warning);
            }
        }

        // 低权限时托盘图标根本加不进去，气泡也不会显示，改用不依赖托盘的小窗口提示。
        void ShowLowPrivilegeNotice()
        {
            DragNative.Log("startup | integrity=" + RuntimeAccess.IntegrityName + " | UIPI will block tray + drops");
            if (!AppSettings.LowPrivilegeNotice) return;
            try
            {
                var notice = new StartupNoticeWindow();
                notice.Show();
            }
            catch (Exception exception)
            {
                DragNative.Log("low privilege notice failed: " + exception.Message);
            }
        }

        void ShowBalloon(int milliseconds, string title, string text, Forms.ToolTipIcon icon)
        {
            try
            {
                if (tray == null || !tray.Visible) return;
                tray.ShowBalloonTip(milliseconds, title, text, icon);
            }
            catch (Exception) { }
        }

        static int HotkeyId(HotkeyAction action)
        {
            if (action == HotkeyAction.Toggle) return HotkeyToggle;
            if (action == HotkeyAction.Region) return HotkeyRegion;
            if (action == HotkeyAction.Window) return HotkeyWindow;
            if (action == HotkeyAction.Screen) return HotkeyScreen;
            if (action == HotkeyAction.ImagesToggle) return HotkeyImages;
            if (action == HotkeyAction.DouyinToggle) return HotkeyDouyin;
            if (action == HotkeyAction.ScreenTranslate) return HotkeyScreenTranslate;
            return HotkeyRecord;
        }

        void RegisterHotkeys()
        {
            if (handle == IntPtr.Zero || hotkeys == null) return;
            foreach (HotkeyBinding item in hotkeys)
            {
                if (item.Key == 0) continue;
                RegisterHotKey(handle, HotkeyId(item.Action), item.Modifiers | ModNoRepeat, item.Key);
            }
        }

        void UnregisterHotkeys()
        {
            if (handle == IntPtr.Zero) return;
            UnregisterHotKey(handle, HotkeyToggle);
            UnregisterHotKey(handle, HotkeyRegion);
            UnregisterHotKey(handle, HotkeyWindow);
            UnregisterHotKey(handle, HotkeyScreen);
            UnregisterHotKey(handle, HotkeyImages);
            UnregisterHotKey(handle, HotkeyRecord);
            UnregisterHotKey(handle, HotkeyDouyin);
            UnregisterHotKey(handle, HotkeyScreenTranslate);
        }

        string ApplyHotkeys(HotkeyBinding[] next)
        {
            for (int i = 0; i < next.Length; i++)
            {
                if (next[i].Key == 0) continue;
                for (int j = i + 1; j < next.Length; j++)
                {
                    if (next[j].Key == next[i].Key && next[j].Modifiers == next[i].Modifiers)
                        return "“" + next[i].Name + "”和“" + next[j].Name + "”不能使用同一个快捷键。";
                }
            }
            UnregisterHotkeys();
            var failed = new List<string>();
            foreach (HotkeyBinding item in next)
            {
                if (item.Key == 0) continue;
                if (!RegisterHotKey(handle, HotkeyId(item.Action), item.Modifiers | ModNoRepeat, item.Key))
                    failed.Add(item.Name);
            }
            if (failed.Count > 0)
            {
                UnregisterHotkeys();
                RegisterHotkeys();
                return "以下快捷键被系统或其他程序占用：" + string.Join("、", failed.ToArray());
            }
            hotkeys = next;
            HotkeyStore.Save(next);
            return null;
        }

        void OpenHotkeySettings()
        {
            var window = new HotkeySettingsWindow(hotkeys, ApplyHotkeys);
            window.ShowDialog();
        }

        static System.Drawing.Icon LoadApplicationIcon()
        {
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
            catch (Exception) { }
            return System.Drawing.SystemIcons.Application;
        }

        // 开机自启动走 HKCU\...\Run：普通用户权限启动，不弹 UAC，也不依赖计划任务。
        const string StartupValueName = "MyriadIslePro";
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        bool StartupEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(StartupValueName) != null;
                }
            }
            catch (Exception) { return false; }
        }

        void ToggleStartup()
        {
            try
            {
                if (StartupEnabled())
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        if (key != null) key.DeleteValue(StartupValueName, false);
                    }
                    ShowBalloon(4000, "已关闭开机自启动", "下次登录不会再自动出现胶囊。", Forms.ToolTipIcon.Info);
                    return;
                }

                string exe = RuntimeAccess.ExePath;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        ShowBalloon(5000, "无法开启开机自启动", "注册表 Run 项打不开，可能被安全软件拦截。", Forms.ToolTipIcon.Warning);
                        return;
                    }
                    key.SetValue(StartupValueName, "\"" + exe + "\" --autostart", Microsoft.Win32.RegistryValueKind.String);
                }
                ShowBalloon(4000, "已开启开机自启动", "下次登录会以普通用户权限自动运行，不会弹 UAC。", Forms.ToolTipIcon.Info);
            }
            catch (Exception exception)
            {
                DragNative.Log("startup toggle failed: " + exception.Message);
                ShowBalloon(5000, "无法修改开机自启动", exception.Message, Forms.ToolTipIcon.Warning);
            }
        }

        // 托盘图标：正常权限下这一句就够；低权限时 Shell_NotifyIcon 会被系统拒绝（不会抛异常）。
        void AttachTrayIcon()
        {
            try
            {
                tray.Visible = true;
                DragNative.Log("tray icon attached | mode=" + RuntimeAccess.ModeName);
            }
            catch (Exception exception)
            {
                DragNative.Log("tray icon attach failed: " + exception.Message);
            }
        }

        void ShowTrayMenu()
        {
            try
            {
                if (trayMenuWindow != null) { try { trayMenuWindow.Close(); } catch (Exception) { } }
                TrayMenuWindow menu = null;
                menu = new TrayMenuWindow(
                    delegate { ToggleCapsule(); },
                    delegate { if (menu != null) ShowMoreMenu(menu); },
                    delegate { ToggleStartup(); },
                    StartupEnabled(),
                    delegate { RelaunchNormal(); },
                    delegate { Close(); });
                trayMenuWindow = menu;
                menu.Closed += delegate { if (trayMenuWindow == menu) trayMenuWindow = null; };
                menu.ShowAtCursor();
            }
            catch (Exception exception)
            {
                DragNative.Log("tray menu failed: " + exception.Message);
            }
        }

        void ShowMoreMenu(TrayMenuWindow parent)
        {
            try
            {
                if (parent != null) parent.ChildOpen = true;
                var menu = new MoreMenuWindow(
                    delegate { OpenHotkeySettings(); },
                    delegate { AddImagesDialog(); },
                    delegate { OpenImageFolder(); },
                    delegate { ClearCards(); },
                    delegate { CapsuleEyesEnabled = !CapsuleEyesEnabled; },
                    CapsuleEyesEnabled,
                    delegate { OpenPhonePair(); });
                menu.Closed += delegate { if (parent != null) parent.ChildOpen = false; };
                menu.ShowLeftOf(parent);
            }
            catch (Exception exception)
            {
                if (parent != null) parent.ChildOpen = false;
                DragNative.Log("more menu failed: " + exception.Message);
            }
        }

        void OpenImageFolder()
        {
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(storePath), "screenshots");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception) { }
        }

        void ClearCards()
        {
            cards.Children.Clear();
            empty.Visibility = Visibility.Visible;
            SaveImages();
        }

        // 一键清空图片：两段式（第一下变"确认清空?"，3.5 秒内再点才真删）
        DateTime imageClearArmedAt = DateTime.MinValue;

        void ConfirmClearImages()
        {
            if ((DateTime.UtcNow - imageClearArmedAt).TotalSeconds > 3)
            {
                imageClearArmedAt = DateTime.UtcNow;
                if (imageClear != null) imageClear.Content = "确认清空?";
                ShowToolMessage("清空图片", "再点一次就删掉所有图片，以及硬盘上的截图副本。");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                timer.Tick += delegate
                {
                    timer.Stop();
                    imageClearArmedAt = DateTime.MinValue;
                    if (imageClear != null) imageClear.Content = "清空图片";
                };
                timer.Start();
                return;
            }

            imageClearArmedAt = DateTime.MinValue;
            if (imageClear != null) imageClear.Content = "清空图片";
            ClearAllImages();
        }

        void ClearAllImages()
        {
            int count = cards.Children.Count;
            cards.Children.Clear();
            SaveImages();
            if (empty != null) empty.Visibility = showingText ? Visibility.Collapsed : Visibility.Visible;

            // 删掉 app 自己目录里的图片副本；用户自己电脑上的原图不动
            int deleted = 0;
            string[] folders = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "images"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "screenshots")
            };
            foreach (string folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (string file in Directory.GetFiles(folder))
                    {
                        try { File.Delete(file); deleted++; }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
            }

            CancelAiPick();
            ClearAiAttachmentsFromDeletedFiles();
            ShowToolMessage("清空图片", "清掉 " + count + " 张图片，硬盘上删了 " + deleted + " 个文件。");
        }

        // 给"发送到 AI"用：当前图片面板里所有图片的路径（视频不算）
        internal List<string> CurrentImagePaths()
        {
            var list = new List<string>();
            try
            {
                foreach (UIElement child in cards.Children)
                {
                    var border = child as Border;
                    if (border == null) continue;
                    string path = border.Tag as string;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (IsVideoFile(path)) continue;
                    list.Add(path);
                }
            }
            catch (Exception) { }
            return list;
        }

        // 托盘菜单「以普通权限重新启动」：先让资源管理器拉起新实例，再退出自己。
        // 新实例在 Program 里会等最多 2.5 秒拿互斥体，避免和正在退出的旧实例抢不到。
        void RelaunchNormal()
        {
            try
            {
                bool started = RuntimeAccess.LaunchNormalInstance();
                if (!started)
                {
                    ShowBalloon(5000, "重新启动失败", "没能在资源管理器里启动新实例。", Forms.ToolTipIcon.Warning);
                    return;
                }
                Close();
            }
            catch (Exception exception)
            {
                DragNative.Log("normal restart failed: " + exception.Message);
            }
        }

        // 在可视树里找第一张有图像的 Image（视频卡片的封面藏在 Border 里，不在 StackPanel 第一层）。
        static BitmapSource FindImageSource(DependencyObject root)
        {
            if (root == null) return null;
            Image image = root as Image;
            if (image != null) return image.Source as BitmapSource;
            try
            {
                int count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    BitmapSource found = FindImageSource(VisualTreeHelper.GetChild(root, i));
                    if (found != null) return found;
                }
            }
            catch (Exception) { }
            return null;
        }

        static BitmapSource LoadFullImage(string path)
        {
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        void SaveImages()
        {
            try
            {
                string folder = Path.GetDirectoryName(storePath);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var lines = new List<string>();
                foreach (UIElement child in cards.Children)
                {
                    var item = child as Border;
                    if (item != null && item.Tag is string) lines.Add((string)item.Tag);
                }
                File.WriteAllLines(storePath, lines);
            }
            catch (Exception) { }
        }

        void LoadImages()
        {
            if (!File.Exists(storePath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(storePath))
                    if (!string.IsNullOrWhiteSpace(line)) AddImage(line.Trim());
            }
            catch (Exception) { }
        }

        void HandleDragOver(object sender, DragEventArgs e)
        {
            if (dragOut) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (expanded) { e.Effects = DragDropEffects.Copy; e.Handled = true; return; }
            System.Windows.Point point = e.GetPosition(this);
            bool near = point.X >= Width / 2.0 - 140 && point.X <= Width / 2.0 + 140 && point.Y >= 0 && point.Y <= 90;
            if (!near) { CancelDragTimer(); e.Effects = DragDropEffects.None; e.Handled = true; return; }
            e.Effects = DragDropEffects.None;
            if (!dropArmed)
            {
                if (dragTimer == null)
                {
                    dragTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
                    dragTimer.Tick += delegate
                    {
                        dragTimer.Stop();
                        if (!expanded) { dropArmed = true; aiLastSurface = false; SetExpanded(true); }
                    };
                }
                if (!dragTimer.IsEnabled) dragTimer.Start();
            }
            e.Handled = true;
        }

        void CancelDragTimer()
        {
            if (dragTimer != null) dragTimer.Stop();
            dropArmed = false;
        }

        async void CaptureRegionAsync()
        {
            if (capturing) return;
            bool wasExpanded = expanded;
            capturing = true;
            bool preview = false;
            try
            {
                await Task.Delay(120);
                System.Drawing.Rectangle screen = ScreenCapture.VirtualScreen();
                using (System.Drawing.Bitmap desktop = ScreenCapture.CaptureBitmap(screen))
                {
                    if (desktop == null) return;
                    var picker = new RegionCaptureWindow(desktop, screen);
                    System.Drawing.Bitmap crop = await picker.PickAsync();
                    if (crop == null) return;
                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "screenshots");
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "shot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                    using (crop) crop.Save(path, ImageFormat.Png);
                    AddImage(path);
                    preview = true;
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "MyriadIslePro-region.log"), ex + Environment.NewLine); } catch (Exception) { }
            }
            finally
            {
                capturing = false;
                SetExpanded(preview || wasExpanded);
                suppressDeactivate = false;
                if (preview || wasExpanded)
                {
                    Activate();
                    Focus();
                }
            }
        }

        async void CaptureWindowAsync()
        {
            if (capturing) return;
            capturing = true;
            bool preview = false;
            try
            {
                System.Drawing.Rectangle? bounds = ScreenCapture.ActiveWindow();
                PrepareShot();
                await Task.Delay(120);
                if (!bounds.HasValue) bounds = ScreenCapture.VirtualScreen();
                AddScreenshot(bounds.Value);
                preview = true;
            }
            catch (Exception) { }
            finally
            {
                capturing = false;
                RestoreShot(preview);
            }
        }

        async void CaptureScreenAsync()
        {
            if (capturing) return;
            capturing = true;
            bool preview = false;
            try
            {
                PrepareShot();
                await Task.Delay(120);
                AddScreenshot(ScreenCapture.VirtualScreen());
                preview = true;
            }
            catch (Exception) { }
            finally
            {
                capturing = false;
                RestoreShot(preview);
            }
        }

        void PrepareShot()
        {
            suppressDeactivate = true;
            Hide();
        }

        void RestoreShot(bool preview)
        {
            Show();
            aiLastSurface = false;   // 截图/预览回来时一定是图片面板
            musicLastSurface = false;
            SetExpanded(preview);
            suppressDeactivate = false;
            if (preview) Activate();
        }

        void AddScreenshot(System.Drawing.Rectangle bounds)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "screenshots");
            string path = ScreenCapture.Capture(bounds, folder);
            if (!string.IsNullOrEmpty(path)) AcceptScreenshotImage(path, bounds);
        }

        void CheckConflicts()
        {
            try
            {
                foreach (System.Diagnostics.ProcessModule module in System.Diagnostics.Process.GetCurrentProcess().Modules)
                {
                    if (module.ModuleName != null && module.ModuleName.StartsWith("GPP", StringComparison.OrdinalIgnoreCase))
                    {
                        tray.ShowBalloonTip(8000, "检测到游戏加加覆盖层", "请从托盘退出游戏加加后再使用 万象岛 Pro。", Forms.ToolTipIcon.Warning);
                        break;
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
