<#
.SYNOPSIS
Verifie la mise a jour Store de Haloryva Studio 2.7.2, avant et apres.
.DESCRIPTION
Windows PowerShell 5.1 Desktop x64. Aucune installation ou modification des
reglages : seul un nouveau rapport JSON est ecrit, dans Documents par defaut.
Ferme Haloryva proprement pour comparer les reglages sans changement en cours.
La version installee et la conservation du profil sont deux controles distincts.
.EXAMPLE
.\Verifier-MiseAJour-Haloryva.ps1 -Etape Avant
.EXAMPLE
.\Verifier-MiseAJour-Haloryva.ps1 -Etape Apres -EtatAvant 'C:\chemin\rapport-Avant.json'
.NOTES
Codes : 0 = controle termine (Avant) ou version ET profil confirmes (Apres).
1 = erreur de verification ; 3 = version cible non confirmee ;
4 = version cible confirmee, conservation du profil non confirmee.
Le rapport reste local. -OuvrirStore ouvre la fiche, sans installer l'application.
#>
[CmdletBinding()]
param(
    [ValidateSet('Avant', 'Apres')]
    [string]$Etape = 'Avant',
    [string]$EtatAvant = '',
    [string]$DossierRapports = '',
    [switch]$OuvrirStore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Get-TextHash([string]$Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '')
    }
    finally { $sha.Dispose() }
}

function Read-ProfileState([string]$Path) {
    $state = [ordered]@{ Present = $false; JsonValide = $false; SHA256 = $null; Champs = @(); Erreur = $null }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return [pscustomobject]$state }
    $state.Present = $true
    try {
        $info = Get-Item -LiteralPath $Path
        if ($info.Length -gt 1000000) { throw 'Profil trop volumineux.' }
        $bytes = [IO.File]::ReadAllBytes($Path)
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $state.SHA256 = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '') }
        finally { $sha.Dispose() }
        $json = [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF)
        if (-not $json.TrimStart().StartsWith('{')) { throw 'Objet JSON attendu.' }
        # Affectation directe avant enumeration : compatible PowerShell 5.1.
        $profile = ConvertFrom-Json -InputObject $json -ErrorAction Stop
        if ($null -eq $profile -or $profile -is [array]) { throw 'Objet JSON attendu.' }
        $fields = @()
        foreach ($property in ($profile.PSObject.Properties | Sort-Object Name)) {
            $valueJson = ConvertTo-Json -InputObject $property.Value -Compress -Depth 20
            $fields += [pscustomobject]@{ Nom = [string]$property.Name; SHA256 = Get-TextHash ([string]$valueJson) }
        }
        $state.JsonValide = $true
        $state.Champs = $fields
    }
    catch { $state.Erreur = $_.Exception.GetType().Name }
    return [pscustomobject]$state
}

function Write-NewJson([string]$Path, $Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 30
    $utf8 = New-Object Text.UTF8Encoding($false)
    $bytes = $utf8.GetBytes($json)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush() }
    finally { $stream.Dispose() }
}

try {
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or
        $PSVersionTable.PSVersion.Minor -ne 1 -or -not [Environment]::Is64BitProcess) {
        throw 'Ouvre Windows PowerShell 5.1 en 64 bits, sans x86, dans ton compte habituel.'
    }
    if (-not (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue)) { throw 'Get-AppxPackage est indisponible.' }
    if ([string]::IsNullOrWhiteSpace($DossierRapports)) {
        $DossierRapports = [Environment]::GetFolderPath('MyDocuments')
    }
    if ([string]::IsNullOrWhiteSpace($DossierRapports)) { throw 'Dossier Documents indisponible : fournis -DossierRapports avec un dossier existant.' }
    if (-not (Test-Path -LiteralPath $DossierRapports -PathType Container)) { throw "Dossier de rapports introuvable : $DossierRapports" }
    if ($Etape -eq 'Avant' -and -not [string]::IsNullOrWhiteSpace($EtatAvant)) { throw '-EtatAvant est reserve a l etape Apres.' }

    $identity = 'JulienLecomte.HaloryvaStudio'
    $family = 'JulienLecomte.HaloryvaStudio_twtgq1d174ne0'
    $targetPackage = [version]'2.7.2.0'
    $targetFile = '2.7.2.6'
    $targetProduct = '2.7.2'
    $userSidHash = Get-TextHash ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    $localData = [Environment]::GetFolderPath('LocalApplicationData')
    $profilePath = Join-Path $localData ('Packages\' + $family + '\LocalState\Haloryva\settings.json')

    $packages = @(Get-AppxPackage -Name $identity -PackageTypeFilter Main -ErrorAction Stop)
    if ($packages.Count -gt 1) { throw 'Plusieurs paquets Haloryva trouves : aucune selection automatique.' }
    $packageState = [ordered]@{
        Present = $false; Famille = $null; VersionPaquet = $null; Architecture = $null
        Signature = $null; Etat = $null; VersionProduit = $null; VersionFichier = $null
        BinaireSHA256 = $null; IdentiteConforme = $false; VersionCibleConforme = $false
    }
    $bilan = 'PAQUET_STORE_ABSENT'
    if ($packages.Count -eq 1) {
        $package = $packages[0]
        $packageState.Present = $true
        $packageState.Famille = [string]$package.PackageFamilyName
        $packageState.VersionPaquet = $package.Version.ToString()
        $packageState.Architecture = [string]$package.Architecture
        $packageState.Signature = [string]$package.SignatureKind
        $packageState.Etat = [string]$package.Status
        $packageState.IdentiteConforme = ($package.PackageFamilyName -eq $family)
        if (-not $packageState.IdentiteConforme) { throw 'Famille du paquet differente de la reference Store.' }
        $app = Join-Path $package.InstallLocation 'Haloryva.exe'
        if (Test-Path -LiteralPath $app -PathType Leaf) {
            $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($app)
            $packageState.VersionProduit = $versionInfo.ProductVersion
            $packageState.VersionFichier = $versionInfo.FileVersion
            $packageState.BinaireSHA256 = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
        }
        $packageState.VersionCibleConforme = (
            $package.Version -eq $targetPackage -and
            $packageState.VersionProduit -eq $targetProduct -and
            $packageState.VersionFichier -eq $targetFile -and
            $packageState.Architecture -eq 'X64' -and
            $packageState.Signature -eq 'Store' -and
            $packageState.Etat -eq 'Ok'
        )
        if ($packageState.VersionCibleConforme) { $bilan = 'VERSION_CIBLE_INSTALLEE' }
        elseif ($package.Version -lt $targetPackage) { $bilan = 'MISE_A_JOUR_STORE_ATTENDUE' }
        elseif ($package.Version -gt $targetPackage) { $bilan = 'VERSION_PLUS_RECENTE_PRESENTE' }
        else { $bilan = 'METADONNEES_CIBLE_NON_CONFORMES' }
    }

    $profileState = Read-ProfileState $profilePath
    $processes = @(Get-Process -Name Haloryva, CursorFlow -ErrorAction SilentlyContinue)
    $comparison = [ordered]@{ Etat = 'NON_DEMANDEE'; ChampsAvant = 0; ChampsApres = @($profileState.Champs).Count; Identiques = 0; Modifies = @(); Manquants = @(); Ajoutes = @(); OctetsIdentiques = $null }
    if (-not [string]::IsNullOrWhiteSpace($EtatAvant)) {
        if (-not (Test-Path -LiteralPath $EtatAvant -PathType Leaf)) { throw 'Rapport avant introuvable.' }
        if ((Get-Item -LiteralPath $EtatAvant).Length -gt 1000000) { throw 'Rapport avant trop volumineux.' }
        $beforeText = Get-Content -LiteralPath $EtatAvant -Raw -Encoding UTF8
        $before = ConvertFrom-Json -InputObject $beforeText -ErrorAction Stop
        if ($before.SchemaVersion -ne 1 -or $before.Etape -ne 'Avant' -or $before.FamilleAttendue -ne $family) {
            throw 'Ce fichier ne correspond pas au rapport Avant attendu.'
        }
        if ($before.CompteEmpreinte -ne $userSidHash) { throw 'Les rapports Avant et Apres concernent des comptes Windows differents.' }
        if (-not $before.Profil.JsonValide -or -not $profileState.JsonValide) { $comparison.Etat = 'COMPARAISON_IMPOSSIBLE_PROFIL_ABSENT_OU_ILLISIBLE' }
        else {
            $oldFields = @{}
            $newFields = @{}
            foreach ($field in @($before.Profil.Champs)) { $oldFields[[string]$field.Nom] = [string]$field.SHA256 }
            foreach ($field in @($profileState.Champs)) { $newFields[[string]$field.Nom] = [string]$field.SHA256 }
            $comparison.ChampsAvant = $oldFields.Count
            $comparison.OctetsIdentiques = ($before.Profil.SHA256 -eq $profileState.SHA256)
            foreach ($name in ($oldFields.Keys | Sort-Object)) {
                if (-not $newFields.ContainsKey($name)) { $comparison.Manquants += $name }
                elseif ($oldFields[$name] -eq $newFields[$name]) { $comparison.Identiques++ }
                else { $comparison.Modifies += $name }
            }
            foreach ($name in ($newFields.Keys | Sort-Object)) { if (-not $oldFields.ContainsKey($name)) { $comparison.Ajoutes += $name } }
            if ($oldFields.Count -eq 0) { $comparison.Etat = 'COMPARAISON_IMPOSSIBLE_PROFIL_AVANT_VIDE' }
            elseif ($comparison.Manquants.Count -eq 0 -and $comparison.Modifies.Count -eq 0) { $comparison.Etat = 'VALEURS_EXISTANTES_IDENTIQUES' }
            else { $comparison.Etat = 'DIFFERENCES_A_EXAMINER_SANS_CONCLUSION_DE_PERTE' }
        }
    }
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    $legacyStartup = [ordered]@{ Haloryva = $false; CursorFlow = $false }
    if ($null -ne $runKey) {
        try {
            $legacyStartup.Haloryva = ($null -ne $runKey.GetValue('Haloryva'))
            $legacyStartup.CursorFlow = ($null -ne $runKey.GetValue('CursorFlow'))
        }
        finally { $runKey.Dispose() }
    }
    # Une version installee correcte ne suffit pas a prouver les reglages conserves.
    $conservationConfirmed = ($Etape -eq 'Apres' -and $comparison.Etat -eq 'VALEURS_EXISTANTES_IDENTIQUES')
    $exitCode = 0
    if ($Etape -eq 'Apres' -and -not $packageState.VersionCibleConforme) { $exitCode = 3 }
    elseif ($Etape -eq 'Apres' -and -not $conservationConfirmed) { $exitCode = 4 }
    $report = [ordered]@{
        SchemaVersion = 1; Etape = $Etape; DateUTC = [DateTime]::UtcNow.ToString('o'); Bilan = $bilan
        FamilleAttendue = $family; VersionPaquetAttendue = $targetPackage.ToString(); VersionFichierAttendue = $targetFile
        CompteEmpreinte = $userSidHash; Paquet = [pscustomobject]$packageState
        Profil = $profileState; ComparaisonProfil = [pscustomobject]$comparison
        ConservationProfilConfirmee = $conservationConfirmed
        ValidationCompleteApres = ($Etape -eq 'Apres' -and $exitCode -eq 0)
        CodeSortie = $exitCode
        ProcessusEffetsActifs = $processes.Count; DemarragesClassiquesPresents = [pscustomobject]$legacyStartup
        DemarrageStore = 'A_VERIFIER_DANS_APPLICATION_OU_PARAMETRES_WINDOWS'
        StrategieConsole = [string](Get-ExecutionPolicy)
        Limite = 'Verifie le paquet inscrit pour le compte courant, pas le rendu visuel ni le demarrage apres reconnexion. Les valeurs du profil ne sont pas enregistrees. Un profil peut evoluer pendant que Haloryva tourne.'
    }
    $name = 'MiseAJour-Haloryva-' + $Etape + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6) + '.json'
    $reportPath = Join-Path $DossierRapports $name
    Write-NewJson $reportPath ([pscustomobject]$report)
    Write-Host ('Bilan : {0}' -f $bilan)
    Write-Host ('Version paquet : {0} ; version fichier : {1}' -f $packageState.VersionPaquet, $packageState.VersionFichier)
    Write-Host ('Profil : {0} ; comparaison : {1}' -f $profileState.JsonValide, $comparison.Etat)
    Write-Host ('Conservation du profil confirmee : {0}' -f $conservationConfirmed)
    Write-Host ('Processus d effets actifs : {0}' -f $processes.Count)
    Write-Host ('Rapport : {0}' -f $reportPath)
    if ($Etape -eq 'Avant') { Write-Host 'Conserve ce chemin pour le parametre -EtatAvant lors du controle Apres.' }
    if ($OuvrirStore) {
        Start-Process -FilePath 'ms-windows-store://pdp/?ProductId=9NJ14SZJP55B' -ErrorAction Stop
        Write-Host 'Fiche Store ouverte. Cette ouverture ne confirme ni la disponibilite ni l installation de la mise a jour.'
    }
    if ($Etape -eq 'Apres' -and $exitCode -eq 4) {
        Write-Host 'Version cible presente ; le profil reste a verifier. Un changement de valeur ne prouve pas une perte.'
    }
    Write-Host ('Code de verification : {0}' -f $exitCode)
    exit $exitCode
}
catch {
    Write-Error ('Verification interrompue : ' + $_.Exception.Message) -ErrorAction Continue
    exit 1
}
