using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections.Generic;

// Only About/history height captures of candidate file version 2.7.1.11. No Sonidos or historical suite.
internal static class CandidateCapture
{
    static string folder, language; static StreamWriter log; static Assembly app; static object controller;
    static int code, assertions; static Point original;
    static List<object> measurements = new List<object>();
    static List<string> captures = new List<string>();
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Form Studio { get { return (Form)Get(controller,"settings"); } }
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] values,int size);
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSE mouse; [FieldOffset(0)] public KEY key; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSE { public int x,y; public uint data,flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEY { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
    static void Write(string text) { log.WriteLine(DateTime.UtcNow.ToString("o")+" "+text);log.Flush(); }
    static void Check(bool ok,string label) { assertions++; Write((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label); }
    static string T(string s) { return (string)Static("L","T",s); }
    static int S(int n) { return (int)Static("PremiumPalette","S",n); }
    static Font Font(float size,int weight) { return (Font)app.GetType("CursorFlow.PremiumPalette").GetMethod("Font",Flags,null,new[]{typeof(float),typeof(int)},null).Invoke(null,new object[]{size,weight}); }
    static async Task Navigate(int page)
    {
        Control tab=FindControl(delegate(Control c){return c.GetType().Name=="PremiumNavButton" && (int)c.Tag==page;});
        await UiClick(tab,new Point(tab.Width/2,tab.Height/2));
        await WaitFor(delegate{return (int)Get(Studio,"page")==page;},"Requested page "+page+" displayed");
    }
    static Rectangle Visible(Control control)
    {
        Rectangle r=control.RectangleToScreen(control.ClientRectangle);
        for(Control p=control.Parent;p!=null;p=p.Parent)r=Rectangle.Intersect(r,p.RectangleToScreen(p.ClientRectangle));
        return Rectangle.Intersect(r,Screen.FromControl(Studio).WorkingArea);
    }
    static async Task RevealTop(Control c)
    {
        var workspace=(ScrollableControl)Get(Studio,"workspace");
        for(int n=0;n<4;n++) {
            Rectangle viewport=workspace.RectangleToScreen(workspace.ClientRectangle);
            int shift=c.PointToScreen(Point.Empty).Y-(viewport.Top+S(12));
            workspace.AutoScrollPosition=new Point(0,Math.Max(0,-workspace.AutoScrollPosition.Y+shift));
            await Task.Delay(150);
        }
        await FocusStudio(); await Task.Delay(300);
    }
    static void Measure(Control c,string label,string text,Rectangle rect,Font font)
    {
        Rectangle screen=c.RectangleToScreen(rect);
        Size measured=TextRenderer.MeasureText(text,font,new Size(Int32.MaxValue,Int32.MaxValue),
            TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
        bool visible=Visible(c).Contains(screen), fits=measured.Width<=rect.Width && measured.Height<=rect.Height;
        measurements.Add(new {Label=label,Text=text,TextWidth=measured.Width,TextHeight=measured.Height,
            AvailableWidth=rect.Width,AvailableHeight=rect.Height,Region=screen.ToString(),Visible=visible,Fits=fits});
        Write("MEASURE "+label+" text="+text+" width="+measured.Width+"/"+rect.Width+" height="+measured.Height+"/"+rect.Height+" visible="+visible+" fits="+fits);
        // Preserve the requested screenshot even if a fit issue is detected.
        if(!visible || !fits)code=1;
    }
    static void Capture(string name)
    {
        string file=language+"-"+name+".png";CaptureFull(file);captures.Add(file);
        Write("CAPTURE "+file+" screen="+SystemInformation.VirtualScreen+" studio="+Studio.Bounds);
    }
    static async Task Targeted()
    {
        Check(SystemInformation.VirtualScreen.Width==1024 && SystemInformation.VirtualScreen.Height==768,"Actual desktop 1024x768");
        Check((string)Static("L","get_Language")==language,"Requested startup language "+language);
        Check(FileVersionInfo.GetVersionInfo(Path.Combine(folder,"Haloryva.exe")).FileVersion=="2.7.1.11","Expected distinct candidate file version");
        Call(controller,"ShowSettings");
        await WaitFor(delegate{return Studio!=null && Studio.Visible;},"Studio visible");
        await FocusStudio();
        await Navigate(7);
        Control about=FindControl(delegate(Control c){return c.GetType().Name=="AboutBlock";});
        Control card=about.Parent;
        while(card!=null && card.GetType().Name!="PremiumCard")card=card.Parent;
        if(card==null)throw new Exception("About card not found");
        await RevealTop(card);
        var brand=app.GetType("CursorFlow.Brand");
        string version=(string)brand.GetField("Version",Flags).GetRawConstantValue();
        string date=(string)brand.GetField("BuildDate",Flags).GetRawConstantValue();
        string[][] history=(string[][])brand.GetField("History",Flags).GetValue(null);
        Check(version=="2.7.1" && history[0][0]==version && history[0][1]==date,"About version and date agree with compiled identity");
        Check(history.Length==13,"All 13 history rows retained");
        int rowY=S(128)+S(24),lastY=rowY+(history.Length-1)*S(24);
        int lastBottom=lastY+S(20);
        Write("LAYOUT historyRows="+history.Length+" actualHeight="+about.Height+" lastRowBottom="+lastBottom+
            " bottomMargin="+(about.Height-lastBottom)+" about="+about.Bounds+" viewport="+Visible(about));
        Check(lastBottom<=about.ClientSize.Height,"Last history row is fully inside AboutBlock");
        string expected=language=="fr"?"Langues, outils choisis et corrections de fiabilité":
            language=="en"?"Languages, selected tools and reliability fixes":"Idiomas, herramientas elegidas y mejoras de fiabilidad";
        Check(T(history[0][2])==expected,"Current history translation "+language);
        int left=S(84)+S(20);
        Measure(card,"About card heading",(string)Get(card,"heading"),new Rectangle(S(72),S(20),card.Width-S(92),S(24)),Font(11.5f,1));
        Measure(about,"Version",T("Version ")+version+T(" · build ")+date+" · Windows x64",
            new Rectangle(left,S(32),Math.Max(10,about.Width-left),S(20)),Font(9f,0));
        Measure(about,"History heading",T("HISTORIQUE DES VERSIONS"),new Rectangle(0,S(128),about.Width,S(18)),Font(8f,1));
        for(int i=0;i<history.Length;i++) {
            int y=rowY+i*S(24);
            Measure(about,"History "+i+" version",history[i][0],new Rectangle(0,y,S(50),S(20)),Font(8f,1));
            Measure(about,"History "+i+" text",T(history[i][2]),new Rectangle(S(62),y,Math.Max(10,about.Width-S(150)),S(20)),Font(9f,0));
            Measure(about,"History "+i+" date",history[i][1],new Rectangle(about.Width-S(84),y,S(84),S(20)),Font(8.25f,0));
        }
        Capture("a-propos-historique-complet-1024x768");
        var workspace=(ScrollableControl)Get(Studio,"workspace");
        workspace.AutoScrollPosition=new Point(0,workspace.VerticalScroll.Maximum);
        await Task.Delay(350);await FocusStudio();
        Control copy=FindControl(delegate(Control c){return c.GetType().Name=="PremiumButton" && c.Text==T("Copier les informations de version");});
        Rectangle lastRect=new Rectangle(0,lastY,about.Width,S(20));
        Check(Visible(about).Contains(about.RectangleToScreen(lastRect)),"Last history row remains fully visible at page bottom");
        Check(Visible(copy).Contains(copy.RectangleToScreen(copy.ClientRectangle)),"Copy-version button remains fully visible below the expanded history");
        Check(about.PointToScreen(new Point(0,lastBottom)).Y<=copy.PointToScreen(Point.Empty).Y,"Last row does not overlap copy-version button");
        Control note=FindControl(delegate(Control c){return c.GetType().Name=="InfoNote" && c.Parent==about.Parent;});
        Check(Visible(note).Contains(note.RectangleToScreen(note.ClientRectangle)),"About keyboard-shortcut note remains fully visible");
        Capture("a-propos-bas-1024x768");
        Write("NOT_RUN Sonidos, legacy195, selftests, prior recipes, MSIX, installer, login, language persistence");
    }
    [STAThread] static int Main(string[] args)
    {
        try { Guard(); } catch { return 90; }
        folder=AppDomain.CurrentDomain.BaseDirectory;
        language=args.Length==1?args[0]:"";
        if(language!="fr" && language!="en" && language!="es")return 91;
        log=new StreamWriter(Path.Combine(folder,"capture.log"),false,Encoding.UTF8);
        try {
            app=Assembly.LoadFrom(Path.Combine(folder,"Haloryva.exe"));
            Check(app.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion=="2.7.1","Candidate assembly version");
            SetProcessDPIAware();original=Cursor.Position;
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Static("TrailTrial","Initialize",(object)new[]{"--trail-session",Path.Combine(folder,"profile")});
            object config=Activator.CreateInstance(app.GetType("CursorFlow.Config"),true);
            Set(config,"UiLanguage",language);
            foreach(string key in new[]{"Ring","Trail","Clicks","KeyDisplay","ClickSounds","LaunchAtLogin"})Set(config,key,false);
            Static("ConfigStore","Save",config);
            controller=Activator.CreateInstance(app.GetType("CursorFlow.CursorController"),true);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            SynchronizationContext.Current.Post(async delegate {
                try { await Targeted(); }
                catch(Exception ex) { code=1;Write("ERROR "+ex);try{CaptureFull("failure.png");}catch{} }
                finally {
                    try { Cursor.Position=original;Call(controller,"Exit");Write("Controller exit returned"); }
                    catch(Exception ex) {code=1;Write("CLOSE ERROR "+ex);}
                    Application.ExitThread();
                }
            },null);
            Application.Run((ApplicationContext)controller);
            Write("WinForms message loop returned");
        } catch(Exception ex) {code=1;Write("ERROR "+ex);}
        finally {
            File.WriteAllText(Path.Combine(folder,"capture-result.json"),new JavaScriptSerializer().Serialize(new {
                Status=code==0?"passed":"failed",Language=language,Assertions=assertions,Measurements=measurements,Captures=captures,
                Execution="Actual compiled UI loaded by a capture driver; not Program.Main or an installed application",
                Historical195Executed=false,SelfTestsExecuted=false,Installed=false,Signed=false,Published=false}),Encoding.UTF8);
            log.Dispose();
        }
        return code;
    }
static void Guard()
    {
        string temp=Environment.GetEnvironmentVariable("RUNNER_TEMP");
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true" ||
           Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")!="github-hosted" ||
           String.IsNullOrEmpty(temp) || !Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).StartsWith(Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new Exception("REFUSED: isolated GitHub-hosted runner required.");
    }

static object Get(object obj,string name) { var t=obj.GetType(); var f=t.GetField(name,Flags); return f!=null?f.GetValue(obj):t.GetProperty(name,Flags).GetValue(obj,null); }

static void Set(object obj,string name,object value) { obj.GetType().GetField(name,Flags).SetValue(obj,value); }

static object Call(object obj,string name,params object[] args) { return obj.GetType().GetMethod(name,Flags).Invoke(obj,args); }

static object Static(string type,string name,params object[] args) { return app.GetType("CursorFlow."+type).GetMethod(name,Flags).Invoke(null,args); }

static void CaptureFull(string file)
    {
        Rectangle screen=SystemInformation.VirtualScreen;
        using(var image=new Bitmap(screen.Width,screen.Height)) {
            using(var g=Graphics.FromImage(image)) g.CopyFromScreen(screen.Location,Point.Empty,image.Size);
            image.Save(Path.Combine(folder,file),System.Drawing.Imaging.ImageFormat.Png);
        }
    }

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

static void DialogInputs(IntPtr hwnd,INPUT[] inputs)
    {
        uint pid; GetWindowThreadProcessId(hwnd,out pid);
        if(GetForegroundWindow()!=hwnd || pid!=(uint)Process.GetCurrentProcess().Id) throw new Exception("Refused input outside owned dialog.");
        if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(INPUT)))!=inputs.Length) throw new Exception("Dialog SendInput failed");
    }

static async Task WaitFor(Func<bool> predicate,string label)
    {
        var timer=Stopwatch.StartNew();
        while(!predicate() && timer.ElapsedMilliseconds<4000) await Task.Delay(20);
        Check(predicate(),label);
    }
}
