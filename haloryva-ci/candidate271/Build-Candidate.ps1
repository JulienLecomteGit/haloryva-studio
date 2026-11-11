$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'GitHub-hosted runner required. No local execution.' }
$out=Join-Path $env:RUNNER_TEMP 'haloryva-candidate271-build'
if(Test-Path -LiteralPath $out) { throw 'Fresh build directory required' }
New-Item -ItemType Directory -Path $out | Out-Null
$source=Join-Path $env:GITHUB_WORKSPACE 'livraison-2.7.1-preparation'
$stamped=Join-Path $out 'stamped-source'
New-Item -ItemType Directory -Path $stamped | Out-Null
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-inputs.json') -Raw | ConvertFrom-Json
$files=@()
foreach($entry in $manifest.Inputs) {
 $blob=(& git rev-parse ('HEAD:'+$entry.Path)).Trim()
 if($LASTEXITCODE -ne 0 -or $blob -cne $entry.Blob) { throw "Prepared source changed: $($entry.Path)" }
 $files+=@{Path=$entry.Path;Blob=$blob}
}
if($files.Count -ne 36) { throw 'Expected exactly 36 prepared application inputs' }
foreach($name in @('src','assets','languages')) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $stamped -Recurse }
$brandFile=Join-Path $stamped 'src/Brand.cs'
$brand=[IO.File]::ReadAllText($brandFile,[Text.Encoding]::UTF8)
if(([regex]::Matches($brand,'__DATE_FABRICATION__')).Count -ne 1) { throw 'Expected unique build-date placeholder' }
$builtAt=[DateTimeOffset]::UtcNow
$paris=[TimeZoneInfo]::ConvertTime($builtAt,[TimeZoneInfo]::FindSystemTimeZoneById('Romance Standard Time'))
$date=$paris.ToString('dd/MM/yyyy',[Globalization.CultureInfo]::InvariantCulture)
[IO.File]::WriteAllText($brandFile,$brand.Replace('__DATE_FABRICATION__',$date),(New-Object Text.UTF8Encoding($true)))
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

$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$app=Join-Path $out 'Haloryva.exe'
$argsList=@('/nologo','/utf8output','/codepage:65001','/langversion:5','/platform:x64','/target:winexe','/optimize+',('/out:"'+$app+'"'),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Xml.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll')
$argsList+=@($refs|ForEach-Object {'/reference:"'+$_+'"'})
$argsList+=@('/win32icon:"'+(Join-Path $stamped 'assets/Haloryva.ico')+'"','/win32manifest:"'+(Join-Path $stamped 'assets/app.manifest')+'"','/resource:"'+(Join-Path $stamped 'languages/strings.json')+'",Haloryva.Localization.json')
$argsList+=@(Get-ChildItem -LiteralPath (Join-Path $stamped 'src') -Filter '*.cs'|Sort-Object Name|ForEach-Object {'"'+$_.FullName+'"'})
$rsp=Join-Path $out 'Haloryva.rsp'; [IO.File]::WriteAllLines($rsp,$argsList,(New-Object Text.UTF8Encoding($true)))
& $csc ('@'+$rsp) 2>&1|Tee-Object -FilePath (Join-Path $out 'compile.log')
if($LASTEXITCODE -ne 0){throw 'Candidate 2.7.1 compilation failed'}

$fv=[Diagnostics.FileVersionInfo]::GetVersionInfo($app)
$av=[Reflection.AssemblyName]::GetAssemblyName($app).Version.ToString()
$assembly=[Reflection.Assembly]::LoadFrom($app)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static'
$brandType=$assembly.GetType('CursorFlow.Brand',$true)
$actualDate=$brandType.GetField('BuildDate',$flags).GetRawConstantValue()
$actualVersion=$brandType.GetField('Version',$flags).GetRawConstantValue()
$history=$brandType.GetField('History',$flags).GetValue($null)
$signature=Get-AuthenticodeSignature -LiteralPath $app
$checks=[ordered]@{
 ProductName=($fv.ProductName -ceq 'Haloryva Studio')
 ProductVersion=($fv.ProductVersion -ceq '2.7.1')
 FileVersion=($fv.FileVersion -ceq '2.7.1.10')
 AssemblyVersion=($av -ceq '2.7.1.0')
 BrandVersion=($actualVersion -ceq '2.7.1')
 ActualParisBuildDate=($actualDate -ceq $date)
 FirstHistoryVersion=($history[0][0] -ceq '2.7.1')
 FirstHistoryDate=($history[0][1] -ceq $date)
 NoSignature=($signature.Status.ToString() -eq 'NotSigned')
}
$resource=$assembly.GetManifestResourceStream('Haloryva.Localization.json')
$reader=New-Object IO.StreamReader($resource,[Text.Encoding]::UTF8)
try {$embedded=$reader.ReadToEnd()} finally {$reader.Dispose()}
$checks.CatalogMatchesPreparedSource=($embedded -ceq [IO.File]::ReadAllText((Join-Path $source 'languages/strings.json'),[Text.Encoding]::UTF8))
foreach($key in @($checks.Keys)) { if(!$checks[$key]) { throw "Metadata check failed: $key" } }
$hash=(Get-FileHash -LiteralPath $app).Hash
if($hash -eq '678DB7DBBAB906A438F01F83A27D8A4D9C1904D1E280880D148FE3A1610522DC') { throw 'Expected new candidate distinct from preview.9' }
[ordered]@{
 Version=$fv.ProductVersion;FileVersion=$fv.FileVersion;AssemblyVersion=$av;BuildDate=$actualDate
 BuiltAtUTC=$builtAt.ToString('o');BuiltAtParis=$paris.ToString('o');DateZone='Europe/Paris'
 SHA256=$hash;Commit=$env:GITHUB_SHA;PreparedSourceCommit=$manifest.PreparedSourceCommit
 SourceCount=$files.Count;InputBlobs=$files;StampedBrandSHA256=(Get-FileHash -LiteralPath $brandFile).Hash
 MetadataChecks=$checks;MetadataChecksPassed=$checks.Count
 SDK=$winmd.FullName;ContractCount=$contracts.Count;Bridge=$bridge
 Compiled=$true;ApplicationEntryPointExecuted=$false;SelfTestsExecuted=$false;Historical195Executed=$false
 Signed=$false;SignatureStatus=$signature.Status.ToString();InstallerBuilt=$false;MSIXBuilt=$false;ReleasePublished=$false
}|ConvertTo-Json -Depth 8|Tee-Object -FilePath (Join-Path $out 'build-provenance.json')
$hash|Set-Content -LiteralPath (Join-Path $out 'Haloryva.sha256') -Encoding ASCII
