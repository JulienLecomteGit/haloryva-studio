param([ValidateSet('functional','msix')][string]$Mode)
$ErrorActionPreference='Stop'
# Refuse execution on the user's PC and on a self-hosted runner.
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$env:RUNNER_TEMP) { throw 'GitHub-hosted runner required. No local execution.' }
$out=Join-Path $env:RUNNER_TEMP ('haloryva-'+$Mode)
if(Test-Path -LiteralPath $out) { throw 'Fresh job required.' }
New-Item -ItemType Directory -Path $out | Out-Null
$report=[ordered]@{Mode=$Mode;Status='started';Steps=@();Tests195Repeated=$false;Signed=$false;ReleasePublished=$false}
$child=$null; $registered=$null
Start-Transcript -Path (Join-Path $out 'transcript.txt')
try {
    $os=Get-CimInstance Win32_OperatingSystem
    [ordered]@{Caption=$os.Caption;Build=$os.BuildNumber;OSArchitecture=$os.OSArchitecture;RunnerArch=$env:RUNNER_ARCH;Interactive=[Environment]::UserInteractive;Session=[Diagnostics.Process]::GetCurrentProcess().SessionId;Image=$env:ImageVersion} | ConvertTo-Json | Set-Content (Join-Path $out 'environment.json')
    $expected=(Get-Content (Join-Path $PSScriptRoot 'Inputs/Haloryva.sha256') -Raw).Trim()
    $app=Join-Path $PSScriptRoot 'Inputs/msix-layout/Haloryva.exe'
    if((Get-FileHash -LiteralPath $app).Hash -ne $expected) { throw 'Preview binary SHA256 mismatch.' }
    if($Mode -eq 'functional') {
        if(![Environment]::UserInteractive -or [Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) { throw 'No interactive desktop: functional tests NOT RUN.' }
        Copy-Item -LiteralPath $app -Destination $out
        $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        $rsp=Join-Path $out 'Recipe.rsp'
        $compilerArgs=@('/nologo','/target:exe','/platform:x64','/langversion:5','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',('/out:"'+(Join-Path $out 'Recipe.exe')+'"'),('"'+(Join-Path $PSScriptRoot 'Recipe-Cloud.cs')+'"'))
        [IO.File]::WriteAllLines($rsp,$compilerArgs,(New-Object Text.UTF8Encoding($true)))
        & $compiler ('@'+$rsp) 2>&1 | Out-File (Join-Path $out 'compile.log')
        if($LASTEXITCODE -ne 0) { throw 'Test driver compilation failed.' }
        foreach($stage in @('smoke','functional')) {
            $run='cloud-'+$env:GITHUB_RUN_ID+'-'+$stage
            $child=Start-Process -FilePath (Join-Path $out 'Recipe.exe') -ArgumentList ($stage+' '+$run+' --silent') -PassThru -WindowStyle Hidden
            $done=$child.WaitForExit(90000)
            $logs=Join-Path $env:LOCALAPPDATA ('RecipeHaloryvaRuns/'+$run)
            if(Test-Path -LiteralPath $logs) { Copy-Item -LiteralPath $logs -Destination $out -Recurse }
            if(!$done) { $child.Kill(); throw "$stage timed out: NOT validated." }
            if($child.ExitCode -ne 0) { throw "$stage failed with exit code $($child.ExitCode)." }
            $report.Steps+=($stage+' passed')
        }
        $report.Status='automated-sequences-passed-review-NOT_RUN-lines'
    } else {
        if($os.Caption -notmatch 'Windows 11') { throw 'MSIX pilot requires a Windows 11 runner.' }
        $admin=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if(!$admin.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Unsigned executable MSIX testing requires administrator on the disposable runner.' }
        $layout=Join-Path $out 'layout'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Inputs/msix-layout') -Destination $layout -Recurse
        $manifest=Join-Path $layout 'AppxManifest.xml'
        [xml]$xml=Get-Content -LiteralPath $manifest -Raw
        # Official Windows 11 unsigned-testing identity; original manifest is untouched.
        $xml.Package.Identity.Name='HaloryvaStudio.CloudUnsignedTest'
        $xml.Package.Identity.Publisher='CN=JulienLecomteGit, OID.2.25.311729368913984317654407730594956997722=1'
        $xml.Save($manifest)
        $sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
        $maker=Get-ChildItem -LiteralPath $sdk -Directory | Where-Object {$_.Name -match '^10[.]0[.]\d+[.]0$'} | Sort-Object {[Version]$_.Name} -Descending | ForEach-Object {Join-Path $_.FullName 'x64/makeappx.exe'} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
        if(!$maker) { throw 'Windows SDK MakeAppx not available on runner.' }
        $package=Join-Path $out 'Haloryva-cloud-unsigned.msix'
        & $maker pack /d $layout /p $package /o 2>&1 | Out-File (Join-Path $out 'pack.log')
        if($LASTEXITCODE -ne 0) { throw 'MSIX structure failed.' }
        $report.Steps+='MSIX test package created'
        Add-AppxPackage -Path $package -AllowUnsigned
        $registered=Get-AppxPackage -Name HaloryvaStudio.CloudUnsignedTest
        if(!$registered) { throw 'Package not found after installation.' }
        $registered | Select-Object Name,PackageFullName,PackageFamilyName,InstallLocation,Status | ConvertTo-Json | Set-Content (Join-Path $out 'package.json')
        if((Get-FileHash -LiteralPath (Join-Path $registered.InstallLocation 'Haloryva.exe')).Hash -ne $expected) { throw 'Installed executable differs from preview.' }
        $report.Steps+='unsigned MSIX installed; binary unchanged'
        Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class PackageProbe {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern int GetPackageFullName(IntPtr process,ref uint length,StringBuilder name);
}
'@
        for($cycle=1;$cycle -le 2;$cycle++) {
            Start-Process explorer.exe -ArgumentList ('shell:AppsFolder\'+$registered.PackageFamilyName+'!Haloryva')
            $child=$null
            for($n=0;$n -lt 40 -and !$child;$n++) {
                Start-Sleep -Milliseconds 250
                $child=Get-Process Haloryva -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $registered.InstallLocation 'Haloryva.exe')} | Select-Object -First 1
            }
            if(!$child) { throw "Packaged launch $cycle not observed." }
            [uint32]$size=512; $name=New-Object Text.StringBuilder 512
            $rc=[PackageProbe]::GetPackageFullName($child.Handle,[ref]$size,$name)
            if($rc -ne 0 -or $name.ToString() -ne $registered.PackageFullName) { throw 'Process package identity not confirmed.' }
            Start-Sleep -Seconds 3
            if($child.HasExited) { throw 'Packaged app exited unexpectedly.' }
            $ev=[Threading.EventWaitHandle]::OpenExisting('Local\HaloryvaStudio.Exit.'+$child.Id)
            try { [void]$ev.Set() } finally { $ev.Dispose() }
            if(!$child.WaitForExit(15000) -or $child.ExitCode -ne 0) { throw 'Packaged app did not shut down normally.' }
            $report.Steps+="packaged launch $cycle identity confirmed and clean shutdown"
        }
        Remove-AppxPackage -Package $registered.PackageFullName
        if(Get-AppxPackage -Name HaloryvaStudio.CloudUnsignedTest) { throw 'Package remains registered after removal.' }
        $registered=$null
        $report.Steps+='package removed for test user'
        $report.Status='unsigned-test-identity-passed-NOT-production-signature'
    }
} catch {
    $report.Status='failed-or-environment-blocked'
    $report.Error=$_.Exception.Message
    throw
} finally {
    if($child -and !$child.HasExited) { $child.Kill() }
    if($registered) { Remove-AppxPackage -Package $registered.PackageFullName -ErrorAction Continue }
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'result.json')
    Stop-Transcript
}
