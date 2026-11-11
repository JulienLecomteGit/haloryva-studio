using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CursorFlow
{
    internal static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        public const int WH_MOUSE_LL = 14, WH_KEYBOARD_LL = 13;
        public const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
        public const int WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
        public const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        public const int WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
        public const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
        public const uint GW_HWNDNEXT = 2;
        public const int ULW_ALPHA = 2, AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
        public const int GWL_EXSTYLE = -20, WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        public const uint GA_ROOT = 2;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const int CURSOR_SHOWING = 0x00000001;
        private const int IDC_IBEAM = 32513;
        private static readonly IntPtr systemTextCursor = LoadCursor(IntPtr.Zero, new IntPtr(IDC_IBEAM));
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002,
            SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int Width, Height; public SIZE(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }
        [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT
        {
            public POINT pt; public int mouseData, flags, time; public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        public static void ApplyDarkTitleBar(IntPtr hwnd)
        {
            // Windows 11 / Windows 10 récentes : 20, variante ancienne : 19.
            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
            }
            catch { /* les anciens Windows conservent leur titre standard */ }
        }
        // Windows 11 : coins arrondis natifs + ombre pour la fenêtre sans bordure.
        public static void ApplyRoundedCorners(IntPtr hwnd)
        {
            try { int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); }
            catch { /* Windows 10 : coins droits, sans incidence */ }
        }
        public const int WM_SETREDRAW = 0x000B;
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
        // Barres de défilement sombres (Windows 10 1809+), sans effet ailleurs.
        public static void ApplyDarkScrollbars(IntPtr hwnd)
        {
            try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
        }
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorInfo(ref CURSORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorResource);
        // Lecture du pointeur global, pas GetCursor() (limité au thread courant).
        public static bool IsSystemTextCursor()
        {
            if (systemTextCursor == IntPtr.Zero) return false;
            CURSORINFO info = new CURSORINFO();
            info.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
            return GetCursorInfo(ref info) && (info.flags & CURSOR_SHOWING) != 0 &&
                info.hCursor == systemTextCursor;
        }
        [StructLayout(LayoutKind.Sequential)] public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        // Une DIB 32 bits prémultipliée, créée uniquement au changement de taille,
        // remplace les GetHbitmap/SelectObject/DeleteObject de chaque image.
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO info,
            uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr objectHandle);
        // Clic synthétique rejoué quand un appui court doit garder son comportement Windows
        // (le bouton est retenu le temps de savoir s'il s'agit d'un appui long).
        public static readonly IntPtr OwnInputTag = new IntPtr(0x43463031);
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
        {
            public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public MOUSEINPUT mi; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        public static bool SendMouseClick(string button)
        {
            uint down, up, data = 0;
            switch (button)
            {
                case "Gauche": down = 0x0002; up = 0x0004; break;
                case "Droit": down = 0x0008; up = 0x0010; break;
                case "Milieu": down = 0x0020; up = 0x0040; break;
                case "X1": down = 0x0080; up = 0x0100; data = 1; break;
                case "X2": down = 0x0080; up = 0x0100; data = 2; break;
                default: return false;
            }
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = 0; inputs[0].mi.dwFlags = down; inputs[0].mi.mouseData = data; inputs[0].mi.dwExtraInfo = OwnInputTag;
            inputs[1].type = 0; inputs[1].mi.dwFlags = up; inputs[1].mi.mouseData = data; inputs[1].mi.dwExtraInfo = OwnInputTag;
            uint sent = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            // Partial insertion: never inject DOWN twice and never bypass UIPI.
            if (sent == 1)
            {
                INPUT[] release = new INPUT[] { inputs[1] };
                return SendInput(1, release, Marshal.SizeOf(typeof(INPUT))) == 1;
            }
            return sent == 2;
        }
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, int colorKey, ref BLENDFUNCTION blend, int flags);
    }

}
