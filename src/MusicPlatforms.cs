using System;
using System.IO;
using System.Text;
using System.Windows.Media;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 音乐平台的静态描述：登录页地址、登录成功的判据、品牌配色。
    //
    // 这一层只管两件事：
    //   1. 点平台卡片时，登录窗该打开哪个地址；
    //   2. 登录窗里怎么判断"这个人已经登录了"。
    // 歌单、播放地址一律不在这里（属于下一步），到时候按 Id 分支即可。
    //
    // 为什么用 cookie 判断登录：这几个平台都没有对个人开发者开放的 OAuth，
    // 唯一稳的路子是让用户在自己的账号里登录一次，然后读同一次会话的 cookie。
    // cookie 由 WebView2 自己保管在 data\music\webview2-<id>\ 里，不外发、不写日志。
    // ---------------------------------------------------------------------
    internal sealed class MusicPlatform
    {
        public string Id;
        public string Name;
        public string LoginUrl;
        public string Note;            // 登录卡片上那行小字
        public string Badge;           // 圆形图标里的字（没有官方 logo 素材，用首字代替，换机器不会变豆腐块）
        public string CookieUri;       // 在这个地址下查 cookie
        public string[] CookieNames;   // 命中任意一个且值非空 => 已登录
        public bool NativeQr;          // true = 有原生扫码接口，二维码直接画在面板里（不用开网页）
        public string ProfileUrl;      // "我的音乐 / 我创建的歌单"页（歌单导入用）
        public string PlaylistUrlPrefix; // 歌单详情页前缀，后面拼歌单 Id
        public double LoginWidth;      // 登录窗尺寸（0 = 用默认 900x800）
        public double LoginHeight;
        public double Zoom;            // 网页缩放（0 = 1.0），把二维码放大一点
        public Color ColorFrom;
        public Color ColorTo;
    }

    internal static class MusicPlatforms
    {
        // 顺序即登录卡片上的顺序
        static readonly MusicPlatform[] list = new MusicPlatform[]
        {
            new MusicPlatform
            {
                Id = "netease",
                Name = "网易云音乐",
                Badge = "云",
                LoginUrl = "https://music.163.com/#/login",
                Note = "App 扫码登录",
                CookieUri = "https://music.163.com/",
                CookieNames = new string[] { "MUSIC_U" },
                NativeQr = true,
                ColorFrom = Color.FromRgb(233, 62, 62),
                ColorTo = Color.FromRgb(255, 128, 128)
            },
            new MusicPlatform
            {
                Id = "qq",
                Name = "QQ 音乐",
                Badge = "Q",
                // 直接开 QQ 互联的授权卡（y.qq.com 登录弹窗里那个 iframe 的地址）：
                // 它本身就是一张独立的登录卡，不用加载整个音乐站点，登录完会跳回 y.qq.com 把 cookie 种下。
                LoginUrl = "https://graph.qq.com/oauth2.0/authorize?response_type=code&client_id=100497308" +
                           "&redirect_uri=https%3A%2F%2Fy.qq.com%2Fportal%2Fwx_redirect.html%3Flogin_type%3D1" +
                           "%26surl%3Dhttps%253A%252F%252Fy.qq.com%252Fn%252Fryqq%252F&state=state&display=pc" +
                           "&scope=get_user_info%2Cget_app_friends",
                Note = "QQ / 微信扫码",
                CookieUri = "https://y.qq.com/",
                CookieNames = new string[] { "qm_keyst", "qqmusic_key" },
                LoginWidth = 520,
                LoginHeight = 680,
                Zoom = 1.35,
                ProfileUrl = "https://y.qq.com/n/ryqq_v2/profile/create",
                PlaylistUrlPrefix = "https://y.qq.com/n/ryqq_v2/playlist/",
                ColorFrom = Color.FromRgb(49, 194, 124),
                ColorTo = Color.FromRgb(126, 222, 160)
            },
            new MusicPlatform
            {
                Id = "kugou",
                Name = "酷狗音乐",
                Badge = "酷",
                // 酷狗的登录框本身就是一个独立页面（探针从 www.kugou.com 的弹窗 iframe 里拿到的），
                // 直接开它，连首页导航都不用加载。
                LoginUrl = "https://login-user.kugou.com/login/?appid=1014&ref=https://www.kugou.com/reg/web/" +
                           "&redirect_uri=https://staticssl.kugou.com/common/html/login/regok.html&callback=UsLoginCallback",
                Note = "手机 App 扫码",
                CookieUri = "https://www.kugou.com/",
                // 只认 KuGoo：酷狗给游客也会发 token/kg_mid，把那些当登录态会"假登录"
                CookieNames = new string[] { "KuGoo" },
                NativeQr = true,
                LoginWidth = 520,
                LoginHeight = 600,
                Zoom = 1.25,
                ColorFrom = Color.FromRgb(37, 155, 246),
                ColorTo = Color.FromRgb(110, 199, 255)
            },
            new MusicPlatform
            {
                Id = "qishui",
                Name = "汽水音乐",
                Badge = "汽",
                LoginUrl = "https://music.douyin.com/",
                Note = "抖音账号扫码",
                CookieUri = "https://music.douyin.com/",
                CookieNames = new string[] { "passport_assist_user", "sessionid", "sessionid_ss" },
                NativeQr = true,
                ColorFrom = Color.FromRgb(35, 213, 197),
                ColorTo = Color.FromRgb(126, 240, 226)
            }
        };

        internal static MusicPlatform[] All { get { return list; } }

        internal static MusicPlatform Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < list.Length; i++)
            {
                if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase)) return list[i];
            }
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // 登录记忆：data\music-session.txt，一行一个 key=value。
    //
    // 真正的登录态在 WebView2 的 cookie 里（data\music\webview2-<id>\），
    // 这个文件只是给面板一个"上次登的是哪个平台"的初值，让下次开机不用干等网页加载。
    // 下一步接歌单时会用 cookie 复核一次，复核失败就把这里清掉。
    // ---------------------------------------------------------------------
    internal static class MusicSession
    {
        static string FilePath
        {
            get { return Path.Combine(AiConfigStore.DataDir, "music-session.txt"); }
        }

        /// <summary>上次登录的平台 Id；空字符串 = 没登录过。</summary>
        internal static string Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return "";
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string text = line == null ? "" : line.Trim();
                    if (text.Length == 0 || text.StartsWith("#")) continue;
                    int split = text.IndexOf('=');
                    if (split <= 0) continue;
                    if (!string.Equals(text.Substring(0, split).Trim(), "platform", StringComparison.OrdinalIgnoreCase)) continue;
                    string id = text.Substring(split + 1).Trim();
                    return MusicPlatforms.Find(id) == null ? "" : id;
                }
            }
            catch (Exception) { }
            return "";
        }

        internal static void Save(string platformId)
        {
            try
            {
                if (!Directory.Exists(AiConfigStore.DataDir)) Directory.CreateDirectory(AiConfigStore.DataDir);
                var lines = new string[]
                {
                    "# 万象岛 Pro 音乐面板（一行一个 key=value）",
                    "# platform：上次登录的平台（netease / qq / kugou / qishui）",
                    "platform=" + (platformId == null ? "" : platformId),
                    "since=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        internal static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception) { }
        }
    }
}
