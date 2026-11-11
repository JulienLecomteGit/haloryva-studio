$Mode='msix'
$ErrorActionPreference='Stop'
# Refuse execution on the user's PC and on a self-hosted runner.
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP) { throw 'GitHub-hosted runner required. No local execution.' }
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview3-msix'
if(Test-Path -LiteralPath $out) { throw 'Fresh job required.' }
New-Item -ItemType Directory -Path $out | Out-Null
$report=[ordered]@{Mode=$Mode;Version='2.7.1-preview.3';Status='started';Rebuilt=$false;OriginalArtifactRun=42931395281;Steps=@();Tests195Repeated=$false;Signed=$false;ReleasePublished=$false}
$child=$null; $registered=$null
$testStart=Get-Date
Start-Transcript -Path (Join-Path $out 'transcript.txt')
try {
    $os=Get-CimInstance Win32_OperatingSystem
    [ordered]@{Caption=$os.Caption;Build=$os.BuildNumber;OSArchitecture=$os.OSArchitecture;RunnerArch=$env:RUNNER_ARCH;Interactive=[Environment]::UserInteractive;Session=[Diagnostics.Process]::GetCurrentProcess().SessionId;Image=$env:ImageVersion} | ConvertTo-Json | Set-Content (Join-Path $out 'environment.json')
    $expected='4170BEFEC193F5049051D73744CFCAD6D300240CE840E791E2C469A7CECAB13C'
    $build=Join-Path $env:RUNNER_TEMP 'haloryva-preview3-build'
    $app=Join-Path $build 'Haloryva.exe'
    if((Get-FileHash -LiteralPath $app).Hash -ne $expected) { throw 'Preserved preview.3 SHA256 mismatch.' }
    $report.SHA256=$expected
    Copy-Item -LiteralPath (Join-Path $build 'build-provenance.json') -Destination $out
        if($os.Caption -notmatch 'Windows 11') { throw 'MSIX pilot requires a Windows 11 runner.' }
        $admin=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if(!$admin.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Unsigned executable MSIX testing requires administrator on the disposable runner.' }
        $layout=Join-Path $out 'layout'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Inputs/msix-layout') -Destination $layout -Recurse
        Copy-Item -LiteralPath $app -Destination (Join-Path $layout 'Haloryva.exe') -Force
        $manifest=Join-Path $layout 'AppxManifest.xml'
        [xml]$xml=Get-Content -LiteralPath $manifest -Raw
        # Official Windows 11 unsigned-testing identity; original manifest is untouched.
        $xml.Package.Identity.Name='HaloryvaStudio.Preview3UnsignedTest'
        $xml.Package.Identity.Publisher='CN=JulienLecomteGit, OID.2.25.311729368913984317654407730594956997722=1'
        $xml.Package.Identity.Version='2.7.1.3'
        $xml.Save($manifest)
        $sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
        $maker=Get-ChildItem -LiteralPath $sdk -Directory | Where-Object {$_.Name -match '^10[.]0[.]\d+[.]0$'} | Sort-Object {[Version]$_.Name} -Descending | ForEach-Object {Join-Path $_.FullName 'x64/makeappx.exe'} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
        if(!$maker) { throw 'Windows SDK MakeAppx not available on runner.' }
        $package=Join-Path $out 'Haloryva-cloud-unsigned.msix'
        & $maker pack /d $layout /p $package /o 2>&1 | Out-File (Join-Path $out 'pack.log')
        if($LASTEXITCODE -ne 0) { throw 'MSIX structure failed.' }
        $report.Steps+='MSIX test package created'
        Add-AppxPackage -Path $package -AllowUnsigned
        $registered=Get-AppxPackage -Name HaloryvaStudio.Preview3UnsignedTest
        if(!$registered) { throw 'Package not found after installation.' }
        $registered | Select-Object Name,PackageFullName,PackageFamilyName,InstallLocation,Status | ConvertTo-Json | Set-Content (Join-Path $out 'package.json')
        if((Get-FileHash -LiteralPath (Join-Path $registered.InstallLocation 'Haloryva.exe')).Hash -ne $expected) { throw 'Installed executable differs from preview.' }
        $report.Steps+='unsigned MSIX installed; binary unchanged'
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
'@
        $msixLog=Join-Path $out 'msix-diagnostic.log'
        function Write-MsixDiagnostic([string]$message) {
            $line=[DateTime]::UtcNow.ToString('o')+' '+$message
            $line | Add-Content -LiteralPath $msixLog -Encoding UTF8
            Write-Output $line
        }
        for($cycle=1;$cycle -le 2;$cycle++) {
            $timer=[Diagnostics.Stopwatch]::StartNew()
            Write-MsixDiagnostic "LAUNCH cycle=$cycle runnerSession=$([Diagnostics.Process]::GetCurrentProcess().SessionId)"
            Start-Process explorer.exe -ArgumentList ('shell:AppsFolder\'+$registered.PackageFamilyName+'!Haloryva')
            $child=$null
            for($n=0;$n -lt 40 -and !$child;$n++) {
                Start-Sleep -Milliseconds 250
                $child=Get-Process Haloryva -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $registered.InstallLocation 'Haloryva.exe')} | Select-Object -First 1
            }
            if(!$child) { throw "Packaged launch $cycle not observed." }
            [uint32]$size=512; $name=New-Object Text.StringBuilder 512
            $rc=[PackageProbe]::GetPackageFullName($child.Handle,[ref]$size,$name)
            if($rc -ne 0 -or $name.ToString() -ne $registered.PackageFullName) { throw 'Process package identity not confirmed.' }
            $eventName='Local\HaloryvaStudio.Exit.'+$child.Id
            Write-MsixDiagnostic "IDENTITY cycle=$cycle pid=$($child.Id) session=$($child.SessionId) package=$name event=$eventName"
            Start-Sleep -Seconds 3
            $ready=$false
            for($n=0;$n -le 27;$n++) {
                $child.Refresh()
                if($child.HasExited) { throw "Packaged app exited unexpectedly: $($child.ExitCode)." }
                $native=[PackageProbe]::ExitEvent($eventName,$false)
                $managed='opened'
                try { $ev=[Threading.EventWaitHandle]::OpenExisting($eventName); $ev.Dispose() }
                catch { $managed=$_.Exception.ToString().Replace([Environment]::NewLine,' ') }
                Write-MsixDiagnostic "POLL cycle=$cycle elapsedMs=$($timer.ElapsedMilliseconds) cpuSeconds=$($child.CPU) responding=$($child.Responding) nativeEventCode=$native managedEvent=$managed"
                if($n -in @(0,2,7,17,27) -or $native -eq 0) {
                    [PackageProbe]::Windows([uint32]$child.Id) | ForEach-Object { Write-MsixDiagnostic ('WINDOW '+$_) }
                }
                if($native -eq 0) { $ready=$true; break }
                if($n -lt 27) { Start-Sleep -Seconds 1 }
            }
            if(!$ready) { throw 'Exit event absent after bounded startup wait; inspect msix-diagnostic.log for startup windows and native error.' }
            $signal=[PackageProbe]::ExitEvent($eventName,$true)
            Write-MsixDiagnostic "SIGNAL cycle=$cycle nativeResult=$signal"
            if($signal -ne 0) { throw "Exit signal refused: native code $signal." }
            if(!$child.WaitForExit(15000) -or $child.ExitCode -ne 0) { throw 'Packaged app did not shut down normally.' }
            Write-MsixDiagnostic "EXIT cycle=$cycle code=$($child.ExitCode) elapsedMs=$($timer.ElapsedMilliseconds)"
            $report.Steps+="packaged launch $cycle identity confirmed and clean shutdown"
            if($cycle -eq 1) {
                $packageData=Join-Path $env:LOCALAPPDATA ('Packages/'+$registered.PackageFamilyName)
                $profiles=@()
                if(Test-Path -LiteralPath $packageData) {
                    $profiles+=@(Get-ChildItem -LiteralPath $packageData -Recurse -File -Filter 'settings.json' -ErrorAction SilentlyContinue)
                }
                $legacyProfile=Join-Path $env:LOCALAPPDATA 'Haloryva/settings.json'
                if(Test-Path -LiteralPath $legacyProfile) { $profiles+=Get-Item -LiteralPath $legacyProfile }
                if($profiles.Count -ne 1) { throw "Expected one app settings file after clean shutdown, found $($profiles.Count)." }
                $profilePath=$profiles[0].FullName
                Write-MsixDiagnostic "PROFILE found=$profilePath"
                Copy-Item -LiteralPath $profilePath -Destination (Join-Path $out 'profile-initial.json')
                $settings=Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
                if($settings.AnnotationTool -eq 'Rectangle' -and $settings.AnnotationColor -eq '#0000FF' -and $settings.AnnotationThickness -eq 12) { throw 'Fresh profile unexpectedly already equals persistence sentinel.' }
                $settings.AnnotationTool='Rectangle'; $settings.AnnotationColor='#0000FF'; $settings.AnnotationThickness=12
                $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $profilePath -Encoding UTF8
                [IO.File]::SetLastWriteTimeUtc($profilePath,[DateTime]::Parse('2001-01-01T00:00:00Z').ToUniversalTime())
                $report.Steps+='test profile prepared for restart persistence check'
            } else {
                $saved=Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
                $rewritten=(Get-Item -LiteralPath $profilePath).LastWriteTimeUtc -gt $testStart.ToUniversalTime()
                $retained=$saved.AnnotationTool -eq 'Rectangle' -and $saved.AnnotationColor -eq '#0000FF' -and $saved.AnnotationThickness -eq 12
                Write-MsixDiagnostic "PROFILE roundtrip retained=$retained rewrittenByApp=$rewritten"
                Copy-Item -LiteralPath $profilePath -Destination (Join-Path $out 'profile-after-restart.json')
                if(!$retained -or !$rewritten) { throw 'Packaged profile roundtrip not validated.' }
                $report.Steps+='tool/color/width loaded and saved again by packaged app after restart'
            }
        }
        Remove-AppxPackage -Package $registered.PackageFullName
        if(Get-AppxPackage -Name HaloryvaStudio.Preview3UnsignedTest) { throw 'Package remains registered after removal.' }
        $registered=$null
        $report.Steps+='package removed for test user'
        $report.Status='unsigned-test-identity-passed-NOT-production-signature'
} catch {
    $report.Status='failed-or-environment-blocked'
    $report.Error=$_.Exception.Message
    if($Mode -eq 'msix') {
        Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$testStart} -ErrorAction SilentlyContinue | Where-Object {$_.Message -match 'Haloryva'} | Select-Object TimeCreated,Id,ProviderName,Message | Format-List | Out-File (Join-Path $out 'application-events.log')
    }
    throw
} finally {
    if($child -and !$child.HasExited) { $child.Kill(); $report.Steps+='test child forcibly stopped during cleanup (not a clean shutdown)' }
    if($registered) {
        Remove-AppxPackage -Package $registered.PackageFullName -ErrorAction Continue
        $report.CleanupPackageRemoved=!(Get-AppxPackage -Name HaloryvaStudio.Preview3UnsignedTest)
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'result.json')
    Get-ChildItem -LiteralPath $out -Recurse -File | Where-Object {$_.Name -in @('recipe.log','target-trace.log','compile.log','application-events.log','result.json','environment.json')} | ForEach-Object {
        Write-Output ('EVIDENCE '+$_.Name)
        Get-Content -LiteralPath $_.FullName
    }
    Stop-Transcript
}
