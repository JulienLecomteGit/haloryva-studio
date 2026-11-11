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

// Targeted remaining tools, history and actual PNG export recipe. Loads the preserved preview.3 artifact; never rebuilds it.
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
    static void Write(string message) { lock(log) { log.WriteLine(DateTime.UtcNow.ToString("o")+" "+message); log.Flush(); } }
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
    static void Move(int x,int y)
    {
        Rectangle screen=SystemInformation.VirtualScreen;
        if(!screen.Contains(x,y)) throw new Exception("Requested point outside test desktop.");
        int nx=(int)(((long)(x-screen.Left)*65536+32768)/screen.Width);
        int ny=(int)(((long)(y-screen.Top)*65536+32768)/screen.Height);
        Input(new INPUT {data=new UNION {mouse=new MOUSE {x=nx,y=ny,flags=0xc001}}});
        Write("MOVE SendInput requested="+x+","+y+" cursor="+Cursor.Position);
    }
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
                try { await Functional(); Write("COMPLETE remaining tools/history/export checks passed"); }
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
        Set(config,"KeyDisplay",false); Set(config,"Ring",false); Set(config,"Trail",false);
        Call(controller,"Changed"); await Task.Delay(250);
        await Chord(0x44);
        await WaitFor(delegate { return (bool)Get(controller,"IsDrawing"); },"Drawing mode ready");
        IList strokes=(IList)Get(controller,"Strokes"), redo=(IList)Get(controller,"RedoStrokes");
        int downs=Query(0),ups=Query(1);
        await Stroke("Surligneur","#FFA500",4,new[]{new Point(220,340),new Point(245,340),new Point(270,342),new Point(295,340),new Point(330,340)});
        await Stroke("Ligne droite","#FF0000",4,new[]{new Point(220,390),new Point(250,375),new Point(290,400),new Point(340,390)});
        await Stroke("Ellipse","#0000FF",4,new[]{new Point(400,320),new Point(440,350),new Point(510,390)});
        await Stroke("Flèche droite","#008000",4,new[]{new Point(580,330),new Point(630,350),new Point(710,380)});
        await Stroke("Flèche libre","#CC00CC",4,new[]{new Point(220,455),new Point(240,440),new Point(260,445),new Point(280,470),new Point(310,475),new Point(345,450)});
        await Stroke("Forme libre","#CC6600",4,new[]{new Point(400,490),new Point(410,470),new Point(420,450),new Point(430,430),new Point(440,410),new Point(450,430),new Point(460,450),new Point(470,470),new Point(480,490),new Point(460,490),new Point(440,490),new Point(420,490),new Point(400,490)});
        Check((string)Get(strokes[5],"Tool")=="Triangle" && ((IList)Get(strokes[5],"Points")).Count==3,"Auto-formes recognizes the drawn triangle");
        Check(((IList)Get(strokes[5],"OriginalPoints")).Count>=10,"Auto-formes retains the original gesture");
        await Stroke("Numéro","#008000",4,new[]{new Point(610,450)});
        await Stroke("Numéro","#008000",4,new[]{new Point(665,450)});
        Check((int)Get(strokes[6],"Number")==1 && (int)Get(strokes[7],"Number")==2,"Number tool assigns 1 then 2");
        await TextStroke("Haloryva é 123",true,new Point(225,530));
        Check(strokes.Count==9 && (string)Get(strokes[8],"Text")=="Haloryva é 123","Text entered and accepted through the real prompt");
        await TextStroke("Annuler",false,new Point(700,530));
        Check(strokes.Count==9 && (bool)Get(controller,"IsDrawing"),"Cancelled text leaves no empty annotation and resumes drawing");
        Check(Query(0)==downs && Query(1)==ups,"All drawing gestures consume their button endpoints");
        Move(800,580); await Task.Delay(450); Capture("gallery.png");
        object[] originals=new object[strokes.Count]; strokes.CopyTo(originals,0);
        // Export is the actual controller action and native SaveFileDialog.
        await LeaveDrawing();
        string first=await Export("galerie é 123.png",false,false);
        ValidateGallery(first,true,false);
        int countBefore=Directory.GetFiles(folder,"*.png").Length;
        await Export("cancelled.png",true,false);
        Check(!File.Exists(Path.Combine(folder,"cancelled.png")) && strokes.Count==9 && Directory.GetFiles(folder,"*.png").Length==countBefore,"Cancelling export creates no file and preserves drawings");
        await Chord(0x44); await WaitFor(delegate { return (bool)Get(controller,"IsDrawing"); },"Drawing resumed for native history shortcuts");
        for(int n=originals.Length-1;n>=0;n--) {
            await Ctrl(0x5a);
            Check(strokes.Count==n && redo.Count==originals.Length-n && Object.ReferenceEquals(redo[redo.Count-1],originals[n]),"Ctrl+Z removes last annotation in order "+n);
        }
        await Ctrl(0x5a); Check(strokes.Count==0 && redo.Count==9,"Undo on empty scene is harmless");
        Move(800,580); await Task.Delay(400); Capture("undo-empty.png");
        for(int n=0;n<originals.Length;n++) {
            await Ctrl(0x59);
            Check(strokes.Count==n+1 && redo.Count==originals.Length-n-1 && Object.ReferenceEquals(strokes[n],originals[n]),"Ctrl+Y restores annotation in order "+n);
        }
        await Ctrl(0x59); Check(strokes.Count==9 && redo.Count==0,"Redo on exhausted history is harmless");
        Move(800,580); await Task.Delay(400); Capture("redo-restored.png");
        await LeaveDrawing();
        string restored=await Export("restored.png",false,false);
        Check(PixelsEqual(first,restored),"Export after undo/redo is pixel-identical to original gallery");
        await Chord(0x44); await WaitFor(delegate { return (bool)Get(controller,"IsDrawing"); },"Drawing resumed for alternate history shortcuts");
        await Chord(0x08);
        Check(strokes.Count==8 && redo.Count==1,"Ctrl+Alt+Backspace undoes text");
        await ShiftChord(0x08);
        Check(strokes.Count==9 && redo.Count==0 && Object.ReferenceEquals(strokes[8],originals[8]),"Ctrl+Alt+Shift+Backspace restores text");
        await Ctrl(0x5a);
        await Stroke("Numéro","#008000",4,new[]{new Point(735,450)});
        Check(strokes.Count==9 && redo.Count==0 && (int)Get(strokes[8],"Number")==3,"New number after undo starts a new history branch and keeps numbering");
        await Ctrl(0x59);
        Check(strokes.Count==9 && (string)Get(strokes[8],"Tool")=="Numéro","Redo cannot resurrect discarded text after a new drawing");
        await Ctrl(0x5a);
        await Stroke("Numéro","#008000",4,new[]{new Point(735,450)});
        Check((int)Get(strokes[8],"Number")==3,"Undoing a number lets the next number reuse its position");
        Move(800,580); await Task.Delay(400); Capture("history-new-branch.png");
        await LeaveDrawing();
        string branched=await Export("new-branch.png",false,false);
        ValidateGallery(branched,false,true);
        await Chord(0x43); Check(strokes.Count==0 && redo.Count==0,"Clearing scene also clears undo/redo history");
        int files=Directory.GetFiles(folder,"*.png").Length;
        await Export("empty-must-not-exist.png",false,true);
        Check(Directory.GetFiles(folder,"*.png").Length==files,"Empty export reports no drawing and creates no file");
        Write("CASES additionalTools=8 completedAnnotations=9 textAccept=1 textCancel=1 undoRedoBothShortcuts=true exportSaved=3 exportCancelled=1 exportEmpty=1");
        Write("NOT_RUN legacy195, previous AltGr/short-click/MSIX suites, physical devices, multiple screens/DPI, manual studio tool-selection controls, other assisted shapes");
    }
    static async Task Ctrl(ushort key) { Key(0xa2); Key(key); Key(key,true); Key(0xa2,true); await Task.Delay(200); }
    static async Task ShiftChord(ushort key) { Key(0xa2); Key(0xa4); Key(0xa0); Key(key); Key(key,true); Key(0xa0,true); Key(0xa4,true); Key(0xa2,true); await Task.Delay(250); }
    static async Task LeaveDrawing() { Key(0x1b); Key(0x1b,true); await WaitFor(delegate { return !(bool)Get(controller,"IsDrawing"); },"Drawing left before file dialog"); }
    static async Task RefocusTarget()
    {
        for(int n=0;n<30 && GetForegroundWindow()!=targetWindow;n++) {
            SetForegroundWindow(targetWindow); PostMessage(targetWindow,0x8002,IntPtr.Zero,IntPtr.Zero); await Task.Delay(100);
        }
        FocusGuard();
    }
    static async Task Stroke(string tool,string color,int width,Point[] points)
    {
        object config=Get(controller,"Options");
        Set(config,"AnnotationTool",tool); Set(config,"AnnotationColor",color); Set(config,"AnnotationThickness",width);
        Set(config,"DrawSmoothMode",tool=="Forme libre"?"Formes assistées":"Désactivé");
        Call(controller,"Changed"); await Task.Delay(150);
        IList strokes=(IList)Get(controller,"Strokes"); int before=strokes.Count;
        Move(points[0].X,points[0].Y); await Task.Delay(50); Mouse(2);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")!=null; },tool+" starts through native input");
        object stroke=Get(controller,"CurrentStroke");
        Check(StrokeStyle(stroke,tool,ColorTranslator.FromHtml(color),width),tool+" captures selected style");
        for(int n=1;n<points.Length;n++) { Move(points[n].X,points[n].Y); await Task.Delay(60); }
        Mouse(4); await WaitFor(delegate { return Get(controller,"CurrentStroke")==null; },tool+" completes");
        Check(strokes.Count==before+1 && Object.ReferenceEquals(strokes[before],stroke),tool+" recorded once");
        IList saved=(IList)Get(stroke,"Points");
        if(tool=="Ligne droite" || tool=="Ellipse" || tool=="Flèche droite")
            Check(saved.Count==2 && (Point)saved[0]==points[0] && (Point)saved[1]==points[points.Length-1],tool+" retains exact start/end geometry");
        if(tool=="Surligneur" || tool=="Flèche libre")
            Check(saved.Count>=points.Length && (Point)saved[0]==points[0] && (Point)saved[saved.Count-1]==points[points.Length-1],tool+" retains continuous freehand points");
        Write("STROKE tool="+Get(stroke,"Tool")+" points="+saved.Count+" color="+Get(stroke,"Color")+" width="+Get(stroke,"Width")+" number="+Get(stroke,"Number"));
    }
    static void DialogInputs(IntPtr hwnd,INPUT[] inputs)
    {
        uint pid; GetWindowThreadProcessId(hwnd,out pid);
        if(GetForegroundWindow()!=hwnd || pid!=(uint)Process.GetCurrentProcess().Id) throw new Exception("Refused input outside owned dialog.");
        if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(INPUT)))!=inputs.Length) throw new Exception("Dialog SendInput failed");
    }
    static INPUT K(ushort key,bool up,bool unicode) { return new INPUT {type=1,data=new UNION {key=new KEY {vk=unicode?(ushort)0:key,scan=unicode?key:(ushort)0,flags=(up?2u:0u)|(unicode?4u:0u)}}}; }
    static async Task HandleText(string value,bool accept)
    {
        Form prompt=null;
        for(int n=0;n<100 && prompt==null;n++) {
            foreach(Form form in Application.OpenForms) if(form.GetType().FullName=="CursorFlow.TextPrompt") { prompt=form; break; }
            if(prompt==null) await Task.Delay(30);
        }
        Check(prompt!=null,"Actual text prompt opened");
        try {
            Check(!(bool)Get(controller,"IsDrawing"),"Drawing paused while text prompt is open");
            var input=(TextBox)Get(prompt,"input"); prompt.Activate(); input.Focus(); await Task.Delay(100);
            foreach(char c in value) { DialogInputs(prompt.Handle,new[]{K(c,false,true),K(c,true,true)}); await Task.Delay(20); }
            Check(input.Text==value,"Unicode text reaches annotation field");
            ushort finish=accept?(ushort)13:(ushort)27;
            DialogInputs(prompt.Handle,new[]{K(finish,false,false),K(finish,true,false)});
            await WaitFor(delegate { return prompt.IsDisposed; },accept?"Enter accepts text":"Escape cancels text");
        } finally { if(prompt!=null && !prompt.IsDisposed) prompt.Close(); }
    }
    static async Task TextStroke(string text,bool accept,Point point)
    {
        object config=Get(controller,"Options"); Set(config,"AnnotationTool","Texte"); Set(config,"AnnotationColor","#000080"); Set(config,"AnnotationThickness",3);
        Call(controller,"Changed"); await Task.Delay(150);
        var promptTask=HandleText(text,accept);
        Move(point.X,point.Y); Mouse(2);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")!=null; },"Text placement begins");
        Mouse(4); await promptTask;
        await RefocusTarget();
        await WaitFor(delegate { return (bool)Get(controller,"IsDrawing"); },"Drawing resumes after text prompt");
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int capacity);
    static async Task<string> Export(string name,bool cancel,bool empty)
    {
        string path=Path.Combine(folder,name);
        Check(!File.Exists(path),"Export uses a fresh test-only path: "+name);
        int ownPid=Process.GetCurrentProcess().Id;
        var worker=Task.Run(delegate {
            IntPtr dialog=IntPtr.Zero;
            try {
                for(int n=0;n<150 && dialog==IntPtr.Zero;n++) {
                    var hwnd=GetForegroundWindow(); uint pid; GetWindowThreadProcessId(hwnd,out pid);
                    var cls=new StringBuilder(128); GetClassName(hwnd,cls,cls.Capacity);
                    if(pid==(uint)ownPid && cls.ToString()=="#32770") dialog=hwnd;
                    else Thread.Sleep(100);
                }
                Check(dialog!=IntPtr.Zero,"Owned export dialog appears");
                Thread.Sleep(300);
                var root=System.Windows.Automation.AutomationElement.FromHandle(dialog);
                var nodes=root.FindAll(System.Windows.Automation.TreeScope.Descendants,System.Windows.Automation.Condition.TrueCondition);
                System.Windows.Automation.AutomationElement edit=null,save=null,close=null;
                bool emptyMessage=false;
                foreach(System.Windows.Automation.AutomationElement node in nodes) {
                    var c=node.Current;
                    if(c.ControlType==System.Windows.Automation.ControlType.Edit || c.ControlType==System.Windows.Automation.ControlType.Button || c.ControlType==System.Windows.Automation.ControlType.Text)
                        Write("DIALOG type="+c.ControlType.ProgrammaticName+" id="+c.AutomationId+" name="+c.Name);
                    if(c.ControlType==System.Windows.Automation.ControlType.Edit && c.AutomationId=="1001") edit=node;
                    if(c.ControlType==System.Windows.Automation.ControlType.Button && c.AutomationId=="1") save=node;
                    if(c.ControlType==System.Windows.Automation.ControlType.Button && c.AutomationId=="2") close=node;
                    if(c.Name.Contains("Aucun dessin à exporter.")) emptyMessage=true;
                }
                if(empty) Check(emptyMessage && save!=null,"Empty scene displays the expected information message");
                else {
                    Check(edit!=null && save!=null && close!=null,"Native Save dialog exposes filename, Save and Cancel");
                    var value=(System.Windows.Automation.ValuePattern)edit.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern);
                    value.SetValue(path);
                    Check(value.Current.Value==path,"Save filename matches the isolated Unicode path");
                }
                uint owner; GetWindowThreadProcessId(dialog,out owner);
                Check(owner==(uint)ownPid && GetForegroundWindow()==dialog,"Export action remains within the owned dialog");
                var button=cancel?close:save;
                ((System.Windows.Automation.InvokePattern)button.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
            } catch {
                if(dialog!=IntPtr.Zero) PostMessage(dialog,0x0010,IntPtr.Zero,IntPtr.Zero);
                throw;
            }
        });
        Call(controller,"ExportDrawings");
        await worker;
        await RefocusTarget();
        if(!cancel && !empty) Check(File.Exists(path) && new FileInfo(path).Length>100,"Actual PNG export created: "+name);
        return path;
    }
    static int AlphaPixels(Bitmap image,Rectangle area)
    {
        int count=0; Rectangle screen=SystemInformation.VirtualScreen;
        for(int y=area.Top;y<area.Bottom;y++) for(int x=area.Left;x<area.Right;x++) if(image.GetPixel(x-screen.Left,y-screen.Top).A>0) count++;
        return count;
    }
    static void ValidateGallery(string path,bool text,bool thirdNumber)
    {
        using(var image=new Bitmap(path)) {
            Rectangle screen=SystemInformation.VirtualScreen;
            Check(image.RawFormat.Guid==System.Drawing.Imaging.ImageFormat.Png.Guid && image.Size==screen.Size,"PNG format and virtual desktop dimensions are correct");
            Check(image.GetPixel(0,0).A==0,"Export background remains transparent");
            string[] names={"Surligneur","Ligne droite","Ellipse","Flèche droite","Flèche libre","Auto-formes","Numéro 1","Numéro 2"};
            Rectangle[] regions={new Rectangle(205,325,140,35),new Rectangle(205,380,150,25),new Rectangle(390,310,130,90),new Rectangle(565,310,170,95),new Rectangle(200,420,170,80),new Rectangle(390,400,100,100),new Rectangle(590,430,40,40),new Rectangle(645,430,40,40)};
            for(int n=0;n<names.Length;n++) { int pixels=AlphaPixels(image,regions[n]); Write("EXPORT region="+names[n]+" alphaPixels="+pixels); Check(pixels>100,"Export contains visible "+names[n]); }
            Color highlight=image.GetPixel(260-screen.Left,341-screen.Top);
            Check(highlight.A>0 && highlight.A<180 && highlight.R>200 && highlight.G>90,"Highlighter keeps translucent orange pixels");
            Check(image.GetPixel(455-screen.Left,355-screen.Top).A==0,"Ellipse remains hollow");
            int textPixels=AlphaPixels(image,new Rectangle(220,520,320,65));
            Check(text?textPixels>100:textPixels==0,text?"Accepted text is present in export":"Discarded text is absent from export");
            int third=AlphaPixels(image,new Rectangle(715,430,40,40));
            Check(thirdNumber?third>100:third==0,thirdNumber?"New number 3 is present in export":"No extra number appears in original export");
            Write("EXPORT file="+Path.GetFileName(path)+" size="+image.Width+"x"+image.Height+" textPixels="+textPixels+" thirdNumberPixels="+third);
        }
    }
    static bool PixelsEqual(string first,string second)
    {
        using(var a=new Bitmap(first)) using(var b=new Bitmap(second)) {
            if(a.Size!=b.Size) return false;
            for(int y=0;y<a.Height;y++) for(int x=0;x<a.Width;x++) if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb()) return false;
            return true;
        }
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
