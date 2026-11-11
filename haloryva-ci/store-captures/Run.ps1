$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub only'}
$out=Join-Path $env:RUNNER_TEMP 'store-three-captures'
New-Item -ItemType Directory -Path $out|Out-Null
$zip=Join-Path $env:RUNNER_TEMP 'original-file11.zip'
$h=@{Authorization="Bearer $env:GH_TOKEN";Accept='application/vnd.github+json'}
Invoke-WebRequest -Uri 'https://api.github.com/repos/JulienLecomteGit/haloryva-studio/actions/artifacts/13886896454/zip' -Headers $h -OutFile $zip
if((Get-FileHash $zip).Hash -ne '32568F54E246A6CBEE0AE1D6DDDDA451341D3FE5134FF9FBEC742EE728A9CE67'){throw 'Archive mismatch'}
$source=Join-Path $env:RUNNER_TEMP 'original-file11'
Expand-Archive $zip $source
$app=Join-Path $source 'Haloryva.exe'
$expected='1CB026DDBA084A648832FBD91118A67000373E076DAFA72BE2957B47223A6F81'
if((Get-FileHash $app).Hash -ne $expected){throw 'EXE mismatch'}
Set-DisplayResolution -Width 1920 -Height 1080 -Force
foreach($lang in @('fr','en','es')){
 $driver=Join-Path $PSScriptRoot 'Capture.ps1'
 $png=Join-Path $out ("studio-"+$lang+"-1920x1080.png")
 $argsList='-NoProfile -STA -File "'+$driver+'" -Language '+$lang+' -AppPath "'+$app+'" -OutputPath "'+$png+'"'
 $p=Start-Process -FilePath 'powershell.exe' -ArgumentList $argsList -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out ($lang+'.stdout.txt')) -RedirectStandardError (Join-Path $out ($lang+'.stderr.txt'))
 if(!$p.WaitForExit(60000)){$p.Kill();throw 'Capture timeout'}
 $p.Refresh()
 if(!(Test-Path $png) -or !(Test-Path ($png+'.json'))){throw "Capture missing $lang"}
 if((Get-FileHash $app).Hash -ne $expected){throw 'EXE changed'}
}
[ordered]@{SourceRun=43086180391;SourceArtifact=13886896454;EXESHA256=$expected;CaptureCount=3;ApplicationRecompiled=$false;DriverCompiled=$false;Autotests=$false;Historical195=$false;Installed=$false;Signed=$false;Published=$false;Images=@(Get-ChildItem $out -Filter '*.png'|ForEach-Object {@{Name=$_.Name;SHA256=(Get-FileHash $_.FullName).Hash}})}|ConvertTo-Json -Depth 5|Tee-Object (Join-Path $out 'provenance.json')
