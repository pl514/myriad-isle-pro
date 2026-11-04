using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace IslandShot
{
    internal sealed class MouseHookService : IDisposable
    {
        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_QUIT = 0x0012;

        delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc proc, IntPtr module, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool GetMessage(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG message);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG message);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MSG
        {
            public IntPtr Hwnd;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        readonly Func<IntPtr> window;
        readonly Action clickedOutside;
        Thread thread;
        uint threadId;
        IntPtr hook;
        LowLevelMouseProc proc;
        volatile bool enabled;

        public MouseHookService(Func<IntPtr> window, Action clickedOutside)
        {
            this.window = window;
            this.clickedOutside = clickedOutside;
        }

        public bool Enabled { get { return enabled; } set { enabled = value; } }

        public void Start()
        {
            if (thread != null) return;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "IslandShot.MouseHook";
            thread.Start();
        }

        void Run()
        {
            threadId = GetCurrentThreadId();
            proc = Callback;
            hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            MSG message;
            while (GetMessage(out message, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && enabled && wParam == new IntPtr(WM_LBUTTONDOWN))
                {
                    POINT point;
                    RECT rect;
                    IntPtr hwnd = window();
                    if (GetCursorPos(out point) && hwnd != IntPtr.Zero && GetWindowRect(hwnd, out rect))
                    {
                        bool inside = point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
                        if (!inside) clickedOutside();
                    }
                }
            }
            catch (Exception) { }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            enabled = false;
            if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
            thread = null;
        }
    }
}
