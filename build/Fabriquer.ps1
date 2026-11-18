#requires -Version 5.1
<#
Fabrique la livraison 2.7.2 dans un dossier neuf : tests R5, compilation,
paquet Store non signe et preuves. Aucune installation ni publication.
SHA256SUMS-SOURCES.txt et SHA256.json proviennent du meme commit verifie.
Ils controlent la coherence des fichiers ; ils ne constituent pas une signature.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$ProjetSources = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or
    $PSVersionTable.PSVersion.Minor -ne 1 -or -not [Environment]::Is64BitProcess) {
    throw 'Windows PowerShell 5.1 Desktop x64 requis.'
}
$racine = [IO.Path]::GetFullPath($ProjetSources)
if (-not (Test-Path -LiteralPath $racine -PathType Container)) { throw 'Dossier de sources absent.' }
$livraison = Join-Path $racine 'Livraison\Haloryva-2.7.2'
if (Test-Path -LiteralPath $livraison) { throw 'Le dossier Livraison\Haloryva-2.7.2 existe deja. Utilise une nouvelle copie des sources.' }

Write-Host 'Terminal : Windows PowerShell 5.1 Desktop x64'
Write-Host ('Projet : ' + $racine)
if ($env:GITHUB_ACTIONS -eq 'true') {
    if ($env:GITHUB_REPOSITORY -cne 'JulienLecomteGit/haloryva-studio' -or
        $env:GITHUB_SHA -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Contexte GitHub inattendu.' }
    Write-Host ('GitHub : {0} ; reference {1} ; commit {2}' -f $env:GITHUB_REPOSITORY, $env:GITHUB_REF, $env:GITHUB_SHA)
}
else { Write-Host 'Sources locales : aucune commande Git, branche ou remote utilise par ce script.' }
Write-Host 'Python / environnement virtuel : non utilises.'

$empreintePath = Join-Path $racine 'SHA256SUMS-SOURCES.txt'
$inventairePath = Join-Path $racine 'SHA256.json'
if (-not (Test-Path -LiteralPath $empreintePath -PathType Leaf)) { throw 'SHA256SUMS-SOURCES.txt absent : scellage requis.' }
if (-not (Test-Path -LiteralPath $inventairePath -PathType Leaf)) { throw 'SHA256.json absent : scellage requis.' }
$ligne = (Get-Content -LiteralPath $empreintePath -Raw -Encoding UTF8).Trim()
$match = [regex]::Match($ligne, '\A([A-Fa-f0-9]{64})[ \t]+SHA256\.json\z')
if (-not $match.Success) { throw 'SHA256SUMS-SOURCES.txt doit contenir une seule empreinte de SHA256.json.' }
$empreinte = $match.Groups[1].Value.ToUpperInvariant()
if ((Get-FileHash -LiteralPath $inventairePath -Algorithm SHA256).Hash -cne $empreinte) {
    throw 'Inventaire different de son empreinte livree.'
}

$resultats = Join-Path $racine 'Resultats'
$resumesAvant = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $resultats -PathType Container) {
    foreach ($fichier in (Get-ChildItem -LiteralPath $resultats -Recurse -File -Filter 'Resume.json')) {
        [void]$resumesAvant.Add($fichier.FullName)
    }
}
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$verificateur = Join-Path $racine 'Verifier-Compiler.ps1'
& $powershell -NoProfile -ExecutionPolicy Bypass -File $verificateur -ProjetSources $racine -EmpreinteInventaire $empreinte -Mode Tout
$codeVerification = $LASTEXITCODE
if ($codeVerification -ne 0) { throw ('Verification R5 echouee, code ' + $codeVerification + '. Aucun paquet fabrique.') }

$nouveauxResumes = @(Get-ChildItem -LiteralPath $resultats -Recurse -File -Filter 'Resume.json' | Where-Object {
    -not $resumesAvant.Contains($_.FullName) -and $_.Directory.Name -like 'Verification-R5-*'
})
if ($nouveauxResumes.Count -ne 1) { throw 'Impossible d identifier le resume unique de cette compilation.' }
$resumePath = $nouveauxResumes[0].FullName
$resume = Get-Content -LiteralPath $resumePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($resume.VersionCible -cne '2.7.2' -or $resume.VersionVerificateur -cne 'R5' -or
    $resume.Compilation -cne 'BUILD_OK' -or $resume.Mode -cne 'Tout' -or
    $null -ne $resume.Erreur -or @($resume.PrerequisManquants).Count -ne 0) {
    throw 'La compilation ne satisfait pas les conditions de fabrication.'
}
foreach ($nom in @('Tests', 'TestsRenduTrainee', 'TestsPrecisionTrainee', 'TestsApercuCadence',
    'TestsDiagnosticEcriture', 'PositionsHorodatees', 'TestsGeometrie', 'TestsTrainee')) {
    if ($resume.$nom -cne 'TESTS_OK') { throw ('Suite non reussie : ' + $nom) }
}
foreach ($nom in @('TestExitCode', 'TrailRenderingExitCode', 'PositionsExitCode', 'PrecisionExitCode',
    'ApercuCadenceExitCode', 'DiagnosticEcritureExitCode', 'GeometryExitCode', 'TrailExitCode')) {
    if ($null -eq $resume.$nom -or [int]$resume.$nom -ne 0) { throw ('Code de test non nul : ' + $nom) }
}
if (@($resume.ScriptsPowerShell).Count -eq 0 -or @($resume.ScriptsPowerShell | Where-Object { $_.Erreurs -ne 0 }).Count -ne 0) {
    throw 'Analyse syntaxique PowerShell incomplete ou en echec.'
}
$stamp = $nouveauxResumes[0].Directory.Name.Substring('Verification-'.Length)
$preuves = Join-Path $resultats ('Preuves-' + $stamp + '.zip')
if (-not (Test-Path -LiteralPath $preuves -PathType Leaf)) { throw 'Archive de preuves R5 absente. Aucun paquet fabrique.' }
$sourceZipSHA256 = $null
# GitHub uniquement : archive des objets du commit, jamais de .git ou d'un dossier local incomplet.
# Cette archive reste disponible si la phase MSIX suivante echoue.
if ($env:GITHUB_ACTIONS -eq 'true') {
    $git = Get-Command git.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $racineGit = (& $git.Source -C $racine rev-parse --show-toplevel)
    if ($LASTEXITCODE -ne 0) { throw 'Impossible de lire la racine Git du checkout.' }
    if ([IO.Path]::GetFullPath(([string]$racineGit).Trim()).TrimEnd('\', '/') -ine $racine.TrimEnd('\', '/')) {
        throw 'La racine Git ne correspond pas au projet de fabrication.'
    }
    $commit = (& $git.Source -C $racine rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or ([string]$commit).Trim() -cne $env:GITHUB_SHA) { throw 'Le checkout ne correspond pas au commit GitHub attendu.' }
    $remote = (& $git.Source -C $racine config --get remote.origin.url)
    if ($LASTEXITCODE -ne 0 -or ([string]$remote).Trim() -cnotmatch '^https://github\.com/JulienLecomteGit/haloryva-studio(?:\.git)?$') {
        throw 'Origine Git inattendue. Aucun archivage.'
    }
    $branche = (& $git.Source -C $racine symbolic-ref --quiet --short HEAD)
    $codeBranche = $LASTEXITCODE
    if ($codeBranche -eq 1) { $branche = 'HEAD detache' }
    elseif ($codeBranche -ne 0) { throw 'Lecture de branche Git impossible.' }
    Write-Host ('Git verifie : racine {0} ; branche {1} ; origin JulienLecomteGit/haloryva-studio ; commit {2}' -f $racine, $branche, $env:GITHUB_SHA)

    $distribution = Join-Path $racine 'Distribution'
    if (-not (Test-Path -LiteralPath $distribution -PathType Container)) { New-Item -ItemType Directory -Path $distribution | Out-Null }
    $sourceZip = Join-Path $distribution 'Haloryva-Studio-2.7.2-sources.zip'
    $sourceProvenance = Join-Path $distribution 'Provenance-sources.json'
    $sourceSommes = Join-Path $distribution 'SHA256SUMS.txt'
    foreach ($chemin in @($sourceZip, $sourceProvenance, $sourceSommes)) {
        if (Test-Path -LiteralPath $chemin) { throw 'La destination de sources existe deja. Aucun ecrasement.' }
    }
    $treeLines = @(& $git.Source -c core.quotepath=false -C $racine ls-tree -r $env:GITHUB_SHA)
    if ($LASTEXITCODE -ne 0) { throw 'Lecture de l arbre du commit impossible.' }
    $expectedFiles = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($line in $treeLines) {
        $entry = [regex]::Match($line, '\A[0-7]{6} blob ([a-f0-9]{40})\t(.+)\z')
        if (-not $entry.Success) { throw 'L arbre contient une entree non prise en charge par l archive de sources.' }
        $expectedFiles.Add($entry.Groups[2].Value, $entry.Groups[1].Value)
    }
    if ($expectedFiles.Count -eq 0) { throw 'Arbre Git vide.' }
    & $git.Source -C $racine archive --format=zip --prefix=Haloryva-Studio-2.7.2/ -o $sourceZip $env:GITHUB_SHA
    if ($LASTEXITCODE -ne 0) { throw 'git archive a echoue.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceArchive = [IO.Compression.ZipFile]::OpenRead($sourceZip)
    $verifiedFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    try {
        foreach ($entry in $sourceArchive.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            if (-not $entry.FullName.StartsWith('Haloryva-Studio-2.7.2/', [StringComparison]::Ordinal)) { throw 'Chemin ZIP inattendu.' }
            $relative = $entry.FullName.Substring('Haloryva-Studio-2.7.2/'.Length)
            if (-not $expectedFiles.ContainsKey($relative) -or -not $verifiedFiles.Add($relative)) { throw 'Fichier ZIP inattendu ou en double.' }
            $stream = $entry.Open()
            $memory = New-Object IO.MemoryStream
            try {
                $header = [Text.Encoding]::UTF8.GetBytes(('blob ' + $entry.Length + [char]0))
                $memory.Write($header, 0, $header.Length)
                $stream.CopyTo($memory)
                $memory.Position = 0
                $sha1 = [Security.Cryptography.SHA1]::Create()
                try { $blob = ([BitConverter]::ToString($sha1.ComputeHash($memory))).Replace('-', '').ToLowerInvariant() }
                finally { $sha1.Dispose() }
                if ($blob -cne $expectedFiles[$relative]) { throw ('Octets ZIP differents de l objet Git : ' + $relative) }
            }
            finally { $stream.Dispose(); $memory.Dispose() }
        }
    }
    finally { $sourceArchive.Dispose() }
    if ($verifiedFiles.Count -ne $expectedFiles.Count) { throw 'Archive incomplete par rapport au commit Git.' }
    $sourceZipSHA256 = (Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash
    $provenance = [ordered]@{
        Version = '2.7.2'; Commit = $env:GITHUB_SHA; Reference = $env:GITHUB_REF
        Depot = $env:GITHUB_REPOSITORY; WorkflowRun = $env:GITHUB_RUN_ID
        Creation = 'git archive du commit exact ; chaque fichier compare a son objet Git'
        Fichiers = $verifiedFiles.Count; Archive = [IO.Path]::GetFileName($sourceZip)
        SHA256 = $sourceZipSHA256; Bytes = (Get-Item -LiteralPath $sourceZip).Length
        SourceInventorySHA256 = $empreinte; CompilationEtTestsR5 = 'REUSSIS'
        PaquetStore = 'PHASE_DISTINCTE'; Installation = $false; Publication = $false
        DateUTC = [DateTime]::UtcNow.ToString('o')
    }
    $sourceUtf8 = New-Object Text.UTF8Encoding($true)
    [IO.File]::WriteAllText($sourceProvenance, ($provenance | ConvertTo-Json -Depth 5), $sourceUtf8)
    [IO.File]::WriteAllLines($sourceSommes, @(
        ('{0}  {1}' -f $sourceZipSHA256, [IO.Path]::GetFileName($sourceZip)),
        ('{0}  {1}' -f (Get-FileHash -LiteralPath $sourceProvenance -Algorithm SHA256).Hash, [IO.Path]::GetFileName($sourceProvenance))
    ), (New-Object Text.UTF8Encoding($false)))
    Write-Host ('SOURCES_COMPLETES_OK : {0} fichiers du commit ; SHA256 {1}' -f $verifiedFiles.Count, $sourceZipSHA256)
}
$binaire = Join-Path $nouveauxResumes[0].Directory.FullName 'Haloryva.exe'
$pack = Join-Path $racine 'packaging\Creer-Paquet.ps1'
& $powershell -NoProfile -ExecutionPolicy Bypass -File $pack -ProjetSources $racine -Binaire $binaire -ResumeCompilation $resumePath -Destination $livraison
$codePackage = $LASTEXITCODE
if ($codePackage -ne 0) { throw ('Fabrication du paquet echouee, code ' + $codePackage) }

Copy-Item -LiteralPath $preuves -Destination (Join-Path $livraison 'Preuves-compilation-tests.zip')
$utf8 = New-Object Text.UTF8Encoding($true)
$contexte = [ordered]@{
    Version = '2.7.2'
    VersionFichier = '2.7.2.6'
    VersionPaquet = '2.7.2.0'
    DateUTC = [DateTime]::UtcNow.ToString('o')
    Commit = if ($env:GITHUB_SHA -match '^[A-Fa-f0-9]{40}$') { $env:GITHUB_SHA } else { $null }
    Reference = if ($env:GITHUB_ACTIONS -eq 'true') { $env:GITHUB_REF } else { $null }
    Depot = if ($env:GITHUB_ACTIONS -eq 'true') { $env:GITHUB_REPOSITORY } else { $null }
    WorkflowRun = if ($env:GITHUB_ACTIONS -eq 'true') { $env:GITHUB_RUN_ID } else { $null }
    SourceInventorySHA256 = $empreinte
    CompleteSourceArchiveSHA256 = $sourceZipSHA256
    BinaireSHA256 = $resume.AppSHA256
    VerificationExitCode = $codeVerification
    PackagingExitCode = $codePackage
    Installation = $false
    Signature = $false
    SoumissionStore = $false
    PublicationStore = $false
    Limite = 'Compilation et tests automatiques uniquement. Le paquet non signe doit etre soumis au Store ; aucune installation ou presentation ecran mesuree par cette fabrication.'
}
[IO.File]::WriteAllText((Join-Path $livraison 'Contexte-fabrication.json'), ($contexte | ConvertTo-Json -Depth 6), $utf8)
$fichiers = @()
foreach ($fichier in (Get-ChildItem -LiteralPath $livraison -File | Sort-Object Name)) {
    $fichiers += [ordered]@{ Path = $fichier.Name; SHA256 = (Get-FileHash -LiteralPath $fichier.FullName -Algorithm SHA256).Hash; Bytes = $fichier.Length }
}
[IO.File]::WriteAllText((Join-Path $livraison 'Inventaire-livraison.json'), (ConvertTo-Json -InputObject @($fichiers) -Depth 5), $utf8)
$sommes = @(Get-ChildItem -LiteralPath $livraison -File | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Name
})
[IO.File]::WriteAllLines((Join-Path $livraison 'SHA256SUMS.txt'), $sommes, (New-Object Text.UTF8Encoding($false)))
Write-Host 'FABRICATION_OK : compilation, autotests et structure MSIX valides.'
Write-Host 'Paquet Store non signe. Aucune installation, soumission ou publication realisee.'
Write-Host ('Livraison : ' + $livraison)

