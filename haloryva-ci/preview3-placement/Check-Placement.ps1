$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP) { throw 'GitHub-hosted machine required. No local execution.' }
if(![Environment]::UserInteractive -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) { throw 'Interactive test desktop unavailable.' }
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview3-placement'
if(Test-Path -LiteralPath $out) { throw 'Fresh diagnostic directory required.' }
New-Item -ItemType Directory -Path $out | Out-Null
$expected='4170BEFEC193F5049051D73744CFCAD6D300240CE840E791E2C469A7CECAB13C'
$source=Join-Path $env:RUNNER_TEMP 'haloryva-preview3-build/Haloryva.exe'
if((Get-FileHash -LiteralPath $source).Hash -ne $expected) { throw 'Preserved binary mismatch.' }
$app=Join-Path $out 'Haloryva.exe'
Copy-Item -LiteralPath $source -Destination $app
$report=[ordered]@{Version='2.7.1-preview.3';SHA256=$expected;Rebuilt=$false;Legacy195Executed=$false;Installed=$false;Signed=$false;ReleasePublished=$false;Status='started';Launches=@()}
$records=New-Object Collections.ArrayList
Start-Transcript -Path (Join-Path $out 'transcript.txt')
try {
 Add-Type -AssemblyName System.Windows.Forms
 Add-Type -AssemblyName System.Drawing
 Add-Type -ReferencedAssemblies System.Drawing,System.Windows.Forms -TypeDefinition @'
using System;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;
public static class StudioPlacementProbe {
 public delegate bool Callback(IntPtr window,IntPtr parameter);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback,IntPtr parameter);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder title,int count);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out RECT rect);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
 public static IntPtr Find(int process) {
  IntPtr found=IntPtr.Zero;
  EnumWindows(delegate(IntPtr w,IntPtr unused) {
   uint pid; GetWindowThreadProcessId(w,out pid);
   if(pid!=(uint)process || !IsWindowVisible(w)) return true;
   var title=new StringBuilder(256); GetWindowText(w,title,title.Capacity);
   if(title.ToString()=="Haloryva Studio") { found=w; return false; }
   return true;
  },IntPtr.Zero);
  return found;
 }
 public static Rectangle Bounds(IntPtr window) {
  RECT rect; if(!GetWindowRect(window,out rect)) throw new Exception("Window rectangle unavailable");
  return Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);
 }
}
'@
 [StudioPlacementProbe]::SetProcessDPIAware() | Out-Null
 $os=Get-CimInstance Win32_OperatingSystem
 $report.Environment=[ordered]@{Caption=$os.Caption;Build=$os.BuildNumber;Image=$env:ImageVersion;Arch=$env:RUNNER_ARCH}
 $outside=$false
 foreach($number in 1..2) {
  $child=$null
  $item=[ordered]@{Launch=$number;Ready=$false;ExitNormal=$false;WindowMoved=$false;InputInjected=$false}
  $records.Add($item) | Out-Null
  try {
   $profile=Join-Path $out ('profile-'+$number)
   # Normal executable entry point: no --silent, no assembly-hosted controller,
   # no self-test switch, no repositioning and no generated mouse/keyboard inputs.
   $child=Start-Process -FilePath $app -ArgumentList ('--trail-session "'+$profile+'"') -WindowStyle Hidden -PassThru
   $timer=[Diagnostics.Stopwatch]::StartNew()
   $window=[IntPtr]::Zero
   while($window -eq [IntPtr]::Zero -and $timer.Elapsed.TotalSeconds -lt 20) {
    if($child.HasExited) { throw 'Original executable exited before showing Studio.' }
    $window=[StudioPlacementProbe]::Find($child.Id)
    Start-Sleep -Milliseconds 100
   }
   if($window -eq [IntPtr]::Zero) { throw 'Visible Studio window not found in normal launch.' }
   Start-Sleep -Seconds 4
   [StudioPlacementProbe]::SetForegroundWindow($window) | Out-Null
   Start-Sleep -Milliseconds 300
   $bounds=[StudioPlacementProbe]::Bounds($window)
   $work=[Windows.Forms.Screen]::FromHandle($window).WorkingArea
   $screen=[Windows.Forms.SystemInformation]::VirtualScreen
   $item.Ready=$true
   $item.Bounds=[ordered]@{X=$bounds.X;Y=$bounds.Y;Width=$bounds.Width;Height=$bounds.Height}
   $item.WorkingArea=[ordered]@{X=$work.X;Y=$work.Y;Width=$work.Width;Height=$work.Height}
   $item.Contained=$work.Contains($bounds)
   $item.OverflowRight=[Math]::Max(0,$bounds.Right-$work.Right)
   $item.OverflowBottom=[Math]::Max(0,$bounds.Bottom-$work.Bottom)
   if(!$item.Contained) { $outside=$true }
   $image=New-Object Drawing.Bitmap($screen.Width,$screen.Height)
   $graphics=[Drawing.Graphics]::FromImage($image)
   try {
    $graphics.CopyFromScreen($screen.Location,[Drawing.Point]::Empty,$screen.Size)
    $image.Save((Join-Path $out ('normal-launch-'+$number+'.png')),[Drawing.Imaging.ImageFormat]::Png)
   } finally { $graphics.Dispose(); $image.Dispose() }
  } finally {
   if($child -and !$child.HasExited) {
    $signal=[Threading.EventWaitHandle]::OpenExisting('Local\HaloryvaStudio.Exit.'+$child.Id)
    try { $signal.Set() | Out-Null } finally { $signal.Dispose() }
    if(!$child.WaitForExit(15000)) { $child.Kill(); throw 'Original app failed graceful shutdown.' }
    $item.ExitNormal=$child.ExitCode -eq 0
    if(!$item.ExitNormal) { throw 'Original app returned non-zero shutdown status.' }
   }
  }
  $item | ConvertTo-Json -Depth 5
 }
 if((Get-FileHash -LiteralPath $app).Hash -ne $expected) { throw 'Tested binary changed.' }
 $report.Status=if($outside) {'normal-launch-placement-issue-reproduced'} else {'normal-launch-placement-passed'}
 if($outside) { throw 'Studio opens partly outside the working area on this 1024x768 GitHub desktop. Binary preserved.' }
} catch {
 $report.Error=$_.Exception.Message
 if($report.Status -eq 'started') { $report.Status='diagnostic-incomplete' }
 throw
} finally {
 $report.Launches=@($records.ToArray())
 $report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out 'placement-result.json')
 Get-Content -LiteralPath (Join-Path $out 'placement-result.json')
 Stop-Transcript
}
