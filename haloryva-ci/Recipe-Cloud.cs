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
        if(mode=="target") { SetProcessDPIAware(); Application.Run(new Target(args.Length>1?args[1]:null)); return 0; }
        folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RecipeHaloryvaRuns",args[1]);
        Directory.CreateDirectory(folder); log=new StreamWriter(Path.Combine(folder,"recipe.log"),true,Encoding.UTF8);
        try {
            Write("ENV GitHub-hosted; mode="+mode+"; pid="+Process.GetCurrentProcess().Id);
            if(mode=="probe") { Write("PASS verified sandbox identity; no Haloryva launched, no input generated"); return 0; }
            app=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Haloryva.exe"));
            if(mode=="smoke") { Smoke(); return code; }
            if(mode!="replay-diagnostic") throw new Exception("Unknown mode");
            SetProcessDPIAware(); original=Cursor.Position;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Static("TrailTrial","Initialize",(object)new[]{"--trail-session",Path.Combine(folder,"profile")});
            // New known profile inside virtualized storage, never the normal installed profile.
            var config=Activator.CreateInstance(app.GetType("CursorFlow.Config"));
            Static("ConfigStore","Save",config);
            controller=Activator.CreateInstance(app.GetType("CursorFlow.CursorController"),true);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            SynchronizationContext.Current.Post(async delegate {
                try { await Functional(); Write("COMPLETE targeted short-click checks passed"); }
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
    static void State(string label, object config)
    {
        object hooks=Get(controller,"hooks");
        object error=Get(hooks,"callbackError");
        Write("STATE "+label+" target="+Query(0)+"/"+Query(1)+" ring="+Get(config,"Ring")+
            " pending="+Get(hooks,"Pending")+" polls="+Get(hooks,"PollCount")+
            " installed="+Get(hooks,"Installed")+" mouse="+Get(controller,"LastPhysicalButton")+
            " error="+(error==null?"none":error.ToString()));
    }
    static async Task Functional()
    {
        object hooks=Get(controller,"hooks");
        Check((bool)Get(hooks,"Installed"),"Real hooks installed");
        target=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"target \""+Path.Combine(folder,"target-trace.log")+"\""){UseShellExecute=false});
        for(int n=0;n<40 && target.MainWindowHandle==IntPtr.Zero;n++) { await Task.Delay(100); target.Refresh(); }
        targetWindow=target.MainWindowHandle;
        SetForegroundWindow(targetWindow);
        for(int n=0;n<150 && GetForegroundWindow()!=targetWindow;n++) await Task.Delay(100);
        FocusGuard();
        Check(Query(9)==13579,"Target responds");
        foreach(int k in new[]{1,2,4,5,6,0x10,0x11,0x12,0x5b,0x5c})
            Check((GetAsyncKeyState(k)&0x8000)==0,"Neutral input "+k);
        Move(350,350); await Task.Delay(200);
        object config=Get(controller,"Options");
        Write("CONFIG short="+Get(config,"ButtonMiddle")+" long="+Get(config,"ButtonMiddleLong")+" threshold="+Get(config,"LongPressMs"));
        State("baseline-before",config);
        int d=Query(0),u=Query(1);
        await Click(32,64,0,100);
        State("baseline-after",config);
        Check(Query(0)==d+1 && Query(1)==u+1,"Baseline middle click reaches target");
        Set(config,"ButtonMiddleLong","Halo : activer / arrêter"); Set(config,"LongPressMs",400);
        Call(controller,"Changed"); await Task.Delay(250);
        Write("CONFIG short="+Get(config,"ButtonMiddle")+" long="+Get(config,"ButtonMiddleLong")+" threshold="+Get(config,"LongPressMs"));
        d=Query(0); u=Query(1); FocusGuard();
        object sent=app.GetType("CursorFlow.Native").GetMethod("SendMouseClick",Flags).Invoke(null,new object[]{"Milieu"});
        Write("DIRECT production wrapper returned="+sent);
        await Task.Delay(300); State("direct-wrapper-after",config);
        bool direct=Query(0)==d+1 && Query(1)==u+1;
        Write("OBS direct wrapper paired="+direct);
        int failures=0;
        for(int attempt=1;attempt<=3;attempt++)
        {
            await Task.Delay(700);
            d=Query(0); u=Query(1); bool ring=(bool)Get(config,"Ring");
            State("short-"+attempt+"-before",config);
            var elapsed=Stopwatch.StartNew();
            Mouse(32); await Task.Delay(100);
            Write("TIMING short-"+attempt+" release-request-ms="+elapsed.ElapsedMilliseconds);
            State("short-"+attempt+"-held",config);
            Mouse(64);
            await Task.Delay(200); State("short-"+attempt+"-after-200ms",config);
            await Task.Delay(800); State("short-"+attempt+"-after-1000ms",config);
            bool paired=Query(0)==d+1 && Query(1)==u+1;
            bool unchanged=(bool)Get(config,"Ring")==ring;
            Write("RESULT short-"+attempt+" downDelta="+(Query(0)-d)+" upDelta="+(Query(1)-u)+" ringUnchanged="+unchanged);
            if(!paired || !unchanged) failures++;
        }
        Write("SUMMARY replayFailures="+failures+" directWrapperPaired="+direct);
        Write("NOT_RUN long-action, AltGr, drawing, previous 195 assertions (targeted diagnosis only)");
        Check(failures==0 && direct,"Targeted short-click replay diagnostics");
    }
    sealed class Target : Form
    {
        int downs,ups,chars,last;
        StreamWriter trace;
        IntPtr observer;
        HookProc observerProc;
        delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [StructLayout(LayoutKind.Sequential)] struct MSLL { public int x,y; public uint data,flags,time; public IntPtr extra; }
        void Trace(string message) { if(trace!=null) { trace.WriteLine(DateTime.UtcNow.ToString("o")+" "+message); trace.Flush(); } }
        IntPtr Observe(int code,IntPtr message,IntPtr data) {
            if(code>=0 && (message.ToInt64()==0x207 || message.ToInt64()==0x208)) {
                var m=(MSLL)Marshal.PtrToStructure(data,typeof(MSLL));
                Trace("HOOK msg="+message.ToInt64().ToString("X")+" flags="+m.flags+" extra="+m.extra.ToInt64().ToString("X"));
            }
            return CallNextHookEx(observer,code,message,data);
        }
        protected override void OnShown(EventArgs e) {
            observerProc=Observe; observer=SetWindowsHookEx(14,observerProc,GetModuleHandle(null),0);
            Trace("OBSERVER installed="+(observer!=IntPtr.Zero)+" error="+Marshal.GetLastWin32Error());
            base.OnShown(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e) {
            if(observer!=IntPtr.Zero) UnhookWindowsHookEx(observer);
            if(trace!=null) trace.Dispose(); base.OnFormClosed(e);
        }
        public Target(string tracePath) { if(tracePath!=null) trace=new StreamWriter(tracePath,false,Encoding.UTF8); Text="Haloryva — cible de recette automatique"; StartPosition=FormStartPosition.Manual; Location=new Point(160,160); ClientSize=new Size(700,450); BackColor=Color.WhiteSmoke; Shown+=delegate { Activate(); }; }
        protected override void WndProc(ref Message m) {
            int msg=m.Msg;
            if(msg==0x8001) { int q=(int)m.WParam; m.Result=(IntPtr)(q==9?13579:q==0?downs:q==1?ups:q==2?chars:last); return; }
            if(msg==0x201 || msg==0x204 || msg==0x207 || msg==0x20b || msg==0x203 || msg==0x206 || msg==0x209 || msg==0x20d) downs++;
            if(msg==0x202 || msg==0x205 || msg==0x208 || msg==0x20c) ups++;
            if(msg==0x102) { chars++; last=(int)m.WParam; }
            if(msg==0x207 || msg==0x208 || msg==0x209) Trace("WINDOW msg="+msg.ToString("X")+" counts="+downs+"/"+ups);
            base.WndProc(ref m);
        }
    }
}
