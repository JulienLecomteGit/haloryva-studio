$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'GitHub-hosted runner required. No local execution.' }
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview9-build'
if(Test-Path -LiteralPath $out) { throw 'Fresh build directory required' }
New-Item -ItemType Directory -Path $out | Out-Null
$pf86=[Environment]::GetFolderPath('ProgramFilesX86')
$sdkRoot=Join-Path $pf86 'Windows Kits/10/UnionMetadata'
$winmd=Get-ChildItem -LiteralPath $sdkRoot -Filter Windows.winmd -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
$bridge=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/System.Runtime.WindowsRuntime.dll'
$facades=Join-Path $pf86 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8/Facades'
if(!$winmd -or !(Test-Path -LiteralPath $bridge) -or !(Test-Path -LiteralPath $facades)) { throw "Required installed references unavailable: $winmd ; $bridge ; $facades" }
$sdkVersion=($winmd.FullName -split '\\' | Where-Object {$_ -match '^10\.\d+\.\d+\.\d+$'} | Select-Object -First 1)
$contractRoot=Join-Path $pf86 ('Windows Kits/10/References/'+$sdkVersion)
$contracts=@(Get-ChildItem -LiteralPath $contractRoot -Filter '*.winmd' -Recurse | Group-Object BaseName | ForEach-Object {$_.Group | Sort-Object {[version]$_.Directory.Name} -Descending | Select-Object -First 1} | ForEach-Object FullName)
if(!$contracts.Count) { throw 'Installed SDK contracts missing' }
$refs=@($winmd.FullName,$bridge)+$contracts+@(Get-ChildItem -LiteralPath $facades -Filter '*.dll' | ForEach-Object FullName)

$origin=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-origin.json') -Raw|ConvertFrom-Json
foreach($entry in $origin.PreservedFiles) {
 $blob=(& git rev-parse ('HEAD:'+ $entry.Path)).Trim()
 if($LASTEXITCODE -ne 0 -or $blob -cne $entry.SHA) { throw "Preserved source changed: $($entry.Path)" }
}
$catalog=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'languages/strings.json') -Encoding UTF8 -Raw|ConvertFrom-Json
foreach($e in $catalog){foreach($l in @('fr','en','es')){if([string]::IsNullOrWhiteSpace($e.$l)){throw 'Missing translation'}}}
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$app=Join-Path $out 'Haloryva.exe'
$argsList=@('/nologo','/utf8output','/codepage:65001','/langversion:5','/platform:x64','/target:winexe','/optimize+',('/out:"'+$app+'"'),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Xml.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll')
$argsList+=@($refs|ForEach-Object {'/reference:"'+$_+'"'})
$argsList+=@('/win32icon:"'+(Join-Path $PSScriptRoot 'assets/Haloryva.ico')+'"','/win32manifest:"'+(Join-Path $PSScriptRoot 'assets/app.manifest')+'"','/resource:"'+(Join-Path $PSScriptRoot 'languages/strings.json')+'",Haloryva.Localization.json')
$argsList+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs'|Sort-Object Name|ForEach-Object {'"'+$_.FullName+'"'})
$rsp=Join-Path $out 'Haloryva.rsp'; [IO.File]::WriteAllLines($rsp,$argsList,(New-Object Text.UTF8Encoding($true)))
& $csc ('@'+$rsp) 2>&1|Tee-Object -FilePath (Join-Path $out 'compile.log')
if($LASTEXITCODE -ne 0){throw 'Preview.9 compilation failed'}
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
if($version -ne '2.7.1-preview.9'){throw 'Unexpected version'}
$hash=(Get-FileHash -LiteralPath $app).Hash
[ordered]@{Version=$version;SHA256=$hash;Commit=$env:GITHUB_SHA;Metadata=$winmd.FullName;ContractCount=$contracts.Count;Bridge=$bridge;Compiled=$true;Executed=$false;Historical195Executed=$false;Signed=$false;Published=$false}|ConvertTo-Json|Tee-Object -FilePath (Join-Path $out 'build-provenance.json')
$hash|Set-Content -LiteralPath (Join-Path $out 'Haloryva.sha256') -Encoding ASCII
