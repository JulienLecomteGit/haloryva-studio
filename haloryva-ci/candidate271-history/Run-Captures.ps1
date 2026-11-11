param([Parameter(Mandatory=$true)][ValidateSet('fr','en','es')][string]$Language)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP) { throw 'GitHub-hosted runner required. No local execution.' }
if(![Environment]::UserInteractive -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) { throw 'Interactive desktop unavailable: NOT RUN.' }
$out=Join-Path $env:RUNNER_TEMP ('haloryva-candidate271-history-captures/'+$Language)
if(Test-Path -LiteralPath $out) { throw 'Fresh capture directory required.' }
New-Item -ItemType Directory -Path $out | Out-Null
$report=[ordered]@{Version='2.7.1';Language=$Language;Status='started';Historical195Executed=$false;SelfTestsExecuted=$false;Installed=$false;Signed=$false;Published=$false;RebuiltInCapture=$false}
$child=$null
try {
 $os=Get-CimInstance Win32_OperatingSystem
 [ordered]@{Caption=$os.Caption;Build=$os.BuildNumber;RunnerArch=$env:RUNNER_ARCH;Interactive=[Environment]::UserInteractive;Session=[Diagnostics.Process]::GetCurrentProcess().SessionId;Image=$env:ImageVersion;ScreenRequested='1024x768'}|ConvertTo-Json|Set-Content (Join-Path $out 'environment.json')
 $build=Join-Path $env:RUNNER_TEMP 'haloryva-candidate271-history-build'
 $provenance=Get-Content -LiteralPath (Join-Path $build 'build-provenance.json') -Raw|ConvertFrom-Json
 $expected=(Get-Content -LiteralPath (Join-Path $build 'Haloryva.sha256') -Raw).Trim()
 $app=Join-Path $build 'Haloryva.exe'
 if($provenance.Commit -ne $env:GITHUB_SHA -or $provenance.Version -ne '2.7.1' -or $provenance.FileVersion -ne '2.7.1.11' -or $provenance.SHA256 -ne $expected -or (Get-FileHash -LiteralPath $app).Hash -ne $expected) {throw 'Candidate provenance or SHA256 mismatch'}
 Copy-Item -LiteralPath $app -Destination $out
 Copy-Item -LiteralPath (Join-Path $build 'build-provenance.json') -Destination $out
 $report.SHA256=$expected;$report.BuildDate=$provenance.BuildDate
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 $rsp=Join-Path $out 'Capture.rsp'
 $compilerArgs=@('/nologo','/utf8output','/codepage:65001','/target:exe','/platform:x64','/langversion:5','/reference:System.dll','/reference:System.Core.dll','/reference:System.Web.Extensions.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',('/out:"'+(Join-Path $out 'Capture.exe')+'"'),('"'+(Join-Path $PSScriptRoot 'Capture-Candidate.cs')+'"'))
 [IO.File]::WriteAllLines($rsp,$compilerArgs,(New-Object Text.UTF8Encoding($true)))
 & $compiler ('@'+$rsp) 2>&1|Tee-Object -FilePath (Join-Path $out 'compile-capture.log')
 if($LASTEXITCODE -ne 0) {throw 'Capture driver compilation failed'}
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName=Join-Path $out 'Capture.exe'
 $start.WorkingDirectory=$out;$start.Arguments=$Language;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $child=[Diagnostics.Process]::Start($start)
 $heldHandle=$child.Handle
 if(!$child.WaitForExit(90000)) {$child.Kill();throw 'Capture timed out: not validated'}
 $child.WaitForExit()
 $report.ExitCode=$child.ExitCode
 $captureResult=Get-Content -LiteralPath (Join-Path $out 'capture-result.json') -Raw|ConvertFrom-Json
 if($child.ExitCode -ne 0 -or $captureResult.Status -ne 'passed') {throw "Capture failed: exit $($child.ExitCode), status $($captureResult.Status)"}
 $wanted=2
 if(@($captureResult.Captures).Count -ne $wanted) {throw 'Requested screenshots missing'}
 if((Get-FileHash -LiteralPath (Join-Path $out 'Haloryva.exe')).Hash -ne $expected) {throw 'Candidate binary changed during capture'}
 $report.Status='passed';$report.Captures=$captureResult.Captures
 $report.CaptureHashes=@(Get-ChildItem -LiteralPath $out -Filter '*.png'|ForEach-Object {@{File=$_.Name;SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
} catch {
 $report.Status='failed-or-environment-blocked';$report.Error=$_.Exception.Message
 throw
} finally {
 if($child) {if(!$child.HasExited){$child.Kill()};$child.Dispose()}
 $report|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
 foreach($name in @('environment.json','capture.log','capture-result.json','result.json')) {
  $path=Join-Path $out $name
  if(Test-Path -LiteralPath $path){Write-Output ('EVIDENCE '+$name);Get-Content -LiteralPath $path -Encoding UTF8}
 }
}
