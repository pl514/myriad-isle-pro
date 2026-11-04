using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace IslandShot
{
    /// <summary>全屏录屏：ffmpeg gdigrab 输出 MP4，热键再次触发停止。</summary>
    internal sealed class ScreenRecorder
    {
        Process process;
        readonly StringBuilder errors = new StringBuilder();
        string output;
        public bool Recording { get; private set; }
        public string Error { get; private set; }
        public event Action<string> Finished;

        public bool Start()
        {
            Error = null;
            string ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
            {
                Error = "没有找到 ffmpeg.exe，无法录制 MP4。请安装 ffmpeg 并加入 PATH。";
                return false;
            }
            try
            {
                string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "Recordings");
                Directory.CreateDirectory(directory);
                output = Path.Combine(directory, "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
                var info = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-y -hide_banner -loglevel error -f gdigrab -framerate 30 -draw_mouse 1 -i desktop -c:v libx264 -preset veryfast -pix_fmt yuv420p -movflags +faststart \"" + output + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process = new Process { StartInfo = info, EnableRaisingEvents = true };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (errors) { if (errors.Length < 8000) errors.AppendLine(e.Data); }
                };
                process.Exited += delegate { Finish(); };
                if (!process.Start())
                {
                    Error = "ffmpeg 启动失败。";
                    process = null;
                    return false;
                }
                process.BeginErrorReadLine();
                Recording = true;
                return true;
            }
            catch (Exception error)
            {
                Error = "录屏启动失败：" + error.Message;
                process = null;
                return false;
            }
        }

        public void Stop()
        {
            if (!Recording || process == null) return;
            Recording = false;
            Process active = process;
            try
            {
                active.StandardInput.WriteLine("q");
                active.StandardInput.Flush();
                active.StandardInput.Close();
            }
            catch (Exception) { }
            Task.Run(delegate
            {
                try
                {
                    if (!active.WaitForExit(5000))
                    {
                        try { active.Kill(); } catch (Exception) { }
                        try { active.WaitForExit(2000); } catch (Exception) { }
                    }
                }
                catch (Exception) { }
            });
        }

        void Finish()
        {
            Process active = process;
            if (active == null) return;
            string path = output;
            bool ok = false;
            try
            {
                active.WaitForExit(2000);
                ok = File.Exists(path) && new FileInfo(path).Length > 1024;
            }
            catch (Exception) { }
            if (!ok) path = null;
            Recording = false;
            process = null;
            Action<string> handler = Finished;
            if (handler != null) handler(path);
        }

        public static string FindFfmpeg()
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(path))
                {
                    foreach (string part in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string candidate = Path.Combine(part.Trim().Trim('"'), "ffmpeg.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
                string[] extra =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "ffmpeg.exe"),
                    @"C:\ffmpeg\bin\ffmpeg.exe"
                };
                foreach (string candidate in extra) if (File.Exists(candidate)) return candidate;
            }
            catch (Exception) { }
            return null;
        }
    }
}
