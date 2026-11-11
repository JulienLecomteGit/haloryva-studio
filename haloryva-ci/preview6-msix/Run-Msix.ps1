$Mode='msix'
$ErrorActionPreference='Stop'
# Refuse execution on the user's PC and on a self-hosted runner.
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP) { throw 'GitHub-hosted runner required. No local execution.' }
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview6-msix'
if(Test-Path -LiteralPath $out) { throw 'Fresh job required.' }
New-Item -ItemType Directory -Path $out | Out-Null
$report=[ordered]@{Mode=$Mode;Version='2.7.1-preview.6';Status='started';Rebuilt=$false;OriginalArtifactRun=42951517482;Steps=@();Launches=@();Tests195Repeated=$false;Signed=$false;ReleasePublished=$false}
$child=$null; $registered=$null
$testStart=Get-Date
Start-Transcript -Path (Join-Path $out 'transcript.txt')
try {
    $os=Get-CimInstance Win32_OperatingSystem
    [ordered]@{Caption=$os.Caption;Build=$os.BuildNumber;OSArchitecture=$os.OSArchitecture;RunnerArch=$env:RUNNER_ARCH;Interactive=[Environment]::UserInteractive;Session=[Diagnostics.Process]::GetCurrentProcess().SessionId;Image=$env:ImageVersion} | ConvertTo-Json | Set-Content (Join-Path $out 'environment.json')
    $expected='9978501F8E6B010BAC2739622E7F6E7C5DF6FBBBC19C8FECD76E59E3369A66F8'
    $build=Join-Path $env:RUNNER_TEMP 'haloryva-preview6-build'
    $app=Join-Path $build 'Haloryva.exe'
    if((Get-FileHash -LiteralPath $app).Hash -ne $expected) { throw 'Preserved preview.6 SHA256 mismatch.' }
    $report.SHA256=$expected
    $provenance=Get-Content -LiteralPath (Join-Path $build 'build-provenance.json') -Raw | ConvertFrom-Json
    if($provenance.Version -ne '2.7.1-preview.6' -or $provenance.SHA256 -ne $expected -or $provenance.Commit -ne '92d9887198b4efd932639265912030b9238c2a57') { throw 'Original build provenance mismatch.' }
    if([Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion -ne '2.7.1-preview.6') { throw 'Wrong executable version.' }
    $nativeRoot=Join-Path $env:RUNNER_TEMP 'haloryva-preview6-native'
    $profilesByLanguage=@{}
    foreach($file in Get-ChildItem -LiteralPath $nativeRoot -Recurse -File -Filter 'saved-native-profile.json') {
        $nativeResult=Get-Content -LiteralPath (Join-Path $file.Directory.FullName 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if($nativeResult.Status -ne 'language-passed' -or $nativeResult.SHA256 -ne $expected -or @($nativeResult.Phases | Where-Object {$_.ExitCode -ne 0}).Count -gt 0) { throw 'Only successful native UI evidence may seed MSIX.' }
        $settings=Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if($settings.UiLanguage -notin @('fr','en','es')) { throw 'Unexpected saved language.' }
        if($profilesByLanguage.ContainsKey($settings.UiLanguage)) { throw 'Duplicate native profile language.' }
        if((@($settings.DrawingToolCycle) -join '|') -cne 'Stylo|Rectangle') { throw 'Unexpected native selected tools.' }
        $profilesByLanguage[$settings.UiLanguage]=@{Path=$file.FullName;Hash=(Get-FileHash -LiteralPath $file.FullName).Hash;Settings=$settings}
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $out ('native-ui-profile-'+$settings.UiLanguage+'.json'))
    }
    if($profilesByLanguage.Count -ne 3) { throw 'Three successful native UI profile languages are required.' }
    $report.NativeProfiles=@(foreach($language in @('fr','en','es')) {@{Language=$language;SHA256=$profilesByLanguage[$language].Hash}})
    if(Get-AppxPackage -Name HaloryvaStudio.Preview6UnsignedTest) { throw 'Fresh runner required: test identity already registered.' }
    Copy-Item -LiteralPath (Join-Path $build 'build-provenance.json') -Destination $out
        if($os.Caption -notmatch 'Windows 11') { throw 'MSIX pilot requires a Windows 11 runner.' }
        $admin=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if(!$admin.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Unsigned executable MSIX testing requires administrator on the disposable runner.' }
        $layout=Join-Path $out 'layout'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Inputs/msix-layout') -Destination $layout -Recurse
        Copy-Item -LiteralPath $app -Destination (Join-Path $layout 'Haloryva.exe') -Force
        $manifest=Join-Path $layout 'AppxManifest.xml'
        [xml]$xml=Get-Content -LiteralPath $manifest -Raw -Encoding UTF8
        # Official Windows 11 unsigned-testing identity; no signing or security setting changes.
        # https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package
        $xml.Package.Identity.Name='HaloryvaStudio.Preview6UnsignedTest'
        $xml.Package.Identity.Publisher='CN=JulienLecomteGit, OID.2.25.311729368913984317654407730594956997722=1'
        $xml.Package.Identity.Version='2.7.1.6'
        $xml.Package.Properties.DisplayName='Haloryva Studio — preview.6 MSIX test'
        $ns=$xml.DocumentElement.NamespaceURI
        # Keep the XmlElement: PowerShell's XML adapter exposes an emptied element as a string.
        $resources=$xml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Resources']")
        if(!$resources) { throw 'MSIX Resources element missing.' }
        $resources.RemoveAll()
        foreach($language in @('fr-fr','en-us','es-es')) {
            $node=$xml.CreateElement('Resource',$ns); $node.SetAttribute('Language',$language)
            [void]$resources.AppendChild($node)
        }
        $xml.Package.Applications.Application.VisualElements.SetAttribute('DisplayName','Haloryva Studio — preview.6 test')
        $xml.Save($manifest)
        $report.PackageVersion='2.7.1.6'
        $report.PackageArchitecture=[string]$xml.Package.Identity.ProcessorArchitecture
        $report.ProductionIdentity=$false
        $sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
        $maker=Get-ChildItem -LiteralPath $sdk -Directory | Where-Object {$_.Name -match '^10[.]0[.]\d+[.]0$'} | Sort-Object {[Version]$_.Name} -Descending | ForEach-Object {Join-Path $_.FullName 'x64/makeappx.exe'} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
        if(!$maker) { throw 'Windows SDK MakeAppx not available on runner.' }
        $package=Join-Path $out 'Haloryva-2.7.1-preview.6-test-unsigned.msix'
        & $maker pack /d $layout /p $package /o 2>&1 | Out-File (Join-Path $out 'pack.log')
        if($LASTEXITCODE -ne 0) { throw 'MSIX structure failed.' }
        $report.MSIXSHA256=(Get-FileHash -LiteralPath $package).Hash
        $report.Steps+='MSIX test package created and structure validated'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive=[IO.Compression.ZipFile]::OpenRead($package)
        try {
            if($archive.GetEntry('AppxSignature.p7x')) { throw 'Expected an unsigned test package.' }
            $entry=$archive.GetEntry('Haloryva.exe')
            if(!$entry) { throw 'Executable missing from package.' }
            $stream=$entry.Open(); $sha=[Security.Cryptography.SHA256]::Create()
            try { $packedHash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $sha.Dispose(); $stream.Dispose() }
            if($packedHash -ne $expected) { throw 'Packed executable differs from validated preview.6.' }
            $report.PackedBinarySHA256=$packedHash
        } finally { $archive.Dispose() }
        Add-AppxPackage -Path $package -AllowUnsigned
        $registered=Get-AppxPackage -Name HaloryvaStudio.Preview6UnsignedTest
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
        function Assert-SelectedProfile($actual,$reference,[string]$stage) {
            if((@($actual.DrawingToolCycle) -join '|') -cne (@($reference.DrawingToolCycle) -join '|')) { throw "$stage : selected tool list changed." }
            foreach($property in $reference.PSObject.Properties) {
                $field=$property.Name
                $a=ConvertTo-Json -InputObject $actual.$field -Compress -Depth 10
                $b=ConvertTo-Json -InputObject $reference.$field -Compress -Depth 10
                if($a -cne $b) { throw "$stage : field $field not retained." }
            }
            Write-MsixDiagnostic ("PROFILE_VALIDATED stage="+$stage+" cycle="+(@($actual.DrawingToolCycle) -join '|')+" shortX2="+$actual.ButtonX2+" longX2="+$actual.ButtonX2Long)
        }
        $profilePath=$null
        $cases=@(
            @{Language='fr';Seed=$false;Fresh=$true},
            @{Language='fr';Seed=$true;Fresh=$false}, @{Language='fr';Seed=$false;Fresh=$false},
            @{Language='en';Seed=$true;Fresh=$false}, @{Language='en';Seed=$false;Fresh=$false},
            @{Language='es';Seed=$true;Fresh=$false}, @{Language='es';Seed=$false;Fresh=$false}
        )
        for($cycle=1;$cycle -le $cases.Count;$cycle++) {
            $case=$cases[$cycle-1]
            $language=$case.Language
            $sourceProfile=$profilesByLanguage[$language].Path
            $sourceProfileHash=$profilesByLanguage[$language].Hash
            $expectedSettings=$profilesByLanguage[$language].Settings
            if($case.Seed) {
                if(!$profilePath) { throw 'No package profile discovered.' }
                Copy-Item -LiteralPath $sourceProfile -Destination $profilePath -Force
                if((Get-FileHash -LiteralPath $profilePath).Hash -ne $sourceProfileHash) { throw 'Copied native profile bytes changed.' }
            }
            $launchedAt=[DateTime]::UtcNow
            $timer=[Diagnostics.Stopwatch]::StartNew()
            $record=[ordered]@{Cycle=$cycle;Language=$language;IdentityConfirmed=$false;Ready=$false;ExitNormal=$false;InputInjected=$false}
            $report.Launches+=,$record
            if($cycle -gt 1) {
                if(!$profilePath -or !(Test-Path -LiteralPath $profilePath)) { throw 'Prepared package profile missing.' }
                $record.ProfileBeforeSHA256=(Get-FileHash -LiteralPath $profilePath).Hash
                # Mark metadata only; launch 3 receives unmodified output of launch 2.
                [IO.File]::SetLastWriteTimeUtc($profilePath,[DateTime]::Parse('2001-01-01T00:00:00Z').ToUniversalTime())
            }
            Write-MsixDiagnostic "LAUNCH cycle=$cycle runnerSession=$([Diagnostics.Process]::GetCurrentProcess().SessionId)"
            Start-Process explorer.exe -ArgumentList ('shell:AppsFolder\'+$registered.PackageFamilyName+'!Haloryva') -WindowStyle Hidden
            $child=$null
            for($n=0;$n -lt 40 -and !$child;$n++) {
                Start-Sleep -Milliseconds 250
                $child=Get-Process Haloryva -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $registered.InstallLocation 'Haloryva.exe')} | Select-Object -First 1
            }
            if(!$child) { throw "Packaged launch $cycle not observed." }
            [uint32]$size=512; $name=New-Object Text.StringBuilder 512
            $rc=[PackageProbe]::GetPackageFullName($child.Handle,[ref]$size,$name)
            if($rc -ne 0 -or $name.ToString() -ne $registered.PackageFullName) { throw 'Process package identity not confirmed.' }
            $record.IdentityConfirmed=$true
            $record.ProcessId=$child.Id
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
                if($native -eq 0) { $ready=$true; $record.Ready=$true; $record.ReadyElapsedMs=$timer.ElapsedMilliseconds; break }
                if($n -lt 27) { Start-Sleep -Seconds 1 }
            }
            if(!$ready) { throw 'Exit event absent after bounded startup wait; inspect msix-diagnostic.log for startup windows and native error.' }
            # Observe actual localized controls in the packaged executable, not just the saved field.
            $expectedHomeCaption=@{fr='Accueil';en='Home';es='Inicio'}[$language]
            $expectedPreferencesCaption=@{fr='Préférences';en='Preferences';es='Preferencias'}[$language]
            $localized=$false
            for($n=0;$n -lt 50 -and !$localized;$n++) {
                $windowLines=@([PackageProbe]::Windows([uint32]$child.Id))
                $joined=$windowLines -join "`n"
                $localized=$joined.Contains('text='+$expectedHomeCaption) -and $joined.Contains('text='+$expectedPreferencesCaption)
                if(!$localized) { Start-Sleep -Milliseconds 200 }
            }
            if(!$localized) {
                $windowLines | ForEach-Object {Write-MsixDiagnostic ('WINDOW_LANGUAGE '+$_)}
                throw "Packaged UI language $language not observed."
            }
            $record.VisibleLanguageConfirmed=$true
            Write-MsixDiagnostic "LANGUAGE_UI cycle=$cycle expected=$language home=$expectedHomeCaption preferences=$expectedPreferencesCaption"
            $signal=[PackageProbe]::ExitEvent($eventName,$true)
            Write-MsixDiagnostic "SIGNAL cycle=$cycle nativeResult=$signal"
            if($signal -ne 0) { throw "Exit signal refused: native code $signal." }
            if(!$child.WaitForExit(15000) -or $child.ExitCode -ne 0) { throw 'Packaged app did not shut down normally.' }
            $record.ExitNormal=$true
            $record.ExitCode=$child.ExitCode
            $record.ElapsedMs=$timer.ElapsedMilliseconds
            Write-MsixDiagnostic "EXIT cycle=$cycle code=$($child.ExitCode) elapsedMs=$($timer.ElapsedMilliseconds)"
            $report.Steps+="packaged launch $cycle identity confirmed and clean shutdown"
            if($cycle -eq 1) {
                $packageData=Join-Path $env:LOCALAPPDATA ('Packages/'+$registered.PackageFamilyName)
                $profiles=@()
                if(Test-Path -LiteralPath $packageData) {
                    $profiles+=@(Get-ChildItem -LiteralPath $packageData -Recurse -File -Filter 'settings.json' -ErrorAction SilentlyContinue)
                }
                if($profiles.Count -ne 1) { throw "Expected exactly one package-owned settings file, found $($profiles.Count)." }
                $profilePath=$profiles[0].FullName
                if(!$profilePath.StartsWith($packageData+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Profile must remain under this package data folder.' }
                Write-MsixDiagnostic "PROFILE found=$profilePath"
                Copy-Item -LiteralPath $profilePath -Destination (Join-Path $out 'profile-initial.json')
                $initial=Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
                $defaultCycle='Stylo|Surligneur|Ligne droite|Rectangle|Ellipse|Flèche droite|Flèche libre|Forme libre|Numéro|Texte'
                Write-MsixDiagnostic ("DEFAULT actual="+(@($initial.DrawingToolCycle) -join '|')+" expected="+$defaultCycle)
                if((@($initial.DrawingToolCycle) -join '|') -cne $defaultCycle) { throw 'Fresh MSIX did not retain the complete default cycle.' }
                $record.DefaultTenTools=$true
                $report.ProfileRelativePath=$profilePath.Substring($packageData.Length+1)
                if($initial.UiLanguage -cne 'fr') { throw 'Fresh profile should default to French.' }
                $record.DefaultFrench=$true
                $report.Steps+='fresh French profile with 10 tools created under package data'
            } else {
                $saved=Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
                $rewritten=(Get-Item -LiteralPath $profilePath).LastWriteTimeUtc -ge $launchedAt
                Assert-SelectedProfile $saved $expectedSettings ("launch-"+$cycle)
                if(!$rewritten) { throw 'Packaged application did not rewrite the profile on clean exit.' }
                $record.ProfileAfterSHA256=(Get-FileHash -LiteralPath $profilePath).Hash
                $record.ProfileRewrittenByApp=$rewritten
                $record.SelectionRetained=$true
                $record.LanguageRetained=$saved.UiLanguage
                $record.SelectedTools=@($saved.DrawingToolCycle)
                if($record.ProfileAfterSHA256 -ne $record.ProfileBeforeSHA256) { throw 'Complete saved profile changed unexpectedly during packaged restart.' }
                Copy-Item -LiteralPath $profilePath -Destination (Join-Path $out ('profile-after-launch-'+$cycle+'.json'))
                Write-MsixDiagnostic "PROFILE roundtrip cycle=$cycle selectionRetained=true rewrittenByApp=$rewritten exactBytesRetained=true"
                $report.Steps+="packaged relaunch $cycle retained language, tools and every profile field; complete profile bytes unchanged"
            }
            if((Get-FileHash -LiteralPath (Join-Path $registered.InstallLocation 'Haloryva.exe')).Hash -ne $expected) { throw 'Installed binary changed after launch.' }
        }
        if((Get-FileHash -LiteralPath $app).Hash -ne $expected) { throw 'Original downloaded binary changed.' }
        Remove-AppxPackage -Package $registered.PackageFullName
        if(Get-AppxPackage -Name HaloryvaStudio.Preview6UnsignedTest) { throw 'Package remains registered after removal.' }
        $registered=$null
        $report.CleanupPackageRemoved=$true
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
        $report.CleanupPackageRemoved=!(Get-AppxPackage -Name HaloryvaStudio.Preview6UnsignedTest)
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out 'result.json')
    Get-ChildItem -LiteralPath $out -Recurse -File | Where-Object {$_.Name -in @('recipe.log','target-trace.log','compile.log','application-events.log','result.json','environment.json','msix-diagnostic.log')} | ForEach-Object {
        Write-Output ('EVIDENCE '+$_.Name)
        Get-Content -LiteralPath $_.FullName
    }
    Stop-Transcript
}
