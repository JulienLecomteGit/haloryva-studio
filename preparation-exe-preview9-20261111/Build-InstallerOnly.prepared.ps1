param([switch]$BuildAndCheckExtraction)
$ErrorActionPreference='Stop'
if(!$BuildAndCheckExtraction){throw 'Prepared recipe: build and extraction require an explicit later request'}
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub-hosted only. Never run on the principal PC.'}
$repoRoot=Split-Path -Parent $PSScriptRoot
$inputs=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-inputs.json') -Raw -Encoding UTF8|ConvertFrom-Json
$build=Join-Path $env:RUNNER_TEMP 'haloryva-preview9-frozen'
$app=Join-Path $build 'Haloryva.exe'
$proof=Get-Content -LiteralPath (Join-Path $build 'build-provenance.json') -Raw -Encoding UTF8|ConvertFrom-Json
if((Get-FileHash -LiteralPath $app).Hash -cne $inputs.ApplicationSHA256 -or $proof.SHA256 -cne $inputs.ApplicationSHA256 -or $proof.Commit -cne $inputs.ApplicationBuildCommit -or $proof.Version -cne $inputs.Version){throw 'Frozen preview.9 provenance mismatch'}
if([Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion -cne $inputs.Version){throw 'Application version mismatch'}
foreach($item in $inputs.SourceInputs){
 $actual=(& git -C $repoRoot rev-parse ('HEAD:'+$item.Path)).Trim()
 if($LASTEXITCODE -ne 0 -or $actual -cne $item.GitBlobSHA){throw ('Frozen packaging source changed: '+$item.Path)}
}
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview9-installer'
if(Test-Path -LiteralPath $out){throw 'Fresh output directory required'}
New-Item -ItemType Directory -Path $out|Out-Null
$stage=Join-Path $out 'staged-sources';New-Item -ItemType Directory -Path $stage|Out-Null
$payload=Join-Path $out 'payload';New-Item -ItemType Directory -Path $payload|Out-Null
function Replace-One([string]$text,[string]$before,[string]$after){
 if([regex]::Matches($text,[regex]::Escape($before)).Count -ne 1){throw ('Unexpected source anchor: '+$before)}
 return $text.Replace($before,$after)
}
function Save-Utf8([string]$path,[string]$text){[IO.File]::WriteAllText($path,$text,(New-Object Text.UTF8Encoding($true)))}
# Adapt only the installer's staged copy. The application source and EXE stay frozen.
$brand=Get-Content -LiteralPath (Join-Path $repoRoot 'haloryva-ci/preview9/src/Brand.cs') -Raw -Encoding UTF8
$brand=Replace-One $brand 'public const string Version = "2.7.1";' ('public const string Version = "2.7.1";'+[Environment]::NewLine+'        public const string DisplayVersion = "2.7.1-preview.9";')
$brand=Replace-One $brand 'L.T(" · build ")' '" · build "'
$brand=Replace-One $brand 'Product + " " + Version +' 'Product + " " + DisplayVersion +'
if($brand.Contains('L.T(')){throw 'Unexpected localization dependency in setup brand'}
Save-Utf8 (Join-Path $stage 'Brand.cs') $brand
foreach($filename in @('SetupForm.cs','SetupWork.cs','FileTransaction.cs','RegistryJournal.cs','MigrationPreview.cs')){
 $text=Get-Content -LiteralPath (Join-Path $repoRoot ('Haloryva_Windows64/installer/'+$filename)) -Raw -Encoding UTF8
 if($filename -eq 'SetupForm.cs'){
  if([regex]::Matches($text,'\bBrand\.Version\b').Count -ne 5){throw 'Unexpected UI version references'}
  $text=[regex]::Replace($text,'\bBrand\.Version\b','Brand.DisplayVersion')
 }
 if($filename -eq 'SetupWork.cs'){
  $text=Replace-One $text 'k.SetValue("DisplayVersion", Brand.Version);' 'k.SetValue("DisplayVersion", Brand.DisplayVersion);'
  $text=Replace-One $text 'String.Equals(k.GetValue("DisplayVersion") as string, Brand.Version)' 'String.Equals(k.GetValue("DisplayVersion") as string, Brand.DisplayVersion)'
  # VerifyPayload still compares the assembly numeric version against Brand.Version.
 }
 Save-Utf8 (Join-Path $stage $filename) $text
}
$manifest=Get-Content -LiteralPath (Join-Path $repoRoot 'Haloryva_Windows64/assets/setup.manifest') -Raw -Encoding UTF8
$manifest=Replace-One $manifest 'version="2.7.0.0"' 'version="2.7.1.9"'
Save-Utf8 (Join-Path $stage 'setup.manifest') $manifest
Copy-Item -LiteralPath $app -Destination (Join-Path $payload 'Haloryva.exe')
[IO.File]::WriteAllText((Join-Path $payload 'Haloryva.sha256'),($inputs.ApplicationSHA256+[Environment]::NewLine),[Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $repoRoot 'Haloryva_Windows64/assets/haloryva_demo.png') -Destination (Join-Path $payload 'haloryva_demo.png')
$setup=Join-Path $out $inputs.OutputName
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(!(Test-Path -LiteralPath $csc)){throw 'Installed compiler required; no installation or download fallback'}
$argsList=@('/nologo','/utf8output','/codepage:65001','/langversion:5','/platform:x64','/target:winexe','/optimize+','/main:CursorFlow.Setup.SetupProgram',('/out:"'+$setup+'"'),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll','/reference:System.Web.Extensions.dll')
$argsList+=@('/win32icon:"'+(Join-Path $repoRoot 'haloryva-ci/preview9/assets/Haloryva.ico')+'"','/win32manifest:"'+(Join-Path $stage 'setup.manifest')+'"')
foreach($name in @('Haloryva.exe','Haloryva.sha256','haloryva_demo.png')){$argsList+=('/resource:"'+(Join-Path $payload $name)+'",payload/'+$name)}
$argsList+=@(Get-ChildItem -LiteralPath $stage -Filter '*.cs'|Sort-Object Name|ForEach-Object {'"'+$_.FullName+'"'})
$rsp=Join-Path $out 'Setup.rsp';[IO.File]::WriteAllLines($rsp,$argsList,(New-Object Text.UTF8Encoding($true)))
& $csc ('@'+$rsp) 2>&1|Tee-Object -FilePath (Join-Path $out 'compile-setup.log')
if($LASTEXITCODE -ne 0){throw 'Installer-only compilation failed'}
if([Diagnostics.FileVersionInfo]::GetVersionInfo($setup).ProductVersion -cne $inputs.Version){throw 'Setup metadata version mismatch'}
$signature=Get-AuthenticodeSignature -LiteralPath $setup
if($signature.Status.ToString() -cne 'NotSigned'){throw 'Unexpected setup signature state'}
# Execute only the early --extract mode; no UI, app launch, install or self-test.
$extract=Join-Path $out 'extracted'
if($extract.Contains('"')){throw 'Unexpected quoted path'}
$start=New-Object Diagnostics.ProcessStartInfo
$start.FileName=$setup;$start.Arguments='--extract="'+$extract+'"'
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden;$start.WorkingDirectory=$out
$process=[Diagnostics.Process]::Start($start)
try{
 if(!$process.WaitForExit(30000)){throw 'Extraction timeout; no installation or application test fallback'}
 if($process.ExitCode -ne 0){throw ('Extraction failed: '+$process.ExitCode)}
}finally{if($process.HasExited){$process.Dispose()}}
$names=@(Get-ChildItem -LiteralPath $extract -File|Select-Object -ExpandProperty Name)
if($names.Count -ne 3 -or @($names|Where-Object {$_ -cnotin @('Haloryva.exe','Haloryva.sha256','haloryva_demo.png')}).Count -ne 0){throw 'Unexpected extracted payload names'}
foreach($name in $names){if((Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash -cne (Get-FileHash -LiteralPath (Join-Path $extract $name)).Hash){throw ('Extracted payload differs: '+$name)}}
if((Get-FileHash -LiteralPath $app).Hash -cne $inputs.ApplicationSHA256){throw 'Frozen application changed'}
[ordered]@{Version=$inputs.Version;BuildCommit=$env:GITHUB_SHA;ApplicationSHA256=$inputs.ApplicationSHA256;ApplicationBuildRun=$inputs.ApplicationBuildRun;SetupSHA256=(Get-FileHash -LiteralPath $setup).Hash;InstallerCompiled=$true;InstallerExtractedOnly=$true;ApplicationCompiled=$false;ApplicationExecuted=$false;ApplicationSelfTests=$false;InstallerSelfTest=$false;Installed=$false;Historical195Executed=$false;Signed=$false;Published=$false}|ConvertTo-Json|Tee-Object -FilePath (Join-Path $out 'installer-provenance.json')
