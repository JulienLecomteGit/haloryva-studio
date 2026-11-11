$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'GitHub-hosted runner required' }
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview8-targeted'
if(Test-Path -LiteralPath $out){throw 'Fresh output required'}
New-Item -ItemType Directory -Path $out|Out-Null
$build=Join-Path $env:RUNNER_TEMP 'haloryva-preview8-build'
$app=Join-Path $build 'Haloryva.exe'
$expected='05D47EC2F97B378E7CE09B38ED642BD710095CD4B92E1AAE343C0844F9F4AEB8'
if((Get-FileHash -LiteralPath $app).Hash -cne $expected){throw 'Frozen binary mismatch'}
$p=Get-Content -LiteralPath (Join-Path $build 'build-provenance.json') -Raw|ConvertFrom-Json
if($p.Commit -cne 'e3cbcdb732af337953f246ec267bd0e0d1b109cf' -or $p.Version -ne '2.7.1-preview.8'){throw 'Build provenance mismatch'}
Copy-Item -LiteralPath $app -Destination $out
Copy-Item -LiteralPath (Join-Path $build 'build-provenance.json') -Destination $out
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
$exe=Join-Path $out 'Haloryva.Preview8.Targeted.exe'
$argv=@('/nologo','/utf8output','/codepage:65001','/langversion:5','/target:exe','/platform:x64',('/out:"'+$exe+'"'),('/reference:"'+$app+'"'),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll')+@($refs|ForEach-Object {'/reference:"'+$_+'"'})+@('"'+(Join-Path $PSScriptRoot 'Targeted.cs')+'"')
$rsp=Join-Path $out 'Targeted.rsp';[IO.File]::WriteAllLines($rsp,$argv,(New-Object Text.UTF8Encoding($true)))
& $csc ('@'+$rsp) 2>&1|Tee-Object -FilePath (Join-Path $out 'compile-driver.log')
if($LASTEXITCODE -ne 0){throw 'Targeted driver compilation failed'}
$unit=Join-Path $out 'unit';New-Item -ItemType Directory -Path $unit|Out-Null
$token=[Guid]::NewGuid().ToString('N')
@{GitHubHosted=$true;Token=$token}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $unit 'authorization.json') -Encoding UTF8
$result=Join-Path $unit 'result.json'
$proc=Start-Process -FilePath $exe -ArgumentList ('unit "'+$result+'" '+$token) -PassThru -WindowStyle Hidden
if(!$proc.WaitForExit(90000)){throw 'Targeted driver timed out'}
if(!(Test-Path -LiteralPath $result)){throw 'Result missing'}
$data=Get-Content -LiteralPath $result -Raw -Encoding UTF8|ConvertFrom-Json
$data|ConvertTo-Json -Depth 8|Write-Output
if($proc.ExitCode -ne 0 -or !$data.Passed){throw 'Targeted isolated checks failed'}
$os=Get-CimInstance Win32_OperatingSystem
[ordered]@{Version='2.7.1-preview.8';SHA256=$expected;BuildRun=43012511350;RecipeRun=$env:GITHUB_RUN_ID;Status='targeted-passed';OS=$os.Caption;Build=$os.BuildNumber;Arch=$env:RUNNER_ARCH;Image=$env:ImageVersion;Historical195Executed=$false;Signed=$false;Published=$false;DriverSHA256=(Get-FileHash -LiteralPath $exe).Hash;Assertions=@($data.Checks).Count;SimulatedAssertions=@($data.Checks|Where-Object Kind -eq 'simulated-state').Count}|ConvertTo-Json|Tee-Object -FilePath (Join-Path $out 'result.json')
