using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace IslandShot.Probe
{
    // A real OLE drop target in its own process.  It records exactly what the
    // IslandShot drag hands over, which is the same contract WeChat / Paint /
    // browsers consume.
    internal static class Program
    {
        const string ResultPath = @"E:\deepseek1\IslandShot\test\probe-result.txt";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ProbeForm());
        }

        internal sealed class ProbeForm : Form
        {
            readonly TextBox output;
            bool seenDrag;

            public ProbeForm()
            {
                Text = "IslandShot Drop Probe";
                Width = 620;
                Height = 460;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(1180, 400);
                TopMost = true;
                BackColor = Color.FromArgb(24, 30, 38);
                AllowDrop = true;

                var title = new Label();
                title.Text = "IslandShot 拖拽接收探针（独立进程 / OLE 原生拖放）";
                title.ForeColor = Color.FromArgb(235, 242, 250);
                title.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
                title.SetBounds(16, 12, 580, 26);
                Controls.Add(title);

                var hint = new Label();
                hint.Text = "把胶囊卡片图片拖进这个窗口，下面会显示收到的全部格式与数据。";
                hint.ForeColor = Color.FromArgb(150, 170, 190);
                hint.Font = new Font("Microsoft YaHei UI", 9F);
                hint.SetBounds(16, 42, 580, 22);
                Controls.Add(hint);

                output = new TextBox();
                output.Multiline = true;
                output.ReadOnly = true;
                output.ScrollBars = ScrollBars.Vertical;
                output.BackColor = Color.FromArgb(16, 21, 28);
                output.ForeColor = Color.FromArgb(150, 230, 180);
                output.Font = new Font("Consolas", 9F);
                output.SetBounds(16, 72, 570, 340);
                Controls.Add(output);

                DragEnter += OnDragEnterOver;
                DragOver += OnDragEnterOver;
                DragDrop += OnDragDrop;
                DragLeave += delegate { Append("DragLeave"); };
            }

            void OnDragEnterOver(object sender, DragEventArgs e)
            {
                StringBuilder text = new StringBuilder();
                text.Append("DragEnter/Over | formats: ");
                string[] formats = e.Data.GetFormats();
                text.Append(string.Join(", ", formats));
                if (!seenDrag)
                {
                    seenDrag = true;
                    Append(text.ToString());
                    WriteResult(text.ToString());
                }
                e.Effect = DragDropEffects.Copy;
            }

            void OnDragDrop(object sender, DragEventArgs e)
            {
                StringBuilder report = new StringBuilder();
                report.AppendLine("Drop | effect=" + e.Effect);
                report.AppendLine("Formats: " + string.Join(", ", e.Data.GetFormats()));

                try
                {
                    if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    {
                        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                        foreach (string file in files)
                        {
                            report.AppendLine("FileDrop -> " + file + " | exists=" + File.Exists(file));
                        }
                    }
                }
                catch (Exception exception) { report.AppendLine("FileDrop error: " + exception.Message); }

                try
                {
                    if (e.Data.GetDataPresent(DataFormats.Bitmap))
                    {
                        object raw = e.Data.GetData(DataFormats.Bitmap);
                        Bitmap bitmap = raw as Bitmap;
                        report.AppendLine("Bitmap -> " + (bitmap == null ? "null" : bitmap.Width + "x" + bitmap.Height + " " + bitmap.PixelFormat));
                    }
                    else report.AppendLine("Bitmap -> not offered");
                }
                catch (Exception exception) { report.AppendLine("Bitmap error: " + exception.Message); }

                foreach (string format in new string[] { "PNG", "image/png" })
                {
                    try
                    {
                        if (!e.Data.GetDataPresent(format)) { report.AppendLine(format + " -> not offered"); continue; }
                        object raw = e.Data.GetData(format);
                        MemoryStream stream = raw as MemoryStream;
                        if (stream != null)
                        {
                            byte[] bytes = stream.ToArray();
                            report.AppendLine(format + " -> " + bytes.Length + " bytes, signature=" + Hex(bytes, 8));
                        }
                        else report.AppendLine(format + " -> " + (raw == null ? "null" : raw.GetType().FullName));
                    }
                    catch (Exception exception) { report.AppendLine(format + " error: " + exception.Message); }
                }

                Append(report.ToString());
                WriteResult(report.ToString());
            }

            void Append(string text)
            {
                output.AppendText("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + text + Environment.NewLine);
            }

            static string Hex(byte[] bytes, int count)
            {
                StringBuilder text = new StringBuilder();
                for (int i = 0; i < Math.Min(count, bytes.Length); i++) text.Append(bytes[i].ToString("X2") + " ");
                return text.ToString().Trim();
            }

            static void WriteResult(string text)
            {
                try { File.AppendAllText(ResultPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + text + Environment.NewLine); }
                catch (Exception) { }
            }
        }
    }
}
