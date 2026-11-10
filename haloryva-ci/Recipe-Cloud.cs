using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Test-only driver. Loads the preserved preview binary; never rebuilds production code.
internal static class Recipe
{
    static string folder, mode; static StreamWriter log; static Assembly app; static object controller;
    static Process target; static IntPtr targetWindow; static int code; static Point original;
    static readonly System.Collections.Generic.HashSet<ushort> heldKeys=new System.Collections.Generic.HashSet<ushort>();
    static uint heldMouse,heldData;
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("SbieDll.dll", CharSet=CharSet.Unicode)] static extern int SbieApi_QueryProcess(IntPtr pid, StringBuilder box, IntPtr image, IntPtr sid, IntPtr session);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern short VkKeyScanEx(char c, IntPtr layout);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    delegate bool EnumWindow(IntPtr hwnd,IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd,EnumWindow callback,IntPtr param);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int size);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] values, int size);
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSE mouse; [FieldOffset(0)] public KEY key; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSE { public int x,y; public uint data,flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEY { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
    static void Guard()
    {
        string temp=Environment.GetEnvironmentVariable("RUNNER_TEMP");
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true" ||
           Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")!="github-hosted" ||
           String.IsNullOrEmpty(temp) || !Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).StartsWith(Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new Exception("REFUSED: isolated GitHub-hosted runner required.");
    }
    static void Write(string message) { log.WriteLine(DateTime.UtcNow.ToString("o")+" "+message); log.Flush(); }
    static void Check(bool ok,string label) { Write((ok?"PASS ":"FAIL ")+label); if(!ok) throw new Exception(label); }
    static object Get(object obj,string name) { var t=obj.GetType(); var f=t.GetField(name,Flags); return f!=null?f.GetValue(obj):t.GetProperty(name,Flags).GetValue(obj,null); }
    static void Set(object obj,string name,object value) { obj.GetType().GetField(name,Flags).SetValue(obj,value); }
    static object Call(object obj,string name,params object[] args) { return obj.GetType().GetMethod(name,Flags).Invoke(obj,args); }
    static object Static(string type,string name,params object[] args) { return app.GetType("CursorFlow."+type).GetMethod(name,Flags).Invoke(null,args); }
    static void FocusGuard() { if(target==null || target.HasExited || GetForegroundWindow()!=targetWindow) throw new Exception("ABORT: test target lost foreground; no further inputs."); }
    static void Input(INPUT input) { FocusGuard(); if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))!=1) throw new Exception("SendInput refused: "+Marshal.GetLastWin32Error()); }
    static void Mouse(uint flags,uint data=0) { Input(new INPUT {data=new UNION {mouse=new MOUSE {flags=flags,data=data}}}); heldMouse=(flags==2||flags==8||flags==32||flags==128)?flags*2:0; heldData=data; }
    static void Key(ushort key,bool up=false) { Input(new INPUT {type=1,data=new UNION {key=new KEY {vk=key,flags=(up?2u:0u)|(key==0xa5?1u:0u)}}}); if(up) heldKeys.Remove(key); else heldKeys.Add(key); }
    static async Task Chord(ushort key) { Key(0xa2); Key(0xa4); Key(key); Key(key,true); Key(0xa4,true); Key(0xa2,true); await Task.Delay(250); }
    static int Query(int field) { return (int)SendMessage(targetWindow,0x8001,(IntPtr)field,IntPtr.Zero); }
    static void Move(int x,int y) { FocusGuard(); Cursor.Position=new Point(x,y); }
    static async Task Click(uint down,uint up,uint data=0,int hold=40) { Mouse(down,data); await Task.Delay(hold); Mouse(up,data); await Task.Delay(200); }
    [STAThread] static int Main(string[] args)
    {
        try { Guard(); } catch { return 90; }
        mode=args.Length>0?args[0]:"probe";
        if(mode=="target") { SetProcessDPIAware(); Application.Run(new Target()); return 0; }
        folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RecipeHaloryvaRuns",args[1]);
        Directory.CreateDirectory(folder); log=new StreamWriter(Path.Combine(folder,"recipe.log"),true,Encoding.UTF8);
        try {
            Write("ENV GitHub-hosted; mode="+mode+"; pid="+Process.GetCurrentProcess().Id);
            if(mode=="probe") { Write("PASS verified sandbox identity; no Haloryva launched, no input generated"); return 0; }
            app=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Haloryva.exe"));
            if(mode=="smoke") { Smoke(); return code; }
            if(mode!="functional") throw new Exception("Unknown mode");
            SetProcessDPIAware(); original=Cursor.Position;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Static("TrailTrial","Initialize",(object)new[]{"--trail-session",Path.Combine(folder,"profile")});
            // New known profile inside virtualized storage, never the normal installed profile.
            var config=Activator.CreateInstance(app.GetType("CursorFlow.Config"));
            Static("ConfigStore","Save",config);
            controller=Activator.CreateInstance(app.GetType("CursorFlow.CursorController"),true);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            SynchronizationContext.Current.Post(async delegate {
                try { await Functional(); Write("COMPLETE functional automated checks passed"); }
                catch(Exception ex) { code=1; Write("ERROR "+ex); }
                finally {
                    foreach(ushort key in heldKeys) SendInput(1,new[]{new INPUT {type=1,data=new UNION {key=new KEY {vk=key,flags=2u|(key==0xa5?1u:0u)}}}},Marshal.SizeOf(typeof(INPUT)));
                    if(heldMouse!=0) SendInput(1,new[]{new INPUT {data=new UNION {mouse=new MOUSE {flags=heldMouse,data=heldData}}}},Marshal.SizeOf(typeof(INPUT)));
                    if(target!=null && !target.HasExited) PostMessage(target.MainWindowHandle,0x0010,IntPtr.Zero,IntPtr.Zero);
                    // Release only our generated keys/buttons; target focus failures stop before more gestures.
                    Cursor.Position=original;
                    if(controller!=null) Call(controller,"Exit");
                    Application.ExitThread();
                }
            },null);
            Application.Run((ApplicationContext)controller);
        } catch(Exception ex) { code=1; Write("ERROR "+ex); }
        finally { Write("EXIT "+code); log.Dispose(); }
        return code;
    }
    static void Smoke()
    {
        var p=Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Haloryva.exe"),"--silent --trail-session \""+Path.Combine(folder,"smoke-profile")+"\""){UseShellExecute=false});
        try {
            Thread.Sleep(4000);
            Check(!p.HasExited && p.Responding,"Original process alive and responsive (not yet proof of initialization)");
            Write("Diagnostic title: "+p.MainWindowTitle);
            if(p.MainWindowHandle!=IntPtr.Zero) EnumChildWindows(p.MainWindowHandle,delegate(IntPtr h,IntPtr unused) { var s=new StringBuilder(2048); GetWindowText(h,s,s.Capacity); if(s.Length>0) Write("Diagnostic control: "+s); return true; },IntPtr.Zero);
            using(var ev=EventWaitHandle.OpenExisting("Local\\HaloryvaStudio.Exit."+p.Id)) ev.Set();
            Check(p.WaitForExit(10000) && p.ExitCode==0,"Original binary exits through its own shutdown path");
            Check(File.Exists(Path.Combine(folder,"smoke-profile/settings.json")),"Isolated profile saved on shutdown");
        } finally { if(!p.HasExited) { p.Kill(); Write("FAIL test child stopped after incomplete shutdown or startup error"); code=1; } p.Dispose(); }
    }
    static async Task Functional()
    {
        object hooks=Get(controller,"hooks");
        Check((bool)Get(hooks,"Installed"),"Real mouse and keyboard hooks installed");
        target=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"target"){UseShellExecute=false});
        for(int n=0;n<40 && target.MainWindowHandle==IntPtr.Zero;n++) { await Task.Delay(100); target.Refresh(); }
        targetWindow=target.MainWindowHandle;
        SetForegroundWindow(targetWindow);
        for(int n=0;n<150 && GetForegroundWindow()!=targetWindow;n++) await Task.Delay(100);
        FocusGuard();
        Check(Marshal.SizeOf(typeof(INPUT))==40,"x64 SendInput layout");
        foreach(int k in new[]{1,2,4,5,6,0x10,0x11,0x12,0x5b,0x5c})
            Check((GetAsyncKeyState(k)&0x8000)==0,"Input neutral before automation: "+k);
        Move(350,350); await Task.Delay(100);
        Check(Query(9)==13579,"Target query channel responds");
        Write("Pointer="+Cursor.Position+" target="+targetWindow);
        int before=Query(0),released=Query(1);
        await Click(2,4); Write("left="+Query(0)+"/"+Query(1)); await Click(8,16); Write("right="+Query(0)+"/"+Query(1)); await Click(32,64); Write("middle="+Query(0)+"/"+Query(1)); await Click(128,256,1); Write("X1="+Query(0)+"/"+Query(1)); await Click(128,256,2); Write("X2="+Query(0)+"/"+Query(1));
        Check(Query(0)-before==5 && Query(1)-released==5,"Five normal buttons reach external target once, paired DOWN/UP");
        object config=Get(controller,"Options");
        Set(config,"ButtonMiddleLong","Halo : activer / arrêter"); Set(config,"LongPressMs",400); Call(controller,"Changed"); await Task.Delay(100);
        before=Query(0); released=Query(1); bool ring=(bool)Get(config,"Ring");
        await Click(32,64,0,100);
        Check(Query(0)==before+1 && Query(1)==released+1 && (bool)Get(config,"Ring")==ring,"Short held gesture replays exactly one native click");
        before=Query(0); released=Query(1);
        await Click(32,64,0,650);
        Check(Query(0)==before && Query(1)==released && (bool)Get(config,"Ring")!=ring,"Long press toggles once without leaking click");
        await Task.Delay(500); Check((bool)Get(config,"Ring")!=ring,"Long action not repeated after release");
        // Real keyboard route, using the target's existing layout without changing system settings.
        uint pid; uint tid=GetWindowThreadProcessId(targetWindow,out pid); var layout=GetKeyboardLayout(tid);
        short mapping=VkKeyScanEx('@',layout);
        if(mapping!=-1 && ((mapping>>8)&7)==6) {
            int chars=Query(2); Key(0xa2); Key(0xa5); Key((ushort)(mapping&255)); Key((ushort)(mapping&255),true); Key(0xa5,true); Key(0xa2,true); await Task.Delay(250);
            Check(Query(2)==chars+1 && Query(3)=='@',"AltGr produces @ in external target");
            bool clicks=(bool)Get(config,"Clicks"); Key(0xa2); Key(0xa5); Key(0x43); Key(0x43,true); Key(0xa5,true); Key(0xa2,true); await Task.Delay(250);
            Check((bool)Get(config,"Clicks")==clicks,"AltGr+C does not trigger Ctrl+Alt+C");
        } else Write("NOT_RUN AltGr text: target layout has no Ctrl+Alt mapping for @");
        Set(config,"AnnotationTool","Stylo"); Set(config,"AnnotationColor","#FF0000"); Set(config,"AnnotationThickness",4); Set(config,"DrawSmoothMode","Désactivé"); Call(controller,"Changed");
        await Chord(0x44); Check((bool)Get(controller,"IsDrawing"),"Native Ctrl+Alt+D starts drawing");
        before=Query(0); released=Query(1); Move(350,350); Mouse(2); await Task.Delay(150);
        Check(Get(controller,"CurrentStroke")!=null,"Native DOWN starts a real controller stroke");
        var stroke=Get(controller,"CurrentStroke");
        Set(config,"AnnotationTool","Rectangle"); Set(config,"AnnotationColor","#0000FF"); Set(config,"AnnotationThickness",12); Call(controller,"Changed");
        for(int n=1;n<=15;n++) { Move(350+n*8,350+n*3); await Task.Delay(20); }
        Mouse(4); await Task.Delay(250);
        Check(Get(controller,"CurrentStroke")==null && ((IList)Get(controller,"Strokes")).Count==1,"Stroke finishes once");
        Check((string)Get(stroke,"Tool")=="Stylo" && (int)Get(stroke,"Width")==4 && ((Color)Get(stroke,"Color")).ToArgb()==Color.Red.ToArgb(),"Tool/color/width retained while settings change mid-stroke");
        Check(((IList)Get(stroke,"Points")).Count>=10,"Real mouse moves recorded in stroke");
        Check(Query(0)==before && Query(1)==released,"Drawing consumes both endpoints from target");
        Move(350,350); Mouse(2); await Task.Delay(100); var next=Get(controller,"CurrentStroke"); Move(440,410); Mouse(4); await Task.Delay(200);
        Check((string)Get(next,"Tool")=="Rectangle" && (int)Get(next,"Width")==12 && ((Color)Get(next,"Color")).ToArgb()==Color.Blue.ToArgb(),"Next stroke uses new settings");
        Key(0x1b); Key(0x1b,true); await Task.Delay(200); Check(!(bool)Get(controller,"IsDrawing"),"Escape exits drawing through keyboard hook");
        Check((bool)Static("ConfigStore","Save",config),"Save isolated settings");
        object loaded=Static("ConfigStore","Load");
        Check((string)Get(loaded,"AnnotationTool")=="Rectangle" && (int)Get(loaded,"AnnotationThickness")==12,"Saved settings survive reload");
        Write("NOT_RUN manual visual quality, physical high-rate mouse, multi-monitor/DPI, text prompt, MSIX identity");
    }
    sealed class Target : Form
    {
        int downs,ups,chars,last;
        public Target() { Text="Haloryva — cible de recette automatique"; StartPosition=FormStartPosition.Manual; Location=new Point(160,160); ClientSize=new Size(700,450); BackColor=Color.WhiteSmoke; Shown+=delegate { Activate(); }; }
        protected override void WndProc(ref Message m) {
            int msg=m.Msg;
            if(msg==0x8001) { int q=(int)m.WParam; m.Result=(IntPtr)(q==9?13579:q==0?downs:q==1?ups:q==2?chars:last); return; }
            if(msg==0x201 || msg==0x204 || msg==0x207 || msg==0x20b || msg==0x203 || msg==0x206 || msg==0x209 || msg==0x20d) downs++;
            if(msg==0x202 || msg==0x205 || msg==0x208 || msg==0x20c) ups++;
            if(msg==0x102) { chars++; last=(int)m.WParam; }
            base.WndProc(ref m);
        }
    }
}
