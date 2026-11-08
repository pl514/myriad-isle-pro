using System;
using System.Threading;
using System.Windows;
using System.Runtime.InteropServices;

namespace IslandShot
{
    internal static class Program
    {
        // 低权限进程对 %TEMP% 往往没有写权限（Windows 会对那里做 no-write-up），
        // 所以日志按“低权限临时目录 → 普通临时目录 → 程序目录\data”顺序找一个能写的落点。
        static string logFolder;
        static bool quietStartup;

        internal static string LogFolder
        {
            get
            {
                if (logFolder != null) return logFolder;
                string[] candidates = new string[]
                {
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Low"),
                    System.IO.Path.GetTempPath(),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data")
                };
                foreach (string candidate in candidates)
                {
                    try
                    {
                        if (!System.IO.Directory.Exists(candidate)) System.IO.Directory.CreateDirectory(candidate);
                        string probe = System.IO.Path.Combine(candidate, "write-probe.tmp");
                        System.IO.File.WriteAllText(probe, "1");
                        System.IO.File.Delete(probe);
                        logFolder = candidate;
                        return logFolder;
                    }
                    catch (Exception) { }
                }
                logFolder = System.IO.Path.GetTempPath();
                return logFolder;
            }
        }

        // 启动/退出轨迹。任何异常都吞掉，绝不影响主流程。
        internal static void Boot(string text)
        {
            Append("MyriadIslePro-boot.log", text);
        }

        internal static void Append(string fileName, string text)
        {
            try
            {
                string path = System.IO.Path.Combine(LogFolder, fileName);
                if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 65536) System.IO.File.Delete(path);
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine);
            }
            catch (Exception) { }
        }

        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();

        static void WaitForShell()
        {
            for (int i = 0; i < 60; i++)
            {
                if (GetShellWindow() != IntPtr.Zero) return;
                Thread.Sleep(500);
            }
        }

        // 单实例。这里多等最多 2.5 秒：托盘菜单「以普通权限重新启动」会先拉起新进程再退出，
        // 新进程需要等旧进程释放互斥体，否则会误以为“已经在运行”直接退出。
        static Mutex AcquireInstance(string name, out bool created, int timeoutMilliseconds)
        {
            created = false;
            int waited = 0;
            while (true)
            {
                try
                {
                    var mutex = new Mutex(true, name, out created);
                    if (created) return mutex;
                    mutex.Dispose();
                }
                catch (UnauthorizedAccessException)
                {
                    // 已有一个更高权限（管理员）实例持有互斥体，本进程无权打开。
                    // 这种情况不能静默退出，否则用户双击后“什么都没发生”。
                    created = false;
                    Boot("mutex access denied -> an elevated instance is running");
                    if (!quietStartup)
                    {
                        System.Windows.MessageBox.Show(
                            "已经有一个以管理员权限运行的 万象岛 Pro 在运行。\n\n" +
                            "请先右键托盘图标退出它（或在任务管理器里结束 “MyriadIslePro.exe”），" +
                            "然后再双击「启动 万象岛 Pro.cmd」以普通权限启动。",
                            "万象岛 Pro",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    }
                    return null;
                }
                if (waited >= timeoutMilliseconds) return null;
                Thread.Sleep(200);
                waited += 200;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Boot("APP DOMAIN EX: " + e.ExceptionObject);
            };
            try
            {
                Boot("main enter | mode=" + RuntimeAccess.ModeName + " | low=" + RuntimeAccess.IsLow + " | logs=" + LogFolder);
                quietStartup = args != null && Array.IndexOf(args, "--autostart") >= 0;
                Run(args);
                Boot("main return");
            }
            catch (Exception exception)
            {
                Boot("MAIN EX: " + exception);
                throw;
            }
        }

        static void Run(string[] args)
        {
            WaitForShell();
#if OUTSIDE_CLICK_TEST
            const string mutexName = "MyriadIslePro.OutsideClickTest.SingleInstance";
#else
            const string mutexName = "MyriadIslePro.SingleInstance";
#endif
            bool created;
            Mutex mutex = AcquireInstance(mutexName, out created, 2500);
            try
            {
                Boot("mutex created=" + created);
                if (mutex == null || !created) return;

                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    Boot("DISPATCHER EX: " + e.Exception);
                    Append("MyriadIslePro-error.log", e.Exception.ToString());
                    e.Handled = true;
                };
                Boot("constructing window");
                var window = new IslandWindow();
                Boot("window constructed");
                app.MainWindow = window;
                window.Loaded += delegate { Boot("window.loaded"); };
                window.Closed += delegate { Boot("window.closed"); };
                window.Show();
                Boot("window shown");
                app.Exit += delegate { Boot("app.exit"); };
                System.AppDomain.CurrentDomain.ProcessExit += delegate { Boot("process.exit"); };
                app.Run();
                Boot("app.run returned");
            }
            finally
            {
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch (Exception) { }
                    mutex.Dispose();
                }
            }
        }
    }
}
