$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'GitHub hosted only' }
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview8-preflight'
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
$code=@'
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
public static class Probe {
 public static async Task<string> Read() {
  var t=await StartupTask.GetAsync("HaloryvaStudioStartup");
  return t.State.ToString()+ApplicationData.Current.LocalFolder.Path+ApplicationData.Current.LocalCacheFolder.Path;
 }
}
'@
$cs=Join-Path $out 'Probe.cs'; [IO.File]::WriteAllText($cs,$code)
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$argsList=@('/nologo','/langversion:5','/target:library',('/out:"'+(Join-Path $out 'Probe.dll')+'"'),'/reference:System.dll','/reference:System.Core.dll')+@($refs | ForEach-Object {'/reference:"'+$_+'"'})+@('"'+$cs+'"')
$rsp=Join-Path $out 'Probe.rsp';[IO.File]::WriteAllLines($rsp,$argsList)
& $csc ('@'+$rsp) 2>&1 | Tee-Object -FilePath (Join-Path $out 'compile.log')
if($LASTEXITCODE -ne 0) { throw 'WinRT compile probe failed' }
[ordered]@{Compiler=$csc;Metadata=$winmd.FullName;Bridge=$bridge;Facades=$facades;Compiled=$true;Executed=$false;Installed=$false;Historical195=$false}|ConvertTo-Json|Tee-Object -FilePath (Join-Path $out 'references.json')
