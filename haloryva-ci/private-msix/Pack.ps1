$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub hosted only'}
$out=Join-Path $env:RUNNER_TEMP 'haloryva-private-msix'
if(Test-Path $out){throw 'Fresh output required'}
New-Item -ItemType Directory -Path $out|Out-Null
$checks=[ordered]@{}
function Check([string]$Name,[bool]$Value){$checks[$Name]=$Value;if(!$Value){throw "Check failed: $Name"}}
$expected='1CB026DDBA084A648832FBD91118A67000373E076DAFA72BE2957B47223A6F81'
$headers=@{Authorization="Bearer $env:GH_TOKEN";Accept='application/vnd.github+json'}
$api='https://api.github.com/repos/JulienLecomteGit/haloryva-studio/actions/artifacts/13886896454'
$meta=Invoke-RestMethod -Uri $api -Headers $headers
Check 'ArchiveIdentity' ($meta.name -ceq 'Haloryva-2.7.1-file11-history-build' -and !$meta.expired -and $meta.workflow_run.head_sha -ceq 'ca14eca6be1e0a19ef9004ba0de2499e00c7c624')
$zip=Join-Path $out 'source-build.zip'
Invoke-WebRequest -Uri "$api/zip" -Headers $headers -OutFile $zip
Check 'ArchiveSHA256' ((Get-FileHash $zip).Hash -eq '32568f54e246a6cbee0ae1d6dddda451341d3fe5134ff9fbec742ee728a9ce67')
$src=Join-Path $out 'source-build'
Expand-Archive -LiteralPath $zip -DestinationPath $src
$app=Join-Path $src 'Haloryva.exe'
$provenance=Get-Content -Raw (Join-Path $src 'build-provenance.json')|ConvertFrom-Json
Check 'OriginalProvenance' ($provenance.SHA256 -eq $expected -and $provenance.FileVersion -ceq '2.7.1.11' -and $provenance.Commit -ceq 'ca14eca6be1e0a19ef9004ba0de2499e00c7c624')
Check 'SourceEXEHash' ((Get-FileHash $app).Hash -eq $expected)
$fv=[Diagnostics.FileVersionInfo]::GetVersionInfo($app)
Check 'FileMetadata' ($fv.FileVersion -ceq '2.7.1.11' -and $fv.ProductVersion -ceq '2.7.1' -and $fv.ProductName -ceq 'Haloryva Studio')
Check 'AssemblyMetadata' ([Reflection.AssemblyName]::GetAssemblyName($app).Version.ToString() -ceq '2.7.1.0')
Check 'StampedSource' ((Get-FileHash (Join-Path $src 'stamped-source/src/Brand.cs')).Hash -eq $provenance.StampedBrandSHA256)
$layout=Join-Path $out 'layout'
$assets=Join-Path $layout 'Assets'
New-Item -ItemType Directory -Path $assets -Force|Out-Null
Copy-Item -LiteralPath $app -Destination (Join-Path $layout 'Haloryva.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Destination $layout
Add-Type -AssemblyName System.Drawing
$iconPath=Join-Path $src 'stamped-source/assets/Haloryva.ico'
# WPF decodes PNG-compressed ICO frames without Icon.ToBitmap's GDI limitation.
Add-Type -AssemblyName PresentationCore
$iconStream=[IO.File]::OpenRead($iconPath)
try {
 $decoder=[Windows.Media.Imaging.BitmapDecoder]::Create($iconStream,[Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
 $frame=$decoder.Frames|Sort-Object PixelWidth -Descending|Select-Object -First 1
 $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add($frame)
 $pngStream=New-Object IO.MemoryStream
 $encoder.Save($pngStream);$pngStream.Position=0
 $bitmap=[Drawing.Bitmap]::FromStream($pngStream)
} finally {$iconStream.Dispose()}
try {
 foreach($spec in @(@('StoreLogo.png',50),@('Square150x150Logo.png',150),@('Square44x44Logo.png',44))){
  $size=[int]$spec[1];$target=New-Object Drawing.Bitmap($size,$size)
  $g=[Drawing.Graphics]::FromImage($target)
  try{$g.Clear([Drawing.Color]::Transparent);$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$g.DrawImage($bitmap,0,0,$size,$size);$target.Save((Join-Path $assets $spec[0]),[Drawing.Imaging.ImageFormat]::Png)}
  finally{$g.Dispose();$target.Dispose()}
  $image=[Drawing.Image]::FromFile((Join-Path $assets $spec[0]))
  try{Check ("Logo-"+$spec[0]) ($image.Width -eq $size -and $image.Height -eq $size)}finally{$image.Dispose()}
 }
}finally{$bitmap.Dispose();$pngStream.Dispose()}
$sdkBin=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/bin/*/x64/makeappx.exe'
$makeappx=Get-ChildItem $sdkBin|Sort-Object FullName -Descending|Select-Object -First 1
if(!$makeappx){throw 'Preinstalled MakeAppx absent'}
$package=Join-Path $out 'HaloryvaStudio_2.7.1.0_x64_STORE_UNSIGNED.msix'
& $makeappx.FullName pack /d $layout /p $package /o 2>&1|Tee-Object (Join-Path $out 'makeappx-pack.log')
Check 'MakeAppxPackValidation' ($LASTEXITCODE -eq 0)
$unpacked=Join-Path $out 'unpacked'
& $makeappx.FullName unpack /p $package /d $unpacked /o 2>&1|Tee-Object (Join-Path $out 'makeappx-unpack.log')
Check 'MakeAppxUnpack' ($LASTEXITCODE -eq 0)
[xml]$xml=Get-Content -Raw (Join-Path $unpacked 'AppxManifest.xml')
$n=New-Object Xml.XmlNamespaceManager($xml.NameTable)
$n.AddNamespace('f','http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$n.AddNamespace('d','http://schemas.microsoft.com/appx/manifest/desktop/windows10')
$id=$xml.SelectSingleNode('/f:Package/f:Identity',$n)
Check 'Identity' ($id.Name -ceq 'JulienLecomte.HaloryvaStudio' -and $id.Publisher -ceq 'CN=2B9D024C-A1E6-44DF-B0A6-4C0CA61BFFD1')
Check 'PackageVersionArchitecture' ($id.Version -ceq '2.7.1.0' -and $id.ProcessorArchitecture -ceq 'x64')
$td=$xml.SelectSingleNode('/f:Package/f:Dependencies/f:TargetDeviceFamily',$n)
Check 'WindowsBounds' ($td.Name -ceq 'Windows.Desktop' -and $td.MinVersion -ceq '10.0.26200.0' -and $td.MaxVersionTested -ceq '10.0.26200.0')
$apps=$xml.SelectNodes('/f:Package/f:Applications/f:Application',$n)
Check 'SingleApplication' ($apps.Count -eq 1 -and $apps[0].Id -ceq 'Haloryva' -and $apps[0].Executable -ceq 'Haloryva.exe' -and $apps[0].EntryPoint -ceq 'Windows.FullTrustApplication')
$tasks=$xml.SelectNodes('//d:StartupTask',$n)
Check 'StartupDisabled' ($tasks.Count -eq 1 -and $tasks[0].TaskId -ceq 'HaloryvaStudioStartup' -and $tasks[0].Enabled -ceq 'false')
$ext=$tasks[0].ParentNode
Check 'SilentStartup' ($ext.Executable -ceq 'Haloryva.exe' -and $ext.GetAttribute('Parameters','http://schemas.microsoft.com/appx/manifest/uap/windows10/10') -ceq '--silent')
$langs=@($xml.SelectNodes('/f:Package/f:Resources/f:Resource',$n)|ForEach-Object {$_.Language}|Sort-Object)
Check 'ThreeLanguages' (($langs -join ',') -ceq 'en-US,es-ES,fr-FR')
$caps=$xml.SelectNodes('/f:Package/f:Capabilities/*',$n)
Check 'OnlyRunFullTrust' ($caps.Count -eq 1 -and $caps[0].Name -ceq 'runFullTrust')
$inventory=@()
foreach($file in Get-ChildItem $layout -File -Recurse){
 $rel=$file.FullName.Substring($layout.Length+1)
 $a=(Get-FileHash $file.FullName).Hash;$b=(Get-FileHash (Join-Path $unpacked $rel)).Hash
 Check ("Extracted-"+$rel) ($a -eq $b)
 $inventory+=@{Path=$rel;SHA256=$a;Size=$file.Length}
}
Check 'PayloadCount' ($inventory.Count -eq 5)
Check 'ExtractedEXEExact' ((Get-FileHash (Join-Path $unpacked 'Haloryva.exe')).Hash -eq $expected)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($package)
try{
 $entries=@($archive.Entries|ForEach-Object FullName)
 Check 'NoSignaturePart' (!($entries -contains 'AppxSignature.p7x'))
 $allowed=@('Haloryva.exe','AppxManifest.xml','Assets/StoreLogo.png','Assets/Square150x150Logo.png','Assets/Square44x44Logo.png','AppxBlockMap.xml','[Content_Types].xml')
 Check 'NoUnexpectedPackageFiles' (@($entries|Where-Object {$allowed -cnotcontains $_.Replace('\','/')}).Count -eq 0)
}finally{$archive.Dispose()}
$result=[ordered]@{
 Status='passed';SourceArtifactId=13886896454;SourceRun=43086180391
 SourceCommit=$provenance.Commit;WorkflowCommit=$env:GITHUB_SHA
 PackageVersion='2.7.1.0';FileVersion=$fv.FileVersion
 EXESHA256=$expected;PackageSHA256=(Get-FileHash $package).Hash;PackageBytes=(Get-Item $package).Length
 ArchiveSHA256=(Get-FileHash $zip).Hash;MakeAppx=$makeappx.FullName
 MinVersion=$td.MinVersion;MaxVersionTested=$td.MaxVersionTested
 Checks=$checks;ChecksPassed=$checks.Count;Payload=$inventory;PackageEntries=$entries
 Compiled=$false;ApplicationExecuted=$false;SelfTestsExecuted=$false;Historical195Executed=$false
 Installed=$false;Signed=$false;WACKExecuted=$false;Submitted=$false;Published=$false
 AudienceConfigured=$false;OSQualificationAcquired=$false;CreatedUTC=[DateTimeOffset]::UtcNow.ToString('o')
}
$result|ConvertTo-Json -Depth 8|Tee-Object (Join-Path $out 'package-results.json')
