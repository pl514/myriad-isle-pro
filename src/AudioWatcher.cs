using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 声音触发：微信/QQ 收到消息时只放提示音、不发系统通知（已实测确认：
    // 通知库无记录、无任务栏闪烁、无窗口事件，但音频会话峰值会跳到 0.8+）。
    //
    // 所以这里直接监听它们的音频会话峰值 —— 和窗口状态无关，也不需要任何权限。
    //
    // 已验证（2026-11-07）：微信 4.2 提示音 → pid 33236 峰值 0.870；
    // QQ 关闭声音时无峰值，所以 QQ 没声音就检测不到（符合预期）。
    // ---------------------------------------------------------------------
    internal sealed class AudioWatcher : IDisposable
    {
        const int CLSCTX_ALL = 23;
        const float PeakThreshold = 0.02f;
        const int PollIntervalMs = 120;
        const int DebounceMs = 5000;

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr sessionControl);
            int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr audioVolume);
            int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
            int RegisterSessionNotification(IntPtr notification);
            int UnregisterSessionNotification(IntPtr notification);
            int RegisterDuckNotification(string sessionId, IntPtr notification);
            int UnregisterDuckNotification(IntPtr notification);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            int GetCount(out int count);
            int GetSession(int index, out IAudioSessionControl session);
        }

        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl { }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            int GetState(out int state);
            int GetDisplayName(out IntPtr name);
            int SetDisplayName(string name, IntPtr eventContext);
            int GetIconPath(out IntPtr path);
            int SetIconPath(string path, IntPtr eventContext);
            int GetGroupingParam(out Guid grouping);
            int SetGroupingParam(ref Guid grouping, IntPtr eventContext);
            int RegisterAudioSessionNotification(IntPtr notification);
            int UnregisterAudioSessionNotification(IntPtr notification);
            int GetSessionIdentifier(out IntPtr id);
            int GetSessionInstanceIdentifier(out IntPtr id);
            int GetProcessId(out int pid);
            int IsSystemSoundsSession();
            int SetDuckingPreference(bool optOut);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            int GetPeakValue(out float peak);
            int GetMeteringChannelCount(out int channelCount);
            int GetChannelsPeakValues(int channelCount, [Out] float[] peaks);
            int QueryHardwareSupport(out int mask);
        }

        readonly Action<string> onMessageSound;
        Thread worker;
        volatile bool stopping;
        readonly Dictionary<int, string> processNames = new Dictionary<int, string>();
        readonly Dictionary<int, DateTime> lastLogged = new Dictionary<int, DateTime>();
        DateTime namesRefreshed = DateTime.MinValue;
        readonly Dictionary<string, DateTime> lastFired = new Dictionary<string, DateTime>();

        public AudioWatcher(Action<string> onMessageSound)
        {
            this.onMessageSound = onMessageSound;
        }

        public void Start()
        {
            if (worker != null) return;
            worker = new Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "CapsuleAudioWatcher";
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        public void Dispose()
        {
            stopping = true;
        }

        void RefreshNames()
        {
            if ((DateTime.Now - namesRefreshed).TotalSeconds < 5) return;
            namesRefreshed = DateTime.Now;
            processNames.Clear();
            try
            {
                // 用 GetProcesses 而不是 GetProcessById：微信的进程可能拒绝按 id 打开
                foreach (Process p in Process.GetProcesses())
                {
                    try { processNames[p.Id] = p.ProcessName; }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        static string MessengerForProcessName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // 只认主进程，避免把 WeChatAppEx（小程序宿主）/ 名字里带 QQ 的其他进程算成消息
            if (string.Equals(name, "Weixin", StringComparison.OrdinalIgnoreCase)) return "wechat";
            if (string.Equals(name, "WeChat", StringComparison.OrdinalIgnoreCase)) return "wechat";
            if (string.Equals(name, "QQ", StringComparison.OrdinalIgnoreCase)) return "qq";
            if (name.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) >= 0) return "dsh";
            return null;
        }

        static void DebugLog(string message)
        {
            try
            {
                string folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "audio.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }

        void Loop()
        {
            IAudioSessionManager2 manager = null;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return;
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object managerObject;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out managerObject) != 0) return;
                manager = (IAudioSessionManager2)managerObject;
            }
            catch (Exception) { return; }

            while (!stopping)
            {
                try
                {
                    RefreshNames();
                    IAudioSessionEnumerator sessionEnum;
                    if (manager.GetSessionEnumerator(out sessionEnum) == 0 && sessionEnum != null)
                    {
                        int count;
                        sessionEnum.GetCount(out count);
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                IAudioSessionControl control;
                                if (sessionEnum.GetSession(i, out control) != 0 || control == null) continue;
                                var control2 = control as IAudioSessionControl2;
                                if (control2 == null) continue;
                                int pid;
                                if (control2.GetProcessId(out pid) != 0 || pid == 0) continue;

                                string processName;
                                if (!processNames.TryGetValue(pid, out processName)) continue;
                                var meterAny = control as IAudioMeterInformation;
                                if (meterAny != null)
                                {
                                    float anyPeak;
                                    if (meterAny.GetPeakValue(out anyPeak) == 0 && anyPeak >= 0.05f)
                                    {
                                        DateTime last;
                                        if (!lastLogged.TryGetValue(pid, out last) || (DateTime.Now - last).TotalMilliseconds > 2000)
                                        {
                                            lastLogged[pid] = DateTime.Now;
                                            DebugLog("peak from " + processName + " pid=" + pid + " peak=" + anyPeak.ToString("0.000"));
                                        }
                                    }
                                }

                                string app = MessengerForProcessName(processName);
                                if (app == null) continue;

                                var meter = control as IAudioMeterInformation;
                                if (meter == null) continue;
                                float peak;
                                if (meter.GetPeakValue(out peak) != 0) continue;
                                if (peak < PeakThreshold) continue;

                                DateTime previous;
                                if (lastFired.TryGetValue(app, out previous) &&
                                    (DateTime.Now - previous).TotalMilliseconds < DebounceMs) continue;
                                lastFired[app] = DateTime.Now;

                                DebugLog("audio from " + processName + " pid=" + pid + " peak=" + peak.ToString("0.000"));
                                Action<string> handler = onMessageSound;
                                if (handler != null) handler(app);
                            }
                            catch (Exception) { }
                        }
                    }
                }
                catch (Exception) { }

                Thread.Sleep(PollIntervalMs);
            }
        }
    }
}
