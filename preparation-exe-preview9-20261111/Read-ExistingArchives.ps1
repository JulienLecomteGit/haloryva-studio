$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub-hosted evidence reader only'}
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression.FileSystem
$out=Join-Path $env:RUNNER_TEMP 'preview9-closure-existing'
if(Test-Path -LiteralPath $out){throw 'Fresh evidence directory required'}
New-Item -ItemType Directory -Path $out|Out-Null
$archives=@(
 @{Mode='on';Id=13863825999;SHA256='b54917d0d841e2432eb2efc78ee0fdd299033deefa3eb0ca4bcf3e0be448efb6'},
 @{Mode='off';Id=13862837786;SHA256='0efbe3c42f71e2cb5b1f357c51d0cbb4901702cbfceeade02faaf7e6d15ce443'}
)
$report=[ordered]@{SourceRun=43035528813;ReadExistingArchivesOnly=$true;HaloryvaExecuted=$false;InstallerExecuted=$false;TestsExecuted=$false;Installed=$false;Signed=$false;Published=$false;ZipTimeNote='ZIP date/time has no original timezone; do not infer exact UTC from it';Archives=@()}
foreach($a in $archives){
 $handler=New-Object Net.Http.HttpClientHandler;$handler.AllowAutoRedirect=$false
 $api=New-Object Net.Http.HttpClient($handler)
 $api.DefaultRequestHeaders.UserAgent.ParseAdd('Haloryva-Evidence-Reader/1.0')
 $api.DefaultRequestHeaders.Authorization=New-Object Net.Http.Headers.AuthenticationHeaderValue('Bearer',$env:GH_READ_TOKEN)
 try{
  $response=$api.GetAsync('https://api.github.com/repos/JulienLecomteGit/haloryva-studio/actions/artifacts/'+$a.Id+'/zip').GetAwaiter().GetResult()
  if([int]$response.StatusCode -ne 302){throw ('Expected artifact redirect; got '+[int]$response.StatusCode)}
  $location=$response.Headers.Location
  if($location.Scheme -ne 'https' -or !($location.Host.EndsWith('.blob.core.windows.net') -or $location.Host.EndsWith('.actions.githubusercontent.com'))){throw 'Unexpected artifact host'}
 }finally{$api.Dispose();$handler.Dispose()}
 # Separate client: never send the GitHub token to the blob storage service.
 $download=New-Object Net.Http.HttpClient
 try{$bytes=$download.GetByteArrayAsync($location).GetAwaiter().GetResult()}finally{$download.Dispose()}
 $file=Join-Path $out ($a.Mode+'.zip');[IO.File]::WriteAllBytes($file,$bytes)
 if((Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() -cne $a.SHA256){throw 'Archive digest mismatch'}
 $one=[ordered]@{Mode=$a.Mode;ArtifactId=$a.Id;ArchiveSHA256=$a.SHA256;Entries=@()}
 $zip=[IO.Compression.ZipFile]::OpenRead($file)
 try{
  foreach($entry in $zip.Entries){
   if(!$entry.Name){continue}
   if($entry.Length -gt 1048576){throw 'Unexpected oversized evidence entry'}
   $stream=$entry.Open();$buffer=New-Object IO.MemoryStream
   try{$stream.CopyTo($buffer);$content=$buffer.ToArray()}finally{$stream.Dispose();$buffer.Dispose()}
   $hash=[Security.Cryptography.SHA256]::Create()
   try{$digest=[BitConverter]::ToString($hash.ComputeHash($content)).Replace('-','')}finally{$hash.Dispose()}
   $item=[ordered]@{Path=$entry.FullName;Length=$entry.Length;ZipStoredTime=$entry.LastWriteTime.DateTime.ToString('s');SHA256=$digest}
   if($entry.Name -in @('transcript.txt','studio-after-refocus.txt','functional-result-before-cleanup.json','result.json','startup-refresh-recipe.tsv')){
    $item.Text=[Text.Encoding]::UTF8.GetString($content).TrimStart([char]0xFEFF)
   }
   if($entry.Name -in @('settings.json','settings.json.bak')){
    $settings=[Text.Encoding]::UTF8.GetString($content).TrimStart([char]0xFEFF)|ConvertFrom-Json
    $item.ProfileSummary=@{LaunchAtLogin=$settings.LaunchAtLogin;UiLanguage=$settings.UiLanguage}
   }
   $one.Entries+=,$item
  }
 }finally{$zip.Dispose()}
 $report.Archives+=,$one
}
$report.CompilerPresentForFuturePackaging=Test-Path -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe')
$report|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $out 'archive-inventory.json') -Encoding UTF8
$report|ConvertTo-Json -Depth 15|Write-Output
