using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 投射到桌面（WebView2 原型）：花火样式
    //
    // 和旧 WPF 舞台不同，这个窗口把整个舞台交给本地网页：
    //   · Canvas 2D 画夜空、星光、纸片、彩纸、烟花粒子、发光大字、镜头；
    //   · HTML 画底栏和「歌词样式」面板；
    //   · C# 只负责 MediaPlayer/歌词快照和窗口层级，通过 WebMessage 桥接。
    //
    // 这是"最像 Folia 的形态"的第一步：网页渲染层以后可以继续接其余 17 个样式。
    // 本地页面在 build\stage-web，由 Build.ps1 复制。
    // ---------------------------------------------------------------------
    internal sealed class MusicDesktopWebStageWindow : Window
    {
        readonly Func<MusicStageSnapshot> snapshotProvider;
        readonly Action togglePlay;
        readonly Action nextTrack;
        readonly Action previousTrack;
        readonly Action<TimeSpan> seek;
        readonly Action cycleMode;
        readonly Action closedCallback;
        readonly Action raiseHost;
        readonly WebView2 view;
        readonly DispatcherTimer timer;
        readonly DispatcherTimer cursorTimer;
        bool initialized;
        bool ready;
        bool closed;
        bool failed;
        bool wallpaperSent;

        internal event Action Failed;

        internal MusicDesktopWebStageWindow(Func<MusicStageSnapshot> snapshotProvider, Action togglePlay,
            Action nextTrack, Action previousTrack, Action<TimeSpan> seek, Action cycleMode, Action closedCallback,
            Action raiseHost)
        {
            this.snapshotProvider = snapshotProvider;
            this.togglePlay = togglePlay;
            this.nextTrack = nextTrack;
            this.previousTrack = previousTrack;
            this.seek = seek;
            this.cycleMode = cycleMode;
            this.closedCallback = closedCallback;
            this.raiseHost = raiseHost;

            Title = "投射到桌面 · HANABI";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 6, 15));
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            view = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(5, 6, 15),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Content = view;

            timer = new DispatcherTimer(DispatcherPriority.Render);
            timer.Interval = TimeSpan.FromMilliseconds(90);
            timer.Tick += delegate { SendState(); };

            cursorTimer = new DispatcherTimer();
            cursorTimer.Interval = TimeSpan.FromSeconds(3.2);
            cursorTimer.Tick += delegate
            {
                cursorTimer.Stop();
                if (!closed) Cursor = Cursors.None;
            };

            Loaded += delegate
            {
                ApplyBounds();
                InitWebView();
                cursorTimer.Start();
            };

            SizeChanged += delegate { ApplyBounds(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
            Activated += delegate
            {
                if (raiseHost != null)
                {
                    try { raiseHost(); } catch (Exception) { }
                }
            };
            MouseMove += delegate
            {
                Cursor = Cursors.Arrow;
                cursorTimer.Stop();
                cursorTimer.Start();
            };
            Closed += delegate
            {
                closed = true;
                timer.Stop();
                cursorTimer.Stop();
                try { if (view != null) view.Dispose(); } catch (Exception) { }
                if (closedCallback != null)
                {
                    Action callback = closedCallback;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { callback(); } catch (Exception) { }
                    }));
                }
            };
        }

        void ApplyBounds()
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            double sh = SystemParameters.PrimaryScreenHeight;
            if (sw <= 0 || sh <= 0) return;
            if (Math.Abs(Left) > 0.5 || Math.Abs(Top) > 0.5) { Left = 0; Top = 0; }
            if (Math.Abs(Width - sw) > 0.5) Width = sw;
            if (Math.Abs(Height - sh) > 0.5) Height = sh;
        }

        async void InitWebView()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                string stageDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stage-web");
                if (!Directory.Exists(stageDir))
                {
                    Fail("缺少 stage-web 目录");
                    return;
                }
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "webview2-stage");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, folder, options);
                await view.EnsureCoreWebView2Async(env);
                CoreWebView2 core = view.CoreWebView2;
                if (core == null)
                {
                    Fail("WebView2 没有就绪");
                    return;
                }

                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;

                core.SetVirtualHostNameToFolderMapping("capsule.stage", stageDir, CoreWebView2HostResourceAccessKind.Allow);
                core.WebMessageReceived += OnWebMessage;
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs args)
                {
                    args.Handled = true;
                };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    initialized = true;
                };
                core.Navigate("https://capsule.stage/index.html");
            }
            catch (Exception exception)
            {
                Fail("WebView2 初始化失败：" + exception.Message);
            }
        }

        void Fail(string message)
        {
            if (failed || closed) return;
            failed = true;
            try { if (view != null) view.Visibility = Visibility.Collapsed; } catch (Exception) { }
            Action handler = Failed;
            if (handler != null)
            {
                try { handler(); } catch (Exception) { }
                return;
            }
            try { Close(); } catch (Exception) { }
        }

        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string json = args.WebMessageAsJson;
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                var message = serializer.Deserialize<Dictionary<string, object>>(json);
                if (message == null) return;
                string type = message.ContainsKey("type") ? Convert.ToString(message["type"]) : "";
                if (type == "ready")
                {
                    ready = true;
                    SendState();
                    SendWallpaper();
                    timer.Start();
                    return;
                }
                if (type == "transparent")
                {
                    bool transparent = false;
                    if (message.ContainsKey("value")) bool.TryParse(Convert.ToString(message["value"]), out transparent);
                    try
                    {
                        view.DefaultBackgroundColor = transparent
                            ? System.Drawing.Color.Transparent
                            : System.Drawing.Color.FromArgb(5, 6, 15);
                    }
                    catch (Exception) { }
                    return;
                }
                if (type == "toggle")
                {
                    if (togglePlay != null) togglePlay();
                    return;
                }
                if (type == "seek")
                {
                    double position = 0;
                    if (message.ContainsKey("position")) double.TryParse(Convert.ToString(message["position"]), out position);
                    if (seek != null) seek(TimeSpan.FromSeconds(Math.Max(0, position)));
                    return;
                }
                if (type == "next")
                {
                    if (nextTrack != null) nextTrack();
                    return;
                }
                if (type == "prev")
                {
                    if (previousTrack != null) previousTrack();
                    return;
                }
                if (type == "mode")
                {
                    if (cycleMode != null) cycleMode();
                    return;
                }
                if (type == "close")
                {
                    Close();
                }
            }
            catch (Exception) { }
        }

        void SendState()
        {
            if (closed || !ready || view == null || view.CoreWebView2 == null) return;
            try
            {
                MusicStageSnapshot s = snapshotProvider == null ? null : snapshotProvider();
                var payload = new Dictionary<string, object>();
                payload["type"] = "state";
                payload["songMid"] = s == null ? "" : (s.SongMid == null ? "" : s.SongMid);
                payload["title"] = s == null ? "" : (s.Title == null ? "" : s.Title);
                payload["artist"] = s == null ? "" : (s.Artist == null ? "" : s.Artist);
                payload["position"] = s == null ? 0 : s.Position.TotalSeconds;
                payload["duration"] = s == null ? 0 : s.Duration.TotalSeconds;
                payload["playing"] = s != null && s.Playing;
                var lyrics = new List<object>();
                if (s != null && s.Lyrics != null)
                {
                    for (int i = 0; i < s.Lyrics.Count; i++)
                    {
                        MusicStageLyric line = s.Lyrics[i];
                        if (line == null) continue;
                        var item = new Dictionary<string, object>();
                        item["time"] = line.Time.TotalSeconds;
                        item["text"] = line.Text == null ? "" : line.Text;
                        lyrics.Add(item);
                    }
                }
                payload["lyrics"] = lyrics;
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                string json = serializer.Serialize(payload);
                view.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception) { }
        }

        // WebView2 本身不能把逐像素透明直接透到桌面（WPF 分层窗口会黑/崩）。
        // 透明背景先用桌面的静态壁纸做底图，视觉上接近透明；真正叠加到桌面
        // 需要 Electron 壳。这个数据只发一次，避免每帧传输。
        void SendWallpaper()
        {
            if (wallpaperSent || closed || !ready || view == null || view.CoreWebView2 == null) return;
            wallpaperSent = true;
            try
            {
                string path = null;
                try
                {
                    object value = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null);
                    path = value == null ? null : Convert.ToString(value);
                }
                catch (Exception) { }
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                int width = Math.Max(640, (int)SystemParameters.PrimaryScreenWidth);
                int height = Math.Max(360, (int)SystemParameters.PrimaryScreenHeight);
                using (var source = System.Drawing.Image.FromFile(path))
                using (var bitmap = new System.Drawing.Bitmap(width, height))
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                using (var stream = new MemoryStream())
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    double scale = Math.Max((double)width / source.Width, (double)height / source.Height);
                    int dw = (int)Math.Ceiling(source.Width * scale);
                    int dh = (int)Math.Ceiling(source.Height * scale);
                    graphics.DrawImage(source, (width - dw) / 2, (height - dh) / 2, dw, dh);

                    var codec = FindJpegCodec();
                    if (codec == null)
                    {
                        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                    else
                    {
                        using (var parameters = new System.Drawing.Imaging.EncoderParameters(1))
                        {
                            parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                                System.Drawing.Imaging.Encoder.Quality, 86L);
                            bitmap.Save(stream, codec, parameters);
                        }
                    }
                    string base64 = Convert.ToBase64String(stream.ToArray());
                    var payload = new Dictionary<string, object>();
                    payload["type"] = "wallpaper";
                    payload["data"] = "data:image/jpeg;base64," + base64;
                    var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                    view.CoreWebView2.PostWebMessageAsJson(serializer.Serialize(payload));
                }
            }
            catch (Exception) { }
        }

        static System.Drawing.Imaging.ImageCodecInfo FindJpegCodec()
        {
            try
            {
                System.Drawing.Imaging.ImageCodecInfo[] codecs =
                    System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();
                for (int i = 0; i < codecs.Length; i++)
                {
                    if (string.Equals(codecs[i].MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                        return codecs[i];
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}
