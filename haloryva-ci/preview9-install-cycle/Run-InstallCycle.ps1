param([switch]$AuthorizedInstallCycle)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(!$AuthorizedInstallCycle){throw 'Explicit install/uninstall authorization required'}
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub-hosted only; never run on the principal PC'}
$expectedSetup='3FC34DB4BE84B242AD439FE400B4F0C302995AAF5CF46AA89E535824EB845394'
$expectedApp='678DB7DBBAB906A438F01F83A27D8A4D9C1904D1E280880D148FE3A1610522DC'
$inputRoot=Join-Path $env:RUNNER_TEMP 'preview9-frozen-installer'
$setup=Join-Path $inputRoot 'HaloryvaSetup_2.7.1-preview.9.exe'
$out=Join-Path $env:RUNNER_TEMP 'preview9-install-cycle'
if(Test-Path -LiteralPath $out){throw 'Fresh evidence directory required'}
New-Item -ItemType Directory -Path $out|Out-Null
$results=New-Object 'Collections.Generic.List[object]'
$report=[ordered]@{Version='2.7.1-preview.9';Commit=$env:GITHUB_SHA;Run=$env:GITHUB_RUN_ID;Status='running';Phase='preflight';StartedUtc=[DateTime]::UtcNow.ToString('o');SetupSHA256=$expectedSetup;ApplicationSHA256=$expectedApp;SourceArtifactRun=43060226353;SourceArtifactId=13872288880;Profile='synthetic pre-existing settings, no real 2.7.0 installation';InstallExecuted=$false;UninstallExecuted=$false;ApplicationRecompiled=$false;InstallerRecompiled=$false;Historical195Executed=$false;SeparateSelftestInvoked=$false;Signed=$false;Published=$false;Checks=@();AppEvents=@()}
$trace=$null; $worker=$null; $wscript=$null
function Save-Report {
 $report.Checks=@($results.ToArray())
 if($null -ne $trace){$report.AppEvents=@($trace.Snapshot())}
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
 do {if(&$probe){return}; Start-Sleep -Milliseconds 200}while($watch.Elapsed.TotalSeconds -lt $seconds)
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
 $args=@{FilePath=$file;WorkingDirectory=(Split-Path -Parent $file);PassThru=$true;WindowStyle='Hidden'}
 if($uninstall){$args.ArgumentList='--uninstall'}
 $launcher=Start-Process @args
 $launcherId=$launcher.Id
 if(!$launcher.WaitForExit(20000)){throw 'Setup relocation launcher did not exit'}
 if($launcher.ExitCode -ne 0){throw ('Setup relocation failed: '+$launcher.ExitCode)}
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
  if([SetupUi]::CanonicalPath($actualRoot) -ine [SetupUi]::CanonicalPath($expectedRoot) -or (Split-Path -Leaf $workerDirectory) -notmatch '^run-[a-f0-9]{32}$'){throw ('Unexpected setup worker location: '+($location|ConvertTo-Json -Compress))}
  if((Hash $info.ExecutablePath) -cne $expectedSetup){throw 'Relocated setup bytes changed'}
  $candidate=[Diagnostics.Process]::GetProcessById($info.ProcessId);$candidate.Refresh()
  if($candidate.MainWindowHandle -eq [IntPtr]::Zero){return $false}
  if(!$candidate.MainWindowTitle.StartsWith('Haloryva Studio')){throw 'Unexpected setup title'}
  $script:worker=$candidate;return $true
 } 35
 $phase=if($uninstall){'uninstall'}else{'install'}
 $report[($phase+'Worker')]=[ordered]@{Pid=$worker.Id;LauncherPid=$launcherId;Path=$worker.Path;Title=$worker.MainWindowTitle}
 Save-Report
 Start-Sleep -Milliseconds 700
 [SetupUi]::Capture($worker.MainWindowHandle,(Join-Path $out ($phase+'-welcome.png')))
 [SetupUi]::Click($worker.MainWindowHandle,715,505)
}
function Close-Setup([string]$phase) {
 Start-Sleep -Seconds 3
 $worker.Refresh()
 [SetupUi]::Capture($worker.MainWindowHandle,(Join-Path $out ($phase+'-completed.png')))
 # Normal WM_CLOSE closes the completed form without requesting a normal app launch.
 [SetupUi]::CloseNormally($worker.MainWindowHandle)
 if(!$worker.WaitForExit(15000)){throw ($phase+' worker did not close normally')}
 Check ($phase+'-normal-exit') ($worker.ExitCode -eq 0) ([ordered]@{WorkerPid=$worker.Id;ExitCode=$worker.ExitCode;Closure='Normal WM_CLOSE on completed real GUI; no normal app launch requested'})
}
try {
 $os=Get-CimInstance Win32_OperatingSystem
 $session=[Diagnostics.Process]::GetCurrentProcess().SessionId
 $report.Environment=[ordered]@{Caption=$os.Caption;Version=$os.Version;Build=$os.BuildNumber;Architecture=$env:PROCESSOR_ARCHITECTURE;ImageOS=$env:ImageOS;ImageVersion=$env:ImageVersion;SessionId=$session;User=[Environment]::UserName}
 Check 'windows11-interactive-runner' ($os.Caption -match 'Windows 11' -and $session -gt 0 -and [Environment]::Is64BitOperatingSystem) $report.Environment
 $provenance=Get-Content -LiteralPath (Join-Path $inputRoot 'installer-provenance.json') -Raw -Encoding UTF8|ConvertFrom-Json
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
 Add-Type -AssemblyName System.Drawing,System.Management
 Add-Type -Path (Join-Path $PSScriptRoot 'SetupUi.cs') -ReferencedAssemblies System.dll,System.Drawing.dll,System.Management.dll
 $trace=New-Object AppTrace
 New-Item -ItemType Directory -Path $target|Out-Null
 $fixture=[ordered]@{UiLanguage='es';Enabled=$true;DrawingToolCycle=@('Stylo','Rectangle');AnnotationTool='Rectangle';AnnotationColor='#137AC4';AnnotationThickness=9;DrawSmoothMode='Formes assistées';DrawSmoothStrength=65;ButtonX1='Outil de dessin suivant';ButtonX2='Outil de dessin précédent';LongPressMs=550;RingRadius=44;LaunchAtLogin=$false}
 $fixture|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $settings -Encoding UTF8
 [IO.File]::WriteAllText(($settings+'.bak'),'{"UiLanguage":"en","DrawingToolCycle":["Stylo"],"LaunchAtLogin":false}',(New-Object Text.UTF8Encoding($false)))
 [IO.File]::WriteAllText((Join-Path $target 'fixture-note.txt'),'Synthetic preservation sentinel; no personal data.',(New-Object Text.UTF8Encoding($false)))
 $before=Profile-Map; $report.ProfileBefore=$before
 Copy-Item -LiteralPath $settings -Destination (Join-Path $out 'profile-before.json')
 $report.UiAutomation='Targeted external mouse messages to the verified setup window; normal WM_CLOSE after completion. Not physical-desktop click validation. No interaction with the Microsoft account window.'
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
 Check 'windows-uninstall-registration' ($registration.DisplayVersion -ceq '2.7.1-preview.9' -and $registration.InstallLocation -eq $target -and $registration.UninstallString -eq $expectedUninstall) ([ordered]@{DisplayVersion=$registration.DisplayVersion;InstallLocation=$registration.InstallLocation;UninstallString=$registration.UninstallString})
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
 Wait-Until 'automatic selftest process exit event' { @($trace.Snapshot()|Where-Object {$_.Kind -eq 'stop'}).Count -ge 1 } 15
 $starts=@($trace.Snapshot()|Where-Object {$_.Kind -eq 'start'})
 $stops=@($trace.Snapshot()|Where-Object {$_.Kind -eq 'stop'})
 $selfOk=$starts.Count -eq 1 -and $stops.Count -eq 1
 if($selfOk){$selfOk=$starts[0].ParentPid -eq $report.installWorker.Pid -and $stops[0].Pid -eq $starts[0].Pid -and $stops[0].ExitStatus -eq 0}
 if($selfOk -and ![string]::IsNullOrEmpty($starts[0].CommandLine)){$selfOk=$starts[0].CommandLine -match '(?:^|\s)--selftest(?:\s|$)'}
 Check 'automatic-installation-selftest' $selfOk ([ordered]@{Events=@($trace.Snapshot());Meaning='Only application invocation allowed by installer source before commit; WMI records observed child start and zero exit. No separate selftest invoked.'})
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
 Check 'no-extra-app-execution' (@($trace.Snapshot()|Where-Object {$_.Kind -eq 'start'}).Count -eq 1) @($trace.Snapshot())
 Check 'original-artifact-unchanged' ((Hash $setup) -ceq $expectedSetup -and (Hash (Join-Path $inputRoot 'payload\Haloryva.exe')) -ceq $expectedApp) ([ordered]@{Setup=Hash $setup;Application=Hash (Join-Path $inputRoot 'payload\Haloryva.exe')})
 Copy-Item -LiteralPath $settings -Destination (Join-Path $out 'profile-after-uninstall.json')
 $report.MaintenanceFilesRemaining=@(Get-ChildItem -LiteralPath $maintenance -File -Recurse|ForEach-Object {$_.FullName.Substring($maintenance.Length+1)})
 $report.Limits=@('Synthetic settings fixture, not a real previous 2.7.0 installation','Windows 11 ARM64 with x64 emulation; physical x64 PC not tested','No normal app launch, no reboot/logon, no 195-suite, no MSIX or signing test','Installer maintenance executable in temporary run folder and unlocked operation.lock may remain by design')
 $report.Status='passed';$report.Phase='finished'
} catch {
 $report.Status='failed';$report.Error=$_.Exception.ToString();$report.ErrorPosition=$_.InvocationInfo.PositionMessage
 Write-Host ('FAILURE: '+$report.Error+' '+$report.ErrorPosition)
 try {if($null -ne $worker){$worker.Refresh();if(!$worker.HasExited -and $worker.MainWindowHandle -ne [IntPtr]::Zero){[SetupUi]::Capture($worker.MainWindowHandle,(Join-Path $out 'failure.png'))}}}catch{Write-Host ('Capture failed: '+$_)}
 throw
} finally {
 $report.FinishedUtc=[DateTime]::UtcNow.ToString('o');Save-Report
 Write-Host 'RESULTS_JSON_BEGIN';Write-Host ($report|ConvertTo-Json -Depth 12 -Compress);Write-Host 'RESULTS_JSON_END'
 if($null -ne $trace){$trace.Dispose()}
 if($null -ne $wscript){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($wscript)}
}
