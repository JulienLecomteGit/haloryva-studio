param([ValidateSet('on','off')][string]$TraceMode='off')
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP){throw 'GitHub-hosted only; no local execution'}
$out=Join-Path $env:RUNNER_TEMP ('preview9-live-focus-'+$TraceMode)
if(Test-Path -LiteralPath $out){throw 'Fresh output required'}
New-Item -ItemType Directory -Path $out|Out-Null
$name='HaloryvaStudio.MsixLifecycleTest'
$expected='5A7CF59D6C4638F68474EBCCC9D4FDCEC610638D0EBCD612B2B5828B7411EE87'
$package=Join-Path $env:RUNNER_TEMP 'haloryva-preview9-msix/Haloryva-test-2.7.1.90.msix'
$packageProof=Get-Content -LiteralPath (Join-Path $env:RUNNER_TEMP 'haloryva-preview9-msix/package-provenance.json') -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $package).Hash -cne $packageProof.PackageSHA256 -or $packageProof.ExecutableSHA256 -cne $expected -or $packageProof.BuildCommit -cne '0175bc2fbb1e8b77a099e10d59818580715fa67c'){throw 'Frozen preview.9 package mismatch'}
$script:launchArguments=if($TraceMode -eq 'on'){'--startup-refresh-trace'}else{''}
$registered=$null;$child=$null
$report=[ordered]@{Version='2.7.1-preview.9';Status='started';ExecutableSHA256=$expected;PackageSHA256=(Get-FileHash -LiteralPath $package).Hash;Actual270Installed=$false;FixturesOnly=$true;Scope='Preview.9 first live refocus only';TraceMode=$TraceMode;RealLogonExecuted=$false;ApplicationRelaunched=$false;ActivationMessagesInjected=$false;RegistryWrittenByDriver=$false;Rebuilt=$false;Signed=$false;ReleasePublished=$false;Historical195Executed=$false;AcquiredSuitesRerun=$false;Checks=@();Dialogs=@()}
Start-Transcript -Path (Join-Path $out 'transcript.txt')
Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $PSScriptRoot '../preview8-windows-startup/Native.cs') -Raw -Encoding UTF8)
Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $PSScriptRoot '../preview8-live-focus/RootFocus.cs') -Raw -Encoding UTF8)
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
 $appPid=[ActivatePackaged]::Launch(($script:registered.PackageFamilyName+'!Haloryva'),$script:launchArguments)
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
 Assert ([RootFocus]::Confirm($studio) -and $report.LiveControlDisabled -and (Read-State) -eq 1) 'live-refocus-refreshes-disabled-user-choice'
 Assert ($child.Id -eq $originalPid -and !$child.HasExited -and $child.StartTime.ToUniversalTime().ToString('o') -ceq $originalStart) 'same-process-no-relaunch'
 $afterProfile=(Get-FileHash -LiteralPath $targetFile).Hash
 $report.ProfileAfterRefocus=@{SHA256=$afterProfile;LaunchAtLogin=(Profile).LaunchAtLogin}
 Assert ($afterProfile -ceq $beforeProfile -and (Profile).LaunchAtLogin -eq $true) 'refocus-read-only-profile-and-no-reactivation'
 $traceFile=Join-Path $target 'Logs/startup-refresh-recipe.tsv'
 $report.DiagnosticTrace=@{Requested=($TraceMode -eq 'on');Exists=(Test-Path -LiteralPath $traceFile)}
 if($TraceMode -eq 'on'){
  if(!(Test-Path -LiteralPath $traceFile)){throw 'Requested diagnostic trace missing'}
  $trace=Get-Content -LiteralPath $traceFile -Raw -Encoding UTF8
  Assert ($trace -match 'read_publish\t\d+\t1') 'trace-confirms-real-disabled-by-user-read'
  Start-Sleep -Milliseconds 3200
  $stableTrace=(Get-FileHash -LiteralPath $traceFile).Hash
  Start-Sleep -Milliseconds 1100
  Assert ((Get-FileHash -LiteralPath $traceFile).Hash -ceq $stableTrace) 'bounded-refresh-stops-without-permanent-polling'
  $report.DiagnosticTrace.Text=Get-Content -LiteralPath $traceFile -Raw -Encoding UTF8
 }else{
  Assert (!(Test-Path -LiteralPath $traceFile)) 'normal-mode-no-diagnostic-file'
 }
 [PackageProbe]::Windows($child.Id)|Set-Content -LiteralPath (Join-Path $out 'studio-after-refocus.txt') -Encoding UTF8
 [SettingsUi]::Close($settingsRoot);$settingsRoot=$null
 Finish
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
 $report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
 $report|ConvertTo-Json -Depth 10|Write-Output
 Get-ChildItem -LiteralPath $out -Filter '*tree.txt'|ForEach-Object {Get-Content -LiteralPath $_.FullName}
 Stop-Transcript
}
