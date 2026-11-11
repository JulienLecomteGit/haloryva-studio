param([Parameter(Mandatory=$true)][ValidateSet('fr','en','es')][string]$Language)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'GitHub-hosted only' }
$out=Join-Path $env:RUNNER_TEMP ('haloryva-candidate271-history-captures/'+$Language)
# Deliver PNG bytes through read-only job logs; full original files are also retained in the artifact.
Get-ChildItem -LiteralPath $out -Filter '*.png' | ForEach-Object {
 $file=$_.Name;$b64=[Convert]::ToBase64String([IO.File]::ReadAllBytes($_.FullName));$index=0
 for($offset=0;$offset -lt $b64.Length;$offset+=4096){
  Write-Output ('PNGCHUNK|'+$file+'|'+$index.ToString('D5')+'|'+$b64.Substring($offset,[Math]::Min(4096,$b64.Length-$offset)))
  $index++
 }
 Write-Output ('PNGEND|'+$file+'|'+$index+'|'+(Get-FileHash -LiteralPath $_.FullName).Hash)
}
