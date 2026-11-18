#requires -Version 5.1
<#
Version R5 - 18 novembre 2026. Verification de la livraison 2.7.2, adaptee du kit exp4 valide.
Reprend la selection WinRT R2 validee sur Windows : un seul Windows.winmd.
L'empreinte attendue du manifeste est fournie par le bloc de lancement ;
le manifeste inclut ce script et tous les fichiers du kit, sans boucle de hash.
Aucune installation, modification de profil, ouverture d'interface ou commande Git.
Apres compilation : --selftest et --trail-selftest uniquement.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$ProjetSources = $PSScriptRoot,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$EmpreinteInventaire,
    [ValidateSet('Tout', 'Tests')]
    [string]$Mode = 'Tout'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or
    $PSVersionTable.PSVersion.Minor -ne 1 -or -not [Environment]::Is64BitProcess) {
    throw 'Ouvre Windows PowerShell 5.1 en 64 bits. Ce script ne demande aucun droit administrateur.'
}

$cheminAvecLecteur = $ProjetSources -match '^[A-Za-z]:[\\/]'
$cheminUNC = $ProjetSources -match '^\\\\[^\\/]+[\\/][^\\/]+(?:[\\/]|$)'
if (-not ($cheminAvecLecteur -or $cheminUNC)) {
    throw 'ProjetSources doit etre un chemin absolu vers la copie de diffusion 2.7.2.'
}
$racine = [IO.Path]::GetFullPath($ProjetSources)
if (-not (Test-Path -LiteralPath $racine -PathType Container)) {
    throw ('Dossier de sources introuvable : ' + $racine)
}
Write-Host 'Terminal : Windows PowerShell 5.1 Desktop x64'
Write-Host ('Projet : ' + $racine)
Write-Host 'Git : sources selectionnees ; aucune commande Git executee par ce verificateur.'
Write-Host 'Python / environnement virtuel : non utilises.'

# Affectation directe requise pour un tableau JSON sous Windows PowerShell 5.1.
$inventaireKit = Join-Path $racine 'SHA256.json'
# EmpreinteInventaire vient du fichier de controle livre dans le meme commit.
# Ce controle etablit la coherence des octets ; ce n'est pas une signature externe.
if (-not (Test-Path -LiteralPath $inventaireKit -PathType Leaf)) {
    throw ('Inventaire introuvable : ' + $inventaireKit)
}
if ((Get-FileHash -LiteralPath $inventaireKit -Algorithm SHA256).Hash -ne $empreinteInventaire) {
    throw 'Inventaire different du kit 2.7.2 livre : aucun lancement effectue.'
}
$texteInventaire = Get-Content -LiteralPath $inventaireKit -Raw -Encoding UTF8
if (-not $texteInventaire.TrimStart().StartsWith('[')) { throw 'Inventaire JSON : tableau attendu.' }
$fichiersKit = ConvertFrom-Json -InputObject $texteInventaire
$fichiersKit = @($fichiersKit)
if ($fichiersKit.Count -lt 50) { throw 'Inventaire incomplet.' }
$cheminsKit = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$racinePrefixe = $racine.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($fichierKit in $fichiersKit) {
    if (-not ($fichierKit.Path -is [string]) -or -not ($fichierKit.SHA256 -is [string]) -or
        $fichierKit.SHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        [IO.Path]::IsPathRooted($fichierKit.Path) -or $fichierKit.Path -match '(^|[\\/])\.\.([\\/]|$)') {
        throw 'Entree d inventaire invalide.'
    }
    $cheminKit = [IO.Path]::GetFullPath((Join-Path -Path $racine -ChildPath ($fichierKit.Path.Replace('/', '\'))))
    if (-not $cheminKit.StartsWith($racinePrefixe, [StringComparison]::OrdinalIgnoreCase) -or -not $cheminsKit.Add($cheminKit)) {
        throw 'Chemin en double ou exterieur au kit.'
    }
    if (-not (Test-Path -LiteralPath $cheminKit -PathType Leaf)) { throw ('Fichier manquant : ' + $cheminKit) }
    if ((Get-FileHash -LiteralPath $cheminKit -Algorithm SHA256).Hash -ine $fichierKit.SHA256) {
        throw ('Fichier different du kit 2.7.2 : ' + $cheminKit)
    }
}
$racineCS = Join-Path $racine 'Haloryva_Windows64\src'
foreach ($sourceCS in (Get-ChildItem -LiteralPath $racineCS -File -Filter '*.cs')) {
    if (-not $cheminsKit.Contains($sourceCS.FullName)) { throw ('Source C# hors inventaire : ' + $sourceCS.FullName) }
}
Write-Host ('Inventaire OK : {0} fichiers verifies.' -f $fichiersKit.Count)
$appRoot = Join-Path $racine 'Haloryva_Windows64'
$sourceRoot = Join-Path $appRoot 'src'
$testRoot = Join-Path $appRoot 'tests'
$resultRoot = Join-Path $racine 'Resultats'
$stamp = 'R5-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff')
$sortie = Join-Path $resultRoot ('Verification-' + $stamp)
if (Test-Path -LiteralPath $sortie) { throw 'Le dossier de resultat existe deja. Relance le script pour une nouvelle verification.' }
if (-not (Test-Path -LiteralPath $resultRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $resultRoot | Out-Null
}
New-Item -ItemType Directory -Path $sortie | Out-Null
$utf8 = New-Object Text.UTF8Encoding($true)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$resume = [ordered]@{
    SchemaVersion = 1
    VersionCible = '2.7.2'
    VersionVerificateur = 'R5'
    VerificateurSHA256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    InventaireKitSHA256 = $empreinteInventaire
    FichiersKitVerifies = $fichiersKit.Count
    ModeReferencesWinRT = 'UnionMetadata uniquement ; contrats individuels exclus'
    SdkWindowsSelectionne = $null
    BaseCommit = '8fa015f798f6cc9803acf40972d2bcac8f28d434'
    DebutUTC = [DateTime]::UtcNow.ToString('o')
    FinUTC = $null
    Terminal = 'Windows PowerShell 5.1 Desktop x64'
    Racine = $racine
    DepotGit = 'Aucune commande Git executee par le verificateur'
    CommitConstruction = if ($env:GITHUB_SHA -match '^[a-fA-F0-9]{40}$') { $env:GITHUB_SHA } else { $null }
    ReferenceConstruction = if ($env:GITHUB_ACTIONS -eq 'true') { $env:GITHUB_REF } else { $null }
    Python = 'Non utilise'
    Mode = $Mode
    Bilan = 'EN_COURS'
    Tests = 'NON_EXECUTES'
    TestsRenduTrainee = 'NON_EXECUTES'
    TestsPrecisionTrainee = 'NON_EXECUTES'
    TestsApercuCadence = 'NON_EXECUTES'
    TestsDiagnosticEcriture = 'NON_EXECUTES'
    DiagnosticEcritureExitCode = $null
    ApercuCadenceExitCode = $null
    PrecisionExitCode = $null
    PositionsHorodatees = 'NON_EXECUTEES'
    TestsGeometrie = 'NON_EXECUTES'
    TestsTrainee = 'NON_EXECUTES'
    AdaptateurWin32 = 'NON_COMPILE_NON_EXECUTE'
    ScriptsPowerShell = @()
    Compilation = 'NON_EXECUTEE'
    ValidationVisuelleWindows = 'NON_TESTEE'
    InterfaceHaloryvaOuverte = $false
    ExecutableHaloryvaExecute = $false
    ModesAutotestApplicationExecutes = @()
    InstallationEffectuee = $false
    Compiler = $compiler
    PrerequisManquants = @()
    ReferencesCompilation = @()
    RessourcesVerifiees = @()
    ManifesteApplication = $null
    TestExitCode = $null
    TrailRenderingExitCode = $null
    PositionsExitCode = $null
    GeometryExitCode = $null
    TrailExitCode = $null
    AppSHA256 = $null
    AppProductVersion = $null
    AppFileVersion = $null
    AppAssemblyVersion = $null
    Erreur = $null
}
$codeSortie = 1

function Invoke-Outil {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [string]$Arguments = '',
        [Parameter(Mandatory = $true)][string]$Journal,
        [int]$TimeoutMs = 60000,
        [switch]$AutoTestApplication
    )
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = $Arguments
    $info.WorkingDirectory = $sortie
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
    $info.StandardErrorEncoding = New-Object Text.UTF8Encoding($false)
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw ('Demarrage impossible : ' + $Executable) }
        if ($AutoTestApplication) {
            $resume.ExecutableHaloryvaExecute = $true
            $resume.ModesAutotestApplicationExecutes += $Arguments
        }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $termine = $process.WaitForExit($TimeoutMs)
        if (-not $termine) {
            # Seul ce processus enfant est termine, jamais une instance deja installee.
            $process.Kill()
            $process.WaitForExit()
        }
        $texteOut = $stdout.GetAwaiter().GetResult()
        $texteErr = $stderr.GetAwaiter().GetResult()
        $texte = $texteOut
        if (-not [string]::IsNullOrWhiteSpace($texteErr)) { $texte += "`r`nERREURS :`r`n" + $texteErr }
        if (-not $termine) { $texte += "`r`nDELAI_MAXIMUM_ATTEINT`r`n" }
        [IO.File]::WriteAllText($Journal, $texte, $utf8)
        if (-not [string]::IsNullOrWhiteSpace($texte)) { Write-Host $texte.TrimEnd() }
        if (-not $termine) { throw ('Delai maximum atteint. Consulte : ' + $Journal) }
        return [int]$process.ExitCode
    }
    finally { $process.Dispose() }
}

function Invoke-Compilation {
    param(
        [Parameter(Mandatory = $true)][string]$Nom,
        [Parameter(Mandatory = $true)][string[]]$Lignes
    )
    $responseFile = Join-Path $sortie ($Nom + '.rsp')
    [IO.File]::WriteAllLines($responseFile, $Lignes, $utf8)
    return (Invoke-Outil -Executable $compiler -Arguments ('@"' + $responseFile + '"') -Journal (Join-Path $sortie ($Nom + '.log')))
}

try {
    Write-Host 'Verificateur : R5 ; application : 2.7.2'
    Write-Host ('Resultats : ' + $sortie)

    # Analyse syntaxique PowerShell 5.1 sans executer les autres scripts.
    $scripts = @(Get-Item -LiteralPath $PSCommandPath) + @(Get-ChildItem -LiteralPath $racine -Filter '*.ps1' -File -Recurse | Where-Object {
        -not $_.FullName.StartsWith($resultRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    })
    $scripts = @($scripts | Sort-Object FullName -Unique)
    foreach ($script in $scripts) {
        $tokens = $null
        $parseErrors = $null
        [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
        $errors = @($parseErrors)
        $resume.ScriptsPowerShell += [ordered]@{ Fichier = $script.FullName; Erreurs = $errors.Count }
        if ($errors.Count -gt 0) {
            $details = @($errors | ForEach-Object { '{0}:{1} {2}' -f $_.Extent.StartLineNumber, $_.Extent.StartColumnNumber, $_.Message })
            [IO.File]::WriteAllLines((Join-Path $sortie 'Erreurs-syntaxe-PowerShell.txt'), $details, $utf8)
            throw ('Erreur de syntaxe PowerShell 5.1 : ' + $script.FullName)
        }
    }

    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
        $resume.Tests = 'TESTS_PREREQUIS_MISSING'
        $resume.Bilan = 'TESTS_PREREQUIS_MISSING'
        $resume.PrerequisManquants = @($compiler)
        $codeSortie = 3
        throw 'Le compilateur .NET Framework x64 est absent. Aucune installation automatique.'
    }

    # Les ressources originales doivent garder les empreintes de la provenance.
    $provenance = Get-Content -LiteralPath (Join-Path $racine 'docs\PROVENANCE-SOURCES-2.7.1.11.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $ressources = @('Haloryva_Windows64/assets/Haloryva.ico')
    foreach ($relative in $ressources) {
        $entry = @($provenance.Files | Where-Object { $_.Path -ceq $relative })
        if ($entry.Count -ne 1) { throw ('Reference de ressource absente ou ambigue : ' + $relative) }
        $path = Join-Path $racine ($relative.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Ressource absente : ' + $path) }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($hash -cne $entry[0].SHA256 -or (Get-Item -LiteralPath $path).Length -ne $entry[0].Bytes) {
            throw ('Ressource differente de la provenance : ' + $relative)
        }
        $resume.RessourcesVerifiees += [ordered]@{ Chemin = $relative; SHA256 = $hash }
    }

    # Le catalogue conserve les traductions M01 : comparer a l'inventaire courant.
    $catalogRelative = 'Haloryva_Windows64/languages/strings.json'
    $catalogPath = Join-Path $racine ($catalogRelative.Replace('/', '\'))
    $inventory = Get-Content -LiteralPath (Join-Path $racine 'Inventaire-sources.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $catalogEntry = @($inventory.Files | Where-Object { $_.Path -ceq $catalogRelative })
    if ($catalogEntry.Count -ne 1 -or -not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
        throw 'Catalogue ou entree d''inventaire absente ou ambigue.'
    }
    $catalogHash = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash
    if ($catalogHash -cne $catalogEntry[0].SHA256 -or (Get-Item -LiteralPath $catalogPath).Length -ne $catalogEntry[0].Bytes) {
        throw 'Le catalogue differe de l''inventaire courant du kit.'
    }
    $catalogText = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8
    if (-not $catalogText.TrimStart().StartsWith('[')) { throw 'Le catalogue doit etre un tableau JSON.' }
    $catalogItems = ConvertFrom-Json -InputObject $catalogText
    $catalogItems = @($catalogItems)
    if ($catalogItems.Count -eq 0) { throw 'Le catalogue est vide.' }
    $catalogKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($catalogItem in $catalogItems) {
        foreach ($field in @('key', 'fr', 'en', 'es')) {
            $property = $catalogItem.PSObject.Properties[$field]
            if ($null -eq $property -or -not ($property.Value -is [string]) -or [string]::IsNullOrWhiteSpace($property.Value)) {
                throw ('Entree du catalogue incomplete : ' + $field)
            }
        }
        if (-not $catalogKeys.Add($catalogItem.key)) { throw ('Cle de traduction en double : ' + $catalogItem.key) }
    }
    $resume.RessourcesVerifiees += [ordered]@{ Chemin = $catalogRelative; SHA256 = $catalogHash; Entrees = $catalogItems.Count; Reference = 'Inventaire-sources.json' }

    # Le manifeste a une nouvelle identite ; sa provenance conserve le hash historique.
    $manifestPath = Join-Path $appRoot 'assets\app.manifest'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Manifeste applicatif absent.' }
    $manifestXml = New-Object Xml.XmlDocument
    $manifestXml.XmlResolver = $null
    $manifestXml.Load($manifestPath)
    $manifestNs = New-Object Xml.XmlNamespaceManager -ArgumentList $manifestXml.NameTable
    $manifestNs.AddNamespace('assembly', 'urn:schemas-microsoft-com:asm.v1')
    $manifestNs.AddNamespace('security', 'urn:schemas-microsoft-com:asm.v3')
    $identities = @($manifestXml.SelectNodes('/assembly:assembly/assembly:assemblyIdentity', $manifestNs))
    $levels = @($manifestXml.SelectNodes('//security:requestedExecutionLevel', $manifestNs))
    if ($identities.Count -ne 1 -or $levels.Count -ne 1) { throw 'Identite ou niveau de privilege absent ou ambigu dans le manifeste.' }
    $manifestVersion = $identities[0].GetAttribute('version')
    $manifestLevel = $levels[0].GetAttribute('level')
    $manifestUiAccess = $levels[0].GetAttribute('uiAccess')
    if ($manifestVersion -cne '2.7.2.0' -or $manifestLevel -cne 'asInvoker' -or $manifestUiAccess -cne 'false') {
        throw 'Manifeste applicatif inattendu : version 2.7.2.0, asInvoker et uiAccess=false requis.'
    }
    $resume.ManifesteApplication = [ordered]@{
        Version = $manifestVersion
        Niveau = $manifestLevel
        UiAccess = $manifestUiAccess
        SHA256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    }

    $testFiles = @(
        (Join-Path $sourceRoot 'BufferedDiagnosticWriter.cs'),
        (Join-Path $sourceRoot 'OverlayRecoveryPolicy.cs'),
        (Join-Path $sourceRoot 'OverlayRecoveryLog.cs'),
        (Join-Path $sourceRoot 'OverlayRecovery.cs'),
        (Join-Path $sourceRoot 'WinNative.cs'),
        (Join-Path $testRoot 'OverlayRecoveryTests.cs')
    )
    foreach ($file in $testFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Source de test absente : ' + $file) }
    }
    $testExe = Join-Path $sortie 'Haloryva.Tests.exe'
    $testArguments = @(
        '/nologo', '/utf8output', '/codepage:65001', '/langversion:5', '/platform:x64', '/target:exe', '/optimize+',
        ('/out:"' + $testExe + '"'), '/reference:System.dll', '/reference:System.Core.dll',
        '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll'
    )
    $testArguments += @($testFiles | ForEach-Object { '"' + $_ + '"' })
    $resume.Tests = 'TESTS_COMPILATION_EN_COURS'
    $resume.AdaptateurWin32 = 'COMPILATION_EN_COURS_NON_EXECUTE'
    if ((Invoke-Compilation -Nom 'Compilation-tests' -Lignes $testArguments) -ne 0) {
        $resume.Tests = 'TESTS_COMPILATION_ECHEC'
        $resume.AdaptateurWin32 = 'COMPILATION_ECHEC_NON_EXECUTE'
        throw 'La compilation des tests a echoue. Consulte Compilation-tests.log.'
    }
    # L'adaptateur natif est compile avec le runner ; les tests utilisent FakePlatform.
    # Aucun Form, OverlayRecovery ni appel Win32 n'est instancie par ces tests.
    $resume.AdaptateurWin32 = 'COMPILE_NON_EXECUTE'
    $resume.Tests = 'TESTS_EN_COURS'
    $testExit = Invoke-Outil -Executable $testExe -Journal (Join-Path $sortie 'Tests.log')
    $resume.TestExitCode = $testExit
    if ($testExit -ne 0) {
        $resume.Tests = 'TESTS_ECHEC'
        throw 'Un test a echoue. La compilation de Haloryva est annulee.'
    }
    $resume.Tests = 'TESTS_OK'
    Write-Host 'TESTS_OK : tests du correctif reussis. Le rendu Windows reste a verifier.'

    # Conservation bornee des positions/date : tests sans hooks ni entree systeme.
    $positionFiles = @(
        (Join-Path $sourceRoot 'PointerTrailBuffer.cs'),
        (Join-Path $sourceRoot 'PointerTrailSelfTest.cs'),
        (Join-Path $testRoot 'PointerTrailTests.cs')
    )
    foreach ($file in $positionFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Source de test des positions absente : ' + $file) }
    }
    $positionsExe = Join-Path $sortie 'Haloryva.Positions.Tests.exe'
    $positionArguments = @(
        '/nologo', '/utf8output', '/codepage:65001', '/langversion:5', '/platform:x64', '/target:exe', '/optimize+',
        ('/out:"' + $positionsExe + '"'), '/reference:System.dll', '/reference:System.Core.dll'
    )
    $positionArguments += @($positionFiles | ForEach-Object { '"' + $_ + '"' })
    $resume.PositionsHorodatees = 'TESTS_COMPILATION_EN_COURS'
    if ((Invoke-Compilation -Nom 'Compilation-tests-positions' -Lignes $positionArguments) -ne 0) {
        $resume.PositionsHorodatees = 'TESTS_COMPILATION_ECHEC'
        throw 'La compilation des tests de positions a echoue.'
    }
    $resume.PositionsHorodatees = 'TESTS_EN_COURS'
    $positionsExit = Invoke-Outil -Executable $positionsExe -Journal (Join-Path $sortie 'Tests-positions.log')
    $resume.PositionsExitCode = $positionsExit
    if ($positionsExit -ne 0) {
        $resume.PositionsHorodatees = 'TESTS_ECHEC'
        throw 'Un test de conservation des positions a echoue.'
    }
    $resume.PositionsHorodatees = 'TESTS_OK'

    # Precision sous-pixel et attributs : code de production, sans interface.
    $precisionFiles = @(
        (Join-Path $sourceRoot 'TrailEngine.cs'),
        (Join-Path $sourceRoot 'TrailPipeline.cs'),
        (Join-Path $sourceRoot 'TrailPrecisionSelfTest.cs'),
        (Join-Path $testRoot 'TrailPrecisionTests.cs')
    )
    foreach ($file in $precisionFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Source de test precision absente : ' + $file) }
    }
    $precisionExe = Join-Path $sortie 'Haloryva.Precision.Tests.exe'
    $precisionArguments = @(
        '/nologo', '/utf8output', '/codepage:65001', '/langversion:5', '/platform:x64', '/target:exe', '/optimize+',
        ('/out:"' + $precisionExe + '"'), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll'
    )
    $precisionArguments += @($precisionFiles | ForEach-Object { '"' + $_ + '"' })
    $resume.TestsPrecisionTrainee = 'TESTS_COMPILATION_EN_COURS'
    if ((Invoke-Compilation -Nom 'Compilation-tests-precision' -Lignes $precisionArguments) -ne 0) {
        $resume.TestsPrecisionTrainee = 'TESTS_COMPILATION_ECHEC'
        throw 'La compilation des tests de precision a echoue.'
    }
    $resume.TestsPrecisionTrainee = 'TESTS_EN_COURS'
    $resume.PrecisionExitCode = Invoke-Outil -Executable $precisionExe -Journal (Join-Path $sortie 'Tests-precision.log')
    if ($resume.PrecisionExitCode -ne 0) {
        $resume.TestsPrecisionTrainee = 'TESTS_ECHEC'
        throw 'Un test de precision a echoue.'
    }
    $resume.TestsPrecisionTrainee = 'TESTS_OK'

    # Apercu analytique, dates et sauvegardes seriees avec backend fictif.
    $apercuFiles = @(
        (Join-Path $sourceRoot 'TrailEngine.cs'),
        (Join-Path $sourceRoot 'TrailPipeline.cs'),
        (Join-Path $sourceRoot 'TrailPreviewSampler.cs'),
        (Join-Path $sourceRoot 'SerializedSettingsWriter.cs'),
        (Join-Path $sourceRoot 'FrameTiming.cs'),
        (Join-Path $sourceRoot 'PreviewCadenceSelfTest.cs'),
        (Join-Path $testRoot 'PreviewCadenceTests.cs')
    )
    foreach ($file in $apercuFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Source de test apercu/cadence absente : ' + $file) }
    }
    $apercuExe = Join-Path $sortie 'Haloryva.Apercu.Tests.exe'
    $apercuArguments = @(
        '/nologo', '/utf8output', '/codepage:65001', '/langversion:5', '/platform:x64', '/target:exe', '/optimize+',
        ('/out:"' + $apercuExe + '"'), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll'
    )
    $apercuArguments += @($apercuFiles | ForEach-Object { '"' + $_ + '"' })
    $resume.TestsApercuCadence = 'TESTS_COMPILATION_EN_COURS'
    if ((Invoke-Compilation -Nom 'Compilation-tests-apercu-cadence' -Lignes $apercuArguments) -ne 0) {
        $resume.TestsApercuCadence = 'TESTS_COMPILATION_ECHEC'
        throw 'La compilation des tests apercu/cadence a echoue.'
    }
    $resume.TestsApercuCadence = 'TESTS_EN_COURS'
    $resume.ApercuCadenceExitCode = Invoke-Outil -Executable $apercuExe -Journal (Join-Path $sortie 'Tests-apercu-cadence.log')
    if ($resume.ApercuCadenceExitCode -ne 0) {
        $resume.TestsApercuCadence = 'TESTS_ECHEC'
        throw 'Un test apercu/cadence a echoue.'
    }
    $resume.TestsApercuCadence = 'TESTS_OK'

    # Ecriture diagnostic : worker de production, backends de test lents et fautifs.
    # Aucun hook, profil, fenetre ou acces Win32 dans cette suite autonome.
    $diagnosticFiles = @(
        (Join-Path $sourceRoot 'BufferedDiagnosticWriter.cs'),
        (Join-Path $sourceRoot 'DiagnosticWriterSelfTest.cs'),
        (Join-Path $testRoot 'DiagnosticWriterTests.cs')
    )
    foreach ($file in $diagnosticFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Source de test diagnostic absente : ' + $file) }
    }
    $diagnosticExe = Join-Path $sortie 'Haloryva.Diagnostic.Tests.exe'
    $diagnosticArguments = @(
        '/nologo', '/utf8output', '/codepage:65001', '/langversion:5', '/platform:x64', '/target:exe', '/optimize+',
        ('/out:"' + $diagnosticExe + '"'), '/reference:System.dll', '/reference:System.Core.dll'
    )
    $diagnosticArguments += @($diagnosticFiles | ForEach-Object { '"' + $_ + '"' })
    $resume.TestsDiagnosticEcriture = 'TESTS_COMPILATION_EN_COURS'
    if ((Invoke-Compilation -Nom 'Compilation-tests-diagnostic' -Lignes $diagnosticArguments) -ne 0) {
        $resume.TestsDiagnosticEcriture = 'TESTS_COMPILATION_ECHEC'
        throw 'La compilation des tests du diagnostic a echoue.'
    }
    $resume.TestsDiagnosticEcriture = 'TESTS_EN_COURS'
    $resume.DiagnosticEcritureExitCode = Invoke-Outil -Executable $diagnosticExe -Journal (Join-Path $sortie 'Tests-diagnostic.log')
    if ($resume.DiagnosticEcritureExitCode -ne 0) {
        $resume.TestsDiagnosticEcriture = 'TESTS_ECHEC'
        throw 'Un test du diagnostic en arriere-plan a echoue.'
    }
    $resume.TestsDiagnosticEcriture = 'TESTS_OK'

    # Rendu de production sur bitmaps fictifs : pas de fenetre, hook ou profil.
    # Les adaptateurs Config/Controller/WinRT ne sont pas inclus dans ce runner.
    $ribbonTestFiles = @(
        (Join-Path $sourceRoot 'TrailEngine.cs'),
        (Join-Path $sourceRoot 'TrailPipeline.cs'),
        (Join-Path $sourceRoot 'TrailRibbon.cs'),
        (Join-Path $sourceRoot 'RenderStageTiming.cs'),
        (Join-Path $sourceRoot 'TrailRenderingSelfTest.cs'),
        (Join-Path $testRoot 'TrailRenderingTests.cs')
    )
    foreach ($file in $ribbonTestFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Source de test du rendu absente : ' + $file) }
    }
    $ribbonExe = Join-Path $sortie 'Haloryva.Trainee.Tests.exe'
    $ribbonArguments = @(
        '/nologo', '/utf8output', '/codepage:65001', '/langversion:5', '/platform:x64', '/target:exe', '/optimize+',
        ('/out:"' + $ribbonExe + '"'), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll'
    )
    $ribbonArguments += @($ribbonTestFiles | ForEach-Object { '"' + $_ + '"' })
    $resume.TestsRenduTrainee = 'TESTS_COMPILATION_EN_COURS'
    if ((Invoke-Compilation -Nom 'Compilation-tests-trainee' -Lignes $ribbonArguments) -ne 0) {
        $resume.TestsRenduTrainee = 'TESTS_COMPILATION_ECHEC'
        throw 'La compilation des tests du rendu a echoue. Consulte Compilation-tests-trainee.log.'
    }
    $resume.TestsRenduTrainee = 'TESTS_EN_COURS'
    $ribbonExit = Invoke-Outil -Executable $ribbonExe -Journal (Join-Path $sortie 'Tests-rendu-trainee.log')
    $resume.TrailRenderingExitCode = $ribbonExit
    if ($ribbonExit -ne 0) {
        $resume.TestsRenduTrainee = 'TESTS_ECHEC'
        throw 'Un test du rendu de trainee a echoue. La compilation de Haloryva est annulee.'
    }
    $resume.TestsRenduTrainee = 'TESTS_OK'
    Write-Host 'TESTS_RENDU_TRAINEE_OK : geometrie et pixels fictifs testes ; presentation ecran non mesuree.'

    if ($Mode -eq 'Tests') {
        $resume.Compilation = 'BUILD_NON_DEMANDE'
        $resume.Bilan = 'TESTS_OK_BUILD_NON_DEMANDE'
        $codeSortie = 0
    }
    else {
        # R2 : un seul ensemble WinRT, observe dans le SDK du retour reel.
        # Ne pas melanger Windows.winmd de UnionMetadata et les contrats individuels.
        $pf86 = [Environment]::GetFolderPath('ProgramFilesX86')
        $sdkRoot = Join-Path $pf86 'Windows Kits\10\UnionMetadata'
        $bridge = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll'
        $facades = Join-Path $pf86 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
        $winmd = $null
        if (Test-Path -LiteralPath $sdkRoot -PathType Container) {
            $sdkVersions = @(Get-ChildItem -LiteralPath $sdkRoot -Directory | Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending)
            foreach ($sdkVersion in $sdkVersions) {
                $candidate = Join-Path $sdkVersion.FullName 'Windows.winmd'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    $winmd = $candidate
                    $resume.SdkWindowsSelectionne = $sdkVersion.Name
                    break
                }
            }
        }
        $missing = @()
        if ($null -eq $winmd) { $missing += 'SDK Windows 10/11 : UnionMetadata\<version>\Windows.winmd' }
        if (-not (Test-Path -LiteralPath $bridge -PathType Leaf)) { $missing += $bridge }
        $facadeFiles = @()
        if (Test-Path -LiteralPath $facades -PathType Container) { $facadeFiles = @(Get-ChildItem -LiteralPath $facades -Filter '*.dll' -File | ForEach-Object FullName) }
        if ($facadeFiles.Count -eq 0) { $missing += '.NET Framework 4.8 : assemblies de reference / Facades' }
        if ($missing.Count -gt 0) {
            $resume.PrerequisManquants = $missing
            $resume.Compilation = 'BUILD_PREREQUIS_MISSING'
            $resume.Bilan = 'TESTS_OK_BUILD_PREREQUIS_MISSING'
            $codeSortie = 2
            Write-Host 'BUILD_PREREQUIS_MISSING : compilation complete non effectuee.'
            foreach ($item in $missing) { Write-Host ('Manquant : ' + $item) }
        }
        else {
            $refs = @($winmd, $bridge) + $facadeFiles
            $winmdRefs = @($refs | Where-Object { [IO.Path]::GetExtension($_) -ieq '.winmd' })
            if ($winmdRefs.Count -ne 1 -or $winmdRefs[0] -ine $winmd) {
                throw 'References WinRT incoherentes : un seul Windows.winmd est requis.'
            }
            Write-Host ('References WinRT R5 : ' + $winmd + ' ; aucun contrat individuel ajoute.')
            $resume.ReferencesCompilation = $refs
            $app = Join-Path $sortie 'Haloryva.exe'
            $argumentsApp = @(
                '/nologo', '/utf8output', '/codepage:65001', '/langversion:5', '/platform:x64', '/target:winexe', '/optimize+',
                ('/out:"' + $app + '"'), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xml.dll',
                '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll'
            )
            $argumentsApp += @($refs | ForEach-Object { '/reference:"' + $_ + '"' })
            $argumentsApp += @(
                ('/win32icon:"' + (Join-Path $appRoot 'assets\Haloryva.ico') + '"'),
                ('/win32manifest:"' + (Join-Path $appRoot 'assets\app.manifest') + '"'),
                ('/resource:"' + (Join-Path $appRoot 'languages\strings.json') + '",Haloryva.Localization.json')
            )
            $sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name)
            if ($sourceFiles.Count -lt 33) { throw 'Inventaire applicatif incomplet : moins de 33 sources C#.' }
            $argumentsApp += @($sourceFiles | ForEach-Object { '"' + $_.FullName + '"' })
            $resume.Compilation = 'BUILD_EN_COURS'
            if ((Invoke-Compilation -Nom 'Compilation-application' -Lignes $argumentsApp) -ne 0) {
                $resume.Compilation = 'BUILD_ECHEC'
                throw 'La compilation complete a echoue. Consulte Compilation-application.log.'
            }
            $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($app)
            $resume.AppProductVersion = $info.ProductVersion
            $resume.AppFileVersion = $info.FileVersion
            $resume.AppAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($app).Version.ToString()
            if ($info.ProductVersion -cne '2.7.2' -or $info.FileVersion -cne '2.7.2.6' -or $resume.AppAssemblyVersion -cne '2.7.2.0') {
                $resume.Compilation = 'BUILD_IDENTITE_ECHEC'
                throw 'Identite inattendue : produit 2.7.2, fichier 2.7.2.6, assembly 2.7.2.0 requis.'
            }
            $resume.AppSHA256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
            $resume.Compilation = 'BUILD_OK'
            Write-Host 'BUILD_OK : compilation complete reussie. L''interface Haloryva reste fermee.'

            # Ces deux modes quittent Program.Main avant profils, mutex, hooks et UI.
            $resume.TestsGeometrie = 'TESTS_EN_COURS'
            $geometryExit = Invoke-Outil -Executable $app -Arguments '--selftest' -Journal (Join-Path $sortie 'Tests-geometrie.log') -AutoTestApplication
            $resume.GeometryExitCode = $geometryExit
            $resume.TestsGeometrie = if ($geometryExit -eq 0) { 'TESTS_OK' } else { 'TESTS_ECHEC' }

            $resume.TestsTrainee = 'TESTS_EN_COURS'
            $trailExit = Invoke-Outil -Executable $app -Arguments '--trail-selftest' -Journal (Join-Path $sortie 'Tests-trainee.log') -AutoTestApplication
            $resume.TrailExitCode = $trailExit
            $resume.TestsTrainee = if ($trailExit -eq 0) { 'TESTS_OK' } else { 'TESTS_ECHEC' }

            if ($geometryExit -ne 0 -or $trailExit -ne 0) {
                $resume.Bilan = 'BUILD_OK_REGRESSION_ECHEC'
                throw 'Au moins un autotest geometrique ou de trainee a echoue. Consulte les journaux separes.'
            }
            $resume.Bilan = 'TESTS_OK_BUILD_OK_RECETTE_VISUELLE_NON_FAITE'
            $codeSortie = 0
        }
    }
}
catch {
    $resume.Erreur = $_.Exception.Message
    if ($resume.Tests -eq 'TESTS_EN_COURS') { $resume.Tests = 'TESTS_ECHEC' }
    if ($resume.Tests -eq 'TESTS_COMPILATION_EN_COURS') { $resume.Tests = 'TESTS_COMPILATION_ECHEC' }
    if ($resume.TestsDiagnosticEcriture -eq 'TESTS_EN_COURS') { $resume.TestsDiagnosticEcriture = 'TESTS_ECHEC' }
    if ($resume.TestsDiagnosticEcriture -eq 'TESTS_COMPILATION_EN_COURS') { $resume.TestsDiagnosticEcriture = 'TESTS_COMPILATION_ECHEC' }
    if ($resume.TestsApercuCadence -eq 'TESTS_EN_COURS') { $resume.TestsApercuCadence = 'TESTS_ECHEC' }
    if ($resume.TestsApercuCadence -eq 'TESTS_COMPILATION_EN_COURS') { $resume.TestsApercuCadence = 'TESTS_COMPILATION_ECHEC' }
    if ($resume.TestsPrecisionTrainee -eq 'TESTS_EN_COURS') { $resume.TestsPrecisionTrainee = 'TESTS_ECHEC' }
    if ($resume.TestsPrecisionTrainee -eq 'TESTS_COMPILATION_EN_COURS') { $resume.TestsPrecisionTrainee = 'TESTS_COMPILATION_ECHEC' }
    if ($resume.TestsRenduTrainee -eq 'TESTS_EN_COURS') { $resume.TestsRenduTrainee = 'TESTS_ECHEC' }
    if ($resume.TestsRenduTrainee -eq 'TESTS_COMPILATION_EN_COURS') { $resume.TestsRenduTrainee = 'TESTS_COMPILATION_ECHEC' }
    if ($resume.PositionsHorodatees -eq 'TESTS_EN_COURS') { $resume.PositionsHorodatees = 'TESTS_ECHEC' }
    if ($resume.PositionsHorodatees -eq 'TESTS_COMPILATION_EN_COURS') { $resume.PositionsHorodatees = 'TESTS_COMPILATION_ECHEC' }
    if ($resume.AdaptateurWin32 -eq 'COMPILATION_EN_COURS_NON_EXECUTE') { $resume.AdaptateurWin32 = 'COMPILATION_ECHEC_NON_EXECUTE' }
    if ($resume.TestsGeometrie -eq 'TESTS_EN_COURS') { $resume.TestsGeometrie = 'TESTS_ECHEC' }
    if ($resume.TestsTrainee -eq 'TESTS_EN_COURS') { $resume.TestsTrainee = 'TESTS_ECHEC' }
    if ($resume.Compilation -eq 'BUILD_EN_COURS') { $resume.Compilation = 'BUILD_ECHEC' }
    if ($resume.Bilan -eq 'EN_COURS') {
        if ($resume.Compilation -eq 'BUILD_OK' -and ($resume.TestsGeometrie -eq 'TESTS_ECHEC' -or $resume.TestsTrainee -eq 'TESTS_ECHEC')) {
            $resume.Bilan = 'BUILD_OK_REGRESSION_ECHEC'
        }
        else { $resume.Bilan = 'ECHEC' }
    }
    Write-Host ('ERREUR : ' + $_.Exception.Message) -ForegroundColor Red
}
finally {
    $resume.FinUTC = [DateTime]::UtcNow.ToString('o')
    $resumePath = Join-Path $sortie 'Resume.json'
    [IO.File]::WriteAllText($resumePath, ($resume | ConvertTo-Json -Depth 8), $utf8)
    Write-Host ('Bilan : ' + $resume.Bilan)
    Write-Host ('Tests recuperation : ' + $resume.Tests)
    Write-Host ('Tests rendu trainee : ' + $resume.TestsRenduTrainee)
    Write-Host ('Tests precision : ' + $resume.TestsPrecisionTrainee)
    Write-Host ('Tests apercu/cadence : ' + $resume.TestsApercuCadence)
    Write-Host ('Tests diagnostic : ' + $resume.TestsDiagnosticEcriture)
    Write-Host ('Positions horodatees : ' + $resume.PositionsHorodatees)
    Write-Host ('Adaptateur Win32 : ' + $resume.AdaptateurWin32)
    Write-Host ('Tests geometrie : ' + $resume.TestsGeometrie)
    Write-Host ('Tests trainee : ' + $resume.TestsTrainee)
    Write-Host ('Compilation : ' + $resume.Compilation)
    Write-Host ('Resume a transmettre : ' + $resumePath)
    # Archive uniquement les preuves ; aucun executable, DLL ou profil n'est transmis.
    $proofZip = Join-Path $resultRoot ('Preuves-' + $stamp + '.zip')
    $partialZip = $proofZip + '.part'
    try {
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        if ((Test-Path -LiteralPath $proofZip) -or (Test-Path -LiteralPath $partialZip)) { throw 'Le nom du ZIP de preuves existe deja.' }
        $extensions = @('.json', '.log', '.png', '.txt', '.csv', '.rsp')
        $proofFiles = @(Get-ChildItem -LiteralPath $sortie -File -Recurse | Where-Object {
            $extensions -contains $_.Extension.ToLowerInvariant() -and
            $_.Name -notmatch '^(settings|preferences|profil)([.\-_]|$)'
        })
        $zipStream = $null
        $archive = $null
        try {
            $zipStream = New-Object IO.FileStream($partialZip, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $archive = New-Object IO.Compression.ZipArchive($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
            foreach ($proof in $proofFiles) {
                $relative = $proof.FullName.Substring($sortie.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $proof.FullName, $relative,
                    [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally {
            if ($null -ne $archive) { $archive.Dispose() }
            if ($null -ne $zipStream) { $zipStream.Dispose() }
        }
        [IO.File]::Move($partialZip, $proofZip)
        Write-Host ('ZIP a transmettre : ' + $proofZip)
    }
    catch {
        # L'echec d'archivage ne remplace jamais le code des tests/compilations.
        Write-Host ('ZIP non cree : ' + $_.Exception.Message) -ForegroundColor Yellow
        Write-Host ('Dossier de preuves a transmettre : ' + $sortie)
    }
    Write-Host ('Code de sortie : ' + $codeSortie)
}
exit $codeSortie
