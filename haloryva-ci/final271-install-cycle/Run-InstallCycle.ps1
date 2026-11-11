param([switch]$AuthorizedInstallCycle)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(!$AuthorizedInstallCycle){throw 'Explicit install/uninstall authorization required'}
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub-hosted only; never run on the principal PC'}
$expectedSetup='BC782C5DD43A795E1F5666A08621F7176F2F4DF1663F844C8BD83A8E55BFFDB1'
$expectedApp='1CB026DDBA084A648832FBD91118A67000373E076DAFA72BE2957B47223A6F81'
$inputRoot=Join-Path $env:RUNNER_TEMP 'final271-frozen-installer'
$setup=Join-Path $inputRoot 'HaloryvaSetup_2.7.1.exe'
$out=Join-Path $env:RUNNER_TEMP 'final271-install-cycle'
if(Test-Path -LiteralPath $out){throw 'Fresh evidence directory required'}
New-Item -ItemType Directory -Path $out|Out-Null
$results=New-Object 'Collections.Generic.List[object]'
$report=[ordered]@{Version='2.7.1';Commit=$env:GITHUB_SHA;Run=$env:GITHUB_RUN_ID;Status='running';Phase='preflight';StartedUtc=[DateTime]::UtcNow.ToString('o');SetupSHA256=$expectedSetup;ApplicationSHA256=$expectedApp;SourceArtifactRun=43088395238;SourceArtifactId=13886898481;Profile='synthetic pre-existing settings, no real 2.7.0 installation';InstallExecuted=$false;UninstallExecuted=$false;ApplicationRecompiled=$false;InstallerRecompiled=$false;DriverCompiled=$false;AnyCompilation=$false;Historical195Executed=$false;SeparateSelftestInvoked=$false;Signed=$false;Published=$false;Checks=@();AppEvents=@()}
$worker=$null; $wscript=$null; $fileSystem=$null; $watching=$false
$startSubscription=$null; $stopSubscription=$null
$startEventId='Haloryva.Final271.Start';$stopEventId='Haloryva.Final271.Stop'
$appEvents=New-Object 'Collections.Generic.List[object]'

# Reuse compiled Windows/.NET components. No Add-Type source, compiler or helper build.
function Initialize-PlatformDriver {
 $script:interopFlags=[Reflection.BindingFlags]'Public,NonPublic,Static'
 $systemAssembly=[Diagnostics.Process].Assembly
 $native=$systemAssembly.GetType('Microsoft.Win32.UnsafeNativeMethods',$true)
 $signature=[Type[]]@([Runtime.InteropServices.HandleRef],[int],[IntPtr],[IntPtr])
 $script:sendMessage=$native.GetMethod('SendMessage',$interopFlags,$null,$signature,$null)
 $script:postMessage=$native.GetMethod('PostMessage',$interopFlags,$null,$signature,$null)
 if($null -eq $sendMessage -or $null -eq $postMessage){throw 'Existing .NET window messaging methods unavailable; no compilation fallback'}
 $wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
 foreach($dll in @('WindowsBase.dll','UIAutomationTypes.dll','UIAutomationClient.dll')){
  $file=Join-Path $wpf $dll
  if(!(Test-Path -LiteralPath $file)){throw ('Existing Windows component unavailable: '+$dll)}
  [void][Reflection.Assembly]::LoadFrom($file)
 }
 $script:fileSystem=New-Object -ComObject Scripting.FileSystemObject
 $report.Driver=[ordered]@{Compiled=$false;SystemAssembly=$systemAssembly.Location;SendMessage=$sendMessage.ToString();PostMessage=$postMessage.ToString();UiAutomationAssembly=[System.Windows.Automation.AutomationElement].Assembly.Location;Method='Compiled framework methods, WMI event queues, retained Process.Handle';Screenshots='Not requested; no capture helper compiled'}
 [void](Canonical-Path $env:TEMP)
 $script:startSubscription=Register-WmiEvent -Query "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName='Haloryva.exe'" -SourceIdentifier $startEventId
 $script:stopSubscription=Register-WmiEvent -Query "SELECT * FROM Win32_ProcessStopTrace WHERE ProcessName='Haloryva.exe'" -SourceIdentifier $stopEventId
 $script:watching=$true
}
function Canonical-Path([string]$path) {
 $folder=$fileSystem.GetFolder($path)
 try {return [IO.Path]::GetFullPath([string]$folder.ShortPath).TrimEnd([IO.Path]::DirectorySeparatorChar)}
 finally {[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($folder)}
}
function Read-AppEvents {
 if(!$watching){return}
 foreach($source in @($startEventId,$stopEventId)){
  foreach($event in @(Get-Event -SourceIdentifier $source -ErrorAction SilentlyContinue)){
   $nativeEvent=$event.SourceEventArgs.NewEvent
   $kind=if($source -ceq $startEventId){'start'}else{'stop'}
   $entry=[ordered]@{Kind=$kind;TimeUtc=$event.TimeGenerated.ToUniversalTime().ToString('o');Name=[string]$nativeEvent['ProcessName'];Pid=[uint32]$nativeEvent['ProcessID'];ParentPid=[uint32]$nativeEvent['ParentProcessID'];CommandLine=$null;ReadError=$null;ExitStatus=$null}
   if($kind -ceq 'start'){
    try {
     $childInfo=Get-CimInstance Win32_Process -Filter ('ProcessId='+$entry.Pid)
     if($null -ne $childInfo){$entry.CommandLine=$childInfo.CommandLine}else{$entry.ReadError='Process no longer present at polling time'}
    }catch{$entry.ReadError=$_.Exception.Message}
   }else{$entry.ExitStatus=[uint32]$nativeEvent['ExitStatus']}
   $appEvents.Add([pscustomobject]$entry)
   Remove-Event -EventIdentifier $event.EventIdentifier
  }
 }
}
function Get-AppEvents {Read-AppEvents;return $appEvents.ToArray()}
function Get-OwnedWindowInfo([IntPtr]$window) {
 if($null -eq $worker -or $worker.HasExited){throw 'Setup worker unavailable'}
 $worker.Refresh()
 if($worker.MainWindowHandle -ne $window -or !$worker.Responding){throw 'Verified setup window unavailable'}
 $element=[System.Windows.Automation.AutomationElement]::FromHandle($window)
 $info=$element.Current
 if($info.ProcessId -ne $worker.Id -or !$info.Name.StartsWith('Haloryva Studio') -or !$info.IsEnabled){throw 'Window identity mismatch; no command sent'}
 $bounds=$info.BoundingRectangle
 if($bounds.Width -lt 400 -or $bounds.Height -lt 250){throw 'Unexpected setup bounds'}
 return $info
}
function Click-OwnedSetup([IntPtr]$window,[int]$designX,[int]$designY) {
 $info=Get-OwnedWindowInfo $window
 $bounds=$info.BoundingRectangle
 # The unchanged installer is borderless; bounding and client sizes coincide.
 $x=[int][Math]::Round($designX*$bounds.Width/860.0)
 $y=[int][Math]::Round($designY*$bounds.Height/560.0)
 $point=[IntPtr](($y -shl 16) -bor ($x -band 65535))
 $handleRef=New-Object Runtime.InteropServices.HandleRef($worker,$window)
 foreach($message in @(0x200,0x201,0x202)){
  $wparam=if($message -eq 0x201){[IntPtr]1}else{[IntPtr]::Zero}
  [void]$sendMessage.Invoke($null,[object[]]@($handleRef,[int]$message,$wparam,$point))
  if($message -eq 0x201){Start-Sleep -Milliseconds 80}
 }
 $report.LastUiCommand=[ordered]@{Pid=$worker.Id;Title=$info.Name;Window=$window.ToInt64();Width=$bounds.Width;Height=$bounds.Height;Command='Targeted left click';X=$x;Y=$y}
}
function Close-OwnedSetup([IntPtr]$window) {
 [void](Get-OwnedWindowInfo $window)
 $handleRef=New-Object Runtime.InteropServices.HandleRef($worker,$window)
 $sent=$postMessage.Invoke($null,[object[]]@($handleRef,[int]0x10,[IntPtr]::Zero,[IntPtr]::Zero))
 if(!$sent){throw 'Normal close request failed'}
}

function Save-Report {
 $report.Checks=@($results.ToArray())
 $report.AppEvents=@(Get-AppEvents)
 $report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $out 'results.json') -Encoding UTF8
}
function Check([string]$name,[bool]$ok,$detail) {
 $results.Add([pscustomobject]@{Name=$name;Passed=$ok;Detail=$detail;Utc=[DateTime]::UtcNow.ToString('o')})
 Save-Report
 Write-Host (($name+': '+$ok)+' | '+($detail|ConvertTo-Json -Compress -Depth 6))
 if(!$ok){throw ('Check failed: '+$name)}
}
function Wait-Until([string]$label,[scriptblock]$probe,[int]$seconds=45) {
 $watch=[Diagnostics.Stopwatch]::StartNew()
 do {Read-AppEvents;if(&$probe){return}; Start-Sleep -Milliseconds 200}while($watch.Elapsed.TotalSeconds -lt $seconds)
 throw ('Timed out: '+$label)
}
function Hash([string]$path) {if(Test-Path -LiteralPath $path -PathType Leaf){return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash};return ''}
function Read-RunValue {
 $k=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
 if($null -eq $k){return $null}
 try {return $k.GetValue('Haloryva',$null)} finally {$k.Dispose()}
}
function Profile-Map {
 $h=[ordered]@{}
 foreach($name in @('settings.json','settings.json.bak','fixture-note.txt')){$h[$name]=Hash (Join-Path $target $name)}
 return $h
}
function Check-Profile([string]$phase) {
 $current=Get-Content -LiteralPath $settings -Raw -Encoding UTF8|ConvertFrom-Json
 $differences=@()
 foreach($prop in $fixture.Keys){
  $before=$fixture[$prop]|ConvertTo-Json -Compress
  $after=$current.$prop|ConvertTo-Json -Compress
  if($before -cne $after){$differences+=($prop+': '+$before+' -> '+$after)}
 }
 Check ($phase+'-all-settings') ($differences.Count -eq 0) ([ordered]@{Fields=$fixture.Count;Differences=$differences;UiLanguage=$current.UiLanguage;DrawingToolCycle=$current.DrawingToolCycle})
}
function Start-Setup([string]$file,[bool]$uninstall) {
 $startInfo=New-Object Diagnostics.ProcessStartInfo
 $startInfo.FileName=$file;$startInfo.WorkingDirectory=Split-Path -Parent $file
 $startInfo.UseShellExecute=$false;$startInfo.CreateNoWindow=$true
 $startInfo.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 if($uninstall){$startInfo.Arguments='--uninstall'}
 $launcher=[Diagnostics.Process]::Start($startInfo)
 $heldLauncherHandle=$launcher.Handle
 $launcherId=$launcher.Id
 try {
  if(!$launcher.WaitForExit(20000)){throw 'Setup relocation launcher did not exit'}
  $launcher.WaitForExit()
  if($launcher.ExitCode -ne 0){throw ('Setup relocation failed: '+$launcher.ExitCode)}
 }finally{if($launcher.HasExited){$launcher.Dispose()}}
 $script:worker=$null
 Wait-Until 'relocated worker window' {
  $candidates=@(Get-CimInstance Win32_Process -Filter "Name='HaloryvaSetup.exe'"|Where-Object {$_.CommandLine -match ('--worker\s+--parent='+$launcherId+'(?:\s|$)')})
  if($candidates.Count -gt 1){throw 'Ambiguous setup worker'}
  if($candidates.Count -ne 1){return $false}
  $info=$candidates[0]
  $expectedRoot=Join-Path ([IO.Path]::GetTempPath()) 'HaloryvaMaintenance'
  $workerDirectory=Split-Path -Parent $info.ExecutablePath
  $actualRoot=Split-Path -Parent $workerDirectory
  $location=[ordered]@{ExecutablePath=$info.ExecutablePath;ExpectedRoot=$expectedRoot;ActualRoot=$actualRoot;TempEnvironment=$env:TEMP;TmpEnvironment=$env:TMP;CommandLine=$info.CommandLine}
  $report.LastWorkerLocation=$location;Save-Report
  if((Canonical-Path $actualRoot) -ine (Canonical-Path $expectedRoot) -or (Split-Path -Leaf $workerDirectory) -notmatch '^run-[a-f0-9]{32}$'){throw ('Unexpected setup worker location: '+($location|ConvertTo-Json -Compress))}
  if((Hash $info.ExecutablePath) -cne $expectedSetup){throw 'Relocated setup bytes changed'}
  $candidate=[Diagnostics.Process]::GetProcessById($info.ProcessId);$candidate.Refresh()
  if($candidate.MainWindowHandle -eq [IntPtr]::Zero){return $false}
  if(!$candidate.MainWindowTitle.StartsWith('Haloryva Studio')){throw 'Unexpected setup title'}
  $script:heldWorkerHandle=$candidate.Handle;$script:worker=$candidate;return $true
 } 35
 $phase=if($uninstall){'uninstall'}else{'install'}
 $report[($phase+'Worker')]=[ordered]@{Pid=$worker.Id;LauncherPid=$launcherId;Path=$worker.Path;Title=$worker.MainWindowTitle}
 Save-Report
 Start-Sleep -Milliseconds 700
 [void](Get-OwnedWindowInfo $worker.MainWindowHandle)
 Click-OwnedSetup $worker.MainWindowHandle 715 505
}
function Close-Setup([string]$phase) {
 Start-Sleep -Seconds 3
 $worker.Refresh()
 [void](Get-OwnedWindowInfo $worker.MainWindowHandle)
 # Normal WM_CLOSE closes the completed form without requesting a normal app launch.
 Close-OwnedSetup $worker.MainWindowHandle
 if(!$worker.WaitForExit(15000)){throw ($phase+' worker did not close normally')}
 $worker.WaitForExit()
 $exitCode=$worker.ExitCode
 Check ($phase+'-normal-exit') ($exitCode -eq 0) ([ordered]@{WorkerPid=$worker.Id;ExitCode=$exitCode;ExitEvidence='Process.Handle retained before GUI command; WaitForExit followed by ExitCode';Closure='Normal WM_CLOSE on completed real GUI; no normal app launch requested'})
}
try {
 $os=Get-CimInstance Win32_OperatingSystem
 $session=[Diagnostics.Process]::GetCurrentProcess().SessionId
 $report.Environment=[ordered]@{Caption=$os.Caption;Version=$os.Version;Build=$os.BuildNumber;Architecture=$env:PROCESSOR_ARCHITECTURE;ImageOS=$env:ImageOS;ImageVersion=$env:ImageVersion;SessionId=$session;User=[Environment]::UserName}
 Check 'windows11-interactive-runner' ($os.Caption -match 'Windows 11' -and $session -gt 0 -and [Environment]::Is64BitOperatingSystem) $report.Environment
 $provenance=Get-Content -LiteralPath (Join-Path $inputRoot 'installer-provenance.json') -Raw -Encoding UTF8|ConvertFrom-Json
 if($provenance.BuildCommit -cne '4da5e2c9f55b51ef7f1b7963133dc7c2ccf95bb4' -or $provenance.Status -cne 'passed' -or $provenance.FileVersion -cne '2.7.1.11'){throw 'Installer artifact provenance mismatch'}
 Check 'frozen-setup-input' ((Hash $setup) -ceq $expectedSetup -and $provenance.SetupSHA256 -ceq $expectedSetup) (Hash $setup)
 Check 'frozen-application-input' ((Hash (Join-Path $inputRoot 'payload\Haloryva.exe')) -ceq $expectedApp -and $provenance.ApplicationSHA256 -ceq $expectedApp) (Hash (Join-Path $inputRoot 'payload\Haloryva.exe'))
 $target=Join-Path $env:LOCALAPPDATA 'Haloryva'
 $maintenance=Join-Path $env:LOCALAPPDATA 'Haloryva.Maintenance'
 $settings=Join-Path $target 'settings.json'
 $installedApp=Join-Path $target 'Haloryva.exe'
 $installedSetup=Join-Path $target 'Setup\HaloryvaSetup.exe'
 $reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Haloryva'
 $menu=Join-Path ([Environment]::GetFolderPath('Programs')) 'Haloryva.lnk'
 $desktop=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Haloryva Studio.lnk'
 $occupied=@(@($target,$maintenance,$reg,$menu,$desktop)|Where-Object {Test-Path -LiteralPath $_})
 $apps=@(Get-Process -Name Haloryva,HaloryvaSetup -ErrorAction SilentlyContinue)
 Check 'fresh-test-profile' ($occupied.Count -eq 0 -and $apps.Count -eq 0 -and $null -eq (Read-RunValue)) ([ordered]@{Occupied=$occupied;ExistingProcesses=$apps.Count})
 Initialize-PlatformDriver
 Check 'precompiled-windows-driver-ready' ($null -ne $sendMessage -and $null -ne $postMessage -and $watching) $report.Driver
 New-Item -ItemType Directory -Path $target|Out-Null
 $fixture=[ordered]@{UiLanguage='es';Enabled=$true;DrawingToolCycle=@('Stylo','Rectangle');AnnotationTool='Rectangle';AnnotationColor='#137AC4';AnnotationThickness=9;DrawSmoothMode='Formes assistées';DrawSmoothStrength=65;ButtonX1='Outil de dessin suivant';ButtonX2='Outil de dessin précédent';LongPressMs=550;RingRadius=44;LaunchAtLogin=$false}
 $fixture|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $settings -Encoding UTF8
 [IO.File]::WriteAllText(($settings+'.bak'),'{"UiLanguage":"en","DrawingToolCycle":["Stylo"],"LaunchAtLogin":false}',(New-Object Text.UTF8Encoding($false)))
 [IO.File]::WriteAllText((Join-Path $target 'fixture-note.txt'),'Synthetic preservation sentinel; no personal data.',(New-Object Text.UTF8Encoding($false)))
 $before=Profile-Map; $report.ProfileBefore=$before
 Copy-Item -LiteralPath $settings -Destination (Join-Path $out 'profile-before.json')
 $report.UiAutomation='No compilation: precompiled .NET window messaging and UIAutomation. Targeted external mouse messages to the verified setup window; normal WM_CLOSE after completion. Not physical-desktop click validation. No interaction with the Microsoft account window.'
 $report.Phase='install';Save-Report
 Start-Setup $setup $false
 $report.InstallExecuted=$true;Save-Report
 Wait-Until 'committed installation' {
  if(!(Test-Path -LiteralPath $installedApp) -or !(Test-Path -LiteralPath $reg)){return $false}
  foreach($journal in @(Get-ChildItem -LiteralPath $maintenance -Filter files.xml -Recurse -ErrorAction SilentlyContinue)){
   try{[xml]$xml=Get-Content -LiteralPath $journal.FullName -Raw;if($xml.FileJournal.State -ceq 'committed'){return $true}}catch{}
  }
  return $false
 } 100
 $journalEvidence=Join-Path $out 'maintenance-after-install'
 New-Item -ItemType Directory -Path $journalEvidence|Out-Null
 foreach($transactionDir in @(Get-ChildItem -LiteralPath $maintenance -Directory -Filter 'transaction-*')){
  Copy-Item -LiteralPath $transactionDir.FullName -Destination $journalEvidence -Recurse
 }
 Close-Setup 'install'
 Check 'installed-app-identical' ((Hash $installedApp) -ceq $expectedApp) (Hash $installedApp)
 Check 'registered-setup-identical' ((Hash $installedSetup) -ceq $expectedSetup) (Hash $installedSetup)
 $registration=Get-ItemProperty -LiteralPath $reg
 $expectedUninstall='"'+$installedSetup+'" --uninstall'
 Check 'windows-uninstall-registration' ($registration.DisplayVersion -ceq '2.7.1' -and $registration.InstallLocation -eq $target -and $registration.UninstallString -eq $expectedUninstall) ([ordered]@{DisplayVersion=$registration.DisplayVersion;InstallLocation=$registration.InstallLocation;UninstallString=$registration.UninstallString})
 $wscript=New-Object -ComObject WScript.Shell
 foreach($link in @($menu,$desktop)){
  if(!(Test-Path -LiteralPath $link)){throw ('Missing shortcut: '+$link)}
  $shortcut=$wscript.CreateShortcut($link)
  Check ('shortcut-'+[IO.Path]::GetFileName($link)) ($shortcut.TargetPath -eq $installedApp -and $shortcut.WorkingDirectory -eq $target) ([ordered]@{Link=$link;Target=$shortcut.TargetPath;WorkingDirectory=$shortcut.WorkingDirectory})
  [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
 }
 Check-Profile 'install'
 $afterInstall=Profile-Map; $report.ProfileAfterInstall=$afterInstall
 Check 'install-backup-and-custom-file-preserved' ($before['settings.json.bak'] -ceq $afterInstall['settings.json.bak'] -and $before['fixture-note.txt'] -ceq $afterInstall['fixture-note.txt']) $afterInstall
 Check 'install-autostart-remains-off' ($null -eq (Read-RunValue)) 'No Haloryva Run value'
 Wait-Until 'automatic selftest process exit event' { @((Get-AppEvents)|Where-Object {$_.Kind -eq 'stop'}).Count -ge 1 } 15
 $starts=@((Get-AppEvents)|Where-Object {$_.Kind -eq 'start'})
 $stops=@((Get-AppEvents)|Where-Object {$_.Kind -eq 'stop'})
 $selfOk=$starts.Count -eq 1 -and $stops.Count -eq 1
 if($selfOk){$selfOk=$starts[0].ParentPid -eq $report.installWorker.Pid -and $stops[0].Pid -eq $starts[0].Pid -and $stops[0].ExitStatus -eq 0}
 if($selfOk -and ![string]::IsNullOrEmpty($starts[0].CommandLine)){$selfOk=$starts[0].CommandLine -match '(?:^|\s)--selftest(?:\s|$)'}
 Check 'automatic-installation-selftest' $selfOk ([ordered]@{Events=@((Get-AppEvents));Meaning='Only application invocation allowed by installer source before commit; WMI event queue records observed child start and zero exit. No separate selftest invoked.'})
 Check 'no-normal-app-left-running' (@(Get-Process -Name Haloryva -ErrorAction SilentlyContinue).Count -eq 0) 'Completed setup closed through normal WM_CLOSE'
 Copy-Item -LiteralPath $settings -Destination (Join-Path $out 'profile-after-install.json')
 $report.Phase='uninstall';Save-Report
 # Execute precisely the installed, validated Windows uninstall entry. Keep RemoveSettings unchecked.
 Start-Setup $installedSetup $true
 $report.UninstallExecuted=$true;Save-Report
 Wait-Until 'removed application registration and committed uninstall cleanup' {
  if((Test-Path -LiteralPath $reg) -or (Test-Path -LiteralPath $installedApp) -or (Test-Path -LiteralPath $installedSetup)){return $false}
  return @(Get-ChildItem -LiteralPath $maintenance -Directory -Filter 'transaction-*' -ErrorAction SilentlyContinue).Count -eq 0
 } 75
 Close-Setup 'uninstall'
 Check 'application-and-setup-removed' (!(Test-Path -LiteralPath $installedApp) -and !(Test-Path -LiteralPath $installedSetup)) 'Installed application and registered uninstaller absent'
 Check 'windows-uninstall-entry-removed' (!(Test-Path -LiteralPath $reg)) $reg
 Check 'shortcuts-removed' (!(Test-Path -LiteralPath $menu) -and !(Test-Path -LiteralPath $desktop)) @($menu,$desktop)
 Check 'uninstall-autostart-absent' ($null -eq (Read-RunValue)) 'No Haloryva Run value'
 Check-Profile 'uninstall'
 $afterUninstall=Profile-Map; $report.ProfileAfterUninstall=$afterUninstall
 $changed=@($afterInstall.Keys|Where-Object {$afterInstall[$_] -cne $afterUninstall[$_]})
 Check 'uninstall-profile-files-byte-identical' ($changed.Count -eq 0) ([ordered]@{Files=$afterUninstall;Changed=$changed})
 Check 'demo-asset-removed' (!(Test-Path -LiteralPath (Join-Path $target 'Logos\haloryva_demo.png'))) 'Bundled demo removed'
 $remaining=@(Get-ChildItem -LiteralPath $target -File -Recurse|ForEach-Object {$_.FullName.Substring($target.Length+1)}|Sort-Object)
 Check 'only-preserved-profile-files-remain' (($remaining -join '|') -ceq 'fixture-note.txt|settings.json|settings.json.bak') $remaining
 $remainingProcesses=@(Get-Process -Name Haloryva,HaloryvaSetup -ErrorAction SilentlyContinue)
 Check 'normal-process-shutdown' ($remainingProcesses.Count -eq 0) ([ordered]@{Count=$remainingProcesses.Count})
 Start-Sleep -Seconds 1
 Check 'no-extra-app-execution' (@((Get-AppEvents)|Where-Object {$_.Kind -eq 'start'}).Count -eq 1) @((Get-AppEvents))
 Check 'original-artifact-unchanged' ((Hash $setup) -ceq $expectedSetup -and (Hash (Join-Path $inputRoot 'payload\Haloryva.exe')) -ceq $expectedApp) ([ordered]@{Setup=Hash $setup;Application=Hash (Join-Path $inputRoot 'payload\Haloryva.exe')})
 Copy-Item -LiteralPath $settings -Destination (Join-Path $out 'profile-after-uninstall.json')
 $report.MaintenanceFilesRemaining=@(Get-ChildItem -LiteralPath $maintenance -File -Recurse|ForEach-Object {$_.FullName.Substring($maintenance.Length+1)})
 $report.Limits=@('Synthetic settings fixture, not a real previous 2.7.0 installation','Windows 11 ARM64 with x64 emulation; physical x64 PC not tested','No normal app launch, no reboot/logon, no 195-suite, no MSIX or signing test','Installer maintenance executable in temporary run folder and unlocked operation.lock may remain by design')
 $report.Status='passed';$report.Phase='finished'
} catch {
 $report.Status='failed';$report.Error=$_.Exception.ToString();$report.ErrorPosition=$_.InvocationInfo.PositionMessage
 Write-Host ('FAILURE: '+$report.Error+' '+$report.ErrorPosition)
 # Failure evidence is textual; no screenshot helper compiled.
 throw
} finally {
 $report.FinishedUtc=[DateTime]::UtcNow.ToString('o');Save-Report
 Write-Host 'RESULTS_JSON_BEGIN';Write-Host ($report|ConvertTo-Json -Depth 12 -Compress);Write-Host 'RESULTS_JSON_END'
 foreach($source in @($startEventId,$stopEventId)){
  Unregister-Event -SourceIdentifier $source -ErrorAction SilentlyContinue
  Get-Event -SourceIdentifier $source -ErrorAction SilentlyContinue|Remove-Event -ErrorAction SilentlyContinue
 }
 if($null -ne $worker){$worker.Dispose()}
 if($null -ne $fileSystem){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($fileSystem)}
 if($null -ne $wscript){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($wscript)}
}
