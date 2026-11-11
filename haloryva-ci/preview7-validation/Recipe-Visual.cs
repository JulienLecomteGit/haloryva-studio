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

// Preview.7 only: tool title/cycle, plurals and missing scroll coverage. No historical suite.
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
            if(mode!="features" && mode!="reload") throw new Exception("Unknown mode");
            observe=args.Length>2 && args[2]=="on";
            requestedLanguage=Environment.GetEnvironmentVariable("HALORYVA_LANGUAGE")??"fr";
            Check(app.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion=="2.7.1-preview.7","Expected preview.7 assembly identity");
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
                Set(config,"DrawingToolCycle",new[]{"Stylo","Rectangle"});
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
        await WaitFor(delegate { return Studio!=null && Studio.Visible; },"Studio window opened from preview.7 binary");
        VerifyPlacement("initial");
        await FocusStudio();
        CaptureFull("studio-initial.png"); DumpUi("initial");
        if(suite!="visual7") throw new Exception("Only the preview.7 targeted suite is authorized.");
        if(mode=="reload") await VisualReload(); else await VisualRecipe();
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

    static void VerifyPlacement(string label)
    {
        Rectangle work=Screen.FromControl(Studio).WorkingArea,bounds=Studio.Bounds;
        Write("PLACEMENT "+label+" bounds="+bounds+" work="+work+" driverMovedWindow=false");
        Check(work.Contains(bounds),"Studio stays within working area: "+label);
        Check(Math.Abs((bounds.Left*2+bounds.Width)-(work.Left*2+work.Width))<=1 &&
            Math.Abs((bounds.Top*2+bounds.Height)-(work.Top*2+work.Height))<=1,"Studio starts centered: "+label);
    }
    static async Task RefocusTarget()
    {
        for(int n=0;n<30 && GetForegroundWindow()!=targetWindow;n++) {
            SetForegroundWindow(targetWindow); PostMessage(targetWindow,0x8002,IntPtr.Zero,IntPtr.Zero); await Task.Delay(100);
        }
        FocusGuard();
    }
    static void DialogInputs(IntPtr hwnd,INPUT[] inputs)
    {
        uint pid; GetWindowThreadProcessId(hwnd,out pid);
        if(GetForegroundWindow()!=hwnd || pid!=(uint)Process.GetCurrentProcess().Id) throw new Exception("Refused input outside owned dialog.");
        if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(INPUT)))!=inputs.Length) throw new Exception("Dialog SendInput failed");
    }
    static INPUT K(ushort key,bool up,bool unicode) { return new INPUT {type=1,data=new UNION {key=new KEY {vk=unicode?(ushort)0:key,scan=unicode?key:(ushort)0,flags=(up?2u:0u)|(unicode?4u:0u)}}}; }
    static async Task WaitFor(Func<bool> predicate,string label)
    {
        var timer=Stopwatch.StartNew();
        while(!predicate() && timer.ElapsedMilliseconds<4000) await Task.Delay(20);
        Check(predicate(),label);
    }

    static void CheckToast(int expectedIndex,int expectedPosition,int expectedCount,string label)
    {
        object toast=Get(controller,"toast");
        Check((int)Get(toast,"index")==expectedIndex &&
            (int)Get(toast,"cyclePosition")==expectedPosition &&
            (int)Get(toast,"cycleCount")==expectedCount,label+" actual="+Get(toast,"cyclePosition")+"/"+Get(toast,"cycleCount"));
        Check((bool)Get(toast,"IsLive"),"Tool toast is visible: "+label);
    }
    static async Task ToastShot(string name,int index,int position,int count)
    {
        await Task.Delay(520);
        CheckToast(index,position,count,name);
        Check(GetForegroundWindow()==targetWindow,"Toast does not steal foreground: "+name);
        CaptureFull("toast-"+name+"-"+startupLanguage+".png");
    }
    static void RenderCases()
    {
        string[] captions=(string[])app.GetType("CursorFlow.DrawingTools").GetField("Captions",Flags).GetValue(null);
        foreach(float scale in new[]{1f,1.5f,2f})
        {
            Size cardSize=(Size)Static("ToolToast","Measure",scale);
            using(var atlas=new Bitmap(cardSize.Width*2,cardSize.Height*5))
            using(var atlasG=Graphics.FromImage(atlas))
            {
                atlasG.Clear(Color.FromArgb(25,28,38));
                for(int tool=0;tool<10;tool++)
                {
                    Rectangle titleArea=new Rectangle(10,10,(int)Math.Round(218*scale),(int)Math.Round(34*scale));
                    Font font=(Font)app.GetType("CursorFlow.PremiumPalette").GetMethod("Font",Flags,null,new[]{typeof(float),typeof(int)},null).Invoke(null,new object[]{19.5f*scale,2});
                    using(var titleImage=new Bitmap(titleArea.Right+10,titleArea.Bottom+10))
                    using(var g=Graphics.FromImage(titleImage))
                    {
                        g.Clear(Color.Transparent); g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        Static("ToolToast","PaintTitle",g,T(captions[tool]),font,titleArea,Color.White,Color.White);
                        int ink=0,outside=0;
                        for(int y=0;y<titleImage.Height;y++) for(int x=0;x<titleImage.Width;x++)
                            if(titleImage.GetPixel(x,y).A>20) { ink++; if(!titleArea.Contains(x,y)) outside++; }
                        Check(ink>35*scale*scale && outside==0,"Title visible and contained: "+captions[tool]+" scale="+scale+" ink="+ink+" outside="+outside);
                        if(tool==0 && scale==1f) {
                            g.Clear(Color.Transparent);
                            Static("PremiumPalette","GradientText",g,captions[tool],font,titleArea,Color.White,Color.White);
                            int oldInk=0;
                            for(int y=0;y<titleImage.Height;y++) for(int x=0;x<titleImage.Width;x++)
                                if(titleImage.GetPixel(x,y).A>20) oldInk++;
                            Write("DIAGNOSIS unchanged preview.6 GradientText title-area ink="+oldInk+" corrected="+ink);
                        }
                    }
                    using(var card=new Bitmap(cardSize.Width,cardSize.Height))
                    using(var g=Graphics.FromImage(card))
                    {
                        Static("ToolToast","PaintCard",g,cardSize,scale,tool,tool,10,(float)tool,1f,1f,1f,Color.FromArgb(34,211,238),Color.FromArgb(150,108,255));
                        atlasG.DrawImageUnscaled(card,(tool%2)*cardSize.Width,(tool/2)*cardSize.Height);
                    }
                }
                atlas.Save(Path.Combine(folder,"render-all-tools-"+startupLanguage+"-"+(int)(scale*100)+".png"),System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
    static string ExpectedCount(int count,string group)
    {
        bool one=startupLanguage=="fr"?count<=1:count==1;
        string suffix;
        if(group=="module") suffix=startupLanguage=="fr"?(one?" module actif":" modules actifs"):startupLanguage=="en"?(one?" active module":" active modules"):(one?" módulo activo":" módulos activos");
        else if(group=="annotation") suffix=startupLanguage=="fr"?(one?" annotation":" annotations"):startupLanguage=="en"?(one?" annotation":" annotations"):(one?" anotación":" anotaciones");
        else suffix=startupLanguage=="fr"?(one?" trait":" traits"):startupLanguage=="en"?(one?" stroke":" strokes"):(one?" trazo":" trazos");
        return count+suffix;
    }
    static async Task PluralCases()
    {
        string screen=startupLanguage=="fr"?" à l'écran":startupLanguage=="en"?" on screen":" en pantalla";
        foreach(int count in new[]{0,1,2})
        {
            // Known display fixtures; no drawing geometry or already acquired input suite is replayed.
            Strokes.Clear();
            for(int i=0;i<count;i++) Strokes.Add(Activator.CreateInstance(app.GetType("CursorFlow.AnnotationStroke"),true));
            foreach(string field in new[]{"Ring","Trail","Clicks","KeyDisplay","Loupe","Focus"}) Set(Config,field,false);
            Set(Config,"Ring",count>=1); Set(Config,"Trail",count>=2); Set(Config,"Enabled",true);
            Call(controller,"Changed");
            await Navigate(0); Call(Studio,"RefreshStates"); await Task.Delay(150);
            object hero=Get(Studio,"hero");
            string expected=ExpectedCount(count,"module")+"  ·  "+ExpectedCount(count,"annotation")+screen;
            Check((string)Get(hero,"Status")==expected,"Home plural "+count+": "+Get(hero,"Status"));
            CaptureFull("plural-home-"+count+"-"+startupLanguage+".png");
            await Navigate(5); await Section(0); Call(Studio,"RefreshStates"); await Task.Delay(100);
            string strokes=ExpectedCount(count,"stroke")+screen;
            Control label=FindControl(delegate(Control c) { return c is Label && c.Text==strokes; });
            Check(label.Text==strokes,"Drawing-history plural "+count+": "+label.Text);
            ((ScrollableControl)Get(Studio,"workspace")).ScrollControlIntoView(label); await Task.Delay(150);
            CaptureFull("plural-history-"+count+"-"+startupLanguage+".png");
        }
        Strokes.Clear();
        Set(Config,"Ring",false); Set(Config,"Trail",false); Call(controller,"Changed");
    }
    static async Task ScrollCoverage(int page,int part)
    {
        var workspace=(ScrollableControl)Get(Studio,"workspace");
        workspace.AutoScrollPosition=Point.Empty; await Task.Delay(220);
        int viewport=workspace.ClientSize.Height;
        int maximum=Math.Max(0,workspace.AutoScrollMinSize.Height-viewport);
        int step=Math.Max(1,viewport-90),previous=-1,shots=0;
        var covered=new System.Collections.Generic.HashSet<Control>();
        for(int requested=0;;requested=Math.Min(maximum,requested+step))
        {
            workspace.AutoScrollPosition=new Point(0,requested); await Task.Delay(220);
            int actual=-workspace.AutoScrollPosition.Y;
            if(actual<=previous && previous>=0 && requested!=0) throw new Exception("Scroll did not progress: "+page+"/"+part);
            previous=actual;
            Rectangle viewportScreen=workspace.RectangleToScreen(workspace.ClientRectangle);
            foreach(Control control in Descendants(workspace)) {
                if(!control.Visible || control.Height==0 || control.Width==0) continue;
                Rectangle bounds=control.RectangleToScreen(control.ClientRectangle);
                if(bounds.IntersectsWith(viewportScreen)) covered.Add(control);
            }
            string name="scroll-page-"+page+"-section-"+part+"-y"+actual+"-"+startupLanguage;
            CaptureFull(name+".png");
            Write("SCROLL_CAPTURE page="+page+" section="+part+" requested="+requested+" actual="+actual+" viewport="+viewport+" extent="+workspace.AutoScrollMinSize.Height);
            shots++;
            if(requested>=maximum) break;
            if(shots>20) throw new Exception("Unexpected scroll extent");
        }
        var missing=new System.Collections.Generic.List<string>();
        foreach(Control control in Descendants(workspace)) {
            if(control.Visible && control.Width>0 && control.Height>0 && !covered.Contains(control))
                missing.Add(control.GetType().Name+":"+control.Text);
        }
        Check(missing.Count==0 && previous>=maximum-2,"Scroll coverage reaches bottom with no omitted visible controls: "+page+"/"+part+" missing="+String.Join("|",missing));
        DumpUi("page-"+page+"-section-"+part+"-complete");
        workspace.AutoScrollPosition=Point.Empty; await Task.Delay(100);
    }
    static async Task VisualRecipe()
    {
        Check(startupLanguage==requestedLanguage,"Requested saved language active");
        CheckCycle(new[]{"Stylo","Rectangle"},"Saved two-tool choice reaches preview.7");
        if(!observe) { RenderCases(); await PluralCases(); }
        await Navigate(4); await UiClick(Button("X2 outil suivant · appui long : précédent"));
        await Navigate(5); await ChooseCycle("Stylo","Rectangle"); await Tool("Stylo"); await StartDesktop();
        await CycleButton(2,"Rectangle",45); await ToastShot("two-second",3,1,2);
        await CycleButton(2,"Stylo",45); await ToastShot("two-wrap",0,0,2);
        await CycleButton(2,"Rectangle",650); await ToastShot("two-long",3,1,2);
        await StopDesktop();
        await ChooseCycle("Texte"); await Tool("Texte"); await StartDesktop();
        await CycleButton(2,"Texte",45); await ToastShot("single",9,0,1);
        await CycleButton(2,"Texte",650); CheckToast(9,0,1,"single-long");
        await StopDesktop();
        await ChooseCycle(AllTools); await Tool("Stylo"); await StartDesktop();
        await CycleButton(2,"Surligneur",45); await ToastShot("all-second",1,1,10);
        await CycleButton(2,"Stylo",650); await CycleButton(2,"Texte",650);
        await ToastShot("all-wrap",9,9,10);
        await StopDesktop();
        await ChooseCycle("Stylo","Rectangle"); await Tool("Ellipse"); await StartDesktop();
        await CycleButton(2,"Stylo",45); await ToastShot("outside-enters-selection",0,0,2);
        // Keep the toast alive and verify a changed cycle cannot retain the old dot index.
        Set(Config,"DrawingToolCycle",new[]{"Texte"}); Call(controller,"Changed");
        await CycleButton(2,"Texte",45); await ToastShot("live-cycle-change",9,0,1);
        await StopDesktop();
        await ChooseCycle("Stylo","Rectangle"); await Tool("Stylo");
        if(!observe) {
            for(int page=0;page<8;page++) {
                await Navigate(page);
                int sections=page==5?3:(page==1||page==2?2:1);
                for(int part=0;part<sections;part++) { await Section(part); await ScrollCoverage(page,part); }
            }
            await Navigate(4);
            Control select=FindControl(delegate(Control c) { return c.GetType().Name=="PremiumSelect"; });
            await UiClick(select); await Task.Delay(250);
            CaptureFull("dropdown-mouse-actions-"+startupLanguage+".png"); await UiKey(27);
        }
        Call(controller,"Changed");
        CheckCycle(new[]{"Stylo","Rectangle"},"Final chosen tools remain ready for restart");
        Write("TARGETED_SCOPE title-rendering="+(!observe)+" plural-0-1-2="+(!observe)+" scroll-coverage="+(!observe)+" native-toast=true");
    }
    static async Task VisualReload()
    {
        Check(startupLanguage==requestedLanguage && (string)Get(Config,"UiLanguage")==requestedLanguage,"Saved language preserved on restart");
        CheckCycle(new[]{"Stylo","Rectangle"},"Saved chosen tools preserved on restart");
        await Navigate(5); await Tool("Stylo"); await StartDesktop();
        await CycleButton(2,"Rectangle",45); await ToastShot("restart-two-second",3,1,2);
        await StopDesktop();
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

