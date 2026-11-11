$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP) { throw 'GitHub-hosted runner required. No local execution.' }
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview3-build'
if(Test-Path -LiteralPath $out) { throw 'Fresh build directory required.' }
New-Item -ItemType Directory -Path $out | Out-Null
$origin=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-origin.json') -Raw | ConvertFrom-Json
foreach($item in $origin.Files) {
    if($item.path -notin @('src/Brand.cs','src/GlobalHooks.cs')) {
        if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $item.path)).Hash -ne $item.sha256) { throw "Unexpected source change: $($item.path)" }
    }
}
$original=Join-Path $PSScriptRoot '../Inputs/msix-layout/Haloryva.exe'
$preview2='F655930368239905C2DAED1948C22ECC9E7256782C4EC2454E539780C0BD0D2E'
if((Get-FileHash -LiteralPath $original).Hash -ne $preview2) { throw 'Preserved preview.2 changed.' }
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$source=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -File -Filter '*.cs' | Sort-Object Name)
$app=Join-Path $out 'Haloryva.exe'
$argsList=@('/nologo','/utf8output','/codepage:65001','/langversion:5','/platform:x64','/optimize+','/target:winexe',('/out:"'+$app+'"'),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Xml.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',('/win32icon:"'+(Join-Path $PSScriptRoot 'assets/Haloryva.ico')+'"'),('/win32manifest:"'+(Join-Path $PSScriptRoot 'assets/app.manifest')+'"'))
foreach($file in $source) { $argsList+='"'+$file.FullName+'"' }
$rsp=Join-Path $out 'Haloryva.rsp'
[IO.File]::WriteAllLines($rsp,$argsList,(New-Object Text.UTF8Encoding($true)))
& $compiler ('@'+$rsp) 2>&1 | Tee-Object -FilePath (Join-Path $out 'compile.log')
if($LASTEXITCODE -ne 0) { throw 'Preview.3 compilation failed.' }
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
if($version -ne '2.7.1-preview.3') { throw "Unexpected built version: $version" }
$hash=(Get-FileHash -LiteralPath $app).Hash
if($hash -eq $preview2) { throw 'New binary unexpectedly equals preview.2.' }
$hash | Set-Content -LiteralPath (Join-Path $out 'Haloryva.sha256') -Encoding ASCII
$files=@(foreach($item in $origin.Files) {[ordered]@{Path=$item.path;SHA256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $item.path)).Hash}})
[ordered]@{Version=$version;SHA256=$hash;Commit=$env:GITHUB_SHA;OriginalPreview2SHA256=$preview2;SourceOrigin=$origin.Origin;SourceFiles=$files;Legacy195Executed=$false;Installed=$false;Signed=$false;ReleasePublished=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $out 'build-provenance.json') -Encoding UTF8
Write-Output "BUILT version=$version sha256=$hash sourceFiles=$($source.Count) unchangedSourceFiles=28 legacy195Executed=false"
