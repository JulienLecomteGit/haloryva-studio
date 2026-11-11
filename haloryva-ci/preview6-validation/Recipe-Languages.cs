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

// Targeted Studio controls, smoothing and assisted shapes recipe. Loads the new preview.6 artifact; never rebuilds it.
internal static class Recipe
{
    static string folder, mode; static StreamWriter log; static Assembly app; static object controller;
    static Process target; static IntPtr targetWindow; static int code; static Point original;
    static readonly System.Collections.Generic.HashSet<ushort> heldKeys=new System.Collections.Generic.HashSet<ushort>();
    static readonly System.Collections.Generic.Dictionary<uint,uint> heldButtons=new System.Collections.Generic.Dictionary<uint,uint>();
    static bool observe; static int assertions;
    static string requestedLanguage, startupLanguage;
    static string T(string value) { return (string)Static("L","T",value); }
    static string NextLanguage(string value) { return value=="fr"?"en":value=="en"?"es":"fr"; }
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
    static void Mouse(uint flags,uint data=0) {
        Input(new INPUT {data=new UNION {mouse=new MOUSE {flags=flags,data=data}}});
        if(flags==2||flags==8||flags==32||flags==128) heldButtons[flags*2]=data;
        else heldButtons.Remove(flags);
    }
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
            if(mode!="features" && mode!="reload") throw new Exception("Unknown mode");
            observe=args.Length>2 && args[2]=="on";
            requestedLanguage=Environment.GetEnvironmentVariable("HALORYVA_LANGUAGE")??"fr";
            Check(app.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion=="2.7.1-preview.6","Expected preview.6 assembly identity");
            Write("TEST observer="+observe+" assembly="+app.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
            SetProcessDPIAware(); original=Cursor.Position;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string trialProfile=Path.Combine(folder,mode=="reload"?"profile-reload":"profile");
            Static("TrailTrial","Initialize",(object)new[]{"--trail-session",trialProfile});
            if(mode=="reload") {
                if(args.Length<4) throw new Exception("Saved profile evidence path is required");
                string savedProfile=Path.GetFullPath(args[3]);
                string runnerRoot=Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!savedProfile.StartsWith(runnerRoot,StringComparison.OrdinalIgnoreCase)) throw new Exception("Saved test profile must be under RUNNER_TEMP");
                Check(File.Exists(savedProfile),"Saved profile from successful native feature phase exists");
                string destination=Path.Combine(trialProfile,"settings.json");
                if(File.Exists(destination)) throw new Exception("Unexpected existing settings in fresh test profile");
                // TrailTrial intentionally requires a new directory on each run.
                // Copy the exact persisted bytes into that new isolated profile;
                // production ConfigStore.Load below performs the actual reload.
                File.Copy(savedProfile,destination);
                string before=FileSHA256(savedProfile),after=FileSHA256(destination);
                Check(before==after,"Fresh profile contains exactly the previously persisted JSON bytes");
                Write("PERSISTENCE sourceSHA256="+before+" copiedSHA256="+after);
            }
            // New known profile inside virtualized storage, never the normal installed profile.
            if(mode=="features") {
                string legacy=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"legacy-profile.json"),Encoding.UTF8);
                var config=Static("ConfigStore","Deserialize",legacy);
                Set(config,"UiLanguage",requestedLanguage);
                Static("ConfigStore","Save",config);
            }
            controller=Activator.CreateInstance(app.GetType("CursorFlow.CursorController"),true);
            startupLanguage=(string)Static("L","get_Language");
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            SynchronizationContext.Current.Post(async delegate {
                try { await Functional(); Write("COMPLETE targeted cycle phase="+mode+" passed"); }
                catch(Exception ex) { code=1; Write("ERROR "+ex); try { CaptureFull("failure.png"); DumpUi("failure"); } catch(Exception captureError) { Write("DIAGNOSTIC "+captureError.Message); } }
                finally {
                    foreach(ushort key in heldKeys) SendInput(1,new[]{new INPUT {type=1,data=new UNION {key=new KEY {vk=key,flags=2u|(key==0xa5?1u:0u)}}}},Marshal.SizeOf(typeof(INPUT)));
                    foreach(var button in heldButtons) SendInput(1,new[]{new INPUT {data=new UNION {mouse=new MOUSE {flags=button.Key,data=button.Value}}}},Marshal.SizeOf(typeof(INPUT)));
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
        Call(controller,"Changed"); await Task.Delay(200);
        suite=Environment.GetEnvironmentVariable("HALORYVA_RECIPE_SUITE")??"studio";
        Call(controller,"ShowSettings");
        await WaitFor(delegate { return Studio!=null && Studio.Visible; },"Studio window opened from preview.6 binary");
        VerifyPlacement("initial");
        await FocusStudio();
        CaptureFull("studio-initial.png"); DumpUi("initial");
        if(suite!="language") throw new Exception("Only the targeted localization suite is authorized.");
        if(mode=="reload") await LocalizationReload(); else await LocalizationRecipe();
        Write("NOT_RUN legacy195, complete prior suites, MSIX, physical devices, multiple screens/DPI");
    }
    static string suite;
    static Form Studio { get { return (Form)Get(controller,"settings"); } }
    static IList Strokes { get { return (IList)Get(controller,"Strokes"); } }
    static IList Redo { get { return (IList)Get(controller,"RedoStrokes"); } }
    static object Config { get { return Get(controller,"Options"); } }
    static System.Collections.Generic.List<Control> Descendants(Control root)
    {
        var result=new System.Collections.Generic.List<Control>();
        foreach(Control c in root.Controls) { result.Add(c); result.AddRange(Descendants(c)); }
        return result;
    }
    static Control FindControl(Func<Control,bool> predicate)
    {
        foreach(Control c in Descendants(Studio)) if(predicate(c)) return c;
        throw new Exception("Requested Studio control not found");
    }
    static Control Button(string text) { return FindControl(delegate(Control c) { return c.GetType().Name=="PremiumButton" && c.Text==T(text); }); }
    static Control Grid(string contains) { return FindControl(delegate(Control c) { return c.GetType().Name=="PremiumChoiceGrid" && Array.IndexOf((string[])Get(c,"values"),contains)>=0; }); }
    static TextBox Editor(string accessibleName) {
        // A color field composes its localized base name and suffix separately.
        const string suffix=" (hexadécimal)";
        string expected=accessibleName.EndsWith(suffix,StringComparison.Ordinal)
            ? T(accessibleName.Substring(0,accessibleName.Length-suffix.Length))+T(suffix) : T(accessibleName);
        foreach(Control c in Descendants(Studio)) if(c is TextBox && c.AccessibleName==expected) return (TextBox)c;
        foreach(Control c in Descendants(Studio)) if(c is TextBox) Write("EDITOR available="+c.AccessibleName);
        throw new Exception("Expected localized editor not found: "+expected);
    }
    static async Task FocusStudio()
    {
        if(Studio==null || Studio.IsDisposed || !Studio.Visible) throw new Exception("Studio not visible");
        if(!Screen.FromControl(Studio).WorkingArea.Contains(Studio.Bounds))
            throw new Exception("Studio is outside the working area. Preview.4 pilot must not recenter it.");
        for(int n=0;n<30 && GetForegroundWindow()!=Studio.Handle;n++) { Studio.Activate(); SetForegroundWindow(Studio.Handle); await Task.Delay(100); }
        if(GetForegroundWindow()!=Studio.Handle) throw new Exception("Studio could not acquire foreground; no input sent");
        await Task.Delay(100);
    }
    static INPUT AbsoluteMove(Point p)
    {
        Rectangle r=SystemInformation.VirtualScreen;
        if(!r.Contains(p)) throw new Exception("UI point outside test screen");
        return new INPUT {data=new UNION {mouse=new MOUSE {x=(int)(((long)(p.X-r.Left)*65536+32768)/r.Width),y=(int)(((long)(p.Y-r.Top)*65536+32768)/r.Height),flags=0xc001}}};
    }
    static async Task UiClick(Control control) { await UiClick(control,new Point(control.Width/2,control.Height/2)); }
    static async Task UiClick(Control control,Point local)
    {
        await FocusStudio();
        if(control.IsDisposed || !control.Visible || !control.Enabled) throw new Exception("Cannot click unavailable control "+control.GetType().Name+" "+control.Text);
        var workspace=(ScrollableControl)Get(Studio,"workspace");
        bool inside=false;
        for(Control p=control;p!=null;p=p.Parent) if(p==workspace) inside=true;
        if(inside) {
            workspace.ScrollControlIntoView(control); await Task.Delay(80);
            for(int attempt=0;attempt<4;attempt++) {
                Point screenPoint=control.PointToScreen(local);
                Rectangle viewport=Rectangle.Intersect(workspace.RectangleToScreen(workspace.ClientRectangle),Screen.FromControl(Studio).WorkingArea);
                int shift=screenPoint.Y<viewport.Top+25?screenPoint.Y-(viewport.Top+25):screenPoint.Y>viewport.Bottom-25?screenPoint.Y-(viewport.Bottom-25):0;
                if(shift==0) break;
                Write("UI scroll point="+screenPoint+" viewport="+viewport+" before="+workspace.AutoScrollPosition+" shift="+shift);
                workspace.AutoScrollPosition=new Point(0,-workspace.AutoScrollPosition.Y+shift);
                await Task.Delay(120);
                Write("UI scroll after="+workspace.AutoScrollPosition+" point="+control.PointToScreen(local));
            }
        }
        Point point=control.PointToScreen(local);
        if(!Screen.FromControl(Studio).WorkingArea.Contains(point)) throw new Exception("Studio click would leave visible working area: "+point);
        for(Control p=control;p!=null;p=p.Parent)
            if(!p.RectangleToScreen(p.ClientRectangle).Contains(point)) throw new Exception("Click point is clipped by "+p.GetType().Name+" for "+control.Text+" at "+point);
        Write("UI CLICK type="+control.GetType().Name+" text="+control.Text+" accessible="+control.AccessibleName+" point="+point);
        IntPtr hwnd=Studio.Handle;
        DialogInputs(hwnd,new[]{AbsoluteMove(point)}); await Task.Delay(80);
        DialogInputs(hwnd,new[]{new INPUT {data=new UNION {mouse=new MOUSE {flags=2}}},new INPUT {data=new UNION {mouse=new MOUSE {flags=4}}}});
        await Task.Delay(180);
    }
    static async Task UiKey(ushort key)
    {
        await FocusStudio(); DialogInputs(Studio.Handle,new[]{K(key,false,false),K(key,true,false)}); await Task.Delay(100);
    }
    static async Task TypeEditor(TextBox editor,string value)
    {
        await UiClick(editor);
        DialogInputs(Studio.Handle,new[]{K(0xa2,false,false),K(0x41,false,false),K(0x41,true,false),K(0xa2,true,false)});
        foreach(char ch in value) { DialogInputs(Studio.Handle,new[]{K(ch,false,true),K(ch,true,true)}); await Task.Delay(10); }
        await UiKey(13);
    }
    static async Task Navigate(int page)
    {
        Control tab=FindControl(delegate(Control c) { return c.GetType().Name=="PremiumNavButton" && (int)c.Tag==page; });
        bool clicked=false;
        EventHandler observed=delegate { clicked=true; Write("NAV_NATIVE_CLICK requested="+page+" actual="+Get(Studio,"page")); };
        tab.Click+=observed;
        var navigationClock=Stopwatch.StartNew();
        try {
            Write("NAV_BEGIN requested="+page+" actual="+Get(Studio,"page")+" control="+tab.Text+" bounds="+tab.Bounds);
            await UiClick(tab);
            await WaitFor(delegate { return (int)Get(Studio,"page")==page; },"Visible navigation reaches page "+page);
            Write("NAV_DONE requested="+page+" clicked="+clicked+" elapsedMs="+navigationClock.ElapsedMilliseconds);
        } finally { tab.Click-=observed; }
    }
    static async Task Section(int index)
    {
        if((int)Get(Studio,"section")==index) return;
        Control tabs=(Control)Get(Studio,"sections");
        Rectangle r=(Rectangle)Call(tabs,"Segment",index);
        await UiClick(tabs,new Point(r.X+r.Width/2,r.Y+r.Height/2));
        Check((int)Get(Studio,"section")==index,"Visible drawing tab switches to "+index);
    }
    static async Task Choose(Control grid,string value)
    {
        int i=Array.IndexOf((string[])Get(grid,"values"),value);
        if(i<0) throw new Exception("Unknown visible choice "+value);
        Rectangle r=(Rectangle)Call(grid,"Cell",i);
        await UiClick(grid,new Point(r.X+r.Width/2,r.Y+r.Height/2));
        Check((string)Get(grid,"Value")==value,"Visible choice selected: "+value);
    }
    static async Task Tool(string value)
    {
        await Section(0); await Choose(Grid("Stylo"),value);
        Check((string)Get(Config,"AnnotationTool")==value,"Studio tool reaches engine: "+value);
    }
    static async Task SmoothMode(string mode,int strength)
    {
        await Section(1); await Choose(Grid("Lissage léger"),mode);
        Check((string)Get(Config,"DrawSmoothMode")==mode,"Studio smoothing mode reaches engine: "+mode);
        await TypeEditor(Editor("Intensité du lissage"),strength.ToString());
        Check((int)Get(Config,"DrawSmoothStrength")==strength,"Studio smoothing intensity reaches engine: "+strength);
    }
    static async Task ColorSetting(string color)
    {
        await Section(0); await TypeEditor(Editor("Couleur du trait (hexadécimal)"),color);
        Check((string)Get(Config,"AnnotationColor")==color,"Studio color reaches engine: "+color);
    }
    static async Task StartDesktop(bool quick=false)
    {
        await UiClick(quick?(Control)Get(Studio,"quickDraw"):Button("Dessiner sur le Bureau"));
        await WaitFor(delegate { return (bool)Get(controller,"IsDrawing") && !Studio.Visible; },"Draw command hides Studio and starts drawing");
        await RefocusTarget();
    }
    static async Task StopDesktop(bool right=false)
    {
        if(right) { Mouse(8); Mouse(16); } else { Key(27); Key(27,true); }
        await WaitFor(delegate { return !(bool)Get(controller,"IsDrawing") && Studio.Visible; },right?"Right click stops drawing and restores Studio":"Escape stops drawing and restores Studio");
        await FocusStudio();
    }
    static Point[] CopyPoints(object stroke,string field="Points")
    {
        IList list=(IList)Get(stroke,field); var points=new Point[list.Count]; list.CopyTo(points,0); return points;
    }
    static bool SamePoints(Point[] a,Point[] b)
    {
        if(a.Length!=b.Length) return false;
        for(int n=0;n<a.Length;n++) if(a[n]!=b[n]) return false;
        return true;
    }
    static double PathLength(Point[] points)
    {
        double length=0;
        for(int n=1;n<points.Length;n++) { double dx=points[n].X-points[n-1].X,dy=points[n].Y-points[n-1].Y; length+=Math.Sqrt(dx*dx+dy*dy); }
        return length;
    }
    static void PointFile(string name,Point[] points)
    {
        var content=new StringBuilder("index,x,y\r\n");
        for(int n=0;n<points.Length;n++) content.AppendLine(n+","+points[n].X+","+points[n].Y);
        File.WriteAllText(Path.Combine(folder,name+".csv"),content.ToString(),Encoding.UTF8);
    }
    static async Task<object> Gesture(string name,Point[] points)
    {
        int count=Strokes.Count,downs=Query(0),ups=Query(1);
        string tool=(string)Get(Config,"AnnotationTool");
        Move(points[0].X,points[0].Y); await Task.Delay(60); Mouse(2);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")!=null; },name+" starts from native DOWN");
        object stroke=Get(controller,"CurrentStroke");
        for(int n=1;n<points.Length;n++) { Move(points[n].X,points[n].Y); await Task.Delay(45); }
        await Task.Delay(80);
        Point[] held=CopyPoints(stroke);
        Check((string)Get(stroke,"Tool")==tool && !(bool)Get(stroke,"Smoothed"),name+" stays raw until mouse release");
        PointFile(name+"-held",held);
        Mouse(4); await WaitFor(delegate { return Get(controller,"CurrentStroke")==null; },name+" completes through native UP");
        Check(Strokes.Count==count+1 && Object.ReferenceEquals(Strokes[count],stroke),name+" recorded exactly once");
        Check(Query(0)==downs && Query(1)==ups,name+" consumes drawing click");
        PointFile(name+"-result",CopyPoints(stroke)); PointFile(name+"-original",CopyPoints(stroke,"OriginalPoints"));
        Write("GESTURE name="+name+" tool="+Get(stroke,"Tool")+" points="+CopyPoints(stroke).Length+" original="+CopyPoints(stroke,"OriginalPoints").Length+" smoothed="+Get(stroke,"Smoothed"));
        return stroke;
    }
    static void CaptureFull(string file)
    {
        Rectangle screen=SystemInformation.VirtualScreen;
        using(var image=new Bitmap(screen.Width,screen.Height)) {
            using(var g=Graphics.FromImage(image)) g.CopyFromScreen(screen.Location,Point.Empty,image.Size);
            image.Save(Path.Combine(folder,file),System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    static void DumpUi(string label)
    {
        if(Studio==null) return;
        var text=new StringBuilder("Studio bounds="+Studio.Bounds+" screen="+SystemInformation.VirtualScreen+" work="+Screen.FromControl(Studio).WorkingArea+"\r\n");
        foreach(Control c in Descendants(Studio)) text.AppendLine(c.GetType().Name+" | "+c.Text+" | "+c.AccessibleName+" | "+c.Bounds+" | visible="+c.Visible+" enabled="+c.Enabled);
        File.WriteAllText(Path.Combine(folder,"ui-"+label+".txt"),text.ToString(),Encoding.UTF8);
    }

    static string FileSHA256(string path)
    {
        using(var stream=File.OpenRead(path))
        using(var sha=System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
    }
    static string[] AllTools { get { return (string[])app.GetType("CursorFlow.DrawingTools").GetField("Names",Flags).GetValue(null); } }
    static string[] Cycle(object config) { return (string[])Get(config,"DrawingToolCycle"); }
    static bool SameTools(string[] a,string[] b) { return a!=null && b!=null && String.Join("|",a)==String.Join("|",b); }
    static void CheckCycle(string[] expected,string label) { Check(SameTools(Cycle(Config),expected),label+" ["+String.Join(", ",Cycle(Config))+"]"); }
    static Control CycleSwitch(string tool)
    {
        int i=Array.IndexOf(AllTools,tool);
        string[] captions=(string[])app.GetType("CursorFlow.DrawingTools").GetField("Captions",Flags).GetValue(null);
        return FindControl(delegate(Control c) { return c.GetType().Name=="PremiumSwitch" && c.AccessibleName==T("Inclure ")+T(captions[i])+T(" dans le défilement"); });
    }
    static async Task ChooseCycle(params string[] wanted)
    {
        await Section(2);
        foreach(string tool in wanted) if(!(bool)Get(CycleSwitch(tool),"Checked")) await UiClick(CycleSwitch(tool));
        foreach(string tool in AllTools)
            if(Array.IndexOf(wanted,tool)<0 && (bool)Get(CycleSwitch(tool),"Checked")) await UiClick(CycleSwitch(tool));
        CheckCycle(wanted,"Native switches select the requested cycle");
        foreach(string tool in AllTools)
            Check((bool)Get(CycleSwitch(tool),"Checked")==(Array.IndexOf(wanted,tool)>=0),"Switch reflects inclusion: "+tool);
    }
    static async Task SelectAction(string name,string value)
    {
        Control control=FindControl(delegate(Control c) { return c.GetType().Name=="PremiumSelect" && c.AccessibleName==name; });
        await UiClick(control); await UiKey(27);
        control.Focus();
        string[] options=(string[])Get(control,"options");
        int wanted=Array.IndexOf(options,value);
        for(int n=0;n<options.Length && (string)Get(control,"Value")!=value;n++) {
            int current=Array.IndexOf(options,(string)Get(control,"Value"));
            await UiKey((ushort)(current<wanted?0x28:0x26));
        }
        Check((string)Get(control,"Value")==value,"Native mapping selector: "+name+" = "+value);
    }
    static async Task CycleButton(uint button,string expected,int hold=45)
    {
        // Zero denotes middle; 1 and 2 are native X1 / X2.
        int down=Query(0),up=Query(1),unpaired=Query(7);
        await Click(button==0?32u:128u,button==0?64u:256u,button,hold);
        await WaitFor(delegate { return (string)Get(Config,"AnnotationTool")==expected; },"Native button "+button+" hold="+hold+" selects "+expected);
        Check(Query(0)==down && Query(1)==up && Query(7)==unpaired,"Mapped button emits no native click or unpaired event");
        Check((int)Get(Get(controller,"toast"),"index")==Array.IndexOf(AllTools,expected),"Toast identifies the actual selected tool");
    }

    static void CheckProfileFields(string originalJson,object actual,string label)
    {
        var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
        var original=serializer.Deserialize<System.Collections.Generic.Dictionary<string,object>>(originalJson);
        var current=serializer.Deserialize<System.Collections.Generic.Dictionary<string,object>>(serializer.Serialize(actual));
        foreach(var pair in original) {
            if(!current.ContainsKey(pair.Key) || serializer.Serialize(pair.Value)!=serializer.Serialize(current[pair.Key]))
                throw new Exception(label+": changed field "+pair.Key);
        }
        Check(true,label+" ("+original.Count+" original fields)");
    }
    static void TranslationCases()
    {
        var culture=System.Globalization.CultureInfo.CurrentCulture.Name;
        Check((string)Static("L","get_Language")==requestedLanguage,"Application starts in requested language "+requestedLanguage);
        Check((int)Static("L","get_EntryCount")>=440,"Embedded complete UI source catalog is loaded");
        string[] cases={null,"","invalid"," FR-fr ","en-GB","es_ES"};
        string[] expected={"fr","fr","fr","fr","en","es"};
        for(int i=0;i<cases.Length;i++)
            Check((string)Static("L","NormalizeLanguage",new object[]{cases[i]})==expected[i],"Language fallback/normalization case "+i);
        string legacy=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"legacy-profile.json"),Encoding.UTF8);
        object old=Static("ConfigStore","Deserialize",legacy);
        Check((string)Get(old,"UiLanguage")=="fr","Old profile missing language defaults to French");
        CheckProfileFields(legacy,old,"Old preview.5 profile retains every existing field");
        object invalid=Static("ConfigStore","Deserialize","{\"UiLanguage\":\"xx\",\"AnnotationTool\":\"Ellipse\",\"DrawingToolCycle\":[\"Stylo\",\"Rectangle\"]}");
        Check((string)Get(invalid,"UiLanguage")=="fr" && (string)Get(invalid,"AnnotationTool")=="Ellipse","Unknown language preserves valid drawing settings");
        Check(SameTools(AllTools,new[]{"Stylo","Surligneur","Ligne droite","Rectangle","Ellipse","Flèche droite","Flèche libre","Forme libre","Numéro","Texte"}),"Ten canonical tool identities remain unchanged");
        Check(T("Accueil")==(requestedLanguage=="fr"?"Accueil":requestedLanguage=="en"?"Home":"Inicio"),"Home caption matches independently specified translation");
        Check(T("Outil de dessin suivant")==(requestedLanguage=="fr"?"Outil de dessin suivant":requestedLanguage=="en"?"Next drawing tool":"Siguiente herramienta de dibujo"),"Button action has its expected translated caption");
        Check(System.Globalization.CultureInfo.CurrentCulture.Name==culture,"Localization keeps the existing data culture");
    }
    static void CheckGridFit(string label)
    {
        foreach(Control c in Descendants(Studio)) if(c.GetType().Name=="PremiumChoiceGrid") {
            string[] captions=(string[])Get(c,"captions");
            for(int i=0;i<captions.Length;i++) {
                Rectangle cell=(Rectangle)Call(c,"Cell",i);
                int width=TextRenderer.MeasureText(T(captions[i]),c.Font,new Size(4000,200),
                    TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix).Width;
                if(width>cell.Width-8) throw new Exception("Translated caption clipped: "+label+" "+T(captions[i])+" width="+width+" available="+(cell.Width-8));
            }
        }
        Check(true,"Translated choice captions fit: "+label);
    }
    static async Task LocalizationRecipe()
    {
        TranslationCases();
        string[] pageNames={"Accueil","Halo","Traînées","Clics","Boutons souris","Dessin","Présentation","Préférences"};
        for(int page=0;page<8;page++) {
            await Navigate(page);
            Control tab=FindControl(delegate(Control c) { return c.GetType().Name=="PremiumNavButton" && (int)c.Tag==page; });
            Check(tab.Text==T(pageNames[page]) && tab.AccessibleName==T(pageNames[page]),"Navigation caption and accessible name localized: "+page);
            CheckGridFit("page-"+page);
            CaptureFull("page-"+page+"-"+startupLanguage+".png"); DumpUi("page-"+page);
            if(page==1 || page==2 || page==5) {
                int sections=page==5?3:2;
                for(int part=1;part<sections;part++) {
                    await Section(part); CheckGridFit("page-"+page+"-section-"+part);
                    CaptureFull("page-"+page+"-section-"+part+"-"+startupLanguage+".png");
                    DumpUi("page-"+page+"-section-"+part);
                }
            }
        }
        // Real mouse actions run against the same binary under each UI language.
        await Navigate(4);
        await UiClick(Button("X2 outil suivant · appui long : précédent"));
        Check((string)Get(Config,"ButtonX2")=="Outil de dessin suivant" &&
              (string)Get(Config,"ButtonX2Long")=="Outil de dessin précédent","Localized preset preserves canonical short/long actions");
        await Navigate(5); await ChooseCycle("Stylo","Rectangle"); await Tool("Stylo");
        await StartDesktop(); await CycleButton(2,"Rectangle",45); await CycleButton(2,"Stylo",650); await StopDesktop();
        await Navigate(7);
        Control language=FindControl(delegate(Control c) { return c.Name=="UiLanguage"; });
        string next=NextLanguage(requestedLanguage);
        string fingerprint=(string)Call(Studio,"PresetFingerprint");
        IntPtr keyboard=GetKeyboardLayout(0);
        string culture=System.Globalization.CultureInfo.CurrentCulture.Name;
        await Choose(language,next);
        Check((string)Get(Config,"UiLanguage")==next && (string)Static("L","get_Language")==startupLanguage,"Native language choice is saved for the next launch, not applied mid-session");
        Check((string)Call(Studio,"PresetFingerprint")==fingerprint,"Language choice does not change the active preset fingerprint");
        Check(GetKeyboardLayout(0)==keyboard && System.Globalization.CultureInfo.CurrentCulture.Name==culture,"Language selection changes neither keyboard layout nor data culture");
        await Task.Delay(800);
        Check((string)Get(Static("ConfigStore","Load"),"UiLanguage")==next,"Language choice is saved by deferred autosave");
        CaptureFull("language-pending-"+startupLanguage+".png");
        // Invoke the real preference control callback during a native held stroke.
        // The callback is invoked by the driver; it is not claimed as a second mouse click while held.
        await StartDesktop(true);
        int count=Strokes.Count,down=Query(0),up=Query(1);
        Move(300,370); await Task.Delay(80); Mouse(2);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")!=null; },"Native held stroke starts before language callback");
        object held=Get(controller,"CurrentStroke");
        string color=(string)Get(Config,"AnnotationColor"); int width=(int)Get(Config,"AnnotationThickness");
        Call(language,"Pick",Array.IndexOf((string[])Get(language,"values"),requestedLanguage));
        Check(Object.ReferenceEquals(Get(controller,"CurrentStroke"),held) &&
            (string)Static("L","get_Language")==startupLanguage,"Language preference callback leaves the current stroke and active UI intact");
        Move(350,395); await Task.Delay(100); await CycleButton(2,"Rectangle",45);
        Check(StrokeStyle(held,"Stylo",ColorTranslator.FromHtml(color),width),"Held stroke keeps its captured tool, color and width");
        Move(420,380); await Task.Delay(100); Mouse(4);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")==null; },"Held stroke completes normally");
        Check(Strokes.Count==count+1 && Object.ReferenceEquals(Strokes[count],held),"Held stroke is recorded exactly once");
        Check(Query(0)==down && Query(1)==up,"Language callback and drawing do not leak clicks");
        object rectangle=await Gesture("language-next-rectangle",new[]{new Point(470,400),new Point(520,440),new Point(590,460)});
        Check((string)Get(rectangle,"Tool")=="Rectangle","Next stroke uses the canonical next tool");
        // A real localized text dialog accepts accents without translating user content.
        await TextStroke("Stylo / Français / English / Español / ñ @ €",true,new Point(320,500));
        object textStroke=Strokes[Strokes.Count-1];
        Check((string)Get(textStroke,"Text")=="Stylo / Français / English / Español / ñ @ €","Annotation data is not translated");
        await StopDesktop();
        await Navigate(7); language=FindControl(delegate(Control c) { return c.Name=="UiLanguage"; });
        await Choose(language,next);
        await Navigate(5); await Tool("Stylo");
        await TypeEditor(Editor("Épaisseur"),"7"); await ColorSetting("#12A4DE");
        await UiClick(Button("Annuler")); Check(Redo.Count==1,"Translated Undo command works");
        await UiClick(Button("Rétablir")); Check(Redo.Count==0,"Translated Redo command works");
        string png=await Export("localized-annotations-"+startupLanguage+".png",false,false);
        using(var bitmap=new Bitmap(png)) Check(bitmap.Size==SystemInformation.VirtualScreen.Size && bitmap.GetPixel(0,0).A==0,"Localized export produces a transparent annotation PNG");
        await Task.Delay(800);
        string profile=Path.Combine(folder,"localized-profile-export.json");
        Static("ConfigStore","Export",Config,profile);
        object imported=Static("ConfigStore","Import",profile);
        CheckProfileFields(File.ReadAllText(profile,Encoding.UTF8),imported,"Export/import keeps every stored value");
        Check((string)Get(imported,"UiLanguage")==next && SameTools(Cycle(imported),new[]{"Stylo","Rectangle"}),"Language and selected tools survive export/import");
        Control close=FindControl(delegate(Control c) { return c.AccessibleName==T("Fermer (Haloryva reste actif près de l'horloge)"); });
        await UiClick(close); Call(controller,"ShowSettings");
        await WaitFor(delegate { return Studio!=null && Studio.Visible; },"Studio reopens without restarting the application");
        Check((string)Static("L","get_Language")==startupLanguage && (string)Get(Config,"UiLanguage")==next,"Reopening Studio keeps the current language until process restart");
        Write("CASES language="+startupLanguage+" pending="+next+" nativeSelection=true allPages=true layoutGrid=true legacyProfile=true shortLong=true heldStrokeCallback=true unicodeText=true export=true");
    }
    static async Task LocalizationReload()
    {
        string expected=NextLanguage(requestedLanguage);
        Check(startupLanguage==expected && (string)Get(Config,"UiLanguage")==expected,"Fresh process applies the language saved through the native UI");
        CheckCycle(new[]{"Stylo","Rectangle"},"Fresh process preserves selected tools");
        Check((string)Get(Config,"ButtonX2")=="Outil de dessin suivant" && (string)Get(Config,"ButtonX2Long")=="Outil de dessin précédent","Fresh process preserves native button actions");
        Check((string)Get(Config,"AnnotationColor")=="#12A4DE" && (int)Get(Config,"AnnotationThickness")==7,"Fresh process preserves color and thickness");
        await Navigate(7);
        Control choice=FindControl(delegate(Control c) { return c.Name=="UiLanguage"; });
        Check((string)Get(choice,"Value")==expected,"Language selector displays the persisted language");
        CaptureFull("after-restart-"+expected+".png"); DumpUi("after-restart-"+expected);
        await Navigate(5); await Section(2);
        foreach(string tool in new[]{"Stylo","Rectangle"}) Check((bool)Get(CycleSwitch(tool),"Checked"),"Translated cycle switch remains selected: "+tool);
        await Tool("Stylo"); await StartDesktop(); await CycleButton(2,"Rectangle",45); await CycleButton(2,"Stylo",650); await StopDesktop();
        Write("CASES freshProcessLanguage="+expected+" persistedTools=true nativeShortLong=true");
    }

    static void CycleConfigCases()
    {
        Check(SameTools(Cycle(Activator.CreateInstance(app.GetType("CursorFlow.Config"))),AllTools),"Fresh profile preserves the original complete cycle");
        object old=Static("ConfigStore","Deserialize","{\"AnnotationTool\":\"Ellipse\",\"AnnotationThickness\":9}");
        Check(SameTools(Cycle(old),AllTools) && (string)Get(old,"AnnotationTool")=="Ellipse" && (int)Get(old,"AnnotationThickness")==9,"Older JSON profile keeps full cycle and existing settings");
        foreach(string json in new[]{"{\"DrawingToolCycle\":null}","{\"DrawingToolCycle\":[]}","{\"DrawingToolCycle\":[\"unknown\",null]}"}) {
            object recovered=Static("ConfigStore","Deserialize",json);
            Check(SameTools(Cycle(recovered),AllTools),"Invalid or empty cycle safely restores the complete list");
        }
        object cleaned=Static("ConfigStore","Deserialize","{\"DrawingToolCycle\":[\"Rectangle\",\"Stylo\",\"Rectangle\",\"unknown\",null]}");
        Check(SameTools(Cycle(cleaned),new[]{"Stylo","Rectangle"}),"Imported cycle removes unknowns/duplicates and uses stable display order");
        object first=Activator.CreateInstance(app.GetType("CursorFlow.Config")),second=Activator.CreateInstance(app.GetType("CursorFlow.Config"));
        Cycle(first)[0]="Rectangle";
        Check(Cycle(second)[0]=="Stylo" && AllTools[0]=="Stylo","Default cycle arrays are independent per profile");
    }
    static async Task CycleRecipe()
    {
        if(mode=="reload") { await ReloadCycle(); return; }
        CycleConfigCases();
        await Navigate(4);
        await SelectAction("Latéral X1 (arrière), appui court","Outil de dessin suivant");
        await SelectAction("Latéral X2 (avant), appui court","Outil de dessin précédent");
        await SelectAction("Molette (clic), appui court","Outil de dessin suivant");
        await UiClick(Button("Choisir les outils à faire défiler"));
        Check((int)Get(Studio,"page")==5 && (int)Get(Studio,"section")==2,"Mouse mapping links directly to tool selection");
        CheckCycle(AllTools,"Initial UI profile includes every tool");
        await Tool("Stylo"); await StartDesktop();
        for(int i=1;i<=AllTools.Length;i++) await CycleButton(1,AllTools[i%AllTools.Length]);
        await CycleButton(2,"Texte"); await CycleButton(0,"Stylo");
        await StopDesktop();

        await ChooseCycle("Stylo","Rectangle");
        CaptureFull("cycle-two-tools.png"); DumpUi("cycle-two");
        await Tool("Ellipse");
        CheckCycle(new[]{"Stylo","Rectangle"},"Direct selection outside cycle does not change it");
        await StartDesktop(); await CycleButton(1,"Stylo"); await CycleButton(1,"Rectangle");
        await CycleButton(1,"Stylo"); await CycleButton(2,"Rectangle"); await CycleButton(2,"Stylo");
        await StopDesktop();
        await Tool("Ellipse"); await StartDesktop(); await CycleButton(2,"Rectangle"); await CycleButton(1,"Stylo");

        // While a real left-button stroke is held, change the next tool with X1.
        Set(Config,"DrawSmoothMode","Désactivé"); Set(Config,"AnnotationColor","#12A4DE"); Set(Config,"AnnotationThickness",7);
        Call(controller,"Changed"); await Task.Delay(180);
        int count=Strokes.Count,down=Query(0),up=Query(1);
        Move(300,370); await Task.Delay(60); Mouse(2);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")!=null; },"Held stroke begins with Stylo");
        object held=Get(controller,"CurrentStroke");
        Move(350,395); await Task.Delay(100);
        await CycleButton(1,"Rectangle");
        Check(Object.ReferenceEquals(Get(controller,"CurrentStroke"),held) && StrokeStyle(held,"Stylo",ColorTranslator.FromHtml("#12A4DE"),7),"Cycling during held stroke preserves its identity, tool, color and width");
        Move(420,380); await Task.Delay(100); Mouse(4);
        await WaitFor(delegate { return Get(controller,"CurrentStroke")==null; },"Held stroke finishes after cycling");
        Check(Strokes.Count==count+1 && Object.ReferenceEquals(Strokes[count],held) && (string)Get(held,"Tool")=="Stylo","Held stroke is recorded exactly once as Stylo");
        Check(Query(0)==down && Query(1)==up,"Drawing and cycling do not leak clicks to the target");
        object next=await Gesture("cycle-next-rectangle",new[]{new Point(470,400),new Point(520,440),new Point(590,460)});
        Check((string)Get(next,"Tool")=="Rectangle","Next stroke uses the newly selected Rectangle");
        CaptureFull("cycle-held-stroke.png");
        await StopDesktop();

        await ChooseCycle("Rectangle");
        Control only=CycleSwitch("Rectangle");
        Check(!only.Enabled && (bool)Get(only,"Checked"),"Last selected tool cannot be disabled");
        await Tool("Rectangle"); await StartDesktop();
        await CycleButton(1,"Rectangle"); await CycleButton(2,"Rectangle"); await CycleButton(0,"Rectangle");
        await StopDesktop();
        await Section(2); await UiClick(Button("Inclure tous les outils")); CheckCycle(AllTools,"Restore-all button includes the complete list");
        await ChooseCycle("Stylo","Rectangle","Forme libre");
        await UiClick(Button("Configurer les boutons souris"));
        Check((int)Get(Studio,"page")==4,"Tool selection links back to mouse mappings");
        await UiClick(Button("X2 outil suivant · appui long : précédent"));
        Check((string)Get(Config,"ButtonX2")=="Outil de dessin suivant" && (string)Get(Config,"ButtonX2Long")=="Outil de dessin précédent","Native preset configures short-next and long-previous");
        await Navigate(5); await SmoothMode("Désactivé",0); await Tool("Stylo"); await StartDesktop();
        await CycleButton(2,"Rectangle",45);
        await CycleButton(2,"Stylo",650);
        await CycleButton(2,"Forme libre",1100);
        Check((string)Get(Config,"DrawSmoothMode")=="Formes assistées","Cycling into Auto-formes enables assisted shapes");
        await Task.Delay(500); Check((string)Get(Config,"AnnotationTool")=="Forme libre","Long hold triggers only once and no short action on release");
        await CycleButton(2,"Stylo",45);
        await CycleButton(1,"Rectangle"); await CycleButton(1,"Forme libre"); await CycleButton(1,"Stylo");
        await StopDesktop();
        await Section(2);
        // Exercise keyboard access on an included switch, then restore it.
        Control rectangle=CycleSwitch("Rectangle");
        await UiClick(rectangle); CheckCycle(new[]{"Stylo","Forme libre"},"Native click removes a selected tool");
        rectangle.Focus(); await UiKey(0x20);
        CheckCycle(new[]{"Stylo","Rectangle","Forme libre"},"Space toggles a focused tool back on");
        CaptureFull("cycle-three-tools.png"); DumpUi("cycle-three");
        await Task.Delay(800);
        object saved=Static("ConfigStore","Load");
        Check(SameTools(Cycle(saved),Cycle(Config)),"Deferred autosave persists the selected tools without explicit Save");
        string export=Path.Combine(folder,"cycle-profile-export.json");
        Static("ConfigStore","Export",Config,export);
        object imported=Static("ConfigStore","Import",export);
        Check(SameTools(Cycle(imported),Cycle(Config)),"Profile export/import retains the selected cycle");
        Control close=FindControl(delegate(Control c) { return c.AccessibleName=="Fermer (Haloryva reste actif près de l'horloge)"; });
        await UiClick(close); Call(controller,"ShowSettings");
        await WaitFor(delegate { return Studio!=null && Studio.Visible; },"Studio reopens with the selected cycle");
        VerifyPlacement("reopened"); await Navigate(5); await Section(2);
        CheckCycle(new[]{"Stylo","Rectangle","Forme libre"},"Closing and reopening Studio preserves tool selection");
        Write("CASES cycle nativeSwitches=true defaultAll=true twoTools=true oneTool=true threeTools=true wrapBothDirections=true directOutside=true middleX1X2=true shortLong=true toast=true heldStroke=true autosave=true exportImport=true keyboard=true reopen=true");
    }
    static async Task ReloadCycle()
    {
        CheckCycle(new[]{"Stylo","Rectangle","Forme libre"},"New process loads the persisted tool selection");
        Check((string)Get(Config,"ButtonX2")=="Outil de dessin suivant" && (string)Get(Config,"ButtonX2Long")=="Outil de dessin précédent","New process loads short and long button assignments");
        await Navigate(5); await Section(2);
        foreach(string tool in AllTools)
            Check((bool)Get(CycleSwitch(tool),"Checked")==(Array.IndexOf(Cycle(Config),tool)>=0),"Reloaded UI reflects persisted selection: "+tool);
        CaptureFull("cycle-after-restart.png");
        await Tool("Stylo"); await StartDesktop();
        await CycleButton(2,"Rectangle",45); await CycleButton(2,"Stylo",650);
        await StopDesktop();
        Write("CASES freshProcessReload=true persistedNativeShortLong=true");
    }

    static void VerifyPlacement(string label)
    {
        Rectangle work=Screen.FromControl(Studio).WorkingArea,bounds=Studio.Bounds;
        Write("PLACEMENT "+label+" bounds="+bounds+" work="+work+" driverMovedWindow=false");
        Check(work.Contains(bounds),"Studio stays within working area: "+label);
        Check(Math.Abs((bounds.Left*2+bounds.Width)-(work.Left*2+work.Width))<=1 &&
            Math.Abs((bounds.Top*2+bounds.Height)-(work.Top*2+work.Height))<=1,"Studio starts centered: "+label);
    }
    static async Task StudioCommands()
    {
        Rectangle initial=Studio.Bounds;
        Control maximize=FindControl(delegate(Control c) { return c.AccessibleName=="Agrandir"; });
        await UiClick(maximize);
        Check(Studio.WindowState==FormWindowState.Maximized && Screen.FromControl(Studio).WorkingArea.Contains(Studio.Bounds),"Studio maximize stays inside the working area");
        Rectangle maximized=Studio.Bounds;
        Call(controller,"ShowSettings"); await Task.Delay(150);
        Check(Studio.WindowState==FormWindowState.Maximized && Studio.Bounds==maximized,"Showing existing Studio preserves maximized state");
        await UiClick(maximize);
        Check(Studio.WindowState==FormWindowState.Normal && Studio.Bounds==initial,"Restore window size retains initial centered bounds");
        for(int n=0;n<8;n++) await Navigate(n);
        await Navigate(5); CaptureFull("studio-drawing.png"); DumpUi("drawing");
        Control grid=Grid("Stylo");
        string[] tools=(string[])Get(grid,"values");
        foreach(string name in tools) await Tool(name);
        await Tool("Forme libre");
        Check((string)Get(Config,"DrawSmoothMode")=="Formes assistées","Auto-formes tool enables assisted mode");
        await SmoothMode("Désactivé",0);
        Check((string)Get(Config,"AnnotationTool")=="Stylo","Choosing raw mode returns Auto-formes to pen");
        await SmoothMode("Formes assistées",100); await SmoothMode("Lissage léger",50);
        await Section(0);
        await TypeEditor(Editor("Épaisseur"),"0"); Check((int)Get(Config,"AnnotationThickness")==1,"Thickness UI clamps below minimum");
        await TypeEditor(Editor("Épaisseur"),"999"); Check((int)Get(Config,"AnnotationThickness")==24,"Thickness UI clamps above maximum");
        await TypeEditor(Editor("Épaisseur"),"7"); Check((int)Get(Config,"AnnotationThickness")==7,"Thickness editor accepts 7");
        await ColorSetting("#12A4DE");
        await TypeEditor(Editor("Couleur du trait (hexadécimal)"),"#GGGGGG");
        Check((string)Get(Config,"AnnotationColor")=="#12A4DE" && Editor("Couleur du trait (hexadécimal)").Text=="#12A4DE","Invalid color is rejected without changing the selected color");
        await Tool("Stylo");
        grid=Grid("Stylo"); await UiKey(0x27);
        Check((string)Get(Config,"AnnotationTool")=="Surligneur","Tool grid supports Right arrow");
        await UiKey(0x25); Check((string)Get(Config,"AnnotationTool")=="Stylo","Tool grid supports Left arrow");
        Control status=(Control)Get(Studio,"statusPill");
        await UiClick(status); Check(!(bool)Get(Config,"Enabled"),"Studio status button pauses effects");
        await UiClick(status); Check((bool)Get(Config,"Enabled"),"Studio status button resumes effects");
        await StartDesktop(true);
        object stroke=await Gesture("studio-pen",new[]{new Point(300,370),new Point(335,395),new Point(370,380),new Point(410,400),new Point(450,370)});
        Check(StrokeStyle(stroke,"Stylo",ColorTranslator.FromHtml("#12A4DE"),7),"Drawn stroke uses the controls selected in Studio");
        Point[] original=CopyPoints(stroke,"OriginalPoints");
        await StopDesktop(true);
        Check(Button("Annuler").Enabled && !Button("Rétablir").Enabled,"Studio history buttons reflect available actions");
        await UiClick(Button("Annuler")); Check(Strokes.Count==0 && Redo.Count==1,"Studio Annuler removes the stroke");
        Check(!Button("Annuler").Enabled && Button("Rétablir").Enabled,"History enabled states update after undo");
        await UiClick(Button("Rétablir")); Check(Strokes.Count==1 && Redo.Count==0 && Object.ReferenceEquals(Strokes[0],stroke),"Studio Retablir restores the same stroke");
        await UiClick(Button("Restaurer le trait original"));
        Check(SamePoints(CopyPoints(stroke),original) && !(bool)Get(stroke,"Smoothed") && CopyPoints(stroke,"OriginalPoints").Length==0,"Studio original-stroke command restores the recorded gesture");
        string png=await Export("studio-button-export.png",false,false);
        using(var image=new Bitmap(png)) {
            Check(image.Size==SystemInformation.VirtualScreen.Size && image.GetPixel(0,0).A==0,"Studio Exporter PNG creates a transparent desktop-sized image");
            Check(AlphaPixels(image,new Rectangle(280,350,200,80))>100,"Studio-exported PNG contains the drawn stroke");
        }
        await UiClick(Button("Tout effacer"));
        Check(Strokes.Count==0 && Redo.Count==0 && !Button("Annuler").Enabled && !Button("Rétablir").Enabled && !Button("Tout effacer").Enabled,"Studio clear button empties both history stacks and disables unavailable actions");
        await StartDesktop(); await StopDesktop();
        CaptureFull("studio-final.png");
        Control close=FindControl(delegate(Control c) { return c.AccessibleName=="Fermer (Haloryva reste actif près de l'horloge)"; });
        await UiClick(close);
        Check(Studio==null && (bool)Get(Get(controller,"hooks"),"Installed"),"Closing Studio keeps application input workers active");
        Call(controller,"ShowSettings"); await WaitFor(delegate { return Studio!=null && Studio.Visible; },"Studio reopens");
        VerifyPlacement("reopened");
        Check((string)Get(Config,"AnnotationColor")=="#12A4DE" && (int)Get(Config,"AnnotationThickness")==7,"Reopened Studio retains edited settings");
        Write("CASES studio navigationPages=8 toolSelectors=10 tabs=2 thicknessBounds=2 colorValidInvalid=2 gridKeyboard=true pauseResume=true drawStartPaths=2 rightClickEscapeReturn=true historyButtons=true originalRestore=true exportButton=true closeReopen=true");
    }
    static Point[] Wavy(int baseline)
    {
        var points=new Point[46];
        for(int n=0;n<points.Length;n++) points[n]=new Point(220+n*12,baseline+(int)Math.Round(14*Math.Sin(n*Math.PI/10))+(n%2==0?7:-7));
        return points;
    }
    static async Task Smoothing()
    {
        await Navigate(5); await Tool("Stylo");
        double light=0,strong=0; object last=null; Point[] lastRaw=null;
        string[] modes={"Désactivé","Lissage léger","Lissage léger","Lissage léger"};
        int[] amounts={50,0,50,100},rows={330,400,470,540};
        string[] colors={"#FF0000","#CC6600","#0000FF","#008000"};
        for(int i=0;i<4;i++) {
            await ColorSetting(colors[i]); await SmoothMode(modes[i],amounts[i]);
            await StartDesktop();
            Point[] requested=Wavy(rows[i]);
            object stroke=await Gesture("smooth-"+i,requested);
            Point[] output=CopyPoints(stroke),raw=CopyPoints(stroke,"OriginalPoints");
            Check(output[0]==requested[0] && output[output.Length-1]==requested[requested.Length-1],"Smoothing preserves exact endpoints case "+i);
            if(i<2) Check(SamePoints(output,requested) && !(bool)Get(stroke,"Smoothed"),i==0?"Disabled smoothing leaves native points unchanged":"Zero intensity leaves native points unchanged");
            else {
                Check(raw.Length==requested.Length && SamePoints(raw,requested),"Smoothing retains complete original points case "+i);
                Check((bool)Get(stroke,"Smoothed") && !SamePoints(output,raw),"Smoothing changes the completed stroke case "+i);
                double before=PathLength(raw),after=PathLength(output);
                Write("METRIC smoothing intensity="+amounts[i]+" beforeLength="+before.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" afterLength="+after.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" points="+output.Length);
                Check(after<before*.85,"Smoothing reduces the jagged path length by at least 15 percent case "+i);
                if(i==2) light=after; else { strong=after; last=stroke; lastRaw=raw; }
            }
            if(i==3) { Move(800,580); await Task.Delay(300); Capture("smoothing-comparison.png"); }
            await StopDesktop();
        }
        Check(strong<=light+1,"100 percent smoothing is at least as smooth as 50 percent on the same translated gesture");
        await Section(0); await UiClick(Button("Restaurer le trait original"));
        Check(SamePoints(CopyPoints(last),lastRaw) && !(bool)Get(last,"Smoothed") && CopyPoints(last,"OriginalPoints").Length==0,"Restore original exactly reverses strong smoothing");
        await StartDesktop(); Move(800,580); await Task.Delay(300); Capture("smoothing-restored.png"); await StopDesktop();
        Write("CASES smoothing disabled=true zero=true fifty=true hundred=true endpoints=true originalRetained=true nativeRestore=true");
    }
    static Point[] Oval(int cx,int cy,int rx,int ry,double angle)
    {
        var points=new Point[49]; double cos=Math.Cos(angle),sin=Math.Sin(angle);
        for(int n=0;n<48;n++) { double t=n*Math.PI*2/48,x=rx*Math.Cos(t),y=ry*Math.Sin(t); points[n]=new Point(cx+(int)Math.Round(x*cos-y*sin),cy+(int)Math.Round(x*sin+y*cos)); }
        points[48]=points[0]; return points;
    }
    static Point[] Box(int cx,int cy,int width,int height,double angle)
    {
        Point[] corners={new Point(-width/2,-height/2),new Point(width/2,-height/2),new Point(width/2,height/2),new Point(-width/2,height/2)};
        var points=new System.Collections.Generic.List<Point>(); double cos=Math.Cos(angle),sin=Math.Sin(angle);
        for(int side=0;side<4;side++) for(int n=0;n<10;n++) {
            Point a=corners[side],b=corners[(side+1)%4]; double x=a.X+(b.X-a.X)*n/10.0,y=a.Y+(b.Y-a.Y)*n/10.0;
            points.Add(new Point(cx+(int)Math.Round(x*cos-y*sin),cy+(int)Math.Round(x*sin+y*cos)));
        }
        points.Add(points[0]); return points.ToArray();
    }
    static async Task Assisted()
    {
        await Navigate(5); await Tool("Forme libre"); await ColorSetting("#008000");
        await SmoothMode("Formes assistées",50);
        Point[] line=new Point[16]; for(int n=0;n<line.Length;n++) line[n]=new Point(220+n*10,335+(n%3-1)*2);
        Point[][] gestures={line,Box(470,360,110,70,0),Oval(650,360,40,40,0),Oval(290,470,70,35,0),Box(470,480,110,60,Math.PI/7),Oval(665,480,65,30,Math.PI/6)};
        string[] names={"assisted-line","assisted-rectangle","assisted-circle","assisted-ellipse","assisted-rotated-rectangle","assisted-rotated-ellipse"};
        string[] expected={"Ligne droite","Rectangle","Ellipse","Ellipse","Polygone","Polygone"};
        object last=null; Point[] original=null;
        await StartDesktop();
        for(int n=0;n<gestures.Length;n++) {
            object stroke=await Gesture(names[n],gestures[n]); Point[] p=CopyPoints(stroke);
            Check((string)Get(stroke,"Tool")==expected[n],names[n]+" recognized as "+expected[n]);
            Check(!(bool)Get(stroke,"Smoothed") && CopyPoints(stroke,"OriginalPoints").Length>=10 && (string)Get(stroke,"OriginalTool")=="Forme libre",names[n]+" retains source without rounding recognized geometry");
            if(n<4) Check(p.Length==2,names[n]+" has two defining endpoints");
            if(n==0) Check(p[0]==line[0] && p[1]==line[line.Length-1],"Assisted line preserves endpoints");
            if(n==1) Check(Math.Abs(Math.Abs(p[1].X-p[0].X)-110)<=8 && Math.Abs(Math.Abs(p[1].Y-p[0].Y)-70)<=8,"Assisted rectangle preserves intended dimensions");
            if(n==2) Check(Math.Abs(Math.Abs(p[1].X-p[0].X)-Math.Abs(p[1].Y-p[0].Y))<=2,"Assisted circle is round");
            if(n==3) Check(Math.Abs(p[1].X-p[0].X)>Math.Abs(p[1].Y-p[0].Y)*1.5,"Assisted ellipse preserves its elongated shape");
            if(n==4) Check(p.Length==4 && Math.Abs(p[1].X-p[0].X)>8 && Math.Abs(p[1].Y-p[0].Y)>8,"Rotated rectangle retains four sloping corners");
            if(n==5) { Check(p.Length>20,"Rotated ellipse keeps a closed polygonal contour"); last=stroke; original=CopyPoints(stroke,"OriginalPoints"); }
        }
        Move(800,580); await Task.Delay(350); Capture("assisted-gallery.png");
        await StopDesktop(); await Section(0); await UiClick(Button("Restaurer le trait original"));
        Check((string)Get(last,"Tool")=="Forme libre" && SamePoints(CopyPoints(last),original) && CopyPoints(last,"OriginalPoints").Length==0 && !(bool)Get(last,"Smoothed"),"Restore original reverses assisted rotated ellipse exactly");
        await StartDesktop(); Move(800,580); await Task.Delay(300); Capture("assisted-restored.png"); await StopDesktop();
        await UiClick(Button("Tout effacer")); Check(Strokes.Count==0,"Clear gallery before negative recognition case");
        await StartDesktop();
        var scribble=new System.Collections.Generic.List<Point>();
        for(int n=0;n<24;n++) scribble.Add(new Point(n%2==0?300:550,350+(n%4)*40)); scribble.Add(scribble[0]);
        object free=await Gesture("assisted-scribble",scribble.ToArray());
        Check((string)Get(free,"Tool")=="Forme libre","Repeated zigzag is not falsely recognized as a geometric shape");
        Move(800,580); await Task.Delay(300); Capture("assisted-scribble.png"); await StopDesktop();
        Write("CASES assisted line=true rectangle=true circle=true ellipse=true rotatedRectangle=true rotatedEllipse=true scribbleRejected=true originalRestore=true");
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
            Check(input.Text==value,"Unicode annotation content stays exactly as typed");
            Check(prompt.Text==T("Haloryva · Annotation texte"),"Text prompt title is localized");
            Check(input.AccessibleName==T("Texte de l'annotation"),"Text input accessible name is localized");
            bool cancelCaption=false;
            foreach(Control c in prompt.Controls) if(c.Text==(string)Static("L","Key","dialog.cancel")) cancelCaption=true;
            Check(cancelCaption,"Cancel uses its own translation, distinct from Undo");
            CaptureFull("annotation-dialog-"+startupLanguage+".png");
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
    static async Task<string> Export(string name,bool cancel,bool empty,bool probe=false)
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
                System.Windows.Automation.AutomationElement edit=null,save=null,close=null,ok=null;
                bool emptyMessage=false;
                foreach(System.Windows.Automation.AutomationElement node in nodes) {
                    var c=node.Current;
                    if(c.ControlType==System.Windows.Automation.ControlType.Edit || c.ControlType==System.Windows.Automation.ControlType.Button || c.ControlType==System.Windows.Automation.ControlType.Text)
                        Write("DIALOG type="+c.ControlType.ProgrammaticName+" id="+c.AutomationId+" name="+c.Name);
                    if(c.ControlType==System.Windows.Automation.ControlType.Edit && c.AutomationId=="1001") edit=node;
                    if(c.ControlType==System.Windows.Automation.ControlType.Button && c.AutomationId=="1") save=node;
                    if(c.ControlType==System.Windows.Automation.ControlType.Button && c.AutomationId=="2") close=node;
                    if(c.ControlType==System.Windows.Automation.ControlType.Button && c.Name=="OK" && (c.AutomationId=="1" || c.AutomationId=="2")) ok=node;
                    if(c.Name.Contains(T("Aucun dessin à exporter."))) emptyMessage=true;
                }
                if(empty) { Check(emptyMessage && ok!=null,"Empty scene displays the expected information message"); save=ok; }
                else {
                    Check(edit!=null && save!=null && close!=null,"Native Save dialog exposes filename, Save and Cancel");
                    var value=(System.Windows.Automation.ValuePattern)edit.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern);
                    edit.SetFocus(); Thread.Sleep(100);
                    // ValuePattern.SetValue changes visible text on this native dialog without
                    // updating IFileDialog's accepted filename. Type normally so EN_CHANGE is raised.
                    DialogInputs(dialog,new[]{K(0xa2,false,false),K(0x41,false,false),K(0x41,true,false),K(0xa2,true,false)});
                    foreach(char ch in path) { DialogInputs(dialog,new[]{K(ch,false,true),K(ch,true,true)}); Thread.Sleep(5); }
                    Thread.Sleep(100);
                    Check(value.Current.Value==path,"Save filename matches the isolated Unicode path");
                }
                uint owner; GetWindowThreadProcessId(dialog,out owner);
                Check(owner==(uint)ownPid && GetForegroundWindow()==dialog,"Export action remains within the owned dialog");
                using(var shot=new Bitmap(SystemInformation.VirtualScreen.Width,SystemInformation.VirtualScreen.Height)) {
                    using(var g=Graphics.FromImage(shot)) g.CopyFromScreen(SystemInformation.VirtualScreen.Location,Point.Empty,shot.Size);
                    string diagnostics=Path.Combine(folder,"dialogs"); Directory.CreateDirectory(diagnostics);
                    shot.Save(Path.Combine(diagnostics,name+".png"),System.Drawing.Imaging.ImageFormat.Png);
                }
                Write("EXPORT dialog ready cancel="+cancel+" empty="+empty+" thread="+Thread.CurrentThread.ManagedThreadId);
                if(!cancel && !empty) {
                    // Commit the edited filename via the actual keyboard, after focusing the filename field.
                    edit.SetFocus(); Thread.Sleep(100);
                    DialogInputs(dialog,new[]{K(13,false,false),K(13,true,false)});
                    Write("EXPORT native Enter sent to filename field");
                } else {
                    var button=cancel?close:save;
                    ((System.Windows.Automation.InvokePattern)button.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
                }
            } catch {
                if(dialog!=IntPtr.Zero) PostMessage(dialog,0x0010,IntPtr.Zero,IntPtr.Zero);
                throw;
            }
        });
        Write("EXPORT controller action entered");
        if(probe) {
            using(var testDialog=new SaveFileDialog()) {
                testDialog.Filter="Image PNG transparente|*.png"; testDialog.FileName="Haloryva_annotations.png";
                DialogResult result=testDialog.ShowDialog();
                Write("EXPORT PROBE result="+result+" filename="+testDialog.FileName+" expected="+path);
                Check(result==DialogResult.OK && testDialog.FileName==path,"Standalone dialog accepts the actual intended filename before application export");
            }
        } else { await UiClick(Button("Exporter PNG")); await WaitFor(delegate { return File.Exists(path); },"Studio export button finishes writing the PNG"); }
        Write("EXPORT controller action returned fileExists="+File.Exists(path));
        await worker;
        await FocusStudio();
        if(!cancel && !empty && !probe) {
            foreach(string file in Directory.GetFiles(folder)) Write("EXPORT observedFile="+Path.GetFileName(file)+" bytes="+new FileInfo(file).Length);
            Check(File.Exists(path) && new FileInfo(path).Length>100,"Actual PNG export created: "+name);
        }
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
