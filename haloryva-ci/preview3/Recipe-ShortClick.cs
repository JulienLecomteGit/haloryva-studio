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

// Targeted driver for a separately built preview.3. No legacy 195-test suite.
internal static class Recipe
{
    static string folder, mode; static StreamWriter log; static Assembly app; static object controller;
    static Process target; static IntPtr targetWindow; static int code; static Point original;
    static readonly System.Collections.Generic.HashSet<ushort> heldKeys=new System.Collections.Generic.HashSet<ushort>();
    static uint heldMouse,heldData;
    static bool observe; static int assertions;
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
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
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
    static void Check(bool ok,string label) { assertions++; Write((ok?"PASS ":"FAIL ")+label); if(!ok) throw new Exception(label); }
    static object Get(object obj,string name) { var t=obj.GetType(); var f=t.GetField(name,Flags); return f!=null?f.GetValue(obj):t.GetProperty(name,Flags).GetValue(obj,null); }
    static void Set(object obj,string name,object value) { obj.GetType().GetField(name,Flags).SetValue(obj,value); }
    static object Call(object obj,string name,params object[] args) { return obj.GetType().GetMethod(name,Flags).Invoke(obj,args); }
    static object Static(string type,string name,params object[] args) { return app.GetType("CursorFlow."+type).GetMethod(name,Flags).Invoke(null,args); }
    static IntPtr FindTargetWindow()
    {
        IntPtr found=IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd,IntPtr unused) {
            uint pid; GetWindowThreadProcessId(hwnd,out pid);
            if(pid!=(uint)target.Id || !IsWindowVisible(hwnd)) return true;
            var title=new StringBuilder(256); GetWindowText(hwnd,title,title.Capacity);
            if(title.ToString()=="Haloryva — cible de recette automatique") { found=hwnd; return false; }
            return true;
        },IntPtr.Zero);
        return found;
    }
    static void FocusState(string label)
    {
        IntPtr foreground=GetForegroundWindow(); uint owner;
        GetWindowThreadProcessId(foreground,out owner);
        var title=new StringBuilder(256); GetWindowText(foreground,title,title.Capacity);
        Write("FOCUS "+label+" targetHwnd="+targetWindow.ToInt64().ToString("X")+" targetPid="+(target==null?0:target.Id)+
            " foregroundHwnd="+foreground.ToInt64().ToString("X")+" foregroundPid="+owner+" title="+title);
    }
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
        if(mode=="target") { SetProcessDPIAware(); Application.Run(new Target(args.Length>1?args[1]:null,args.Length>2 && args[2]=="on")); return 0; }
        folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RecipeHaloryvaRuns",args[1]);
        Directory.CreateDirectory(folder); log=new StreamWriter(Path.Combine(folder,"recipe.log"),true,Encoding.UTF8);
        try {
            Write("ENV GitHub-hosted; mode="+mode+"; pid="+Process.GetCurrentProcess().Id);
            if(mode=="probe") { Write("PASS verified sandbox identity; no Haloryva launched, no input generated"); return 0; }
            app=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Haloryva.exe"));
            if(mode=="smoke") { Smoke(); return code; }
            if(mode!="short-click") throw new Exception("Unknown mode");
            observe=args.Length>2 && args[2]=="on";
            Write("TEST observer="+observe+" assembly="+app.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
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
        finally { Write("SUMMARY assertions="+assertions+" exit="+code+" observer="+observe); Write("EXIT "+code); log.Dispose(); }
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
        target=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"target \""+Path.Combine(folder,"target-trace.log")+"\" "+(observe?"on":"off")){UseShellExecute=false});
        // MainWindowHandle may identify the child's console on ARM emulation.
        // Select our visible WinForms target by its owning PID and exact title instead.
        for(int n=0;n<100 && targetWindow==IntPtr.Zero;n++) { await Task.Delay(100); targetWindow=FindTargetWindow(); }
        if(targetWindow==IntPtr.Zero) throw new Exception("Target WinForms window not found; no input generated.");
        FocusState("before-acquisition");
        for(int n=0;n<75 && GetForegroundWindow()!=targetWindow;n++) {
            SetForegroundWindow(targetWindow); PostMessage(targetWindow,0x8002,IntPtr.Zero,IntPtr.Zero);
            await Task.Delay(200);
        }
        await Task.Delay(300);
        FocusState("ready-check");
        FocusGuard();
        Check(Query(9)==13579,"Target responds");
        Check(Query(8)==(observe?1:0),"Requested observer state confirmed");
        foreach(int k in new[]{1,2,4,5,6,0x10,0x11,0x12,0x5b,0x5c})
            Check((GetAsyncKeyState(k)&0x8000)==0,"Neutral input "+k);
        Move(350,350); await Task.Delay(250);
        object config=Get(controller,"Options");
        string[] buttons={"Milieu","X1","X2"};
        uint[] down={32,128,128},up={64,256,256},data={0,1,2};
        for(int b=0;b<buttons.Length;b++)
        {
            int d=Query(0),u=Query(1);
            await Click(down[b],up[b],data[b],100);
            Check(Query(0)==d+1 && Query(1)==u+1,"Normal baseline "+buttons[b]);
        }
        foreach(string field in new[]{"ButtonMiddleLong","ButtonX1Long","ButtonX2Long"}) Set(config,field,"Halo : activer / arrêter");
        Set(config,"LongPressMs",400); Call(controller,"Changed"); await Task.Delay(250);
        Write("CONFIG threshold=400 short=Normal long=Halo-toggle; observer="+observe);
        for(int b=0;b<buttons.Length;b++)
        {
            await Task.Delay(350);
            int d=Query(0),u=Query(1);
            FocusGuard();
            bool sent=(bool)app.GetType("CursorFlow.Native").GetMethod("SendMouseClick",Flags).Invoke(null,new object[]{buttons[b]});
            await Task.Delay(250);
            Check(sent && Query(0)==d+1 && Query(1)==u+1,"Direct native wrapper "+buttons[b]);
            foreach(int hold in new[]{60,100,180})
            {
                await Task.Delay(350);
                d=Query(0); u=Query(1); bool ring=(bool)Get(config,"Ring");
                var elapsed=Stopwatch.StartNew();
                Mouse(down[b],data[b]); await Task.Delay(hold);
                long actualHold=elapsed.ElapsedMilliseconds;
                var release=Stopwatch.StartNew(); Mouse(up[b],data[b]); long releaseMs=release.ElapsedMilliseconds;
                for(int n=0;n<100 && (Query(0)<d+1 || Query(1)<u+1);n++) await Task.Delay(10);
                long deliveryMs=release.ElapsedMilliseconds;
                await Task.Delay(250);
                Write("RESULT short "+buttons[b]+" requestedHold="+hold+" actualHold="+actualHold+" releaseCallMs="+releaseMs+" deliveryMs="+deliveryMs+
                    " downDelta="+(Query(0)-d)+" upDelta="+(Query(1)-u)+" ringUnchanged="+((bool)Get(config,"Ring")==ring)+" unpaired="+Query(7));
                State("short "+buttons[b],config);
                Check(actualHold<400 && Query(0)==d+1 && Query(1)==u+1 && (bool)Get(config,"Ring")==ring && Query(7)==0,"Short click "+buttons[b]+" "+hold+"ms");
            }
            await Task.Delay(450);
            d=Query(0); u=Query(1); bool oldRing=(bool)Get(config,"Ring");
            for(int n=0;n<3;n++) { Mouse(down[b],data[b]); await Task.Delay(50); Mouse(up[b],data[b]); await Task.Delay(40); }
            await Task.Delay(400);
            Write("RESULT burst "+buttons[b]+" clicks=3 downDelta="+(Query(0)-d)+" upDelta="+(Query(1)-u)+" unpaired="+Query(7));
            Check(Query(0)==d+3 && Query(1)==u+3 && (bool)Get(config,"Ring")==oldRing && Query(7)==0,"Three rapid short clicks "+buttons[b]);
            d=Query(0); u=Query(1); oldRing=(bool)Get(config,"Ring");
            Mouse(down[b],data[b]); await Task.Delay(650); Mouse(up[b],data[b]); await Task.Delay(250);
            Write("RESULT long-control "+buttons[b]+" downDelta="+(Query(0)-d)+" upDelta="+(Query(1)-u)+" ringChanged="+((bool)Get(config,"Ring")!=oldRing));
            Check(Query(0)==d && Query(1)==u && (bool)Get(config,"Ring")!=oldRing && Query(7)==0,"Long action preserved "+buttons[b]);
        }
        Write("CASES short=18 longControls=3 baseline=3 direct=3 observer="+observe);
        Write("NOT_RUN legacy 195 assertions; AltGr, drawing, MSIX, physical-device validation");
    }
    sealed class Target : Form
    {
        int downs,ups,chars,last,unpaired;
        bool observerEnabled;
        readonly System.Collections.Generic.HashSet<int> pressed=new System.Collections.Generic.HashSet<int>();
        [DllImport("user32.dll")] static extern IntPtr GetMessageExtraInfo();
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
            if(code>=0 && (message.ToInt64()==0x207 || message.ToInt64()==0x208 || message.ToInt64()==0x20b || message.ToInt64()==0x20c)) {
                var m=(MSLL)Marshal.PtrToStructure(data,typeof(MSLL));
                Trace("HOOK msg="+message.ToInt64().ToString("X")+" data="+m.data+" flags="+m.flags+" extra="+m.extra.ToInt64().ToString("X"));
            }
            return CallNextHookEx(observer,code,message,data);
        }
        protected override void OnShown(EventArgs e) {
            if(observerEnabled) { observerProc=Observe; observer=SetWindowsHookEx(14,observerProc,GetModuleHandle(null),0); }
            Trace("OBSERVER requested="+observerEnabled+" installed="+(observer!=IntPtr.Zero)+" targetHwnd="+Handle.ToInt64().ToString("X"));
            base.OnShown(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e) {
            if(observer!=IntPtr.Zero) UnhookWindowsHookEx(observer);
            if(trace!=null) trace.Dispose(); base.OnFormClosed(e);
        }
        public Target(string tracePath,bool enabled) { observerEnabled=enabled; if(tracePath!=null) trace=new StreamWriter(tracePath,false,Encoding.UTF8); Text="Haloryva — cible de recette automatique"; StartPosition=FormStartPosition.Manual; Location=new Point(160,160); ClientSize=new Size(700,450); BackColor=Color.WhiteSmoke; Shown+=delegate { Activate(); }; }
        protected override void WndProc(ref Message m) {
            int msg=m.Msg;
            if(msg==0x8002) { Activate(); m.Result=IntPtr.Zero; return; }
            if(msg==0x8001) { int q=(int)m.WParam; m.Result=(IntPtr)(q==9?13579:q==8?(observer!=IntPtr.Zero?1:0):q==7?unpaired:q==0?downs:q==1?ups:q==2?chars:last); return; }
            if(msg==0x201 || msg==0x204 || msg==0x207 || msg==0x20b || msg==0x203 || msg==0x206 || msg==0x209 || msg==0x20d) downs++;
            if(msg==0x202 || msg==0x205 || msg==0x208 || msg==0x20c) ups++;
            if(msg==0x102) { chars++; last=(int)m.WParam; }
            if(msg==0x207 || msg==0x208 || msg==0x209 || msg==0x20b || msg==0x20c || msg==0x20d) {
                int button=(msg==0x207 || msg==0x208 || msg==0x209)?0:(int)((m.WParam.ToInt64()>>16)&0xffff);
                bool isUp=msg==0x208 || msg==0x20c;
                if(isUp) { if(!pressed.Remove(button)) unpaired++; } else if(!pressed.Add(button)) unpaired++;
                Trace("WINDOW msg="+msg.ToString("X")+" button="+button+" extra="+GetMessageExtraInfo().ToInt64().ToString("X")+" counts="+downs+"/"+ups+" unpaired="+unpaired);
            }
            base.WndProc(ref m);
        }
    }
}
