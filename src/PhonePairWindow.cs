using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IslandShot
{
    // 手机传图 · 配对窗口：显示二维码，可切换不同网卡的地址。
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
            Height = 690;
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
            var addrs = new StringBuilder();
            List<string[]> candidates = PhoneInbox.LocalIPv4Candidates();
            bool hasCurrent = false;
            int index = 0;
            foreach (string[] item in candidates)
            {
                string uri = "wanxiang://pair?ip=" + item[0] + "&port=" + port + "&token=" + token;
                if (addrs.Length > 0) addrs.Append(",");
                addrs.Append("{ip:\"" + item[0] + "\",name:\"" + item[1].Replace("\"", "") + "\",uri:\"" + uri + "\"}");
                if (item[0] == ip) { hasCurrent = true; index = addrs.Length; }
            }
            if (!hasCurrent)
            {
                string uri = "wanxiang://pair?ip=" + ip + "&port=" + port + "&token=" + token;
                if (addrs.Length > 0) addrs.Append(",");
                addrs.Append("{ip:\"" + ip + "\",name:\"当前网卡\",uri:\"" + uri + "\"}");
            }

            string html = @"<!DOCTYPE html>
<html lang=""zh-CN""><head><meta charset=""utf-8"">
<style>
  * { box-sizing: border-box; }
  body { margin:0; padding:22px 20px; background:#090b14; color:#eef0f8;
         font-family:'Microsoft YaHei UI','Segoe UI',sans-serif; text-align:center; }
  h1 { font-size:19px; margin:0 0 6px; font-weight:600; }
  p.sub { margin:0 0 14px; font-size:12.5px; color:#9aa2b8; line-height:1.6; }
  .qr { width:238px; height:238px; margin:0 auto 12px; background:#fff; padding:11px;
        border-radius:16px; display:flex; align-items:center; justify-content:center;
        box-shadow:0 14px 40px rgba(0,0,0,.5); }
  .addr { font-family:Consolas,monospace; font-size:13px; color:#7fe0ff; margin:0 0 10px; }
  .alts { display:flex; flex-wrap:wrap; gap:6px; justify-content:center; margin:0 auto 6px; max-width:330px; }
  .alts button { font-family:inherit; font-size:11px; color:#c8cede; background:#151827;
                 border:1px solid #2a2f42; border-radius:8px; padding:5px 9px; cursor:pointer; }
  .alts button.on { border-color:#5ad7ff; color:#7fe0ff; }
  .steps { margin:12px auto 0; max-width:330px; text-align:left; font-size:12.5px; color:#b9bed3; line-height:2; }
  .tip { margin-top:10px; font-size:11.5px; color:#6c7390; line-height:1.7; }
</style></head>
<body>
  <h1>手机扫这个码</h1>
  <p class=""sub"">扫一次就记住，以后在相册里直接分享给「万象岛」</p>
  <div class=""qr""><div id=""q""></div></div>
  <p class=""addr"" id=""addr"">" + ip + ":" + port + @"</p>
  <div class=""alts"" id=""alts""></div>
  <div class=""steps"">
    <div>① 手机装好「万象岛」App</div>
    <div>② 打开 App → 点「扫描电脑上的二维码」</div>
    <div>③ 之后相册选图 → 分享 → 万象岛</div>
  </div>
  <p class=""tip"">手机和电脑要连同一个网络（同一 Wi-Fi 或电脑热点）。<br>扫了连不上？点上面另一个地址换一张码。</p>
  <script src=""qrcode.min.js""></script>
  <script>
    var ADDRS = [" + addrs.ToString() + @"];
    function show(i) {
      var box = document.getElementById('q');
      box.innerHTML = '';
      new QRCode(box, {
        text: ADDRS[i].uri, width: 214, height: 214,
        colorDark: '#0b0d16', colorLight: '#ffffff',
        correctLevel: QRCode.CorrectLevel.M
      });
      document.getElementById('addr').textContent = ADDRS[i].ip + ':PORTNUM';
      var btns = document.querySelectorAll('#alts button');
      for (var k = 0; k < btns.length; k++) btns[k].className = (k === i ? 'on' : '');
    }
    (function () {
      var host = document.getElementById('alts');
      for (var i = 0; i < ADDRS.length; i++) {
        (function (idx) {
          var b = document.createElement('button');
          b.textContent = ADDRS[idx].name + ' · ' + ADDRS[idx].ip;
          b.onclick = function () { show(idx); };
          host.appendChild(b);
        })(i);
      }
      show(0);
    })();
  </script>
</body></html>";
            return html.Replace("PORTNUM", port.ToString());
        }
    }
}