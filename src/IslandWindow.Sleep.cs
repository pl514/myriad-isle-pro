using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 胶囊“收起 = 休眠”
    //
    // 收起胶囊（热键 / 托盘菜单）后屏幕上没有任何界面，这时没必要继续保持常驻渲染：
    //   1. 停掉胶囊的呼吸光晕、高光扫过、眨眼这些无限循环动画 —— 它们会让 WPF
    //      渲染线程一直出帧，工作集刚压下去马上又涨回来，还白白烧掉 20%+ CPU；
    //   2. 把卡片缩略图从内存里卸掉（文件路径还在卡片的 Tag 上），展开时再解码回填；
    //   3. 把 AI 气泡的视觉树也卸掉（聊天文字仍保存在 ChatSession 里），
    //      下次打开 AI 面板再按数据重绘；
    //   4. 强制 GC（含大对象堆整理）后调用 EmptyWorkingSet，把工作集页还给系统。
    //      Windows 让进程休眠时也是这么做的，任务管理器里的“内存”会立刻掉下来。
    //
    // 唤醒只恢复动画；缩略图 / AI 气泡等真正展开面板时才懒加载，所以唤醒是瞬时的。
    // ---------------------------------------------------------------------
    internal sealed partial class IslandWindow
    {
        [DllImport("psapi.dll", SetLastError = true)]
        static extern bool EmptyWorkingSet(IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

        bool sleeping;
        DispatcherTimer sleepTimer;
        DispatcherTimer trimTimer;
        bool aiChatVisualsReleased;
        bool reloadingCards;

        // 收起胶囊后先等收起动画落地（300ms 左右），再真正进入休眠。
        void ScheduleSleep()
        {
            if (!capsuleClosed) return;
            if (sleepTimer == null)
            {
                sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                sleepTimer.Tick += delegate
                {
                    sleepTimer.Stop();
                    if (!capsuleClosed) return;
                    EnterSleep();
                };
            }
            sleepTimer.Stop();
            sleepTimer.Start();
        }

        void CancelScheduledSleep()
        {
            if (sleepTimer != null) sleepTimer.Stop();
            if (trimTimer != null) trimTimer.Stop();
        }

        void EnterSleep()
        {
            if (sleeping || !capsuleClosed) return;
            sleeping = true;
            try
            {
                // 1. 停掉所有会持续出帧的装饰动画和一次性定时器
                StopCapsuleAmbient();
                if (capsuleEyes != null) capsuleEyes.StopBlinking();
                if (clipboardTimer != null) clipboardTimer.Stop();
                if (dragTimer != null) dragTimer.Stop();
                if (collapseTimer != null) collapseTimer.Stop();

                // 2. 卸掉缩略图 / AI 气泡
                int released = ReleaseCardImages();
                ReleaseAiChatVisuals();

                Program.Boot("sleep enter | cards unloaded=" + released + " | " + MemoryText());
            }
            catch (Exception) { }

            // 3. 等最后一帧提交完再 GC + 压工作集（再补一次以接住过渡期新碰到的页）
            if (trimTimer == null)
            {
                trimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
                trimTimer.Tick += delegate
                {
                    trimTimer.Stop();
                    TrimWorkingSet();
                };
            }
            trimTimer.Stop();
            trimTimer.Start();
        }

        void WakeFromSleep()
        {
            CancelScheduledSleep();
            if (!sleeping) return;
            sleeping = false;
            try
            {
                if (capsuleEyes != null) capsuleEyes.StartBlinking();
                StartCapsuleAmbient();
                Program.Boot("sleep wake | " + MemoryText());
            }
            catch (Exception) { }
        }

        // 休眠期间新加进来的图片不解码，卡片先留空，展开时统一回填。
        int ReleaseCardImages()
        {
            if (cards == null) return 0;
            int count = 0;
            foreach (UIElement child in cards.Children)
            {
                var card = child as Border;
                if (card == null) continue;
                string path = card.Tag as string;
                if (string.IsNullOrEmpty(path) || IsVideoFile(path)) continue;
                foreach (Image image in FindImages(card))
                {
                    if (image.Source == null) continue;
                    image.Source = null;
                    count++;
                    break;
                }
            }
            return count;
        }

        // 展开面板时把缩略图补回来：后台线程解码 + Freeze，逐张回填，不卡展开动画。
        void ReloadCardImages()
        {
            if (cards == null || reloadingCards) return;
            var jobs = new List<KeyValuePair<Border, string>>();
            foreach (UIElement child in cards.Children)
            {
                var card = child as Border;
                if (card == null) continue;
                string path = card.Tag as string;
                if (string.IsNullOrEmpty(path) || IsVideoFile(path)) continue;
                foreach (Image image in FindImages(card))
                {
                    if (image.Source == null)
                    {
                        jobs.Add(new KeyValuePair<Border, string>(card, path));
                        break;
                    }
                }
            }
            if (jobs.Count == 0) return;
            reloadingCards = true;
            Task.Run(delegate
            {
                foreach (KeyValuePair<Border, string> job in jobs)
                {
                    BitmapSource bitmap = DecodeCardThumbnail(job.Value);
                    if (bitmap == null) continue;
                    Border target = job.Key;
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            try
                            {
                                if (cards == null || !cards.Children.Contains(target)) return;
                                foreach (Image image in FindImages(target))
                                {
                                    if (image.Source != null) continue;
                                    image.Source = bitmap;
                                    break;
                                }
                            }
                            catch (Exception) { }
                        }), DispatcherPriority.Background);
                    }
                    catch (Exception) { return; }
                }
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate { reloadingCards = false; }), DispatcherPriority.Background);
                }
                catch (Exception) { }
            });
        }

        static BitmapSource DecodeCardThumbnail(string path)
        {
            try
            {
                var loaded = new BitmapImage();
                loaded.BeginInit();
                loaded.CacheOption = BitmapCacheOption.OnLoad;
                loaded.DecodePixelWidth = 480;
                loaded.UriSource = new Uri(path, UriKind.Absolute);
                loaded.EndInit();
                loaded.Freeze();
                return ToOpaque(loaded);
            }
            catch (Exception) { return null; }
        }

        static IEnumerable<Image> FindImages(DependencyObject root)
        {
            if (root == null) yield break;
            var image = root as Image;
            if (image != null) { yield return image; yield break; }
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(root); }
            catch (Exception) { yield break; }
            for (int i = 0; i < count; i++)
            {
                foreach (Image found in FindImages(VisualTreeHelper.GetChild(root, i))) yield return found;
            }
        }

        // ------------------------------------------------------------ AI 气泡

        void ReleaseAiChatVisuals()
        {
            aiChatVisualsReleased = false;
            if (aiStreaming || aiMessageList == null) return;
            if (aiMessageList.Children.Count == 0) return;
            aiMessageList.Children.Clear();
            foreach (ChatSession session in aiSessions)
            {
                foreach (ChatMessage message in session.Messages)
                {
                    message.Body = null;
                    message.Bubble = null;
                }
            }
            aiChatVisualsReleased = true;
        }

        void RestoreAiChatVisuals()
        {
            if (!aiChatVisualsReleased) return;
            aiChatVisualsReleased = false;
            RenderChat();
        }

        // ------------------------------------------------------------ 压工作集

        void TrimWorkingSet()
        {
            if (!sleeping) return;
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            }
            catch (Exception) { }
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    EmptyWorkingSet(process.Handle);
                    SetProcessWorkingSetSize(process.Handle, new IntPtr(-1), new IntPtr(-1));
                }
            }
            catch (Exception) { }
            Program.Boot("sleep done | " + MemoryText());
        }

        static string MemoryText()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    return (process.WorkingSet64 / (1024 * 1024)) + "MB(ws) / "
                        + (process.PrivateMemorySize64 / (1024 * 1024)) + "MB(private)";
                }
            }
            catch (Exception) { return "?"; }
        }
    }
}
