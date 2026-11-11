using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class AppTrace : IDisposable {
    public sealed class Item {
        public string Kind, TimeUtc, Name, CommandLine, ReadError;
        public uint Pid, ParentPid, ExitStatus;
    }
    readonly List<Item> records = new List<Item>();
    readonly ManagementEventWatcher starts, stops;
    public AppTrace() {
        starts = new ManagementEventWatcher("SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName='Haloryva.exe'");
        stops = new ManagementEventWatcher("SELECT * FROM Win32_ProcessStopTrace WHERE ProcessName='Haloryva.exe'");
        starts.EventArrived += delegate(object sender, EventArrivedEventArgs e) {
            Item r = Read(e, "start");
            lock(records) records.Add(r);
            try {
                using(var p = new ManagementObject("Win32_Process.Handle='" + r.Pid + "'")) {
                    p.Get(); string command = p["CommandLine"] as string;
                    lock(records) r.CommandLine = command;
                }
            } catch(Exception ex) { lock(records) r.ReadError = ex.GetType().Name; }
        };
        stops.EventArrived += delegate(object sender, EventArrivedEventArgs e) {
            Item r = Read(e, "stop"); r.ExitStatus = Convert.ToUInt32(e.NewEvent["ExitStatus"]);
            lock(records) records.Add(r);
        };
        stops.Start(); starts.Start();
    }
    static Item Read(EventArrivedEventArgs e, string kind) {
        return new Item { Kind=kind, TimeUtc=DateTime.UtcNow.ToString("o"),
          Name=Convert.ToString(e.NewEvent["ProcessName"]), Pid=Convert.ToUInt32(e.NewEvent["ProcessID"]),
          ParentPid=Convert.ToUInt32(e.NewEvent["ParentProcessID"]) };
    }
    public Item[] Snapshot() { lock(records) return records.ToArray(); }
    public void Dispose() { starts.Stop(); stops.Stop(); starts.Dispose(); stops.Dispose(); }
}
public static class SetupUi {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern uint GetLongPathName(string shortPath,System.Text.StringBuilder longPath,uint size);
    public static string CanonicalPath(string path) {
        var buffer=new System.Text.StringBuilder(32768);
        uint count=GetLongPathName(path,buffer,(uint)buffer.Capacity);
        if(count==0 || count>=buffer.Capacity) throw new Exception("Cannot normalize existing path: "+path);
        return System.IO.Path.GetFullPath(buffer.ToString()).TrimEnd(System.IO.Path.DirectorySeparatorChar);
    }

    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref Point p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
    public static void Focus(IntPtr h) {
        if(!IsWindowVisible(h) || IsHungAppWindow(h)) throw new Exception("Setup window unavailable");
        SetForegroundWindow(h); Thread.Sleep(150);
        if(GetForegroundWindow()!=h) throw new Exception("Setup foreground not confirmed");
    }
    public static void Click(IntPtr h, int designX, int designY) {
        Focus(h); Rect r; if(!GetClientRect(h,out r)) throw new Exception("GetClientRect failed");
        Point p=new Point { X=(int)Math.Round(designX*(r.Right-r.Left)/860.0), Y=(int)Math.Round(designY*(r.Bottom-r.Top)/560.0) };
        if(!ClientToScreen(h,ref p)) throw new Exception("ClientToScreen failed");
        SetCursorPos(p.X,p.Y); Thread.Sleep(120);
        mouse_event(2,0,0,0,UIntPtr.Zero); Thread.Sleep(80); mouse_event(4,0,0,0,UIntPtr.Zero);
    }
    // Escape uses the installer's normal close path; it never requests a normal app launch.
    public static void Escape(IntPtr h) {
        Focus(h); keybd_event(0x1B,0,0,UIntPtr.Zero); Thread.Sleep(60); keybd_event(0x1B,0,2,UIntPtr.Zero);
    }
    public static void Capture(IntPtr h,string file) {
        Focus(h); Thread.Sleep(200); Rect r;
        if(!GetWindowRect(h,out r)) throw new Exception("GetWindowRect failed");
        using(var b=new Bitmap(r.Right-r.Left,r.Bottom-r.Top))
        using(var g=Graphics.FromImage(b)) {
            g.CopyFromScreen(r.Left,r.Top,0,0,b.Size); b.Save(file,ImageFormat.Png);
        }
    }
}
