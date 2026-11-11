$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP){throw 'GitHub-hosted runner required'}
$out=Join-Path $env:RUNNER_TEMP 'haloryva-preview8-msix'
if(Test-Path -LiteralPath $out){throw 'Fresh job required'}
New-Item -ItemType Directory -Path $out|Out-Null
$report=[ordered]@{Version='2.7.1-preview.8';Status='started';Historical195Executed=$false;Signed=$false;Published=$false;Rebuilt=$false;RealLogonExecuted=$false;LegacyInstallerExecuted=$false;Packages=@();Launches=@();Checks=@();HelperResults=@()}
$name='HaloryvaStudio.MsixLifecycleTest'
$registered=$null
$child=$null
Start-Transcript -Path (Join-Path $out 'transcript.txt')
try {
 $os=Get-CimInstance Win32_OperatingSystem
 if($os.Caption -notmatch 'Windows 11'){throw 'Windows 11 required'}
 $report.Environment=@{Caption=$os.Caption;Build=$os.BuildNumber;Architecture=$os.OSArchitecture;RunnerArch=$env:RUNNER_ARCH;Image=$env:ImageVersion;Interactive=[Environment]::UserInteractive}
 if(Get-AppxPackage -Name $name){throw 'Existing recipe identity; refusing'}
 $oldBuild=Join-Path $env:RUNNER_TEMP 'haloryva-preview7-build'
 $kit=Join-Path $env:RUNNER_TEMP 'haloryva-preview8-kit'
 $oldExe=Join-Path $oldBuild 'Haloryva.exe';$newExe=Join-Path $kit 'Haloryva.exe'
 $oldHash='B16B6AC65FA12F30DB35109190E65B8A0ADF86C2A8E5C2863770BEE28A18F604'
 $newHash='7EA6B66B0802BB5700885ACEB33081861201A8F26F66108CC1EE96A9F3AA7E4E'
 if((Get-FileHash $oldExe).Hash -cne $oldHash -or (Get-FileHash $newExe).Hash -cne $newHash){throw 'Frozen binary mismatch'}
 $kitResult=Get-Content -LiteralPath (Join-Path $kit 'result.json') -Raw|ConvertFrom-Json
 if($kitResult.Status -ne 'targeted-passed' -or $kitResult.SHA256 -cne $newHash){throw 'Only successful targeted kit allowed'}
 $helper=Join-Path $kit 'Haloryva.Preview8.Targeted.exe'
 if((Get-FileHash -LiteralPath $helper).Hash -cne $kitResult.DriverSHA256){throw 'Helper differs'}
 $nativeRoot=Join-Path $env:RUNNER_TEMP 'haloryva-preview7-native'
 $profile=Get-ChildItem -LiteralPath $nativeRoot -Recurse -Filter saved-native-profile.json|Select-Object -First 1
 if(!$profile -or (Get-FileHash -LiteralPath $profile.FullName).Hash -cne '8D2BB7BDF7210CC95124C0C1911F13CD455974E9FDA9EC5318CAF69FB9086B4F'){throw 'Validated Spanish profile missing'}
 $expectedProfile=Get-Content -LiteralPath $profile.FullName -Raw -Encoding UTF8|ConvertFrom-Json
 $report.ExecutableSHA256=$newHash;$report.Preview7SHA256=$oldHash
 $sdk=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/bin'
 $maker=Get-ChildItem -LiteralPath $sdk -Directory|Where-Object {$_.Name -match '^10[.]0[.]\d+[.]0$'}|Sort-Object {[Version]$_.Name} -Descending|ForEach-Object {Join-Path $_.FullName 'x64/makeappx.exe'}|Where-Object {Test-Path -LiteralPath $_}|Select-Object -First 1
 if(!$maker){throw 'Installed MakeAppx required'}
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class PackageProbe {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern int GetPackageFullName(IntPtr process,ref uint length,StringBuilder name);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenEvent(uint rights,bool inherit,string name);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetEvent(IntPtr handle);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 public static int ExitEvent(string name,bool signal) {
   IntPtr h=OpenEvent(signal?2u:0x100000u,false,name);
   if(h==IntPtr.Zero) return Marshal.GetLastWin32Error();
   try { if(signal && !SetEvent(h)) return Marshal.GetLastWin32Error(); return 0; }
   finally { CloseHandle(h); }
 }
 delegate bool EnumProc(IntPtr hwnd,IntPtr state);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr state);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumProc callback,IntPtr state);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int length);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int length);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,UIntPtr count,StringBuilder text,uint flags,uint timeout,out UIntPtr result);
 static string Describe(IntPtr hwnd,bool child) {
   var title=new StringBuilder(2048); var cls=new StringBuilder(256);
   GetWindowText(hwnd,title,title.Capacity); GetClassName(hwnd,cls,cls.Capacity);
   if(child && title.Length==0) { UIntPtr result; SendMessageTimeout(hwnd,13,(UIntPtr)2048,title,2,200,out result); }
   return "hwnd="+hwnd.ToInt64().ToString("X")+" visible="+IsWindowVisible(hwnd)+" class="+cls+" text="+title;
 }
 public static string[] Windows(uint pid) {
   var lines=new List<string>();
   EnumWindows(delegate(IntPtr hwnd,IntPtr unused) {
     uint owner; GetWindowThreadProcessId(hwnd,out owner); if(owner!=pid) return true;
     lines.Add(Describe(hwnd,false));
     EnumChildWindows(hwnd,delegate(IntPtr child,IntPtr state) { if(lines.Count<100) lines.Add("  "+Describe(child,true)); return lines.Count<100; },IntPtr.Zero);
     return lines.Count<100;
   },IntPtr.Zero);
   return lines.ToArray();
 }
}
[ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IActivation {
 [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.LPWStr)]string args,uint options,out uint pid);
}
public static class ActivatePackaged {
 public static uint Launch(string id,string args) {
  object manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
  try { uint pid; int hr=((IActivation)manager).ActivateApplication(id,args,0,out pid);
    if(hr<0) Marshal.ThrowExceptionForHR(hr); return pid;
  } finally { Marshal.ReleaseComObject(manager); }
 }
}

'@
 function Build-Package([string]$version,[string]$exe,[string]$hash,[bool]$withStartup) {
  $layout=Join-Path $out ('layout-'+$version)
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Inputs/msix-layout') -Destination $layout -Recurse
  Copy-Item -LiteralPath $exe -Destination (Join-Path $layout 'Haloryva.exe') -Force
  [xml]$xml=Get-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Raw -Encoding UTF8
  $xml.Package.Identity.Name=$name
  $xml.Package.Identity.Publisher='CN=JulienLecomteGit, OID.2.25.311729368913984317654407730594956997722=1'
  $xml.Package.Identity.Version=$version
  $xml.Package.Properties.DisplayName='Haloryva Studio — lifecycle test'
  $ns=$xml.DocumentElement.NamespaceURI
  $resources=$xml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Resources']")
  $resources.RemoveAll()
  foreach($lang in @('fr-fr','en-us','es-es')){$r=$xml.CreateElement('Resource',$ns);$r.SetAttribute('Language',$lang);[void]$resources.AppendChild($r)}
  if($withStartup) {
   Copy-Item -LiteralPath $helper -Destination $layout
   $desktop='http://schemas.microsoft.com/appx/manifest/desktop/windows10'
   $uap10='http://schemas.microsoft.com/appx/manifest/uap/windows10/10'
   foreach($pair in @(@('desktop',$desktop),@('uap10',$uap10))){
    $attr=$xml.CreateAttribute('xmlns',$pair[0],'http://www.w3.org/2000/xmlns/');$attr.Value=$pair[1];[void]$xml.DocumentElement.Attributes.Append($attr)
   }
   $xml.DocumentElement.SetAttribute('IgnorableNamespaces','uap rescap desktop uap10')
   $appNode=$xml.SelectSingleNode("//*[local-name()='Application' and @Id='Haloryva']")
   $extensions=$xml.CreateElement('Extensions',$ns)
   $extension=$xml.CreateElement('desktop','Extension',$desktop)
   $extension.SetAttribute('Category','windows.startupTask');$extension.SetAttribute('Executable','Haloryva.exe');$extension.SetAttribute('EntryPoint','Windows.FullTrustApplication')
   $params=$xml.CreateAttribute('uap10','Parameters',$uap10);$params.Value='--silent';[void]$extension.Attributes.Append($params)
   $task=$xml.CreateElement('desktop','StartupTask',$desktop)
   $task.SetAttribute('TaskId','HaloryvaStudioStartup');$task.SetAttribute('Enabled','false');$task.SetAttribute('DisplayName','Haloryva Studio Lifecycle Test')
   [void]$extension.AppendChild($task);[void]$extensions.AppendChild($extension);[void]$appNode.AppendChild($extensions)
   $recipe=$xml.CreateElement('Application',$ns);$recipe.SetAttribute('Id','Recipe');$recipe.SetAttribute('Executable','Haloryva.Preview8.Targeted.exe');$recipe.SetAttribute('EntryPoint','Windows.FullTrustApplication')
   $visual=$xml.CreateElement('uap','VisualElements','http://schemas.microsoft.com/appx/manifest/uap/windows10')
   $visual.SetAttribute('DisplayName','Haloryva targeted recipe');$visual.SetAttribute('Description','Disposable GitHub test helper')
   $visual.SetAttribute('BackgroundColor','transparent');$visual.SetAttribute('Square150x150Logo','Assets\Square150x150Logo.png');$visual.SetAttribute('Square44x44Logo','Assets\Square44x44Logo.png');$visual.SetAttribute('AppListEntry','none')
   [void]$recipe.AppendChild($visual);[void]$appNode.ParentNode.AppendChild($recipe)
  }
  $manifest=Join-Path $layout 'AppxManifest.xml';$xml.Save($manifest)
  $package=Join-Path $out ('Haloryva-test-'+$version+'.msix')
  & $maker pack /d $layout /p $package /o 2>&1|Out-File (Join-Path $out ('pack-'+$version+'.log'))
  if($LASTEXITCODE -ne 0){throw "Structure failed $version"}
  $zip=[IO.Compression.ZipFile]::OpenRead($package)
  try {
   if($zip.GetEntry('AppxSignature.p7x')){throw 'Unexpected signature'}
   $stream=$zip.GetEntry('Haloryva.exe').Open();$sha=[Security.Cryptography.SHA256]::Create()
   try {$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
   if($actual -cne $hash){throw 'Packaged executable changed'}
  }finally{$zip.Dispose()}
  $report.Packages+=,@{Version=$version;SHA256=(Get-FileHash -LiteralPath $package).Hash;ExecutableSHA256=$hash;Startup=$withStartup}
  return $package
 }
 function Register-Package([string]$path,[string]$version,[string]$hash) {
  Add-AppxPackage -Path $path -AllowUnsigned
  $script:registered=Get-AppxPackage -Name $name
  if(!$script:registered -or [string]$script:registered.Version -cne $version){throw 'Wrong installed package version'}
  if((Get-FileHash -LiteralPath (Join-Path $script:registered.InstallLocation 'Haloryva.exe')).Hash -cne $hash){throw 'Installed EXE changed'}
  if($report.Contains('Family') -and $report.Family -cne $script:registered.PackageFamilyName){throw 'Family changed'}
  $report.Family=$script:registered.PackageFamilyName
 }
 function Launch-App([string]$version,[string]$language,[bool]$silent) {
  $argsForApp='';if($silent){$argsForApp='--silent'}
  $appPid=[ActivatePackaged]::Launch(($script:registered.PackageFamilyName+'!Haloryva'),$argsForApp)
  $script:child=Get-Process -Id $appPid
  [uint32]$size=512;$identity=New-Object Text.StringBuilder 512
  if([PackageProbe]::GetPackageFullName($script:child.Handle,[ref]$size,$identity) -ne 0 -or $identity.ToString() -cne $script:registered.PackageFullName){throw 'Runtime identity not confirmed'}
  $eventName='Local\HaloryvaStudio.Exit.'+$appPid
  $ready=$false
  for($i=0;$i -lt 120;$i++){
   if([PackageProbe]::ExitEvent($eventName,$false) -eq 0){$ready=$true;break}
   $script:child.Refresh();if($script:child.HasExited){throw 'App exited before ready'};Start-Sleep -Milliseconds 250
  }
  if(!$ready){throw 'App never ready'}
  Start-Sleep -Seconds 2
  $windows=[PackageProbe]::Windows($appPid)
  $windows|Set-Content -LiteralPath (Join-Path $out ('windows-'+$version+'-'+$silent+'.txt')) -Encoding UTF8
  $text=$windows -join "`n"
  if($silent){
   if($text -match 'text=(Accueil|Home|Inicio)|text=(Préférences|Preferences|Preferencias)'){throw 'Studio opened during silent activation'}
  }else{
   $word=if($language -eq 'es'){'Inicio'}elseif($language -eq 'en'){'Home'}else{'Accueil'}
   if(!$text.Contains($word)){throw "Visible language missing: $language"}
  }
  if([PackageProbe]::ExitEvent($eventName,$true) -ne 0){throw 'Exit request failed'}
  if(!$script:child.WaitForExit(15000)){throw 'App did not stop cleanly'}
  if($script:child.ExitCode -ne 0){throw 'Nonzero app exit'}
  $report.Launches+=,@{Version=$version;Language=$language;Silent=$silent;IdentityConfirmed=$true;Ready=$true;ExitCode=0;ForcedStop=$false}
  $script:child=$null
 }
 function Compare-Profile([string]$path,$reference,[string]$id) {
  $actual=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
  foreach($property in $reference.PSObject.Properties){
   $field=$property.Name
   if((ConvertTo-Json -InputObject $actual.$field -Compress -Depth 10) -cne (ConvertTo-Json -InputObject $reference.$field -Compress -Depth 10)){throw "Profile field changed: $id / $field"}
  }
  $report.Checks+=,@{Id=$id;Passed=$true;Fields=@($reference.PSObject.Properties).Count;SHA256=(Get-FileHash -LiteralPath $path).Hash}
 }
 function Run-PackageHelper([string]$mode) {
  $dir=Join-Path $out $mode;New-Item -ItemType Directory -Path $dir|Out-Null
  $token=[Guid]::NewGuid().ToString('N')
  @{GitHubHosted=$true;Token=$token}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $dir 'authorization.json') -Encoding UTF8
  $result=Join-Path $dir 'result.json'
  $helperPid=[ActivatePackaged]::Launch(($script:registered.PackageFamilyName+'!Recipe'),($mode+' "'+$result+'" '+$token))
  for($i=0;$i -lt 240 -and !(Test-Path -LiteralPath $result);$i++){Start-Sleep -Milliseconds 250}
  if(!(Test-Path -LiteralPath $result)){throw 'Packaged helper result missing'}
  $r=Get-Content -LiteralPath $result -Raw -Encoding UTF8|ConvertFrom-Json
  $report.HelperResults+=,$r
  if(!$r.Passed){$r|ConvertTo-Json -Depth 8|Write-Output;throw 'Real packaged API checks failed'}
  $hp=Get-Process -Id $helperPid -ErrorAction SilentlyContinue
  if($hp -and !$hp.WaitForExit(10000)){throw 'Helper remains running'}
 }
 $A=Build-Package '2.7.1.70' $oldExe $oldHash $false
 $B=Build-Package '2.7.1.80' $newExe $newHash $true
 $C=Build-Package '2.7.1.81' $newExe $newHash $true
 Register-Package $A '2.7.1.70' $oldHash
 Launch-App '2.7.1.70' 'fr' $false
 $dataRoot=Join-Path $env:LOCALAPPDATA ('Packages\'+$registered.PackageFamilyName)
 $oldProfile=Join-Path $dataRoot 'LocalCache\Local\Haloryva\settings.json'
 if(!(Test-Path -LiteralPath $oldProfile)){throw 'Old package profile not found'}
 Copy-Item -LiteralPath $profile.FullName -Destination $oldProfile -Force
 $sourceHash=(Get-FileHash -LiteralPath $oldProfile).Hash
 # Add a distinct, synthetic legacy fixture only after A has its private profile.
 $legacy=Join-Path $env:LOCALAPPDATA 'Haloryva'
 $legacyFile=Join-Path $legacy 'settings.json'
 if(Test-Path -LiteralPath $legacyFile){throw 'Existing real legacy profile; refuse fixture write'}
 New-Item -ItemType Directory -Path $legacy -Force|Out-Null
 $fixture='{"AnnotationThickness":9,"LaunchAtLogin":true}'
 [IO.File]::WriteAllText($legacyFile,$fixture,(New-Object Text.UTF8Encoding($false)))
 $legacyHash=(Get-FileHash -LiteralPath $legacyFile).Hash
 Register-Package $B '2.7.1.80' $newHash
 Launch-App '2.7.1.80' 'es' $false
 $newProfile=Join-Path $dataRoot 'LocalState\Haloryva\settings.json'
 Compare-Profile $newProfile $expectedProfile 'U1-profile-migrated'
 if((Get-FileHash -LiteralPath $oldProfile).Hash -cne $sourceHash -or (Get-FileHash -LiteralPath $legacyFile).Hash -cne $legacyHash){throw 'Migration touched source'}
 $report.Checks+=,@{Id='M3-old-cache-priority-and-sources-intact';Passed=$true}
 Run-PackageHelper 'package-enable'
 Register-Package $C '2.7.1.81' $newHash
 Launch-App '2.7.1.81' 'es' $false
 Compare-Profile $newProfile $expectedProfile 'U2-profile-retained'
 Run-PackageHelper 'package-retained'
 Launch-App '2.7.1.81' 'es' $true
 Compare-Profile $newProfile $expectedProfile 'silent-relaunch-profile-retained'
 $declined=$false
 try {Add-AppxPackage -Path $B -AllowUnsigned -ErrorAction Stop}
 catch { if($_.ToString() -match '80073D06|higher version|version.*sup'){ $declined=$true } else {throw} }
 if(!$declined -or [string](Get-AppxPackage -Name $name).Version -cne '2.7.1.81'){throw 'Downgrade protection not confirmed'}
 Compare-Profile $newProfile $expectedProfile 'U3-rejected-downgrade-profile-intact'
 if((Get-FileHash -LiteralPath $oldProfile).Hash -cne $sourceHash -or (Get-FileHash -LiteralPath $legacyFile).Hash -cne $legacyHash){throw 'Source changed'}
 $report.Checks+=,@{Id='U3-downgrade-rejected-no-force';Passed=$true}
 Remove-AppxPackage -Package $registered.PackageFullName
 if(Get-AppxPackage -Name $name){throw 'Test package remains'}
 $report.CleanupPackageRemoved=$true
 $registered=$null
 $report.Status='msix8-passed'
}catch{
 $report.Status='failed';$report.Error=$_.ToString()
 throw
}finally{
 if($registered -and !$child){try{Remove-AppxPackage -Package $registered.PackageFullName -ErrorAction Stop}catch{$report.CleanupError=$_.ToString()}}
 $report|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
 $report|ConvertTo-Json -Depth 15|Write-Output
 Stop-Transcript
}
