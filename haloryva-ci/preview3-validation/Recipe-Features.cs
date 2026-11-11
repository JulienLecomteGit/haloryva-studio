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

// Targeted AltGr and drawing recipe. Loads the preserved preview.3 artifact; never rebuilds it.
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
            if(mode!="features") throw new Exception("Unknown mode");
            observe=false;
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
                try { await Functional(); Write("COMPLETE AltGr/drawing/stroke-settings checks passed"); }
                catch(Exception ex) { code=1; Write("ERROR "+ex); }
                finally {
                    foreach(ushort key in heldKeys) SendInput(1,new[]{new INPUT {type=1,data=new UNION {key=new KEY {vk=key,flags=2u|(key==0xa5?1u:0u)}}}},Marshal.SizeOf(typeof(INPUT)));
                    if(heldMouse!=0) SendInput(1,new[]{new INPUT {data=new UNION {mouse=new MOUSE {flags=heldMouse,data=heldData}}}},Marshal.SizeOf(typeof(INPUT)));
                    if(target!=null && !target.HasExited) PostMessage(targetWindow,0x0010,IntPtr.Zero,IntPtr.Zero);
                    // Release only our generated keys/buttons; target focus failures stop before more gestures.
                    Cursor.Position=original;
                    if(controller!=null) {
                        object testHooks=Get(controller,"hooks");
                        var replayThread=(Thread)Get(testHooks,"replayWorker");
                        Call(controller,"Exit");
                        bool ended=!replayThread.IsAlive && !(bool)Get(testHooks,"WorkerAlive");
                        assertions++; Write((ended?"PASS ":"FAIL ")+"Both input workers stopped on shutdown");
                        if(!ended) code=1;
                    }
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
        FocusGuard();
        Check((int)SendMessage(targetWindow,0x8003,IntPtr.Zero,IntPtr.Zero)==1,"French keyboard layout active in test target");
        uint pid; uint tid=GetWindowThreadProcessId(targetWindow,out pid);
        IntPtr layout=GetKeyboardLayout(tid);
        Write("KEYBOARD targetLayout="+layout.ToInt64().ToString("X")+" testVMOnly=true");
        foreach(char symbol in new[]{'@','€','#'})
        {
            short mapping=VkKeyScanEx(symbol,layout);
            Check(mapping!=-1 && ((mapping>>8)&7)==6,"French AltGr mapping for U+"+((int)symbol).ToString("X4"));
            int chars=Query(2);
            await AltGr((ushort)(mapping&255));
            Write("ALTGR char=U+"+((int)symbol).ToString("X4")+" charDelta="+(Query(2)-chars)+" last=U+"+Query(3).ToString("X4"));
            Check(Query(2)==chars+1 && Query(3)==symbol,"AltGr inserts "+symbol+" in external textbox");
        }
        foreach(ushort letter in new ushort[]{0x43,0x4d,0x44})
        {
            bool clicks=(bool)Get(config,"Clicks"),enabled=(bool)Get(config,"Enabled"),drawing=(bool)Get(controller,"IsDrawing");
            await AltGr(letter);
            Check((bool)Get(config,"Clicks")==clicks && (bool)Get(config,"Enabled")==enabled && (bool)Get(controller,"IsDrawing")==drawing,
                "AltGr+"+(char)letter+" does not execute Ctrl+Alt shortcut");
        }
        bool initialClicks=(bool)Get(config,"Clicks");
        await Chord(0x43); Check((bool)Get(config,"Clicks")!=initialClicks,"Left Ctrl+Alt+C remains active");
        await Chord(0x43); Check((bool)Get(config,"Clicks")==initialClicks,"Left Ctrl+Alt+C restores initial state");

        var beforePixels=Capture("drawing-before.png");
        Set(config,"AnnotationTool","Stylo"); Set(config,"AnnotationColor","#FF0000"); Set(config,"AnnotationThickness",4);
        Set(config,"DrawSmoothMode","Désactivé"); Call(controller,"Changed"); await Task.Delay(250);
        await Chord(0x44);
        await WaitFor(delegate { return (bool)Get(controller,"IsDrawing"); },"Ctrl+Alt+D starts drawing");
        int d=Query(0),u=Query(1);
        Move(350,350); Mouse(2);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")!=null; },"Native DOWN creates a stroke");
        object stroke=Get(controller,"CurrentStroke");
        Check(StrokeStyle(stroke,"Stylo",Color.Red,4),"First stroke captures its initial settings");
        Move(374,359); await Task.Delay(80);
        Set(config,"AnnotationTool","Rectangle"); Set(config,"AnnotationColor","#0000FF"); Set(config,"AnnotationThickness",12);
        Call(controller,"Changed"); await Task.Delay(100);
        Check(Object.ReferenceEquals(stroke,Get(controller,"CurrentStroke")) && StrokeStyle(stroke,"Stylo",Color.Red,4),"Mid-stroke edits preserve the current stroke");
        for(int n=4;n<=15;n++) { Move(350+n*8,350+n*3); await Task.Delay(30); }
        Mouse(4);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")==null; },"First stroke completes");
        IList strokes=(IList)Get(controller,"Strokes");
        Check(strokes.Count==1 && Object.ReferenceEquals(strokes[0],stroke),"First stroke recorded exactly once");
        Check(StrokeStyle(stroke,"Stylo",Color.Red,4),"Completed stroke remains red pen width 4");
        int points=((IList)Get(stroke,"Points")).Count;
        Write("DRAW firstStrokePoints="+points+" tool="+Get(stroke,"Tool")+" width="+Get(stroke,"Width")+" color="+Get(stroke,"Color"));
        Check(points>=10,"Native pointer movement recorded as a continuous pen stroke");
        Check(Query(0)==d && Query(1)==u,"Drawing consumes both button endpoints");

        Move(540,330); Mouse(2);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")!=null; },"Second stroke begins");
        object next=Get(controller,"CurrentStroke");
        Check(StrokeStyle(next,"Rectangle",Color.Blue,12),"Next stroke uses the edited settings");
        for(int n=1;n<=10;n++) { Move(540+n*12,330+n*10); await Task.Delay(30); }
        Mouse(4); await WaitFor(delegate { return Get(controller,"CurrentStroke")==null; },"Second stroke completes");
        Check(strokes.Count==2 && Object.ReferenceEquals(strokes[1],next) && StrokeStyle(next,"Rectangle",Color.Blue,12),"Second stroke stored as blue rectangle width 12");
        Check(((IList)Get(next,"Points")).Count==2,"Rectangle keeps its two defining endpoints");
        Check(Query(0)==d && Query(1)==u,"Neither drawn stroke leaks button events to target");
        Move(750,510); await Task.Delay(500);
        var afterPixels=Capture("drawing-after.png");
        bool rendered=afterPixels[0]>beforePixels[0]+100 && afterPixels[1]>beforePixels[1]+100;
        Write("RENDER beforeRed="+beforePixels[0]+" beforeBlue="+beforePixels[1]+" afterRed="+afterPixels[0]+" afterBlue="+afterPixels[1]);

        Key(0x1b); Key(0x1b,true); await WaitFor(delegate { return !(bool)Get(controller,"IsDrawing"); },"Escape exits drawing");
        Check(strokes.Count==2,"Leaving drawing preserves completed strokes");
        Check((bool)Static("ConfigStore","Save",config),"Save isolated settings");
        object loaded=Static("ConfigStore","Load");
        Check((string)Get(loaded,"AnnotationTool")=="Rectangle" && (string)Get(loaded,"AnnotationColor")=="#0000FF" &&
            (int)Get(loaded,"AnnotationThickness")==12,"Tool/color/width survive profile reload");
        Check(rendered,"Desktop capture contains both red pen and blue rectangle");
        Write("CASES AltGrChars=3 AltGrShortcutGuards=3 leftModifierControls=2 strokes=2 midStrokeEdit=1 profileReload=1 desktopCapture=1");
        Write("NOT_RUN legacy195, short-click suite, physical keyboard/mouse, multiple screens/DPI, all drawing tools, MSIX in this job");
    }
    static async Task AltGr(ushort key)
    {
        Key(0xa2); Key(0xa5); Key(key); Key(key,true); Key(0xa5,true); Key(0xa2,true);
        await Task.Delay(300);
    }
    static async Task WaitFor(Func<bool> predicate,string label)
    {
        var timer=Stopwatch.StartNew();
        while(!predicate() && timer.ElapsedMilliseconds<4000) await Task.Delay(20);
        Check(predicate(),label);
    }
    static bool StrokeStyle(object stroke,string tool,Color color,int width)
    {
        return stroke!=null && (string)Get(stroke,"Tool")==tool && (int)Get(stroke,"Width")==width &&
            ((Color)Get(stroke,"Color")).ToArgb()==color.ToArgb();
    }
    static int[] Capture(string file)
    {
        FocusGuard();
        using(var bitmap=new Bitmap(700,450))
        {
            using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(160,160,0,0,bitmap.Size);
            bitmap.Save(Path.Combine(folder,file),System.Drawing.Imaging.ImageFormat.Png);
            int red=0,blue=0;
            for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++) {
                Color c=bitmap.GetPixel(x,y);
                if(c.R>180 && c.G<100 && c.B<100) red++;
                if(c.B>180 && c.R<60 && c.G<100) blue++;
            }
            return new[]{red,blue};
        }
    }
    sealed class Target : Form
    {
        int downs,ups,chars,last,unpaired;
        TextBox textEntry;
        IntPtr previousLayout, frenchLayout; bool loadedFrench;
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadKeyboardLayout(string name,uint flags);
        [DllImport("user32.dll")] static extern IntPtr ActivateKeyboardLayout(IntPtr layout,uint flags);
        [DllImport("user32.dll")] static extern bool UnloadKeyboardLayout(IntPtr layout);
        [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int count,IntPtr[] layouts);
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
            if(previousLayout!=IntPtr.Zero) { ActivateKeyboardLayout(previousLayout,0); Trace("KEYBOARD restored="+GetKeyboardLayout(0).ToInt64().ToString("X")); }
            if(loadedFrench && frenchLayout!=IntPtr.Zero && frenchLayout!=previousLayout) UnloadKeyboardLayout(frenchLayout);
            if(observer!=IntPtr.Zero) UnhookWindowsHookEx(observer);
            if(trace!=null) trace.Dispose(); base.OnFormClosed(e);
        }
        public Target(string tracePath,bool enabled) { observerEnabled=enabled; if(tracePath!=null) trace=new StreamWriter(tracePath,false,Encoding.UTF8); Text="Haloryva — cible de recette automatique"; StartPosition=FormStartPosition.Manual; Location=new Point(160,160); ClientSize=new Size(700,450); BackColor=Color.WhiteSmoke;
            textEntry=new TextBox { Multiline=true,Location=new Point(16,40),Size=new Size(650,80),Font=new Font("Segoe UI",16) };
            Controls.Add(textEntry); Shown+=delegate { Activate(); textEntry.Focus(); }; }
        protected override void WndProc(ref Message m) {
            int msg=m.Msg;
            if(msg==0x8002) { Activate(); m.Result=IntPtr.Zero; return; }
            if(msg==0x8003) {
                previousLayout=GetKeyboardLayout(0);
                var existing=new IntPtr[GetKeyboardLayoutList(0,null)]; GetKeyboardLayoutList(existing.Length,existing);
                frenchLayout=LoadKeyboardLayout("0000040c",1);
                loadedFrench=frenchLayout!=IntPtr.Zero && Array.IndexOf(existing,frenchLayout)<0;
                if(frenchLayout!=IntPtr.Zero) ActivateKeyboardLayout(frenchLayout,0);
                textEntry.Focus();
                IntPtr current=GetKeyboardLayout(0);
                Trace("KEYBOARD previous="+previousLayout.ToInt64().ToString("X")+" french="+frenchLayout.ToInt64().ToString("X")+" active="+current.ToInt64().ToString("X")+" error="+Marshal.GetLastWin32Error());
                m.Result=(IntPtr)(frenchLayout!=IntPtr.Zero && (current.ToInt64()&0xffff)==0x040c?1:0); return;
            }
            if(msg==0x8001) { int q=(int)m.WParam; m.Result=(IntPtr)(q==9?13579:q==8?(observer!=IntPtr.Zero?1:0):q==7?unpaired:q==0?downs:q==1?ups:q==2?textEntry.Text.Length:q==3?(textEntry.Text.Length==0?0:(int)textEntry.Text[textEntry.Text.Length-1]):last); return; }
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
