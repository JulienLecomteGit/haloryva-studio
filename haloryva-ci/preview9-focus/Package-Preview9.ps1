$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub-hosted only'}
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview9-msix'
if(Test-Path -LiteralPath $out){throw 'Fresh output required'}
New-Item -ItemType Directory -Path $out|Out-Null
$build=Join-Path $env:RUNNER_TEMP 'haloryva-preview9-build'
$expected='B19528E2C077AA52B5BE5D293853E142ECD243DE39F1A87176A14A1A9DCF91D8'
$old=Join-Path $env:RUNNER_TEMP 'preview8-frozen/Haloryva-test-2.7.1.80.msix'
if((Get-FileHash -LiteralPath $old).Hash -cne 'D07978FB383F1CD069DDAE53C49771E9ACFA4D47700FF262374F3BCF3EAD34A9'){throw 'Previous manifest package differs'}
$exe=Join-Path $build 'Haloryva.exe'
$proof=Get-Content -LiteralPath (Join-Path $build 'build-provenance.json') -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $exe).Hash -cne $expected -or $proof.SHA256 -cne $expected -or $proof.Commit -cne '4ed3f2ad13594bba2a5425db564cfaf3ff1c5521' -or $proof.Version -cne '2.7.1-preview.9'){throw 'Frozen preview.9 build differs'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$layout=Join-Path $out 'layout'
[IO.Compression.ZipFile]::ExtractToDirectory($old,$layout)
foreach($name in @('AppxBlockMap.xml','[Content_Types].xml','AppxSignature.p7x')){
 $p=Join-Path $layout $name
 if(Test-Path -LiteralPath $p){Remove-Item -LiteralPath $p}
}
Copy-Item -LiteralPath $exe -Destination (Join-Path $layout 'Haloryva.exe') -Force
$manifest=Join-Path $layout 'AppxManifest.xml'
[xml]$xml=Get-Content -LiteralPath $manifest -Raw -Encoding UTF8
if($xml.Package.Identity.Name -cne 'HaloryvaStudio.MsixLifecycleTest'){throw 'Unexpected test identity'}
$apps=@($xml.SelectNodes("//*[local-name()='Application']"))
$tasks=@($xml.SelectNodes("//*[local-name()='StartupTask']"))
if($apps.Count -ne 1 -or $tasks.Count -ne 1 -or $tasks[0].GetAttribute('TaskId') -cne 'HaloryvaStudioStartup' -or $tasks[0].GetAttribute('Enabled') -cne 'false'){throw 'Original single-app manifest differs'}
$xml.Package.Identity.Version='2.7.1.90';$xml.Save($manifest)
$sdk=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/bin'
$maker=Get-ChildItem -LiteralPath $sdk -Directory|Where-Object {$_.Name -match '^10[.]0[.]\d+[.]0$'}|Sort-Object {[Version]$_.Name} -Descending|ForEach-Object {Join-Path $_.FullName 'x64/makeappx.exe'}|Where-Object {Test-Path -LiteralPath $_}|Select-Object -First 1
if(!$maker){throw 'Installed MakeAppx required'}
$package=Join-Path $out 'Haloryva-test-2.7.1.90.msix'
& $maker pack /d $layout /p $package /o 2>&1|Tee-Object -FilePath (Join-Path $out 'pack.log')
if($LASTEXITCODE -ne 0){throw 'MSIX structural validation failed'}
$zip=[IO.Compression.ZipFile]::OpenRead($package)
try{
 if($zip.GetEntry('AppxSignature.p7x')){throw 'Unexpected signature'}
 $stream=$zip.GetEntry('Haloryva.exe').Open();$hasher=[Security.Cryptography.SHA256]::Create()
 try{$actual=[BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$hasher.Dispose()}
 if($actual -cne $expected){throw 'Packaged EXE changed'}
}finally{$zip.Dispose()}
[ordered]@{Version='2.7.1-preview.9';PackageVersion='2.7.1.90';ExecutableSHA256=$expected;PackageSHA256=(Get-FileHash -LiteralPath $package).Hash;BuildCommit='4ed3f2ad13594bba2a5425db564cfaf3ff1c5521';CompiledHere=$false;Signed=$false;Published=$false;PriorIdentityUnchanged=$true;Historical195Executed=$false}|ConvertTo-Json|Tee-Object -FilePath (Join-Path $out 'package-provenance.json')
