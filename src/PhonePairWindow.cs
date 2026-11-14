using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IslandShot
{
    // 手机传图 · 配对窗口：显示二维码，手机扫码后就能直接分享照片过来。
    internal sealed class PhonePairWindow : Window
    {
        readonly string ip;
        readonly int port;
        readonly string token;

        public PhonePairWindow(string ip, int port, string token)
        {
            this.ip = ip;
            this.port = port;
            this.token = token;

            Title = "手机传图 · 配对";
            Width = 470;
            Height = 660;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(9, 11, 20));
            FontFamily = new FontFamily("Microsoft YaHei UI");
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = true;

            var root = new Grid();
            Content = root;

            try
            {
                var web = new Microsoft.Web.WebView2.Wpf.WebView2();
                root.Children.Add(web);
                Loaded += delegate { LoadPage(web); };
            }
            catch (Exception)
            {
                var fallback = new TextBlock
                {
                    Text = "缺少 WebView2 运行时，无法显示二维码。\n\n请在手机 App 里手动填写：\nIP: " + ip + "\n端口: " + port + "\n令牌: " + token,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(28),
                    VerticalAlignment = VerticalAlignment.Center
                };
                root.Children.Add(fallback);
            }
        }

        async void LoadPage(Microsoft.Web.WebView2.Wpf.WebView2 web)
        {
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qrcode");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string html = Path.Combine(folder, "pair.html");
                File.WriteAllText(html, BuildHtml(), new UTF8Encoding(false));

                await web.EnsureCoreWebView2Async(null);
                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                web.Source = new Uri("file:///" + html.Replace('\\', '/'));
            }
            catch (Exception exception)
            {
                Title = "手机传图 · 二维码加载失败：" + exception.Message;
            }
        }

        string BuildHtml()
        {
            string uri = "wanxiang://pair?ip=" + ip + "&port=" + port + "&token=" + token;

            var others = new StringBuilder();
            List<string[]> candidates = PhoneInbox.LocalIPv4Candidates();
            foreach (string[] item in candidates)
            {
                if (item[0] == ip) continue;
                others.Append("<div class='cand'>" + item[1] + "：<b>" + item[0] + "</b><span class='note'>（扫码连不上就换这个）</span></div>");
            }
            string otherBlock = others.Length == 0 ? "" : "<div class='cands'>" + others + "</div>";

            string html = @"<!DOCTYPE html>
<html lang=""zh-CN""><head><meta charset=""utf-8"">
<style>
  * { box-sizing: border-box; }
  body { margin:0; padding:24px 22px; background:#090b14; color:#eef0f8;
         font-family:'Microsoft YaHei UI','Segoe UI',sans-serif; text-align:center; }
  h1 { font-size:19px; margin:0 0 6px; font-weight:600; }
  p.sub { margin:0 0 16px; font-size:12.5px; color:#9aa2b8; line-height:1.6; }
  .qr { width:246px; height:246px; margin:0 auto 14px; background:#fff; padding:12px;
        border-radius:16px; display:flex; align-items:center; justify-content:center;
        box-shadow:0 14px 40px rgba(0,0,0,.5); }
  .addr { font-family:Consolas,monospace; font-size:13px; color:#7fe0ff; margin:0 0 2px; }
  .steps { margin:16px auto 0; max-width:330px; text-align:left; font-size:12.5px; color:#b9bed3; line-height:2; }
  .steps b { color:#eef0f8; }
  .tip { margin-top:12px; font-size:11.5px; color:#6c7390; line-height:1.7; }
  .cands { margin-top:10px; font-size:11.5px; color:#8a92a8; line-height:1.9; }
  .cand b { color:#c8cede; font-family:Consolas,monospace; }
  .cand .note { color:#5e6678; }
</style></head>
<body>
  <h1>手机扫这个码</h1>
  <p class=""sub"">扫一次就记住，以后在相册里直接分享给「万象岛」</p>
  <div class=""qr""><div id=""q""></div></div>
  <p class=""addr"">" + ip + ":" + port + @"</p>
  <div class=""steps"">
    <div>① 手机装好「万象岛」App</div>
    <div>② 打开 App → 点「扫描电脑上的二维码」</div>
    <div>③ 之后相册选图 → 分享 → 万象岛</div>
  </div>
  " + otherBlock + @"
  <p class=""tip"">手机和电脑要连同一个 Wi-Fi；<br>照片不会上传到任何服务器，只走局域网。</p>
  <script src=""qrcode.min.js""></script>
  <script>
    new QRCode(document.getElementById('q'), {
      text: " + "\"" + uri + "\"" + @",
      width: 222, height: 222,
      colorDark: '#0b0d16', colorLight: '#ffffff',
      correctLevel: QRCode.CorrectLevel.M
    });
  </script>
</body></html>";
            return html;
        }
    }
}