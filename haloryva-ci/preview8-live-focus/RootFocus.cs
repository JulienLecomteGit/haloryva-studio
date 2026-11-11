using System;
using System.Text;
using System.Runtime.InteropServices;
public static class RootFocus {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] struct GUIINFO {
  public uint Size,Flags; public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret; public RECT CaretRect;
 }
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint tid,ref GUIINFO value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 public static IntPtr Root(IntPtr h) { if(h==IntPtr.Zero||!IsWindow(h))throw new Exception("Missing window");return GetAncestor(h,2); }
 public static uint Owner(IntPtr h) {uint p;GetWindowThreadProcessId(h,out p);return p;}
 public static string Describe(IntPtr h) {
  uint p;uint tid=GetWindowThreadProcessId(h,out p);var t=new StringBuilder(512);var c=new StringBuilder(256);
  GetWindowText(h,t,t.Capacity);GetClassName(h,c,c.Capacity);
  return "hwnd="+h.ToInt64().ToString("X")+" root="+(h==IntPtr.Zero?"0":Root(h).ToInt64().ToString("X"))+" pid="+p+" tid="+tid+" class="+c+" text="+t;
 }
 public static bool Confirm(IntPtr root) {
  if(root==IntPtr.Zero||Root(root)!=root||GetForegroundWindow()!=root)return false;
  uint p;uint tid=GetWindowThreadProcessId(root,out p);var g=new GUIINFO();g.Size=(uint)Marshal.SizeOf(typeof(GUIINFO));
  return GetGUIThreadInfo(tid,ref g)&&g.Active==root;
 }
 public static bool Activate(IntPtr root) {
  if(root==IntPtr.Zero||Root(root)!=root)throw new Exception("Only a real top-level window can be activated");
  ShowWindow(root,9);SetForegroundWindow(root);return Confirm(root);
 }
 public static void Minimize(IntPtr root) {
  if(root==IntPtr.Zero||Root(root)!=root)throw new Exception("Only top-level window");ShowWindow(root,6);
 }
}
