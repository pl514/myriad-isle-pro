using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace IslandShot
{
    // 手机传图：局域网 HTTP 接收器。
    // 手机 App 扫码配对后，把相册里的照片 POST 到 http://<本机IP>:24680/upload?token=...
    // 收到就落到 data\PhoneInbox\，然后回调给主窗口收进胶囊。
    internal sealed class PhoneInbox : IDisposable
    {
        internal const int DefaultPort = 24680;
        const int MaxBytes = 64 * 1024 * 1024;

        readonly string folder;
        readonly Action<string> onFile;
        readonly Action<string> log;
        TcpListener listener;
        Thread worker;
        volatile bool running;

        internal string Token { get; private set; }
        internal int Port { get; private set; }
        internal bool IsRunning { get { return running; } }
        internal string Folder { get { return folder; } }
        internal int ReceivedCount { get; private set; }

        internal PhoneInbox(string inboxFolder, string token, int port, Action<string> onFile, Action<string> log)
        {
            folder = inboxFolder;
            Token = token;
            Port = port > 0 ? port : DefaultPort;
            this.onFile = onFile;
            this.log = log;
        }

        // ------------------------------------------------------------ 令牌 / 端口
        internal static string LoadOrCreateToken(string dataDir)
        {
            try
            {
                string path = Path.Combine(dataDir, "phone-token.txt");
                if (File.Exists(path))
                {
                    string old = File.ReadAllText(path).Trim();
                    if (old.Length >= 16) return old;
                }
                var buffer = new byte[16];
                using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider()) rng.GetBytes(buffer);
                var sb = new StringBuilder();
                for (int i = 0; i < buffer.Length; i++) sb.Append(buffer[i].ToString("x2"));
                string token = sb.ToString();
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                File.WriteAllText(path, token);
                return token;
            }
            catch (Exception)
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        internal static int LoadPort(string dataDir)
        {
            try
            {
                string path = Path.Combine(dataDir, "phone-port.txt");
                if (File.Exists(path))
                {
                    int port;
                    if (int.TryParse(File.ReadAllText(path).Trim(), out port) && port > 1024 && port < 65535) return port;
                }
            }
            catch (Exception) { }
            return DefaultPort;
        }

        internal static void SavePort(string dataDir, int port)
        {
            try
            {
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                File.WriteAllText(Path.Combine(dataDir, "phone-port.txt"), port.ToString());
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ 本机 IP
        internal static string LocalIPv4()
        {
            string fallback = "";
            try
            {
                foreach (NetworkInterface card in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (card.OperationalStatus != OperationalStatus.Up) continue;
                    if (card.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation info in card.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = info.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        if (ip.StartsWith("192.168.")) return ip;
                        if (ip.StartsWith("10.")) { if (fallback.Length == 0) fallback = ip; continue; }
                        if (ip.StartsWith("172."))
                        {
                            string[] parts = ip.Split('.');
                            int second;
                            if (parts.Length == 4 && int.TryParse(parts[1], out second) && second >= 16 && second <= 31)
                            {
                                if (fallback.Length == 0) fallback = ip;
                                continue;
                            }
                        }
                        if (fallback.Length == 0) fallback = ip;
                    }
                }
            }
            catch (Exception) { }
            return fallback;
        }

        // 列出所有可用的局域网地址（有网关的网卡排在前面）
        internal static System.Collections.Generic.List<string[]> LocalIPv4Candidates()
        {
            var list = new System.Collections.Generic.List<string[]>();
            try
            {
                foreach (NetworkInterface card in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (card.OperationalStatus != OperationalStatus.Up) continue;
                    if (card.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    bool hasGateway = false;
                    foreach (GatewayIPAddressInformation gateway in card.GetIPProperties().GatewayAddresses)
                    {
                        if (gateway.Address.AddressFamily == AddressFamily.InterNetwork) hasGateway = true;
                    }
                    foreach (UnicastIPAddressInformation info in card.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = info.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        list.Add(new string[] { ip, card.Name, hasGateway ? "1" : "0" });
                    }
                }
            }
            catch (Exception) { }
            list.Sort(delegate(string[] a, string[] b)
            {
                int ga = a[2] == "1" ? 0 : 1;
                int gb = b[2] == "1" ? 0 : 1;
                if (ga != gb) return ga.CompareTo(gb);
                bool pa = a[0].StartsWith("192.168.") || a[0].StartsWith("10.");
                bool pb = b[0].StartsWith("192.168.") || b[0].StartsWith("10.");
                if (pa != pb) return pa ? -1 : 1;
                return string.Compare(a[0], b[0], StringComparison.Ordinal);
            });
            return list;
        }

        internal string PairUri()
        {
            return "wanxiang://pair?ip=" + LocalIPv4() + "&port=" + Port + "&token=" + Token;
        }

        // ------------------------------------------------------------ 启动 / 停止
        internal bool Start()
        {
            if (running) return true;
            try
            {
                listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
                running = true;
                worker = new Thread(Loop);
                worker.IsBackground = true;
                worker.Name = "phone-inbox";
                worker.Start();
                Log("手机传图已启动：端口 " + Port + "，IP " + LocalIPv4());
                return true;
            }
            catch (Exception exception)
            {
                Log("手机传图启动失败：" + exception.Message);
                running = false;
                return false;
            }
        }

        internal void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); } catch (Exception) { }
            try { if (worker != null) worker.Join(500); } catch (Exception) { }
            listener = null;
            worker = null;
        }

        public void Dispose() { Stop(); }

        void Log(string message)
        {
            if (log != null) { try { log(message); } catch (Exception) { } }
        }

        void Loop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { Handle(client); });
                }
                catch (Exception)
                {
                    if (!running) break;
                    Thread.Sleep(200);
                }
            }
        }

        // ------------------------------------------------------------ 单次请求
        void Handle(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 15000;
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] head = ReadHeaders(stream);
                    if (head == null) return;
                    string text = Encoding.ASCII.GetString(head);
                    string[] lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string[] requestLine = lines[0].Split(' ');
                    if (requestLine.Length < 3) return;
                    string method = requestLine[0];
                    string target = requestLine[1];

                    Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon <= 0) continue;
                        headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                    }

                    string query = "";
                    int q = target.IndexOf('?');
                    if (q >= 0) { query = target.Substring(q + 1); target = target.Substring(0, q); }
                    Dictionary<string, string> args = ParseQuery(query);

                    if (target == "/ping" || target == "/")
                    {
                        Reply(stream, "200 OK", "ok");
                        return;
                    }

                    if (target != "/upload")
                    {
                        Reply(stream, "404 Not Found", "not found");
                        return;
                    }

                    if (method != "POST")
                    {
                        Reply(stream, "405 Method Not Allowed", "POST only");
                        return;
                    }

                    string token;
                    if (!args.TryGetValue("token", out token) || token != Token)
                    {
                        Log("手机传图：令牌不对，已拒绝");
                        Reply(stream, "403 Forbidden", "bad token");
                        return;
                    }

                    string lengthText;
                    int length;
                    if (!headers.TryGetValue("Content-Length", out lengthText) || !int.TryParse(lengthText, out length))
                    {
                        Reply(stream, "411 Length Required", "need content-length");
                        return;
                    }
                    if (length <= 0 || length > MaxBytes)
                    {
                        Reply(stream, "413 Payload Too Large", "too large");
                        return;
                    }

                    string name;
                    if (!args.TryGetValue("name", out name) || string.IsNullOrWhiteSpace(name)) name = "photo.jpg";
                    name = SanitizeName(name);

                    byte[] body = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        int n = stream.Read(body, read, length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != length)
                    {
                        Reply(stream, "400 Bad Request", "short body");
                        return;
                    }

                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    string path = Path.Combine(folder, stamp + "_" + name);
                    int suffix = 1;
                    while (File.Exists(path))
                    {
                        path = Path.Combine(folder, stamp + "-" + suffix + "_" + name);
                        suffix++;
                    }
                    File.WriteAllBytes(path, body);
                    ReceivedCount++;
                    Log("收到手机照片：" + Path.GetFileName(path) + "（" + (length / 1024) + " KB）");
                    Reply(stream, "200 OK", "ok");
                    if (onFile != null)
                    {
                        try { onFile(path); } catch (Exception) { }
                    }
                }
            }
            catch (Exception exception)
            {
                Log("手机传图出错：" + exception.Message);
            }
            finally
            {
                try { client.Close(); } catch (Exception) { }
            }
        }

        static byte[] ReadHeaders(NetworkStream stream)
        {
            var data = new List<byte>(1024);
            var one = new byte[1];
            while (data.Count < 16384)
            {
                int n = stream.Read(one, 0, 1);
                if (n <= 0) return null;
                data.Add(one[0]);
                // 检测 \r\n\r\n
                int c = data.Count;
                if (one[0] == 10)
                {
                    if (c >= 4 && data[c - 2] == 13 && data[c - 3] == 10 && data[c - 4] == 13) break;
                }
            }
            return data.ToArray();
        }

        static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return result;
            foreach (string pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string key = eq >= 0 ? pair.Substring(0, eq) : pair;
                string value = eq >= 0 ? pair.Substring(eq + 1) : "";
                result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace("+", " "));
            }
            return result;
        }

        static string SanitizeName(string name)
        {
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            name = name.Replace("/", "_").Replace("\\", "_").Trim();
            if (name.Length == 0) name = "photo.jpg";
            if (name.Length > 80) name = name.Substring(name.Length - 80);
            return name;
        }

        static void Reply(NetworkStream stream, string status, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body ?? "");
            string head = "HTTP/1.1 " + status + "\r\n"
                + "Content-Type: text/plain; charset=utf-8\r\n"
                + "Content-Length: " + bytes.Length + "\r\n"
                + "Connection: close\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
    }
}
