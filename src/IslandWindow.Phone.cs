using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace IslandShot
{
    // 手机传图 · 主窗口接线：启动接收器、把收到的照片收进胶囊、打开配对二维码。
    internal sealed partial class IslandWindow
    {
        PhoneInbox phoneInbox;
        PhonePairWindow phonePairWindow;

        internal static string PhoneDataDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); }
        }

        void StartPhoneInbox()
        {
            try
            {
                string dataDir = PhoneDataDir;
                string token = PhoneInbox.LoadOrCreateToken(dataDir);
                int port = PhoneInbox.LoadPort(dataDir);
                string folder = Path.Combine(dataDir, "PhoneInbox");

                if (phoneInbox != null) { try { phoneInbox.Dispose(); } catch (Exception) { } }
                phoneInbox = new PhoneInbox(folder, token, port,
                    delegate(string path)
                    {
                        // 后台线程 → UI 线程：直接当作一张新图片收进胶囊
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            try
                            {
                                AddImage(path);
                                SetExpanded(false);
                            }
                            catch (Exception exception) { PhoneLog("收图失败：" + exception.Message); }
                        }), DispatcherPriority.Background);
                    },
                    delegate(string message) { PhoneLog(message); });
                phoneInbox.Start();
            }
            catch (Exception exception)
            {
                PhoneLog("启动手机传图失败：" + exception.Message);
            }
        }

        static void PhoneLog(string message)
        {
            try
            {
                string dataDir = PhoneDataDir;
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                string path = Path.Combine(dataDir, "phone.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        void OpenPhonePair()
        {
            try
            {
                if (phoneInbox == null || !phoneInbox.IsRunning) StartPhoneInbox();
                if (phoneInbox == null)
                {
                    MessageBox.Show("手机传图启动失败，详情见 data\\phone.log", "万象岛 Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (phonePairWindow != null)
                {
                    try { phonePairWindow.Activate(); } catch (Exception) { }
                    phonePairWindow.Show();
                    return;
                }

                phonePairWindow = new PhonePairWindow(PhoneInbox.LocalIPv4(), phoneInbox.Port, phoneInbox.Token);
                phonePairWindow.Closed += delegate { phonePairWindow = null; };
                phonePairWindow.Show();
            }
            catch (Exception exception)
            {
                PhoneLog("打开配对窗口失败：" + exception.Message);
            }
        }
    }
}
