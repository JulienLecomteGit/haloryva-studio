$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub-hosted required'}
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview8-startup-ui'
if(Test-Path -LiteralPath $out){throw 'Fresh output required'}
New-Item -ItemType Directory -Path $out|Out-Null
$inputKit=Join-Path $env:RUNNER_TEMP 'haloryva-preview8-msix-input'
$name='HaloryvaStudio.MsixLifecycleTest'
$expected='CFB5322F128EBAA7D1BBC6A4F8892CC853C9B6B9ED7F78DBDC4C2FB3B0CDA512'
$registered=$null;$child=$null
$report=[ordered]@{Status='started';Version='2.7.1-preview.8';ExecutableSHA256=$expected;Rebuilt=$false;Signed=$false;Published=$false;Historical195Executed=$false;RealLogonExecuted=$false;Checks=@();States=@()}
Start-Transcript -Path (Join-Path $out 'transcript.txt')
try {
 $os=Get-CimInstance Win32_OperatingSystem
 if($os.Caption -notmatch 'Windows 11'){throw 'Windows 11 required'}
 $report.Environment=@{Caption=$os.Caption;Build=$os.BuildNumber;Arch=$env:RUNNER_ARCH;Image=$env:ImageVersion}
 if(Get-AppxPackage -Name $name){throw 'Identity already present'}
 $b=Join-Path $inputKit 'Haloryva-test-2.7.1.80.msix';$c=Join-Path $inputKit 'Haloryva-test-2.7.1.81.msix'
 if((Get-FileHash -LiteralPath $b).Hash -cne 'D07978FB383F1CD069DDAE53C49771E9ACFA4D47700FF262374F3BCF3EAD34A9' -or (Get-FileHash -LiteralPath $c).Hash -cne '08B68E89F32032C970B4B36EFFDE868772A10A0ECCFF542CF4BED810F57AAE73'){throw 'Frozen MSIX differs'}
 Add-Type -AssemblyName System.Drawing
 Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class PackageProbe {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern int GetPackageFullName(IntPtr process,ref uint length,StringBuilder name);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenEvent(uint rights,bool inherit,string name);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetEvent(IntPtr handle);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 public static int ExitEvent(string name,bool signal) {
   IntPtr h=OpenEvent(signal?2u:0x100000u,false,name);
   if(h==IntPtr.Zero) return Marshal.GetLastWin32Error();
   try { if(signal && !SetEvent(h)) return Marshal.GetLastWin32Error(); return 0; }
   finally { CloseHandle(h); }
 }
 delegate bool EnumProc(IntPtr hwnd,IntPtr state);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr state);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumProc callback,IntPtr state);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int length);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int length);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,UIntPtr count,StringBuilder text,uint flags,uint timeout,out UIntPtr result);
 static string Describe(IntPtr hwnd,bool child) {
   var title=new StringBuilder(2048); var cls=new StringBuilder(256);
   GetWindowText(hwnd,title,title.Capacity); GetClassName(hwnd,cls,cls.Capacity);
   if(child && title.Length==0) { UIntPtr result; SendMessageTimeout(hwnd,13,(UIntPtr)2048,title,2,200,out result); }
   return "hwnd="+hwnd.ToInt64().ToString("X")+" visible="+IsWindowVisible(hwnd)+" class="+cls+" text="+title;
 }
 public static string[] Windows(uint pid) {
   var lines=new List<string>();
   EnumWindows(delegate(IntPtr hwnd,IntPtr unused) {
     uint owner; GetWindowThreadProcessId(hwnd,out owner); if(owner!=pid) return true;
     lines.Add(Describe(hwnd,false));
     EnumChildWindows(hwnd,delegate(IntPtr child,IntPtr state) { if(lines.Count<100) lines.Add("  "+Describe(child,true)); return lines.Count<100; },IntPtr.Zero);
     return lines.Count<100;
   },IntPtr.Zero);
   return lines.ToArray();
 }
}
[ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IActivation {
 [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.LPWStr)]string args,uint options,out uint pid);
}
public static class ActivatePackaged {
 public static uint Launch(string id,string args) {
  object manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
  try { uint pid; int hr=((IActivation)manager).ActivateApplication(id,args,0,out pid);
    if(hr<0) Marshal.ThrowExceptionForHR(hr); return pid;
  } finally { Marshal.ReleaseComObject(manager); }
 }
}


public static class UiProbe {
 delegate bool EnumProc(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumProc cb,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h,uint m,UIntPtr n,StringBuilder b,uint f,uint t,out UIntPtr result);
 static string Text(IntPtr h){var b=new StringBuilder(2048);UIntPtr r;SendMessageTimeout(h,13,(UIntPtr)2048,b,2,200,out r);return b.ToString();}
 public static IntPtr Find(uint pid,string label){
  IntPtr found=IntPtr.Zero;
  EnumWindows(delegate(IntPtr h,IntPtr p){uint owner;GetWindowThreadProcessId(h,out owner);if(owner!=pid)return true;
   if(Text(h)==label && IsWindowVisible(h)) {found=h;return false;}
   EnumChildWindows(h,delegate(IntPtr c,IntPtr q){if(Text(c)==label && IsWindowVisible(c)){found=c;return false;}return true;},IntPtr.Zero);
   return found==IntPtr.Zero;
  },IntPtr.Zero);return found;
 }
 public static void Click(IntPtr h){
  if(h==IntPtr.Zero || !IsWindowVisible(h) || !IsWindowEnabled(h))throw new Exception("UI target unavailable");
  SetForegroundWindow(GetAncestor(h,2));
  // Target the actual WinForms keyboard command; no OS state is fabricated.
  if(!PostMessage(h,0x0100,(IntPtr)32,(IntPtr)1) || !PostMessage(h,0x0101,(IntPtr)32,(IntPtr)0xC0390001L))throw new Exception("Keyboard command not queued");
 }
}

'@
 function Register([string]$path,[string]$version){
  Add-AppxPackage -Path $path -AllowUnsigned
  $script:registered=Get-AppxPackage -Name $name
  if([string]$script:registered.Version -cne $version -or (Get-FileHash -LiteralPath (Join-Path $script:registered.InstallLocation 'Haloryva.exe')).Hash -cne $expected){throw 'Installed version differs'}
  $manifest=Get-AppxPackageManifest -Package $script:registered.PackageFullName
  $manifest.OuterXml|Set-Content -LiteralPath (Join-Path $out ('manifest-'+$version+'.xml')) -Encoding UTF8
  $report.Family=$script:registered.PackageFamilyName
 }
 function Snapshot([string]$stage){
  $key='HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\'+$script:registered.PackageFamilyName+'\HaloryvaStudioStartup'
  $values=@{}
  if(Test-Path -LiteralPath $key){$p=Get-ItemProperty -LiteralPath $key;foreach($x in $p.PSObject.Properties){if($x.Name -notmatch '^PS'){$values[$x.Name]=$x.Value}}}
  $report.States+=,@{Stage=$stage;RegistryReadOnly=$values}
  $logdir=Join-Path $env:LOCALAPPDATA ('Packages\'+$script:registered.PackageFamilyName+'\LocalState\Haloryva\Logs')
  if(Test-Path -LiteralPath $logdir){Get-ChildItem -LiteralPath $logdir -Filter '*.log'|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $out ($stage+'-'+$_.Name))}}
 }
 function Start-Studio {
  $appPid=[ActivatePackaged]::Launch(($script:registered.PackageFamilyName+'!Haloryva'),'')
  $script:child=Get-Process -Id $appPid
  [uint32]$size=512;$identity=New-Object Text.StringBuilder 512
  if([PackageProbe]::GetPackageFullName($script:child.Handle,[ref]$size,$identity) -ne 0 -or $identity.ToString() -cne $script:registered.PackageFullName){throw 'Identity differs'}
  $script:eventName='Local\HaloryvaStudio.Exit.'+$appPid
  for($i=0;$i -lt 120 -and [PackageProbe]::ExitEvent($script:eventName,$false) -ne 0;$i++){Start-Sleep -Milliseconds 250}
  if([PackageProbe]::ExitEvent($script:eventName,$false) -ne 0){throw 'Studio not ready'}
  Start-Sleep -Seconds 2
  $nav=[UiProbe]::Find($appPid,'Préférences')
  if($nav -eq [IntPtr]::Zero){throw 'Preferences control not found'}
  [UiProbe]::Click($nav);Start-Sleep -Seconds 1
  $script:toggle=[UiProbe]::Find($appPid,'Démarrage avec Windows')
  [PackageProbe]::Windows($appPid)|Set-Content -LiteralPath (Join-Path $out ('windows-'+$script:registered.Version+'.txt')) -Encoding UTF8
  Snapshot ('opened-'+$script:registered.Version)
  $report.States+=,@{Stage='control-found';Found=($script:toggle -ne [IntPtr]::Zero);Enabled=[UiProbe]::IsWindowEnabled($script:toggle)}
  if($script:toggle -eq [IntPtr]::Zero -or ![UiProbe]::IsWindowEnabled($script:toggle)){
   [PackageProbe]::Windows($appPid)|Write-Output
   Get-ChildItem -LiteralPath $out -Filter '*.log'|ForEach-Object {Get-Content -LiteralPath $_.FullName}
   throw 'Startup UI unavailable; inspect application logs and manifest'
  }
 }
 function Read-Profile {
  $file=Join-Path $env:LOCALAPPDATA ('Packages\'+$script:registered.PackageFamilyName+'\LocalState\Haloryva\settings.json')
  if(!(Test-Path -LiteralPath $file)){return [pscustomobject]@{LaunchAtLogin=$null}}
  return Get-Content -LiteralPath $file -Raw -Encoding UTF8|ConvertFrom-Json
 }
 function Change-Startup([bool]$desired,[string]$stage){
  [UiProbe]::Click($script:toggle)
  $confirmed=$false
  for($i=0;$i -lt 80;$i++){
   Start-Sleep -Milliseconds 250
   $cfg=Read-Profile
   if($cfg.LaunchAtLogin -eq $desired -and [UiProbe]::IsWindowEnabled($script:toggle)){$confirmed=$true;break}
  }
  Snapshot $stage
  if(!$confirmed){throw "UI action did not confirm $desired ($stage)"}
  $report.Checks+=,@{Id=$stage;Passed=$true;Kind='real-studio-keyboard-command-and-confirmed-settings';LaunchAtLogin=$cfg.LaunchAtLogin}
 }
 function Stop-Studio {
  if([PackageProbe]::ExitEvent($script:eventName,$true) -ne 0 -or !$script:child.WaitForExit(15000) -or $script:child.ExitCode -ne 0){throw 'Normal exit failed'}
  $script:child=$null
 }
 Register $b '2.7.1.80'
 Start-Studio
 if((Read-Profile).LaunchAtLogin){throw 'Fresh profile not disabled'}
 Change-Startup $true 'S2-studio-enable'
 Change-Startup $false 'S2-studio-disable'
 Change-Startup $true 'U2-studio-enabled-before-update'
 Stop-Studio
 Register $c '2.7.1.81'
 Start-Studio
 if(!(Read-Profile).LaunchAtLogin){throw 'Preference lost on update'}
 Change-Startup $false 'U2-studio-retained-and-disable'
 Stop-Studio
 Remove-AppxPackage -Package $registered.PackageFullName
 if(Get-AppxPackage -Name $name){throw 'Package remains'}
 $registered=$null;$report.CleanupPackageRemoved=$true;$report.Status='startup-ui-passed'
}catch{$report.Status='failed';$report.Error=$_.ToString();throw}
finally{
 if($registered){
  try{Snapshot 'final'}catch{}
  if($child){try{[void][PackageProbe]::ExitEvent($script:eventName,$true);[void]$child.WaitForExit(15000);if($child.HasExited){$child=$null}}catch{}}
  if(!$child){try{Remove-AppxPackage -Package $registered.PackageFullName -ErrorAction Stop;$report.CleanupPackageRemoved=$true}catch{$report.CleanupError=$_.ToString()}}
 }
 $report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
 $report|ConvertTo-Json -Depth 12|Write-Output
 Stop-Transcript
}
