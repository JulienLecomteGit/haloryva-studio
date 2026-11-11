$Observer='off'
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP) { throw 'GitHub-hosted runner required. No local execution.' }
if(![Environment]::UserInteractive -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) { throw 'Interactive test desktop unavailable: NOT RUN.' }
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview3-features'
if(Test-Path -LiteralPath $out) { throw 'Fresh test directory required.' }
New-Item -ItemType Directory -Path $out | Out-Null
$report=[ordered]@{Version='2.7.1-preview.3';Observer=$Observer;Status='started';Legacy195Executed=$false;Installed=$false;Signed=$false;ReleasePublished=$false}
$child=$null
$run='cloud-'+$env:GITHUB_RUN_ID+'-'+$env:RUNNER_ARCH+'-'+$Observer
Start-Transcript -Path (Join-Path $out 'transcript.txt')
try {
    $os=Get-CimInstance Win32_OperatingSystem
    [ordered]@{Caption=$os.Caption;Build=$os.BuildNumber;RunnerArch=$env:RUNNER_ARCH;Interactive=[Environment]::UserInteractive;Session=[Diagnostics.Process]::GetCurrentProcess().SessionId;Image=$env:ImageVersion} | ConvertTo-Json | Set-Content (Join-Path $out 'environment.json')
    $build=Join-Path $env:RUNNER_TEMP 'haloryva-preview3-build'
    $expected=(Get-Content -LiteralPath (Join-Path $build 'Haloryva.sha256') -Raw).Trim()
    $app=Join-Path $build 'Haloryva.exe'
    if((Get-FileHash -LiteralPath $app).Hash -ne $expected) { throw 'Downloaded preview.3 SHA256 mismatch.' }
    if($expected -ne '4170BEFEC193F5049051D73744CFCAD6D300240CE840E791E2C469A7CECAB13C') { throw 'This recipe requires the preserved final preview.3 artifact.' }
    $report.SHA256=$expected
    $report.OriginalArtifactRun=42931395281
    $report.Rebuilt=$false
    Copy-Item -LiteralPath $app -Destination $out
    Copy-Item -LiteralPath (Join-Path $build 'build-provenance.json') -Destination $out
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $rsp=Join-Path $out 'Recipe.rsp'
    $compilerArgs=@('/nologo','/utf8output','/codepage:65001','/target:exe','/platform:x64','/langversion:5','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',('/out:"'+(Join-Path $out 'Recipe.exe')+'"'),('"'+(Join-Path $PSScriptRoot 'Recipe-Features.cs')+'"'))
    [IO.File]::WriteAllLines($rsp,$compilerArgs,(New-Object Text.UTF8Encoding($true)))
    & $compiler ('@'+$rsp) 2>&1 | Out-File (Join-Path $out 'compile.log')
    if($LASTEXITCODE -ne 0) { throw 'Test driver compilation failed.' }
    $child=Start-Process -FilePath (Join-Path $out 'Recipe.exe') -ArgumentList ('features '+$run+' off --silent') -PassThru -WindowStyle Hidden
    if(!$child.WaitForExit(90000)) { $child.Kill(); throw 'Targeted driver timed out: NOT validated.' }
    if($child.ExitCode -ne 0) { throw "Targeted driver failed with exit code $($child.ExitCode)." }
    if((Get-FileHash -LiteralPath (Join-Path $out 'Haloryva.exe')).Hash -ne $expected) { throw 'Tested binary changed.' }
    $report.Status='altgr-drawing-stroke-settings-passed'
} catch {
    $report.Status='failed-or-environment-blocked'
    $report.Error=$_.Exception.Message
    throw
} finally {
    if($child -and !$child.HasExited) { $child.Kill() }
    $logs=Join-Path $env:LOCALAPPDATA ('RecipeHaloryvaRuns/'+$run)
    if(Test-Path -LiteralPath $logs) { Copy-Item -LiteralPath $logs -Destination $out -Recurse }
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'result.json')
    Get-ChildItem -LiteralPath $out -Recurse -File | Where-Object {$_.Name -in @('recipe.log','target-trace.log','compile.log','result.json','environment.json')} | ForEach-Object {
        Write-Output ('EVIDENCE '+$_.Name)
        Get-Content -LiteralPath $_.FullName
    }
    Stop-Transcript
}
