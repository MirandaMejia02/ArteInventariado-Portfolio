[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$MinimumCompatibleVersion = "1.14.2",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PackageName,
    [string]$ArtifactsRoot,
    [string]$ReleaseNotes
)

$ErrorActionPreference = "Stop"

$Version = $Version.Trim()
$MinimumCompatibleVersion = $MinimumCompatibleVersion.Trim()
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version debe tener formato semver simple, por ejemplo 1.16.3."
}
if ($MinimumCompatibleVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "MinimumCompatibleVersion debe tener formato semver simple."
}
if ($Runtime -ne "win-x64") {
    throw "Los paquetes machine-wide de ArteInventariado solo admiten win-x64."
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
} else {
    [System.IO.Path]::GetFullPath($ArtifactsRoot)
}
if ([string]::Equals($artifactsRoot, [System.IO.Path]::GetPathRoot($artifactsRoot), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactsRoot debe ser una carpeta de salida dedicada, no la raíz de una unidad."
}
$packageName = if ([string]::IsNullOrWhiteSpace($PackageName)) {
    "ArteInventariado-$Version-Local-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss'))-win-x64"
} else {
    $PackageName.Trim()
}
if ([System.IO.Path]::GetFileName($packageName) -ne $packageName -or
    $packageName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
    $packageName -notmatch "^ArteInventariado-$([regex]::Escape($Version))-.+-win-x64$") {
    throw "PackageName debe ser un nombre simple que identifique ArteInventariado-$Version-...-win-x64."
}

$publishDirectory = Join-Path $artifactsRoot $packageName
$archivePath = Join-Path $artifactsRoot "$packageName.zip"
$manifestPath = Join-Path $artifactsRoot "$packageName.manifest.json"
$publishScript = Join-Path $PSScriptRoot "Publish-V1.ps1"
foreach ($path in @($publishDirectory, $archivePath, $manifestPath)) {
    if (Test-Path -LiteralPath $path) {
        throw "Ya existe '$path'. No se sobrescribió ningún artefacto; elija otro PackageName."
    }
}

& $publishScript `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -Version $Version `
    -PackageName $packageName `
    -ArtifactsRoot $artifactsRoot
if ($LASTEXITCODE -ne 0) {
    throw "Publish-V1.ps1 terminó con código $LASTEXITCODE. Se conservaron sus diagnósticos."
}

if (-not (Test-Path -LiteralPath $archivePath)) {
    throw "No se generó el ZIP esperado: $archivePath"
}
$versionFile = Get-Content -LiteralPath (Join-Path $publishDirectory "VERSION.txt") -Raw
if ($versionFile -notmatch [regex]::Escape("Version: $Version") -or
    $versionFile -notmatch [regex]::Escape("ProductVersion: $Version")) {
    throw "El contenido publicado no declara exactamente la versión $Version."
}

$requiredExecutables = @(
    "ArteInventariado.WinForms.exe",
    "ArteInventariado.Updater.exe",
    "ArteInventariado.Launcher.exe",
    "ArteInventariado.StationHelper.exe",
    "ArteInventariado.BackupRunner.exe",
    "ArteInventariado.CloudBackupRunner.exe"
)
foreach ($name in $requiredExecutables) {
    $path = Join-Path $publishDirectory $name
    if (-not (Test-Path -LiteralPath $path) -or
        [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion -ne $Version) {
        throw "El paquete no contiene '$name' con ProductVersion '$Version'."
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($name in $requiredExecutables) {
        if ($name -cnotin $entryNames) {
            throw "El ZIP no contiene '$name' en su raíz."
        }
    }
    $forbiddenEntries = $archive.Entries | Where-Object {
        $_.FullName -match '(?i)(^|/)(setup\.exe|ArteInventariado\.Setup\.exe)$' -or
        [System.IO.Path]::GetExtension($_.FullName).ToLowerInvariant() -in @(".db", ".sqlite", ".sqlite3", ".aibak", ".wal", ".shm", ".journal")
    }
    if ($forbiddenEntries) {
        throw "El ZIP contiene datos locales o un instalador fuera del alcance: $($forbiddenEntries.FullName -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

$archiveInfo = Get-Item -LiteralPath $archivePath
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
$notes = if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    "Actualización machine-wide de ArteInventariado a $Version. Update Center valida el paquete y crea PRE_UPDATE; conserva la base de datos del negocio. No es instalación limpia ni reinicio de base de datos."
} else {
    $ReleaseNotes.Trim()
}
$manifest = [ordered]@{
    version = $Version
    minimumCompatibleVersion = $MinimumCompatibleVersion
    maximumCompatibleVersion = $null
    packagePath = [System.IO.Path]::GetFileName($archivePath)
    deploymentTarget = "MachineWide"
    sha256 = $archiveHash
    sizeBytes = [long]$archiveInfo.Length
    releaseNotes = $notes
    publishedUtc = [DateTimeOffset]::UtcNow.ToString("O")
}
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

$writtenManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($writtenManifest.version -ne $Version -or
    $writtenManifest.deploymentTarget -ne "MachineWide" -or
    $writtenManifest.packagePath -ne [System.IO.Path]::GetFileName($archivePath) -or
    $writtenManifest.sizeBytes -ne $archiveInfo.Length -or
    $writtenManifest.sha256 -ne $archiveHash) {
    throw "El manifest generado no coincide con el ZIP local."
}

[pscustomobject]@{
    Status = "LOCAL_MACHINE_WIDE_UPDATE_BUILT_NOT_INSTALLED"
    Version = $Version
    DeploymentTarget = $writtenManifest.deploymentTarget
    PublishDirectory = $publishDirectory
    PackagePath = $archivePath
    ManifestPath = $manifestPath
    SizeBytes = $archiveInfo.Length
    Sha256 = $archiveHash
}
