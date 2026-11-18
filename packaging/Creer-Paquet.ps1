#requires -Version 5.1
<# Paquet Store 2.7.2 : utilise le binaire qui vient de passer les tests R5.
Identite, tache de demarrage et logos issus du paquet 2.7.1 de production.
Aucune signature, installation, modification de certificat ou publication. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Binaire,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ResumeCompilation,
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$Destination,
    [ValidateNotNullOrEmpty()][string]$ProjetSources = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or
    $PSVersionTable.PSVersion.Minor -ne 1 -or -not [Environment]::Is64BitProcess) {
    throw 'Windows PowerShell 5.1 Desktop x64 requis.'
}
$racine = [IO.Path]::GetFullPath($ProjetSources)
$sortie = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $sortie) { throw 'Le dossier de fabrication du paquet doit etre neuf.' }
New-Item -ItemType Directory -Path $sortie | Out-Null
$utf8 = New-Object Text.UTF8Encoding($true)
$checks = [ordered]@{}
$resultat = [ordered]@{
    Status = 'EN_COURS'; ProductVersion = '2.7.2'; FileVersion = '2.7.2.6'; PackageVersion = '2.7.2.0'
    BuildCommit = if ($env:GITHUB_SHA -match '^[A-Fa-f0-9]{40}$') { $env:GITHUB_SHA } else { $null }
    HistoricalPackageRun = 43115017982; HistoricalPackageArtifact = 13898131858
    EXESHA256 = $null; PackageSHA256 = $null; PackageBytes = $null; MakeAppx = $null
    Checks = $checks; ChecksPassed = 0; Payload = @(); PackageEntries = @(); Error = $null
    CompiledHere = $false; SourceCompilationAndTestsVerified = $false
    Installed = $false; Signed = $false; WACKExecuted = $false; Submitted = $false; Published = $false
    DateUTC = $null
}
function Check {
    param([string]$Nom, [bool]$Valeur)
    $checks[$Nom] = $Valeur
    if (-not $Valeur) { throw ('Controle du paquet echoue : ' + $Nom) }
}
function Invoke-MakeAppx {
    param([string]$Executable, [string]$Arguments, [string]$Journal)
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = $Arguments
    $info.WorkingDirectory = $sortie
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw 'MakeAppx ne demarre pas.' }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $termine = $process.WaitForExit(120000)
        if (-not $termine) { $process.Kill(); $process.WaitForExit() }
        $texte = $stdout.GetAwaiter().GetResult() + "`r`n" + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($Journal, $texte, $utf8)
        if (-not $termine) { throw 'MakeAppx a depasse le delai de deux minutes.' }
        return [int]$process.ExitCode
    }
    finally { $process.Dispose() }
}

$code = 1
try {
    Check 'ExecutablePresent' (Test-Path -LiteralPath $Binaire -PathType Leaf)
    Check 'CompilationSummaryPresent' (Test-Path -LiteralPath $ResumeCompilation -PathType Leaf)
    $resume = Get-Content -LiteralPath $ResumeCompilation -Raw -Encoding UTF8 | ConvertFrom-Json
    Check 'SuccessfulBuild' ($resume.Compilation -ceq 'BUILD_OK' -and $resume.VersionCible -ceq '2.7.2' -and
        $resume.VersionVerificateur -ceq 'R5' -and $resume.Mode -ceq 'Tout' -and $null -eq $resume.Erreur)
    foreach ($nom in @('Tests', 'TestsRenduTrainee', 'TestsPrecisionTrainee', 'TestsApercuCadence',
        'TestsDiagnosticEcriture', 'PositionsHorodatees', 'TestsGeometrie', 'TestsTrainee')) {
        Check ('Successful-' + $nom) ($resume.$nom -ceq 'TESTS_OK')
    }
    foreach ($nom in @('TestExitCode', 'TrailRenderingExitCode', 'PositionsExitCode', 'PrecisionExitCode',
        'ApercuCadenceExitCode', 'DiagnosticEcritureExitCode', 'GeometryExitCode', 'TrailExitCode')) {
        Check ('Exit-' + $nom) ($null -ne $resume.$nom -and [int]$resume.$nom -eq 0)
    }
    Check 'VerifierUnchanged' ((Get-FileHash -LiteralPath (Join-Path $racine 'Verifier-Compiler.ps1') -Algorithm SHA256).Hash -ceq $resume.VerificateurSHA256)
    Check 'SourceInventoryUnchanged' ((Get-FileHash -LiteralPath (Join-Path $racine 'SHA256.json') -Algorithm SHA256).Hash -ceq $resume.InventaireKitSHA256)
    $expected = [string]$resume.AppSHA256
    Check 'ExpectedHashShape' ($expected -cmatch '^[A-F0-9]{64}$')
    Check 'ExactTestedExecutable' ((Get-FileHash -LiteralPath $Binaire -Algorithm SHA256).Hash -ceq $expected)
    $fv = [Diagnostics.FileVersionInfo]::GetVersionInfo($Binaire)
    Check 'FileMetadata' ($fv.ProductName -ceq 'Haloryva Studio' -and $fv.ProductVersion -ceq '2.7.2' -and $fv.FileVersion -ceq '2.7.2.6')
    Check 'AssemblyMetadata' ([Reflection.AssemblyName]::GetAssemblyName($Binaire).Version.ToString() -ceq '2.7.2.0')
    $resultat.EXESHA256 = $expected
    $resultat.SourceCompilationAndTestsVerified = $true

    $layout = Join-Path $sortie 'layout'
    $assets = Join-Path $layout 'Assets'
    New-Item -ItemType Directory -Path $assets | Out-Null
    Copy-Item -LiteralPath $Binaire -Destination (Join-Path $layout 'Haloryva.exe')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Destination (Join-Path $layout 'AppxManifest.xml')
    $logos = @(
        @{ Name = 'StoreLogo.png'; Size = 50; SHA256 = '9DD27B578325400B518B9748FDA9223B297B880BE2F71EFC0CB245A553D8BB14' },
        @{ Name = 'Square150x150Logo.png'; Size = 150; SHA256 = '6486B94F7AFA87E365E2A63AB3FBB3E889E419E5F30609F57847029F96D0FC32' },
        @{ Name = 'Square44x44Logo.png'; Size = 44; SHA256 = 'D6581AF6778EEB05CE4FB6F8A0EC6EB5B473268997D76BAC3817F8A7BF67F588' }
    )
    Add-Type -AssemblyName System.Drawing
    foreach ($logo in $logos) {
        $sourceLogo = Join-Path (Join-Path $PSScriptRoot 'Assets') $logo.Name
        Check ('HistoricalLogo-' + $logo.Name) ((Get-FileHash -LiteralPath $sourceLogo -Algorithm SHA256).Hash -ceq $logo.SHA256)
        $image = [Drawing.Image]::FromFile($sourceLogo)
        try { Check ('LogoSize-' + $logo.Name) ($image.Width -eq $logo.Size -and $image.Height -eq $logo.Size) }
        finally { $image.Dispose() }
        Copy-Item -LiteralPath $sourceLogo -Destination (Join-Path $assets $logo.Name)
    }

    $sdkRoot = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits\10\bin'
    $makeappx = $null
    if (Test-Path -LiteralPath $sdkRoot -PathType Container) {
        $versions = @(Get-ChildItem -LiteralPath $sdkRoot -Directory | Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending)
        foreach ($version in $versions) {
            $candidate = Join-Path $version.FullName 'x64\makeappx.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $makeappx = $candidate; break }
        }
    }
    if ($null -eq $makeappx) { throw 'MakeAppx x64 absent des SDK Windows installes. Aucune installation automatique.' }
    $resultat.MakeAppx = $makeappx
    $paquet = Join-Path $sortie 'HaloryvaStudio_2.7.2.0_x64_STORE_UNSIGNED.msix'
    $argumentsPack = 'pack /d "' + $layout + '" /p "' + $paquet + '"'
    Check 'MakeAppxPackValidation' ((Invoke-MakeAppx -Executable $makeappx -Arguments $argumentsPack -Journal (Join-Path $sortie 'makeappx-pack.log')) -eq 0)
    $unpacked = Join-Path $sortie 'verification'
    $argumentsUnpack = 'unpack /p "' + $paquet + '" /d "' + $unpacked + '"'
    Check 'MakeAppxUnpack' ((Invoke-MakeAppx -Executable $makeappx -Arguments $argumentsUnpack -Journal (Join-Path $sortie 'makeappx-unpack.log')) -eq 0)

    $xml = New-Object Xml.XmlDocument
    $xml.XmlResolver = $null
    $xml.Load((Join-Path $unpacked 'AppxManifest.xml'))
    $ns = New-Object Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $ns.AddNamespace('d', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10')
    $identites = @($xml.SelectNodes('/f:Package/f:Identity', $ns))
    Check 'SingleIdentity' ($identites.Count -eq 1)
    $id = $identites[0]
    Check 'StoreIdentityPreserved' ($id.Name -ceq 'JulienLecomte.HaloryvaStudio' -and $id.Publisher -ceq 'CN=2B9D024C-A1E6-44DF-B0A6-4C0CA61BFFD1')
    Check 'PackageVersionArchitecture' ($id.Version -ceq '2.7.2.0' -and $id.ProcessorArchitecture -ceq 'x64')
    $targets = @($xml.SelectNodes('/f:Package/f:Dependencies/f:TargetDeviceFamily', $ns))
    Check 'WindowsBoundsPreserved' ($targets.Count -eq 1 -and $targets[0].Name -ceq 'Windows.Desktop' -and
        $targets[0].MinVersion -ceq '10.0.26200.0' -and $targets[0].MaxVersionTested -ceq '10.0.26200.0')
    $apps = @($xml.SelectNodes('/f:Package/f:Applications/f:Application', $ns))
    Check 'SingleApplicationPreserved' ($apps.Count -eq 1 -and $apps[0].Id -ceq 'Haloryva' -and
        $apps[0].Executable -ceq 'Haloryva.exe' -and $apps[0].EntryPoint -ceq 'Windows.FullTrustApplication')
    $tasks = @($xml.SelectNodes('//d:StartupTask', $ns))
    Check 'StartupTaskPreserved' ($tasks.Count -eq 1 -and $tasks[0].TaskId -ceq 'HaloryvaStudioStartup' -and $tasks[0].Enabled -ceq 'false')
    $extension = $tasks[0].ParentNode
    Check 'SilentStartupPreserved' ($extension.Executable -ceq 'Haloryva.exe' -and $extension.GetAttribute('Parameters', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10') -ceq '--silent')
    $langues = @($xml.SelectNodes('/f:Package/f:Resources/f:Resource', $ns) | ForEach-Object { $_.Language } | Sort-Object)
    Check 'ThreeLanguages' (($langues -join ',') -ceq 'en-US,es-ES,fr-FR')
    $capacites = @($xml.SelectNodes('/f:Package/f:Capabilities/*', $ns))
    Check 'OnlyRunFullTrust' ($capacites.Count -eq 1 -and $capacites[0].Name -ceq 'runFullTrust')

    $payload = @()
    foreach ($fichier in (Get-ChildItem -LiteralPath $layout -Recurse -File | Sort-Object FullName)) {
        $relative = $fichier.FullName.Substring($layout.Length + 1)
        $hash = (Get-FileHash -LiteralPath $fichier.FullName -Algorithm SHA256).Hash
        $extrait = Join-Path $unpacked $relative
        Check ('Extracted-' + $relative) ((Get-FileHash -LiteralPath $extrait -Algorithm SHA256).Hash -ceq $hash)
        $payload += [ordered]@{ Path = $relative.Replace('\', '/'); SHA256 = $hash; Bytes = $fichier.Length }
    }
    Check 'PayloadCount' ($payload.Count -eq 5)
    Check 'ExtractedExecutableExact' ((Get-FileHash -LiteralPath (Join-Path $unpacked 'Haloryva.exe') -Algorithm SHA256).Hash -ceq $expected)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($paquet)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $allowed = @('Haloryva.exe', 'AppxManifest.xml', 'Assets/StoreLogo.png', 'Assets/Square150x150Logo.png', 'Assets/Square44x44Logo.png', 'AppxBlockMap.xml', '[Content_Types].xml')
        Check 'NoSignaturePart' (-not ($entries -contains 'AppxSignature.p7x'))
        Check 'NoUnexpectedPackageFiles' (@($entries | Where-Object { $allowed -cnotcontains $_ }).Count -eq 0)
        Check 'AllPackageFilesPresent' (@($allowed | Where-Object { $entries -cnotcontains $_ }).Count -eq 0)
    }
    finally { $archive.Dispose() }
    Copy-Item -LiteralPath $ResumeCompilation -Destination (Join-Path $sortie 'Resume-compilation.json')
    Copy-Item -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Destination (Join-Path $sortie 'AppxManifest.xml')
    $resultat.Payload = $payload
    $resultat.PackageEntries = $entries
    $resultat.PackageSHA256 = (Get-FileHash -LiteralPath $paquet -Algorithm SHA256).Hash
    $resultat.PackageBytes = (Get-Item -LiteralPath $paquet).Length
    $resultat.Status = 'PAQUET_STRUCTURE_OK_NON_SIGNE_NON_INSTALLE'
    $code = 0
}
catch {
    $resultat.Status = 'ECHEC'
    $resultat.Error = $_.Exception.Message
    Write-Host ('ERREUR : ' + $_.Exception.Message) -ForegroundColor Red
}
finally {
    $resultat.ChecksPassed = @($checks.GetEnumerator() | Where-Object { $_.Value -eq $true }).Count
    $resultat.DateUTC = [DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText((Join-Path $sortie 'Resultat-paquet.json'), ($resultat | ConvertTo-Json -Depth 8), $utf8)
    Write-Host ('Bilan paquet : ' + $resultat.Status)
}
exit $code

