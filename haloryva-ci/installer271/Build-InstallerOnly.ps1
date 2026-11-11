param([switch]$BuildAndCheckExtraction)
$ErrorActionPreference='Stop'
if(!$BuildAndCheckExtraction){throw 'Explicit installer build and extraction authorization required'}
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP){throw 'GitHub-hosted only. Never run on the principal PC.'}
$repoRoot=$env:GITHUB_WORKSPACE
$inputs=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer-inputs.json') -Raw -Encoding UTF8|ConvertFrom-Json
$build=Join-Path $env:RUNNER_TEMP 'haloryva-final271-frozen'
$app=Join-Path $build 'Haloryva.exe'
$proof=Get-Content -LiteralPath (Join-Path $build 'build-provenance.json') -Raw -Encoding UTF8|ConvertFrom-Json
if((Get-FileHash -LiteralPath $app).Hash -cne $inputs.ApplicationSHA256 -or $proof.SHA256 -cne $inputs.ApplicationSHA256 -or $proof.Commit -cne $inputs.ApplicationBuildCommit -or $proof.Version -cne $inputs.Version -or $proof.FileVersion -cne $inputs.FileVersion -or $proof.BuildDate -cne $inputs.BuildDate){throw 'Frozen application provenance mismatch'}
$appMetadata=[Diagnostics.FileVersionInfo]::GetVersionInfo($app)
if($appMetadata.ProductVersion -cne $inputs.Version -or $appMetadata.FileVersion -cne $inputs.FileVersion -or [Reflection.AssemblyName]::GetAssemblyName($app).Version.ToString() -cne $inputs.AssemblyVersion){throw 'Frozen application metadata mismatch'}
$archivedBrand=Join-Path $build 'stamped-source/src/Brand.cs'
$archivedIcon=Join-Path $build 'stamped-source/assets/Haloryva.ico'
if((Get-FileHash -LiteralPath $archivedBrand).Hash -cne $inputs.StampedBrandSHA256 -or $proof.StampedBrandSHA256 -cne $inputs.StampedBrandSHA256){throw 'Stamped Brand.cs mismatch'}
$iconBlob=(& git -C $repoRoot hash-object -- $archivedIcon).Trim()
if($LASTEXITCODE -ne 0 -or $iconBlob -cne $inputs.ArchivedIconBlob){throw 'Archived icon mismatch'}
foreach($item in $inputs.SourceInputs){
 $actual=(& git -C $repoRoot rev-parse ('HEAD:'+$item.Path)).Trim()
 if($LASTEXITCODE -ne 0 -or $actual -cne $item.GitBlobSHA){throw ('Frozen packaging source changed: '+$item.Path)}
}
$out=Join-Path $env:RUNNER_TEMP 'haloryva-final271-installer'
if(Test-Path -LiteralPath $out){throw 'Fresh output directory required'}
New-Item -ItemType Directory -Path $out|Out-Null
$stage=Join-Path $out 'staged-sources';New-Item -ItemType Directory -Path $stage|Out-Null
$payload=Join-Path $out 'payload';New-Item -ItemType Directory -Path $payload|Out-Null
$report=[ordered]@{Version=$inputs.Version;FileVersion=$inputs.FileVersion;Status='started';BuildCommit=$env:GITHUB_SHA;ApplicationSHA256=$inputs.ApplicationSHA256;ApplicationBuildRun=$inputs.ApplicationBuildRun;ApplicationBuildCommit=$inputs.ApplicationBuildCommit;ApplicationBuildDate=$inputs.BuildDate;ApplicationCompiled=$false;ApplicationExecuted=$false;ApplicationSelfTests=$false;InstallerSelfTest=$false;Installed=$false;Historical195Executed=$false;Signed=$false;Published=$false}
$process=$null
function Replace-One([string]$text,[string]$before,[string]$after){
 if([regex]::Matches($text,[regex]::Escape($before)).Count -ne 1){throw ('Unexpected source anchor: '+$before)}
 return $text.Replace($before,$after)
}
function Save-Utf8([string]$path,[string]$text){[IO.File]::WriteAllText($path,$text,(New-Object Text.UTF8Encoding($true)))}
try {
 $os=Get-CimInstance Win32_OperatingSystem
 [ordered]@{Caption=$os.Caption;Build=$os.BuildNumber;RunnerArch=$env:RUNNER_ARCH;Image=$env:ImageVersion}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out 'environment.json') -Encoding UTF8
 Copy-Item -LiteralPath (Join-Path $build 'build-provenance.json') -Destination (Join-Path $out 'application-build-provenance.json')
 Copy-Item -LiteralPath $archivedBrand -Destination (Join-Path $out 'Brand.application-stamped.cs')
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'installer-inputs.json') -Destination $out
 # Only adapt the installer's temporary Brand.cs, using the verified stamped archive.
 # The installer remains French as in the proven recipe; no app source is compiled.
 $brand=Get-Content -LiteralPath $archivedBrand -Raw -Encoding UTF8
 if($brand.Contains('__DATE_FABRICATION__')){throw 'Unstamped source is not allowed'}
 $brand=Replace-One $brand 'L.T(" · build ")' '" · build "'
 if($brand.Contains('L.T(')){throw 'Unexpected localization dependency in setup brand'}
 Save-Utf8 (Join-Path $stage 'Brand.cs') $brand
 foreach($filename in @('SetupForm.cs','SetupWork.cs','FileTransaction.cs','RegistryJournal.cs','MigrationPreview.cs')){
  Copy-Item -LiteralPath (Join-Path $repoRoot ('Haloryva_Windows64/installer/'+$filename)) -Destination (Join-Path $stage $filename)
 }
 $manifest=Get-Content -LiteralPath (Join-Path $repoRoot 'Haloryva_Windows64/assets/setup.manifest') -Raw -Encoding UTF8
 $manifest=Replace-One $manifest 'version="2.7.0.0"' ('version="'+$inputs.FileVersion+'"')
 Save-Utf8 (Join-Path $stage 'setup.manifest') $manifest
 [xml]$manifestXml=$manifest
 if($manifestXml.assembly.assemblyIdentity.version -cne $inputs.FileVersion -or $manifestXml.assembly.trustInfo.security.requestedPrivileges.requestedExecutionLevel.level -cne 'asInvoker'){throw 'Staged manifest metadata mismatch'}
 Copy-Item -LiteralPath $app -Destination (Join-Path $payload 'Haloryva.exe')
 [IO.File]::WriteAllText((Join-Path $payload 'Haloryva.sha256'),($inputs.ApplicationSHA256+[Environment]::NewLine),[Text.Encoding]::ASCII)
 Copy-Item -LiteralPath (Join-Path $repoRoot 'Haloryva_Windows64/assets/haloryva_demo.png') -Destination (Join-Path $payload 'haloryva_demo.png')
 $setup=Join-Path $out $inputs.OutputName
 $csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 if(!(Test-Path -LiteralPath $csc)){throw 'Installed compiler required; no installation or download fallback'}
 $sources=@(Get-ChildItem -LiteralPath $stage -Filter '*.cs'|Sort-Object Name)
 if($sources.Count -ne 6 -or @($sources.Name|Where-Object {$_ -cnotin @('Brand.cs','SetupForm.cs','SetupWork.cs','FileTransaction.cs','RegistryJournal.cs','MigrationPreview.cs')}).Count -ne 0){throw 'Installer-only source list mismatch'}
 $argsList=@('/nologo','/utf8output','/codepage:65001','/langversion:5','/platform:x64','/target:winexe','/optimize+','/main:CursorFlow.Setup.SetupProgram',('/out:"'+$setup+'"'),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Xml.dll','/reference:System.Web.Extensions.dll')
 $argsList+=@('/win32icon:"'+$archivedIcon+'"','/win32manifest:"'+(Join-Path $stage 'setup.manifest')+'"')
 foreach($name in @('Haloryva.exe','Haloryva.sha256','haloryva_demo.png')){$argsList+=('/resource:"'+(Join-Path $payload $name)+'",payload/'+$name)}
 $argsList+=@($sources|ForEach-Object {'"'+$_.FullName+'"'})
 $rsp=Join-Path $out 'Setup.rsp';[IO.File]::WriteAllLines($rsp,$argsList,(New-Object Text.UTF8Encoding($true)))
 $builtAt=[DateTimeOffset]::UtcNow
 $paris=[TimeZoneInfo]::ConvertTime($builtAt,[TimeZoneInfo]::FindSystemTimeZoneById('Romance Standard Time'))
 & $csc ('@'+$rsp) 2>&1|Tee-Object -FilePath (Join-Path $out 'compile-setup.log')
 if($LASTEXITCODE -ne 0){throw 'Installer-only compilation failed'}
 $report.InstallerCompiled=$true;$report.SetupBuiltAtUTC=$builtAt.ToString('o');$report.SetupBuiltAtParis=$paris.ToString('o')
 $fv=[Diagnostics.FileVersionInfo]::GetVersionInfo($setup)
 $av=[Reflection.AssemblyName]::GetAssemblyName($setup).Version.ToString()
 $signature=Get-AuthenticodeSignature -LiteralPath $setup
 # Load metadata and resources only, never invoke SetupProgram.Main by reflection.
 $assembly=[Reflection.Assembly]::LoadFrom($setup)
 $flags=[Reflection.BindingFlags]'Public,NonPublic,Static'
 $brandType=$assembly.GetType('CursorFlow.Brand',$true)
 $setupDate=$brandType.GetField('BuildDate',$flags).GetRawConstantValue()
 $setupVersion=$brandType.GetField('Version',$flags).GetRawConstantValue()
 $resources=@($assembly.GetManifestResourceNames()|Sort-Object)
 $stream=[IO.File]::OpenRead($setup);$reader=New-Object IO.BinaryReader($stream)
 try{
  $stream.Position=0x3c;$peOffset=$reader.ReadInt32()
  $stream.Position=$peOffset
  if($reader.ReadUInt32() -ne 0x4550){throw 'Invalid PE signature'}
  $machine=$reader.ReadUInt16()
 }finally{$reader.Dispose()}
 $checks=[ordered]@{
  ProductName=($fv.ProductName -ceq 'Haloryva Studio')
  CompanyName=($fv.CompanyName -ceq 'Julien Lecomte')
  ProductVersion=($fv.ProductVersion -ceq $inputs.Version)
  FileVersion=($fv.FileVersion -ceq $inputs.FileVersion)
  AssemblyVersion=($av -ceq $inputs.AssemblyVersion)
  WindowsX64=($machine -eq 0x8664)
  BrandVersion=($setupVersion -ceq $inputs.Version)
  StampedApplicationDate=($setupDate -ceq $inputs.BuildDate)
  NotSigned=($signature.Status.ToString() -ceq 'NotSigned')
  ExactEmbeddedPayload=($resources.Count -eq 3 -and @($resources|Where-Object {$_ -cnotin @('payload/Haloryva.exe','payload/Haloryva.sha256','payload/haloryva_demo.png')}).Count -eq 0)
 }
 $report.MetadataChecks=$checks;$report.MetadataChecksPassed=@($checks.Values|Where-Object {$_}).Count
 foreach($key in @($checks.Keys)){if(!$checks[$key]){throw ('Setup metadata check failed: '+$key)}}
 $report.AssemblyVersion=$av;$report.SetupBrandBuildDate=$setupDate;$report.SignatureStatus=$signature.Status.ToString()
 $report.SetupManifestSourceVersion=$manifestXml.assembly.assemblyIdentity.version
 $report.SetupManifestSourceExecutionLevel='asInvoker'
 $report.StampedBrandSHA256=$inputs.StampedBrandSHA256
 $report.StagedSourceHashes=@($sources|ForEach-Object {@{Name=$_.Name;SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
 $setupHash=(Get-FileHash -LiteralPath $setup).Hash
 # The early --extract branch returns before GUI, relocation, installation or self-tests.
 $extract=Join-Path $out 'extracted'
 if(Test-Path -LiteralPath $extract){throw 'Fresh extraction directory required'}
 if($extract.Contains('"')){throw 'Unexpected quoted path'}
 $start=New-Object Diagnostics.ProcessStartInfo
 $start.FileName=$setup;$start.Arguments='--extract="'+$extract+'"'
 $start.UseShellExecute=$false;$start.CreateNoWindow=$true
 $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden;$start.WorkingDirectory=$out
 $process=[Diagnostics.Process]::Start($start)
 $heldHandle=$process.Handle
 if(!$process.WaitForExit(30000)){$process.Kill();throw 'Extraction timeout; no installation or application test fallback'}
 $process.WaitForExit()
 $report.ExtractionExitCode=$process.ExitCode
 if($process.ExitCode -ne 0){throw ('Extraction failed: '+$process.ExitCode)}
 $names=@(Get-ChildItem -LiteralPath $extract -Recurse -File|ForEach-Object {$_.FullName.Substring($extract.Length+1)})
 if($names.Count -ne 3 -or @($names|Where-Object {$_ -cnotin @('Haloryva.exe','Haloryva.sha256','haloryva_demo.png')}).Count -ne 0){throw 'Unexpected extracted payload names'}
 $extraction=@()
 foreach($name in $names){
  $before=(Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash
  $after=(Get-FileHash -LiteralPath (Join-Path $extract $name)).Hash
  $extraction+=@{File=$name;PayloadSHA256=$before;ExtractedSHA256=$after;Identical=($before -ceq $after)}
  if($before -cne $after){throw ('Extracted payload differs: '+$name)}
 }
 if((Get-Content -LiteralPath (Join-Path $extract 'Haloryva.sha256') -Raw).Trim() -cne $inputs.ApplicationSHA256){throw 'Extracted hash file mismatch'}
 if((Get-FileHash -LiteralPath (Join-Path $extract 'Haloryva.exe')).Hash -cne $inputs.ApplicationSHA256){throw 'Extracted application differs from frozen candidate'}
 if((Get-FileHash -LiteralPath $app).Hash -cne $inputs.ApplicationSHA256 -or (Get-FileHash -LiteralPath $archivedBrand).Hash -cne $inputs.StampedBrandSHA256){throw 'Frozen application or stamped source changed'}
 if((Get-FileHash -LiteralPath $setup).Hash -cne $setupHash){throw 'Installer changed during extraction'}
 $report.SetupSHA256=$setupHash;$report.SetupBytes=(Get-Item -LiteralPath $setup).Length
 $report.ExtractionFiles=$extraction;$report.InstallerExtractedOnly=$true
 $report.ApplicationAndStampedSourceUnchanged=$true;$report.SetupUnchangedAfterExtraction=$true
 $report.Status='passed'
 [IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'),($setupHash+'  '+$inputs.OutputName+[Environment]::NewLine+$inputs.ApplicationSHA256+'  Haloryva.exe'+[Environment]::NewLine),[Text.Encoding]::ASCII)
} catch {
 $report.Status='failed';$report.Error=$_.Exception.Message
 throw
} finally {
 if($process){if(!$process.HasExited){$process.Kill()};$process.Dispose()}
 $report|ConvertTo-Json -Depth 8|Tee-Object -FilePath (Join-Path $out 'installer-provenance.json')
}
