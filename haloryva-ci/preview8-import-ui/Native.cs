using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class PackageProbe {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern int GetPackageFullName(IntPtr process,ref uint length,StringBuilder name);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenEvent(uint rights,bool inherit,string name);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetEvent(IntPtr handle);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 public static int ExitEvent(string name,bool signal) {
   IntPtr h=OpenEvent(signal?2u:0x100000u,false,name);
   if(h==IntPtr.Zero) return Marshal.GetLastWin32Error();
   try { if(signal && !SetEvent(h)) return Marshal.GetLastWin32Error(); return 0; }
   finally { CloseHandle(h); }
 }
 delegate bool EnumProc(IntPtr hwnd,IntPtr state);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr state);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumProc callback,IntPtr state);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int length);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int length);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,UIntPtr count,StringBuilder text,uint flags,uint timeout,out UIntPtr result);
 static string Describe(IntPtr hwnd,bool child) {
   var title=new StringBuilder(2048); var cls=new StringBuilder(256);
   GetWindowText(hwnd,title,title.Capacity); GetClassName(hwnd,cls,cls.Capacity);
   if(child && title.Length==0) { UIntPtr result; SendMessageTimeout(hwnd,13,(UIntPtr)2048,title,2,200,out result); }
   return "hwnd="+hwnd.ToInt64().ToString("X")+" visible="+IsWindowVisible(hwnd)+" class="+cls+" text="+title;
 }
 public static string[] Windows(uint pid) {
   var lines=new List<string>();
   EnumWindows(delegate(IntPtr hwnd,IntPtr unused) {
     uint owner; GetWindowThreadProcessId(hwnd,out owner); if(owner!=pid) return true;
     lines.Add(Describe(hwnd,false));
     EnumChildWindows(hwnd,delegate(IntPtr child,IntPtr state) { if(lines.Count<100) lines.Add("  "+Describe(child,true)); return lines.Count<100; },IntPtr.Zero);
     return lines.Count<100;
   },IntPtr.Zero);
   return lines.ToArray();
 }
}
[ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IActivation {
 [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.LPWStr)]string args,uint options,out uint pid);
}
public static class ActivatePackaged {
 public static uint Launch(string id,string args) {
  object manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
  try { uint pid; int hr=((IActivation)manager).ActivateApplication(id,args,0,out pid);
    if(hr<0) Marshal.ThrowExceptionForHR(hr); return pid;
  } finally { Marshal.ReleaseComObject(manager); }
 }
}


public static class UiProbe {
 delegate bool EnumProc(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumProc cb,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h,uint m,UIntPtr n,StringBuilder b,uint f,uint t,out UIntPtr result);
 static string Text(IntPtr h){var b=new StringBuilder(2048);UIntPtr r;SendMessageTimeout(h,13,(UIntPtr)2048,b,2,200,out r);return b.ToString();}
 public static IntPtr Find(uint pid,string label){
  IntPtr found=IntPtr.Zero;
  EnumWindows(delegate(IntPtr h,IntPtr p){uint owner;GetWindowThreadProcessId(h,out owner);if(owner!=pid)return true;
   if(Text(h)==label && IsWindowVisible(h)) {found=h;return false;}
   EnumChildWindows(h,delegate(IntPtr c,IntPtr q){if(Text(c)==label && IsWindowVisible(c)){found=c;return false;}return true;},IntPtr.Zero);
   return found==IntPtr.Zero;
  },IntPtr.Zero);return found;
 }
 public static void Click(IntPtr h){
  if(h==IntPtr.Zero || !IsWindowVisible(h) || !IsWindowEnabled(h))throw new Exception("UI target unavailable");
  SetForegroundWindow(GetAncestor(h,2));
  // Target the actual WinForms keyboard command; no OS state is fabricated.
  if(!PostMessage(h,0x0100,(IntPtr)32,(IntPtr)1) || !PostMessage(h,0x0101,(IntPtr)32,(IntPtr)0xC0390001L))throw new Exception("Keyboard command not queued");
 }
}

public static class DialogProbe {
 delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc c,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h,EnumProc c,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr h,int id);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public static IntPtr Find(uint pid) {IntPtr result=IntPtr.Zero;EnumWindows(delegate(IntPtr h,IntPtr p){uint owner;GetWindowThreadProcessId(h,out owner);var c=new StringBuilder(128);GetClassName(h,c,c.Capacity);if(owner==pid&&IsWindowVisible(h)&&c.ToString()=="#32770"){result=h;return false;}return true;},IntPtr.Zero);return result;}
 public static string Content(IntPtr h){var list=new List<string>();var b=new StringBuilder(4096);GetWindowText(h,b,b.Capacity);list.Add(b.ToString());EnumChildWindows(h,delegate(IntPtr child,IntPtr p){var t=new StringBuilder(4096);GetWindowText(child,t,t.Capacity);if(t.Length>0)list.Add(t.ToString());return true;},IntPtr.Zero);return String.Join("\n",list.ToArray());}
 public static void Answer(IntPtr dialog,int id){var button=GetDlgItem(dialog,id);if(button==IntPtr.Zero||!IsWindowVisible(button))throw new Exception("Expected dialog button missing");if(!PostMessage(button,0xF5,IntPtr.Zero,IntPtr.Zero))throw new Exception("Dialog action failed");}
 public static void Close(IntPtr h){PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero);}
}
