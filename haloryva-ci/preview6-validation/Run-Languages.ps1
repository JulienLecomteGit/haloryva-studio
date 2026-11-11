param([ValidateSet('fr','en','es')][string]$Language='fr',[ValidateSet('off','on')][string]$Observer='off')
$Suite='language'
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP) { throw 'GitHub-hosted runner required. No local execution.' }
if(![Environment]::UserInteractive -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) { throw 'Interactive test desktop unavailable: NOT RUN.' }
$out=Join-Path $env:RUNNER_TEMP ('haloryva-preview6-language/'+$Language+'-'+$Observer)
if(Test-Path -LiteralPath $out) { throw 'Fresh test directory required.' }
New-Item -ItemType Directory -Path $out | Out-Null
$report=[ordered]@{Version='2.7.1-preview.6';Suite=$Suite;Observer=$Observer;Language=$Language;Status='started';Legacy195Executed=$false;Installed=$false;Signed=$false;ReleasePublished=$false}
$child=$null
$run='cloud-'+$env:GITHUB_RUN_ID+'-'+$env:RUNNER_ARCH+'-'+$Observer+'-'+$Language+'-'+$Suite
Start-Transcript -Path (Join-Path $out 'transcript.txt')
try {
    $os=Get-CimInstance Win32_OperatingSystem
    [ordered]@{Caption=$os.Caption;Build=$os.BuildNumber;RunnerArch=$env:RUNNER_ARCH;Interactive=[Environment]::UserInteractive;Session=[Diagnostics.Process]::GetCurrentProcess().SessionId;Image=$env:ImageVersion} | ConvertTo-Json | Set-Content (Join-Path $out 'environment.json')
    $build=Join-Path $env:RUNNER_TEMP 'haloryva-preview6-build'
    $expected=(Get-Content -LiteralPath (Join-Path $build 'Haloryva.sha256') -Raw).Trim()
    $app=Join-Path $build 'Haloryva.exe'
    if((Get-FileHash -LiteralPath $app).Hash -ne $expected) { throw 'Downloaded preview.6 SHA256 mismatch.' }
    if($expected -eq '2AB807BB6E01ECE73AE6010854D0A69C74192AF1D48A51D05D7EE155A2E4060C' -or $expected -eq '4170BEFEC193F5049051D73744CFCAD6D300240CE840E791E2C469A7CECAB13C') { throw 'Preview.6 must differ from preserved previews.' }
    $provenance=Get-Content -LiteralPath (Join-Path $build 'build-provenance.json') -Raw | ConvertFrom-Json
    if($provenance.Version -ne '2.7.1-preview.6' -or $provenance.SHA256 -ne $expected -or $provenance.Commit -ne '92d9887198b4efd932639265912030b9238c2a57') { throw 'Preview.6 provenance mismatch.' }
    if($expected -ne '9978501F8E6B010BAC2739622E7F6E7C5DF6FBBBC19C8FECD76E59E3369A66F8') { throw 'Expected frozen preview.6 build.' }
    $report.SHA256=$expected
    $report.BuildRun=42951517482
    $report.RebuiltInTest=$false
    Copy-Item -LiteralPath $app -Destination $out
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../preuves-preview5-defilement-20261111/feature-off/settings.json') -Destination (Join-Path $out 'legacy-profile.json')
    Copy-Item -LiteralPath (Join-Path $build 'build-provenance.json') -Destination $out
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $rsp=Join-Path $out 'Recipe.rsp'
    $wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
    $compilerArgs=@('/nologo','/utf8output','/codepage:65001','/target:exe','/platform:x64','/langversion:5','/reference:System.dll','/reference:System.Core.dll','/reference:System.Web.Extensions.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',('/reference:"'+(Join-Path $wpf 'UIAutomationClient.dll')+'"'),('/reference:"'+(Join-Path $wpf 'UIAutomationTypes.dll')+'"'),('/reference:"'+(Join-Path $wpf 'WindowsBase.dll')+'"'),('/out:"'+(Join-Path $out 'Recipe.exe')+'"'),('"'+(Join-Path $PSScriptRoot 'Recipe-Languages.cs')+'"'))
    [IO.File]::WriteAllLines($rsp,$compilerArgs,(New-Object Text.UTF8Encoding($true)))
    & $compiler ('@'+$rsp) 2>&1 | Out-File (Join-Path $out 'compile.log')
    if($LASTEXITCODE -ne 0) { throw 'Test driver compilation failed.' }
    $env:HALORYVA_RECIPE_SUITE=$Suite
    $env:HALORYVA_LANGUAGE=$Language
    $nativeProfile=Join-Path $env:LOCALAPPDATA ('RecipeHaloryvaRuns/'+$run+'/profile/settings.json')
    $savedProfile=Join-Path $out 'saved-native-profile.json'
    $report.Phases=@()
    foreach($phase in @('features','reload')) {
        $child=Start-Process -FilePath (Join-Path $out 'Recipe.exe') -ArgumentList ($phase+' '+$run+' '+$Observer+' "'+$savedProfile+'"') -PassThru -WindowStyle Hidden
        if(!$child.WaitForExit(300000)) { $child.Kill(); throw "Targeted phase $phase timed out: NOT validated." }
        $report.Phases+=@{Phase=$phase;ExitCode=$child.ExitCode}
        if($child.ExitCode -ne 0) { throw "Targeted phase $phase failed with exit code $($child.ExitCode)." }
        if($phase -eq 'features') { Copy-Item -LiteralPath $nativeProfile -Destination $savedProfile }
        $child.Dispose()
        $child=$null
    }
    if((Get-FileHash -LiteralPath (Join-Path $out 'Haloryva.exe')).Hash -ne $expected) { throw 'Tested binary changed.' }
    $report.Status=$Suite+'-passed'
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
