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
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern uint GetLongPathName(string shortPath,System.Text.StringBuilder longPath,uint size);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr h,uint m,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll",SetLastError=true)] static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
    public static string CanonicalPath(string path) {
        var buffer=new System.Text.StringBuilder(32768);
        uint count=GetLongPathName(path,buffer,(uint)buffer.Capacity);
        if(count==0 || count>=buffer.Capacity) throw new Exception("Cannot normalize existing path: "+path);
        return System.IO.Path.GetFullPath(buffer.ToString()).TrimEnd(System.IO.Path.DirectorySeparatorChar);
    }
    static void Ready(IntPtr h) {
        if(!IsWindowVisible(h)||IsHungAppWindow(h))throw new Exception("Verified setup window unavailable");
    }
    static void Send(IntPtr h,uint message,IntPtr w,IntPtr l) {
        IntPtr result;
        if(SendMessageTimeout(h,message,w,l,2,3000,out result)==IntPtr.Zero)
            throw new Exception("Targeted setup message failed: "+Marshal.GetLastWin32Error());
    }
    // External window messages exercise the real form and its real install job.
    // This is targeted GUI automation, not a physical-desktop click validation.
    // Only the hash-verified setup worker HWND is supplied by the PowerShell driver.
    public static void Click(IntPtr h,int designX,int designY) {
        Ready(h);Rect r;if(!GetClientRect(h,out r))throw new Exception("GetClientRect failed");
        int x=(int)Math.Round(designX*(r.Right-r.Left)/860.0);
        int y=(int)Math.Round(designY*(r.Bottom-r.Top)/560.0);
        IntPtr point=new IntPtr((y<<16)|(x&65535));
        Send(h,0x200,IntPtr.Zero,point);
        Send(h,0x201,new IntPtr(1),point);
        Thread.Sleep(80);
        Send(h,0x202,IntPtr.Zero,point);
    }
    public static void CloseNormally(IntPtr h) {
        Ready(h);
        // Normal WM_CLOSE; the installer's existing critical-section close guard remains active.
        // Does not invoke Finish/LaunchRequested; no normal Haloryva launch is requested.
        if(!PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero))throw new Exception("Normal close request failed");
    }
    public static void Capture(IntPtr h,string file) {
        Ready(h);Rect r;if(!GetWindowRect(h,out r))throw new Exception("GetWindowRect failed");
        using(var b=new Bitmap(r.Right-r.Left,r.Bottom-r.Top))
        using(var g=Graphics.FromImage(b)) {
            IntPtr dc=g.GetHdc();bool ok;
            try {ok=PrintWindow(h,dc,2);}finally{g.ReleaseHdc(dc);}
            if(!ok)throw new Exception("Capture of the verified setup window failed");
            b.Save(file,ImageFormat.Png);
        }
    }
}

public sealed class SetupExitMonitor : IDisposable {
    IntPtr handle;
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public SetupExitMonitor(uint pid) {
        // Retain query/synchronize access while the process is still alive.
        handle=OpenProcess(0x00101000,false,pid);
        if(handle==IntPtr.Zero)throw new Exception("Cannot observe setup process exit: "+Marshal.GetLastWin32Error());
    }
    public uint ReadExitedCode() {
        if(handle==IntPtr.Zero||WaitForSingleObject(handle,0)!=0)throw new Exception("Setup process exit not confirmed");
        uint code;if(!GetExitCodeProcess(handle,out code))throw new Exception("Setup exit code unavailable: "+Marshal.GetLastWin32Error());
        return code;
    }
    public void Dispose() {if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
}
