$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP){throw 'GitHub-hosted only; no local execution'}
$out=Join-Path $env:RUNNER_TEMP 'preview8-windows-startup'
if(Test-Path -LiteralPath $out){throw 'Fresh output required'}
New-Item -ItemType Directory -Path $out|Out-Null
$name='HaloryvaStudio.MsixLifecycleTest'
$expected='CFB5322F128EBAA7D1BBC6A4F8892CC853C9B6B9ED7F78DBDC4C2FB3B0CDA512'
$package=Join-Path $env:RUNNER_TEMP 'preview8-frozen/Haloryva-test-2.7.1.80.msix'
if((Get-FileHash -LiteralPath $package).Hash -cne 'D07978FB383F1CD069DDAE53C49771E9ACFA4D47700FF262374F3BCF3EAD34A9'){throw 'Frozen package mismatch'}
$registered=$null;$child=$null
$report=[ordered]@{Version='2.7.1-preview.8';Status='started';ExecutableSHA256=$expected;PackageSHA256=(Get-FileHash -LiteralPath $package).Hash;Actual270Installed=$false;FixturesOnly=$true;Rebuilt=$false;Signed=$false;ReleasePublished=$false;Historical195Executed=$false;AcquiredSuitesRerun=$false;Checks=@();Dialogs=@()}
Start-Transcript -Path (Join-Path $out 'transcript.txt')
Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Native.cs') -Raw -Encoding UTF8)
function Assert([bool]$condition,[string]$id){
 $report.Checks+=,@{Id=$id;Passed=$condition}
 if(!$condition){throw "Assertion: $id"}
}
function Read-State {
 $key='HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\'+$script:registered.PackageFamilyName+'\HaloryvaStudioStartup'
 if(!(Test-Path -LiteralPath $key)){return -1}
 return [int](Get-ItemProperty -LiteralPath $key).State
}
function Launch {
 $appPid=[ActivatePackaged]::Launch(($script:registered.PackageFamilyName+'!Haloryva'),'')
 $script:child=Get-Process -Id $appPid
 [uint32]$size=512;$identity=New-Object Text.StringBuilder 512
 if([PackageProbe]::GetPackageFullName($script:child.Handle,[ref]$size,$identity) -ne 0 -or $identity.ToString() -cne $script:registered.PackageFullName){throw 'Runtime package identity differs'}
 $script:eventName='Local\HaloryvaStudio.Exit.'+$appPid
}
function Ready {
 for($i=0;$i -lt 120 -and [PackageProbe]::ExitEvent($script:eventName,$false) -ne 0;$i++){Start-Sleep -Milliseconds 250}
 if([PackageProbe]::ExitEvent($script:eventName,$false) -ne 0){throw 'Studio not ready'}
 Start-Sleep -Milliseconds 1200
}
function Control([string]$label){
 $h=[UiProbe]::Find($script:child.Id,$label)
 if($h -eq [IntPtr]::Zero){throw "Control absent: $label"}
 return $h
}
function Command([string]$label){
 [UiProbe]::Click((Control $label))
 Start-Sleep -Milliseconds 350
}
function Preferences {Command 'Préférences';Start-Sleep -Milliseconds 300}
function Dialog([string]$fragment,[int]$answer,[string]$stage) {
 $dialog=[IntPtr]::Zero
 for($i=0;$i -lt 80;$i++){
  $dialog=[DialogProbe]::Find($script:child.Id)
  if($dialog -ne [IntPtr]::Zero -and [DialogProbe]::Content($dialog).Contains($fragment)){break}
  Start-Sleep -Milliseconds 250
 }
 if($dialog -eq [IntPtr]::Zero -or ![DialogProbe]::Content($dialog).Contains($fragment)){throw "Expected consent absent: $stage"}
 $report.Dialogs+=,@{Stage=$stage;Text=[DialogProbe]::Content($dialog);AnswerId=$answer;RealWin32Dialog=$true}
 [DialogProbe]::Answer($dialog,$answer)
 Start-Sleep -Milliseconds 600
}
function Profile {
 if(!(Test-Path -LiteralPath $script:targetFile)){return $null}
 return Get-Content -LiteralPath $script:targetFile -Raw -Encoding UTF8|ConvertFrom-Json
}
function Finish {
 if([PackageProbe]::ExitEvent($script:eventName,$true) -ne 0 -or !$script:child.WaitForExit(15000) -or $script:child.ExitCode -ne 0){throw 'Normal exit failed'}
 $script:child=$null
}

$settingsRoot=$null
try {
 $os=Get-CimInstance Win32_OperatingSystem
 if($os.Caption -notmatch 'Windows 11'){throw 'Windows 11 required'}
 $report.Environment=@{OS=$os.Caption;Build=$os.BuildNumber;Architecture=$env:RUNNER_ARCH;Image=$env:ImageVersion}
 if(Get-AppxPackage -Name $name){throw 'Existing test identity'}
 if(Get-Process SystemSettings -ErrorAction SilentlyContinue){throw 'Settings already open; require fresh runner'}
 $wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
 $references=@('System.dll','System.Core.dll',(Join-Path $wpf 'UIAutomationClient.dll'),(Join-Path $wpf 'UIAutomationTypes.dll'),(Join-Path $wpf 'WindowsBase.dll'))
 Add-Type -ReferencedAssemblies $references -TypeDefinition (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SettingsUi.cs') -Raw -Encoding UTF8)
 Add-AppxPackage -Path $package -AllowUnsigned
 $registered=Get-AppxPackage -Name $name
 Assert ((Get-FileHash -LiteralPath (Join-Path $registered.InstallLocation 'Haloryva.exe')).Hash -ceq $expected) 'frozen-installed-exe'
 $target=Join-Path $env:LOCALAPPDATA ('Packages\'+$registered.PackageFamilyName+'\LocalState\Haloryva')
 $targetFile=Join-Path $target 'settings.json'
 Launch;Ready;Preferences
 # Enable once only as the prerequisite for disabling from Windows; do not repeat the old S2 suite.
 Command 'Démarrage avec Windows'
 for($i=0;$i -lt 80 -and (Read-State) -ne 2;$i++){Start-Sleep -Milliseconds 250}
 if((Read-State) -ne 2){throw 'Setup: startup not enabled'}
 $report.Setup=@{StartupEnabledForWindowsDisable=$true;PriorS2SuiteRerun=$false}
 Start-Process -FilePath explorer.exe -ArgumentList 'ms-settings:startupapps' -WindowStyle Hidden
 for($i=0;$i -lt 100;$i++){$settingsRoot=[SettingsUi]::Root();if($settingsRoot){break};Start-Sleep -Milliseconds 500}
 if(!$settingsRoot){throw 'Windows Startup settings unavailable; no fallback registry edit'}
 $report.WindowsSettingsOpened=$true
 $found=$null
 for($i=0;$i -lt 40;$i++){
  try{$found=[SettingsUi]::Target($settingsRoot,'Haloryva');break}catch{Start-Sleep -Milliseconds 500}
 }
 [SettingsUi]::Dump($settingsRoot)|Set-Content -LiteralPath (Join-Path $out 'windows-startup-before.txt') -Encoding UTF8
 if(!$found){throw 'Unique Haloryva toggle unavailable; no unrelated startup item changed'}
 $report.WindowsControlBefore=[SettingsUi]::Describe($found)
 Assert ([SettingsUi]::State($found) -eq 'On') 'windows-toggle-on-before-action'
 [SettingsUi]::Disable($found)
 for($i=0;$i -lt 80 -and (Read-State) -ne 1;$i++){Start-Sleep -Milliseconds 250}
 $report.WindowsControlAfter=[SettingsUi]::Describe($found)
 $report.StateAfterWindowsAction=Read-State
 Assert ([SettingsUi]::State($found) -eq 'Off' -and (Read-State) -eq 1) 'windows-disabled-by-user-real-state'
 [SettingsUi]::Dump($settingsRoot)|Set-Content -LiteralPath (Join-Path $out 'windows-startup-after.txt') -Encoding UTF8
 [SettingsUi]::Close($settingsRoot);$settingsRoot=$null
 $studio=Control 'Haloryva Studio'
 $focusConfirmed=$false
 for($i=0;$i -lt 30;$i++){
  try{[SettingsUi]::Focus($studio)}catch{}
  [void][FocusProbe]::Focus($studio)
  if([FocusProbe]::GetForegroundWindow() -eq $studio){$focusConfirmed=$true;break}
  Start-Sleep -Milliseconds 200
 }
 $report.LiveFocusConfirmed=$focusConfirmed
 $toggle=Control 'Démarrage avec Windows'
 for($i=0;$i -lt 40 -and [UiProbe]::IsWindowEnabled($toggle);$i++){Start-Sleep -Milliseconds 250}
 $report.LiveControlDisabled=![UiProbe]::IsWindowEnabled($toggle)
 $report.LiveState=Read-State
 if($focusConfirmed){Assert ($report.LiveControlDisabled -and (Read-State) -eq 1) 'live-refocus-respects-windows-user-choice'}
 $report.StoredLaunchAtLoginBeforeRestart=(Profile).LaunchAtLogin
 Finish
 Launch;Ready;Preferences
 Assert (![UiProbe]::IsWindowEnabled((Control 'Démarrage avec Windows')) -and (Read-State) -eq 1) 'relaunch-respects-disabled-by-user'
 Assert ((Profile).LaunchAtLogin -eq $true) 'historical-json-does-not-override-windows'
 Finish
 $report.Status='windows-startup-disable-passed'
}catch{$report.Status='failed';$report.Error=$_.ToString();throw}
finally{
 if($settingsRoot){try{[SettingsUi]::Dump($settingsRoot)|Set-Content -LiteralPath (Join-Path $out 'settings-final-tree.txt') -Encoding UTF8;[SettingsUi]::Close($settingsRoot)}catch{}}
 if($child -and !$child.HasExited){
  try{[void][PackageProbe]::ExitEvent($eventName,$true);[void]$child.WaitForExit(15000);if($child.HasExited){$child=$null}}catch{}
 }
 if($registered){
  $data=Join-Path $env:LOCALAPPDATA ('Packages\'+$registered.PackageFamilyName+'\LocalState\Haloryva')
  if(Test-Path -LiteralPath $data){Copy-Item -LiteralPath $data -Destination (Join-Path $out 'synthetic-msix-data') -Recurse}
  if(!$child){try{Remove-AppxPackage -Package $registered.PackageFullName -ErrorAction Stop;$report.TestPackageRemoved=$true}catch{$report.CleanupError=$_.ToString()}}
 }
 $report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
 $report|ConvertTo-Json -Depth 10|Write-Output
 Get-ChildItem -LiteralPath $out -Filter '*tree.txt'|ForEach-Object {Get-Content -LiteralPath $_.FullName}
 Stop-Transcript
}
