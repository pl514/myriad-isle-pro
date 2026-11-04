using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace IslandShot
{
    // 运行权限（完整性级别）工具。
    //
    // 本机实测结论：
    //   * 普通用户双击（explorer 启动）= Medium：托盘图标、把图片拖到微信都正常。
    //   * Low（沙箱 / 受控环境 / 被低权限父进程拉起）时，Windows 的 UIPI 会静默拒绝：
    //       - Shell_NotifyIcon(NIM_ADD) 直接返回 FALSE → 没有托盘图标
    //       - 拖到微信、资源管理器时 effect=0 → 放不进去
    //   * 管理员（High）也能用，但不是必须；而且从高权限拖到低权限目标同样会被拦。
    //
    // 所以这个项目不再依赖管理员：默认就是 asInvoker（普通用户），自启动也走 HKCU run。
    internal static class RuntimeAccess
    {
        const int TokenIntegrityLevel = 25;
        const int TokenElevation = 20;
        const int TokenQuery = 0x0008;

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int infoLength, out int returnLength);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        public static string ExePath
        {
            get
            {
                try { return Process.GetCurrentProcess().MainModule.FileName; }
                catch (Exception) { return null; }
            }
        }

        public static int IntegrityLevel
        {
            get
            {
                IntPtr token;
                if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token)) return -1;
                try
                {
                    IntPtr buffer = Marshal.AllocHGlobal(256);
                    try
                    {
                        int length;
                        if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, 256, out length)) return -1;
                        IntPtr sid = Marshal.ReadIntPtr(buffer, 0);
                        int count = Marshal.ReadByte(sid, 1);
                        int offset = 8 + 4 * (count - 1);
                        return Marshal.ReadInt32(sid, offset);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseHandle(token);
                }
            }
        }

        // 低完整性：托盘图标、拖放都会被系统拒绝。
        public static bool IsLow
        {
            get
            {
                int level = IntegrityLevel;
                return level > 0 && level <= 0x1000;
            }
        }

        // 是否以管理员（High）身份运行。不是必须的，仅用于提示文案。
        public static bool IsElevated
        {
            get
            {
                IntPtr token;
                if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token)) return false;
                try
                {
                    IntPtr buffer = Marshal.AllocHGlobal(16);
                    try
                    {
                        int length;
                        if (!GetTokenInformation(token, TokenElevation, buffer, 16, out length)) return false;
                        return Marshal.ReadInt32(buffer, 0) != 0;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseHandle(token);
                }
            }
        }

        public static string IntegrityName
        {
            get
            {
                int level = IntegrityLevel;
                if (level < 0) return "Unknown";
                if (level <= 0x1000) return "Low";
                if (level <= 0x2000) return "Medium";
                if (level <= 0x3000) return "High";
                if (level <= 0x4000) return "System";
                return "0x" + level.ToString("X");
            }
        }

        // 托盘菜单/提示里用的中文权限名。
        public static string ModeName
        {
            get
            {
                if (IsLow) return "低权限";
                return IsElevated ? "管理员" : "普通用户";
            }
        }

        // 让资源管理器（普通权限）替我们启动一个新的自身实例。
        // 适用于“当前是管理员，想回到普通用户”的场景；低权限环境下调用会被系统拒绝，
        // 所以调用方不要把它当成万能解药。
        public static bool LaunchNormalInstance()
        {
            try
            {
                string exe = ExePath;
                if (string.IsNullOrEmpty(exe)) return false;
                ProcessStartInfo info = new ProcessStartInfo("explorer.exe", "\"" + exe + "\"");
                info.UseShellExecute = true;
                DragNative.Log("launch normal instance | " + exe);
                Process.Start(info);
                return true;
            }
            catch (Exception exception)
            {
                DragNative.Log("launch normal instance failed: " + exception.Message);
                return false;
            }
        }

        public static int RunSchtasks(string arguments, out string output)
        {
            output = "";
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"),
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(info))
                {
                    if (process == null) return -1;
                    output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(6000)) { try { process.Kill(); } catch (Exception) { } return -1; }
                    return process.ExitCode;
                }
            }
            catch (Exception exception)
            {
                output = exception.Message;
                return -1;
            }
        }

        // 旧版本用“最高权限”计划任务做自启动，每次登录都会以管理员运行。
        // 这里顺手清掉，换成 HKCU\...\Run（普通权限，不弹 UAC）。
        public static void RemoveLegacyStartupTask()
        {
            try
            {
                string output;
                if (RunSchtasks("/Query /TN \"CapsuleImagePro\"", out output) == 0)
                {
                    string deleted;
                    RunSchtasks("/Delete /TN \"CapsuleImagePro\" /F", out deleted);
                    DragNative.Log("legacy elevated startup task removed");
                }
            }
            catch (Exception) { }
        }

        // 仅在用户主动点“以管理员权限重启”时使用；正常使用不需要。
        public static bool TryRestartElevated(string arguments)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(ExePath);
                info.UseShellExecute = true;
                info.Verb = "runas";
                if (!string.IsNullOrEmpty(arguments)) info.Arguments = arguments;
                Process.Start(info);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
