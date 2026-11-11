$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP){throw 'GitHub-hosted only; no local execution'}
$out=Join-Path $env:RUNNER_TEMP 'preview8-live-focus'
if(Test-Path -LiteralPath $out){throw 'Fresh output required'}
New-Item -ItemType Directory -Path $out|Out-Null
$name='HaloryvaStudio.MsixLifecycleTest'
$expected='CFB5322F128EBAA7D1BBC6A4F8892CC853C9B6B9ED7F78DBDC4C2FB3B0CDA512'
$package=Join-Path $env:RUNNER_TEMP 'preview8-frozen/Haloryva-test-2.7.1.80.msix'
if((Get-FileHash -LiteralPath $package).Hash -cne 'D07978FB383F1CD069DDAE53C49771E9ACFA4D47700FF262374F3BCF3EAD34A9'){throw 'Frozen package mismatch'}
$registered=$null;$child=$null
$report=[ordered]@{Version='2.7.1-preview.8';Status='started';ExecutableSHA256=$expected;PackageSHA256=(Get-FileHash -LiteralPath $package).Hash;Actual270Installed=$false;FixturesOnly=$true;Scope='S3 live focus only plus read-only S6 feasibility';RealLogonExecuted=$false;ApplicationRelaunched=$false;ActivationMessagesInjected=$false;RegistryWrittenByDriver=$false;Rebuilt=$false;Signed=$false;ReleasePublished=$false;Historical195Executed=$false;AcquiredSuitesRerun=$false;Checks=@();Dialogs=@()}
Start-Transcript -Path (Join-Path $out 'transcript.txt')
Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $PSScriptRoot '../preview8-windows-startup/Native.cs') -Raw -Encoding UTF8)
Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'RootFocus.cs') -Raw -Encoding UTF8)
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
 $self=Get-Process -Id $PID
 $agents=@(Get-Process -Name 'Runner.Worker','Runner.Listener','explorer','winlogon' -ErrorAction SilentlyContinue|Select-Object ProcessName,Id,SessionId)
 $agentServices=@(Get-CimInstance Win32_Service -Filter "Name LIKE 'actions.runner.%'"|Select-Object Name,State,StartMode)
 $report.SessionFeasibility=@{CurrentProcessId=$PID;CurrentSessionId=$self.SessionId;UserInteractive=[Environment]::UserInteractive;Processes=$agents;RunnerServices=$agentServices;CredentialsRead=$false;SessionConfigurationChanged=$false;LogoffOrRebootIssued=$false;ExternalContinuityEstablished=$false}
 $report.SessionFeasibility|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $out 'session-feasibility.json') -Encoding UTF8
 if(Get-AppxPackage -Name $name){throw 'Existing test identity'}
 if(Get-Process SystemSettings -ErrorAction SilentlyContinue){throw 'Settings already open; require fresh runner'}
 $wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
 $references=@('System.dll','System.Core.dll',(Join-Path $wpf 'UIAutomationClient.dll'),(Join-Path $wpf 'UIAutomationTypes.dll'),(Join-Path $wpf 'WindowsBase.dll'))
 Add-Type -ReferencedAssemblies $references -TypeDefinition (Get-Content -LiteralPath (Join-Path $PSScriptRoot '../preview8-windows-startup/SettingsUi.cs') -Raw -Encoding UTF8)
 Add-AppxPackage -Path $package -AllowUnsigned
 $registered=Get-AppxPackage -Name $name
 if((Get-FileHash -LiteralPath (Join-Path $registered.InstallLocation 'Haloryva.exe')).Hash -cne $expected){throw 'Frozen installed executable differs'}
 $target=Join-Path $env:LOCALAPPDATA ('Packages\'+$registered.PackageFamilyName+'\LocalState\Haloryva')
 $targetFile=Join-Path $target 'settings.json'
 Launch;Ready;Preferences
 $originalPid=$child.Id;$originalStart=$child.StartTime.ToUniversalTime().ToString('o')
 $toggle=Control 'Démarrage avec Windows'
 $studio=[RootFocus]::Root($toggle)
 $oldTextTarget=[UiProbe]::Find($child.Id,'Haloryva Studio')
 $report.TargetDiagnostic=@{OldTextLookup=[RootFocus]::Describe($oldTextTarget);StartupControl=[RootFocus]::Describe($toggle);ActualStudioRoot=[RootFocus]::Describe($studio);OldLookupWasRoot=($oldTextTarget -eq $studio)}
 Assert ([RootFocus]::Owner($studio) -eq $child.Id -and $studio -ne $toggle) 'actual-studio-root-identified'
 # This one activation is only the prerequisite for the new live-refocus observation.
 Command 'Démarrage avec Windows'
 for($i=0;$i -lt 80 -and (Read-State) -ne 2;$i++){Start-Sleep -Milliseconds 250}
 if((Read-State) -ne 2){throw 'Setup: startup not enabled'}
 for($i=0;$i -lt 80 -and !(Test-Path -LiteralPath $targetFile);$i++){Start-Sleep -Milliseconds 250}
 if(!(Test-Path -LiteralPath $targetFile) -or (Profile).LaunchAtLogin -ne $true){throw 'Setup: enabled preference absent'}
 Start-Sleep -Milliseconds 1000
 $beforeProfile=(Get-FileHash -LiteralPath $targetFile).Hash
 $report.Setup=@{StartupEnabledForDisable=$true;PriorS2SuiteRerun=$false;PriorRelaunchSuiteRerun=$false;ProfileSHA256=$beforeProfile}
 Start-Process -FilePath explorer.exe -ArgumentList 'ms-settings:startupapps' -WindowStyle Hidden
 for($i=0;$i -lt 100;$i++){$settingsRoot=[SettingsUi]::Root();if($settingsRoot){break};Start-Sleep -Milliseconds 500}
 if(!$settingsRoot){throw 'Windows Startup settings unavailable; no registry fallback'}
 $found=$null
 for($i=0;$i -lt 40;$i++){try{$found=[SettingsUi]::Target($settingsRoot,'Haloryva');break}catch{Start-Sleep -Milliseconds 500}}
 if(!$found -or [SettingsUi]::State($found) -ne 'On'){throw 'Setup: unique enabled test toggle unavailable'}
 $settingsHandle=[RootFocus]::Root([IntPtr]$settingsRoot.Current.NativeWindowHandle)
 $report.FocusTransitions=@()
 $settingsFocused=$false
 for($i=0;$i -lt 15;$i++){
  try{[SettingsUi]::Focus($settingsHandle)}catch{}
  [void][RootFocus]::Activate($settingsHandle)
  if([RootFocus]::Confirm($settingsHandle)){$settingsFocused=$true;break}
  Start-Sleep -Milliseconds 200
 }
 if(!$settingsFocused){throw 'Cannot establish foreground Windows Settings; no fabricated activation'}
 $report.FocusTransitions+=,@{Stage='before-Windows-disable';UTC=[DateTime]::UtcNow.ToString('o');Foreground=[RootFocus]::Describe([RootFocus]::GetForegroundWindow());SettingsConfirmed=$settingsFocused;StudioForeground=[RootFocus]::Confirm($studio)}
 [SettingsUi]::Disable($found)
 for($i=0;$i -lt 80 -and (Read-State) -ne 1;$i++){Start-Sleep -Milliseconds 250}
 $report.BackgroundBeforeReturn=@{Foreground=[RootFocus]::Describe([RootFocus]::GetForegroundWindow());State=Read-State;ToggleStillEnabled=[UiProbe]::IsWindowEnabled($toggle);WindowsToggle=[SettingsUi]::Describe($found)}
 Assert ([RootFocus]::Confirm($settingsHandle) -and ![RootFocus]::Confirm($studio) -and [SettingsUi]::State($found) -eq 'Off' -and (Read-State) -eq 1) 'real-disabled-by-user-while-studio-in-background'
 [SettingsUi]::Dump($settingsRoot)|Set-Content -LiteralPath (Join-Path $out 'windows-before-refocus.txt') -Encoding UTF8
 # Keep Windows Settings open. Require foreground HWND and actual GUI active HWND.
 $focused=$false
 for($i=0;$i -lt 20;$i++){
  try{[SettingsUi]::Focus($studio)}catch{}
  [void][RootFocus]::Activate($studio)
  $confirmed=[RootFocus]::Confirm($studio)
  $report.FocusTransitions+=,@{Stage='return';Attempt=$i;UTC=[DateTime]::UtcNow.ToString('o');Foreground=[RootFocus]::Describe([RootFocus]::GetForegroundWindow());StudioConfirmed=$confirmed}
  if($confirmed){$focused=$true;break}
  Start-Sleep -Milliseconds 200
 }
 $report.LiveFocusConfirmed=$focused
 Assert $focused 'top-level-foreground-and-active-window-confirmed'
 $timer=[Diagnostics.Stopwatch]::StartNew()
 for($i=0;$i -lt 80;$i++){
  $toggle=Control 'Démarrage avec Windows'
  if(![UiProbe]::IsWindowEnabled($toggle)){break}
  Start-Sleep -Milliseconds 100
 }
 $timer.Stop()
 $report.LiveControlDisabled=![UiProbe]::IsWindowEnabled($toggle)
 $report.LiveState=Read-State
 $report.RefreshObservedWithinMilliseconds=$timer.ElapsedMilliseconds
 $report.ForegroundAfterRefresh=[RootFocus]::Describe([RootFocus]::GetForegroundWindow())
 $primaryPass=([RootFocus]::Confirm($studio) -and $report.LiveControlDisabled -and (Read-State) -eq 1)
 $report.Checks+=,@{Id='live-refocus-refreshes-disabled-user-choice';Passed=$primaryPass}
 $report.UIAutomationEnabledAfterFirstReturn=[System.Windows.Automation.AutomationElement]::FromHandle($toggle).Current.IsEnabled
 $report.LaterFocusDiagnostics=@()
 if(!$primaryPass){
  # Failure retained. Additional real foreground transitions distinguish delayed refresh.
  foreach($mode in @('delayed-second-return','minimize-restore')){
   $background=$false
   for($j=0;$j -lt 15;$j++){
    try{[SettingsUi]::Focus($settingsHandle)}catch{}
    [void][RootFocus]::Activate($settingsHandle)
    if([RootFocus]::Confirm($settingsHandle)){$background=$true;break}
    Start-Sleep -Milliseconds 200
   }
   if(!$background){$report.LaterFocusDiagnostics+=,@{Mode=$mode;BackgroundConfirmed=$false;Executed=$false};continue}
   if($mode -eq 'minimize-restore'){[RootFocus]::Minimize($studio)}
   Start-Sleep -Milliseconds 2000
   $again=$false
   for($j=0;$j -lt 15;$j++){
    [void][RootFocus]::Activate($studio)
    if([RootFocus]::Confirm($studio)){$again=$true;break}
    Start-Sleep -Milliseconds 200
   }
   $clock=[Diagnostics.Stopwatch]::StartNew()
   for($j=0;$j -lt 80;$j++){
    $toggle=Control 'Démarrage avec Windows'
    if(![UiProbe]::IsWindowEnabled($toggle)){break}
    Start-Sleep -Milliseconds 100
   }
   $clock.Stop()
   $report.LaterFocusDiagnostics+=,@{Mode=$mode;Executed=$true;BackgroundConfirmed=$background;ForegroundConfirmed=$again;StillForeground=[RootFocus]::Confirm($studio);State=Read-State;ControlDisabled=![UiProbe]::IsWindowEnabled($toggle);UIAutomationEnabled=[System.Windows.Automation.AutomationElement]::FromHandle($toggle).Current.IsEnabled;WaitMilliseconds=$clock.ElapsedMilliseconds;Foreground=[RootFocus]::Describe([RootFocus]::GetForegroundWindow())}
   if(![UiProbe]::IsWindowEnabled($toggle)){break}
  }
 }

 Assert ($child.Id -eq $originalPid -and !$child.HasExited -and $child.StartTime.ToUniversalTime().ToString('o') -ceq $originalStart) 'same-process-no-relaunch'
 $afterProfile=(Get-FileHash -LiteralPath $targetFile).Hash
 $report.ProfileAfterRefocus=@{SHA256=$afterProfile;LaunchAtLogin=(Profile).LaunchAtLogin}
 Assert ($afterProfile -ceq $beforeProfile -and (Profile).LaunchAtLogin -eq $true) 'refocus-read-only-profile-and-no-reactivation'
 [PackageProbe]::Windows($child.Id)|Set-Content -LiteralPath (Join-Path $out 'studio-after-refocus.txt') -Encoding UTF8
 [SettingsUi]::Close($settingsRoot);$settingsRoot=$null
 Finish
 if(!$primaryPass){throw 'Initial confirmed refocus remained stale; later observations are diagnostics, not a pass'}
 $report.Status='live-refocus-passed'

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
 $report.ApplicationErrorLogs=@()
 if($registered){
  $errorFolder=Join-Path $env:LOCALAPPDATA ('Packages\'+$registered.PackageFamilyName+'\LocalState\Haloryva\Logs')
  # Package data was already copied before package removal; inspect that copy if removed.
  if(!(Test-Path -LiteralPath $errorFolder)){$errorFolder=Join-Path $out 'synthetic-msix-data/Logs'}
  if(Test-Path -LiteralPath $errorFolder){
   foreach($entry in @(Get-ChildItem -LiteralPath $errorFolder -Filter '*.log')){
    $report.ApplicationErrorLogs+=,@{Name=$entry.Name;Text=(Get-Content -LiteralPath $entry.FullName -Raw -Encoding UTF8)}
   }
  }
 }
 $report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
 $report|ConvertTo-Json -Depth 10|Write-Output
 Get-ChildItem -LiteralPath $out -Filter '*tree.txt'|ForEach-Object {Get-Content -LiteralPath $_.FullName}
 Stop-Transcript
}
