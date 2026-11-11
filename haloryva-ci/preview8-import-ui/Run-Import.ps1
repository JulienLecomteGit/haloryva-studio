$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP){throw 'GitHub-hosted only; no local execution'}
$out=Join-Path $env:RUNNER_TEMP 'preview8-import-ui'
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

try {
 $os=Get-CimInstance Win32_OperatingSystem
 if($os.Caption -notmatch 'Windows 11'){throw 'Windows 11 required'}
 $report.Environment=@{OS=$os.Caption;Build=$os.BuildNumber;Architecture=$env:RUNNER_ARCH;Image=$env:ImageVersion}
 if(Get-AppxPackage -Name $name){throw 'Existing test identity'}
 $legacy=Join-Path $env:LOCALAPPDATA 'Haloryva'
 if(Test-Path -LiteralPath $legacy){throw 'Existing classic data; refusing test fixture'}
 New-Item -ItemType Directory -Path $legacy|Out-Null
 $assets=Join-Path $out 'synthetic-assets';New-Item -ItemType Directory -Path $assets|Out-Null
 Add-Type -AssemblyName System.Drawing
 $png=Join-Path $assets 'fixture-logo.png';$bmp=New-Object Drawing.Bitmap 8,8
 try{$g=[Drawing.Graphics]::FromImage($bmp);try{$g.Clear([Drawing.Color]::CornflowerBlue)}finally{$g.Dispose()};$bmp.Save($png,[Drawing.Imaging.ImageFormat]::Png)}finally{$bmp.Dispose()}
 $wav=Join-Path $assets 'fixture-silent.wav'
 $memory=New-Object IO.MemoryStream;$writer=New-Object IO.BinaryWriter $memory
 try{
  $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'));$writer.Write([int]196);$writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '));$writer.Write([int]16);$writer.Write([int16]1);$writer.Write([int16]1);$writer.Write([int]8000);$writer.Write([int]16000);$writer.Write([int16]2);$writer.Write([int16]16);$writer.Write([Text.Encoding]::ASCII.GetBytes('data'));$writer.Write([int]160)
  for($i=0;$i -lt 80;$i++){$writer.Write([int16]0)}
  $writer.Flush();[IO.File]::WriteAllBytes($wav,$memory.ToArray())
 }finally{$writer.Dispose();$memory.Dispose()}
 $source=Join-Path $legacy 'settings.json'
 $fixture=[ordered]@{AnnotationThickness=8;LaunchAtLogin=$true;TrailLogoPath=$png;CustomSound=$wav;ActivePreset='fixture';ActivePresetSignature='fixture-only'}
 $json=$fixture|ConvertTo-Json -Compress
 [IO.File]::WriteAllText($source,$json,(New-Object Text.UTF8Encoding($false)))
 $sourceHash=(Get-FileHash -LiteralPath $source).Hash;$pngHash=(Get-FileHash -LiteralPath $png).Hash;$wavHash=(Get-FileHash -LiteralPath $wav).Hash
 $report.Fixture=@{Kind='synthetic-270-format-no-installed-270';SourceSHA256=$sourceHash;PNG=$pngHash;WAV=$wavHash;AbsentNewFields=@('UiLanguage','DrawingToolCycle')}
 Add-AppxPackage -Path $package -AllowUnsigned
 $registered=Get-AppxPackage -Name $name
 Assert ((Get-FileHash -LiteralPath (Join-Path $registered.InstallLocation 'Haloryva.exe')).Hash -ceq $expected) 'frozen-installed-exe'
 $target=Join-Path $env:LOCALAPPDATA ('Packages\'+$registered.PackageFamilyName+'\LocalState\Haloryva')
 $targetFile=Join-Path $target 'settings.json'
 $choice=Join-Path $target 'legacy-import-choice.txt'
 Launch
 Dialog 'Copier les réglages 2.7.0' 2 'initial-cancel'
 Ready
 Assert ((Get-Content -LiteralPath $choice -Raw).Trim() -eq 'pending') 'cancel-keeps-pending'
 Assert (!(Test-Path -LiteralPath (Join-Path $target 'ImportedAssets'))) 'cancel-no-asset-copy'
 Assert ((Get-FileHash -LiteralPath $source).Hash -ceq $sourceHash) 'cancel-original-unchanged'
 Preferences
 Command 'Reprendre mes réglages 2.7.0'
 Dialog 'Copier les réglages 2.7.0' 7 'explicit-refuse'
 Assert ((Get-Content -LiteralPath $choice -Raw).Trim() -eq 'defaults') 'refusal-remembered'
 Assert (!(Test-Path -LiteralPath (Join-Path $target 'ImportedAssets'))) 'refusal-no-asset-copy'
 Assert ((Read-State) -eq 0) 'refusal-no-startup-change'
 Command 'Reprendre mes réglages 2.7.0'
 Dialog 'Copier les réglages 2.7.0' 6 'explicit-consent'
 for($i=0;$i -lt 40 -and !(Test-Path -LiteralPath $targetFile);$i++){Start-Sleep -Milliseconds 250}
 $cfg=Profile
 Assert ($null -ne $cfg -and $cfg.AnnotationThickness -eq 8 -and $cfg.LaunchAtLogin) 'accepted-fixture-settings'
 Assert ($cfg.UiLanguage -eq 'fr' -and @($cfg.DrawingToolCycle).Count -eq 10) 'new-fields-defaults'
 Assert ($cfg.TrailLogoPath.StartsWith((Join-Path $target 'ImportedAssets'),[StringComparison]::OrdinalIgnoreCase) -and $cfg.CustomSound.StartsWith((Join-Path $target 'ImportedAssets'),[StringComparison]::OrdinalIgnoreCase)) 'independent-asset-paths'
 Assert ((Get-FileHash -LiteralPath $cfg.TrailLogoPath).Hash -ceq $pngHash -and (Get-FileHash -LiteralPath $cfg.CustomSound).Hash -ceq $wavHash) 'png-and-wav-copied-exactly'
 Assert ($cfg.ActivePreset -eq '' -and $cfg.ActivePresetSignature -eq '') 'preset-cleared-for-new-paths'
 Assert ((Get-FileHash -LiteralPath $source).Hash -ceq $sourceHash -and (Get-FileHash -LiteralPath $png).Hash -ceq $pngHash -and (Get-FileHash -LiteralPath $wav).Hash -ceq $wavHash) 'all-source-bytes-unchanged'
 Assert ((Get-FileHash -LiteralPath (Join-Path $target ('migration-source-'+$sourceHash+'.json'))).Hash -ceq $sourceHash) 'raw-recovery-copy'
 Assert ((Read-State) -eq 0) 'import-true-does-not-enable-windows'
 # Refuse a replacement after a successful import.
 $before=(Get-FileHash -LiteralPath $targetFile).Hash
 Command 'Reprendre mes réglages 2.7.0'
 Dialog 'Les réglages actuels seront sauvegardés' 2 'cancel-existing-profile-replacement'
 Assert ((Get-FileHash -LiteralPath $targetFile).Hash -ceq $before) 'cancel-preserves-existing-profile'
 $report.ImportedSummary=@{Language=$cfg.UiLanguage;Tools=@($cfg.DrawingToolCycle);Thickness=$cfg.AnnotationThickness;LaunchAtLoginStored=$cfg.LaunchAtLogin;WindowsStartupState=(Read-State);ProfileSHA256=$before;PNGTargetSHA256=(Get-FileHash -LiteralPath $cfg.TrailLogoPath).Hash;WAVTargetSHA256=(Get-FileHash -LiteralPath $cfg.CustomSound).Hash}
 Finish
 Assert ((Get-FileHash -LiteralPath $source).Hash -ceq $sourceHash) 'source-unchanged-after-normal-exit'
 $report.Status='import-ui-passed'
}catch{$report.Status='failed';$report.Error=$_.ToString();throw}
finally{
 if($child -and !$child.HasExited){
  try{$d=[DialogProbe]::Find($child.Id);if($d -ne [IntPtr]::Zero){[DialogProbe]::Answer($d,2)}}catch{}
  try{[void][PackageProbe]::ExitEvent($eventName,$true);[void]$child.WaitForExit(15000);if($child.HasExited){$child=$null}}catch{}
 }
 if($registered){
  $data=Join-Path $env:LOCALAPPDATA ('Packages\'+$registered.PackageFamilyName+'\LocalState\Haloryva')
  if(Test-Path -LiteralPath $data){Copy-Item -LiteralPath $data -Destination (Join-Path $out 'synthetic-msix-data') -Recurse}
  if(!$child){try{Remove-AppxPackage -Package $registered.PackageFullName -ErrorAction Stop;$report.TestPackageRemoved=$true}catch{$report.CleanupError=$_.ToString()}}
 }
 $report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
 $report|ConvertTo-Json -Depth 10|Write-Output
 Stop-Transcript
}
