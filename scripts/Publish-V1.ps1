[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.16.2",
    [string]$PackageName,
    [string]$ArtifactsRoot,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$Version = $Version.Trim()
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version debe tener formato semver simple (por ejemplo 1.16.2 o 1.16.2-rc1)."
}

$coreVersion = ($Version -split '-', 2)[0]
$versionSuffix = if ($Version.Contains('-')) {
    $Version.Substring($Version.IndexOf('-') + 1)
} else {
    [string]::Empty
}
$versionProperties = @(
    "-p:Version=$Version",
    "-p:VersionPrefix=$coreVersion",
    "-p:VersionSuffix=$versionSuffix",
    "-p:InformationalVersion=$Version",
    "-p:AssemblyVersion=$coreVersion.0",
    "-p:FileVersion=$coreVersion.0"
)

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
    "ArteInventariado-$Version-$Runtime"
} else {
    $PackageName.Trim()
}
if ([System.IO.Path]::GetFileName($packageName) -ne $packageName -or $packageName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw "PackageName debe ser un nombre simple de carpeta/archivo."
}
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot $packageName))
$archivePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "$packageName.zip"))
$projectPath = Join-Path $repoRoot "ArteInventariado.WinForms\ArteInventariado.WinForms.csproj"
$updaterProjectPath = Join-Path $repoRoot "ArteInventariado.Updater\ArteInventariado.Updater.csproj"
$launcherProjectPath = Join-Path $repoRoot "ArteInventariado.Launcher\ArteInventariado.Launcher.csproj"
$stationHelperProjectPath = Join-Path $repoRoot "ArteInventariado.StationHelper\ArteInventariado.StationHelper.csproj"
$backupRunnerProjectPath = Join-Path $repoRoot "ArteInventariado.BackupRunner\ArteInventariado.BackupRunner.csproj"
$cloudBackupRunnerProjectPath = Join-Path $repoRoot "ArteInventariado.CloudBackupRunner\ArteInventariado.CloudBackupRunner.csproj"
$componentStageRoot = Join-Path $artifactsRoot ".publish-components-$([Guid]::NewGuid().ToString('N'))"
$runnerRuntimeSupportFiles = @(
    "e_sqlite3.dll",
    "libSkiaSharp.dll"
)

if (-not $publishDirectory.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "La salida calculada no pertenece al directorio artifacts del repositorio."
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

foreach ($path in @($publishDirectory, $archivePath)) {
    if (Test-Path -LiteralPath $path) {
        if (-not $Force) {
            throw "Ya existe '$path'. Use -Force para reemplazar exclusivamente este artefacto."
        }

        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDirectory `
    @versionProperties

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish termino con codigo $LASTEXITCODE."
}

try {
    foreach ($component in @(
        @{ Name = "actualizador"; Project = $updaterProjectPath; Executable = "ArteInventariado.Updater.exe" },
        @{ Name = "launcher"; Project = $launcherProjectPath; Executable = "ArteInventariado.Launcher.exe" },
        @{ Name = "helper de station"; Project = $stationHelperProjectPath; Executable = "ArteInventariado.StationHelper.exe" },
        @{ Name = "runner de respaldos"; Project = $backupRunnerProjectPath; Executable = "ArteInventariado.BackupRunner.exe" },
        @{ Name = "runner cloud de respaldos"; Project = $cloudBackupRunnerProjectPath; Executable = "ArteInventariado.CloudBackupRunner.exe" }
    )) {
        if (-not (Test-Path -LiteralPath $component.Project)) {
            throw "No existe el proyecto del $($component.Name)."
        }

        $componentOutput = Join-Path $componentStageRoot $component.Name
        # Native libraries stay beside the protected entrypoint. A single-file
        # apphost that self-extracts them depends on TEMP/TMP, which is not a
        # valid contract for SYSTEM tasks.
        $includeNativeLibrariesForSelfExtract = if ($component.Executable -in @(
                "ArteInventariado.BackupRunner.exe",
                "ArteInventariado.CloudBackupRunner.exe")) {
            "false"
        } else {
            "true"
        }
        dotnet publish $component.Project `
            -c $Configuration `
            -r $Runtime `
            --self-contained true `
            -p:PublishSingleFile=true `
            "-p:IncludeNativeLibrariesForSelfExtract=$includeNativeLibrariesForSelfExtract" `
            -o $componentOutput `
            @versionProperties

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish del $($component.Name) termino con codigo $LASTEXITCODE."
        }

        $componentExecutable = Join-Path $componentOutput $component.Executable
        if (-not (Test-Path -LiteralPath $componentExecutable)) {
            throw "No se genero $($component.Executable)."
        }

        Copy-Item -LiteralPath $componentExecutable -Destination (Join-Path $publishDirectory $component.Executable)

        if ($component.Executable -in @("ArteInventariado.BackupRunner.exe", "ArteInventariado.CloudBackupRunner.exe")) {
            foreach ($runtimeFile in $runnerRuntimeSupportFiles) {
                $componentRuntimeFile = Join-Path $componentOutput $runtimeFile
                $packageRuntimeFile = Join-Path $publishDirectory $runtimeFile
                if (-not (Test-Path -LiteralPath $componentRuntimeFile)) {
                    throw "El componente $($component.Executable) no publico la dependencia nativa requerida '$runtimeFile'."
                }

                if (Test-Path -LiteralPath $packageRuntimeFile) {
                    $componentHash = (Get-FileHash -LiteralPath $componentRuntimeFile -Algorithm SHA256).Hash
                    $packageHash = (Get-FileHash -LiteralPath $packageRuntimeFile -Algorithm SHA256).Hash
                    if ($componentHash -ne $packageHash) {
                        throw "La dependencia nativa '$runtimeFile' no coincide con la ya publicada."
                    }
                } else {
                    Copy-Item -LiteralPath $componentRuntimeFile -Destination $packageRuntimeFile
                }
            }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $componentStageRoot) {
        Remove-Item -LiteralPath $componentStageRoot -Recurse -Force
    }
}

$executablePath = Join-Path $publishDirectory "ArteInventariado.WinForms.exe"
$updaterExecutablePath = Join-Path $publishDirectory "ArteInventariado.Updater.exe"
$launcherExecutablePath = Join-Path $publishDirectory "ArteInventariado.Launcher.exe"
$stationHelperExecutablePath = Join-Path $publishDirectory "ArteInventariado.StationHelper.exe"
$backupRunnerExecutablePath = Join-Path $publishDirectory "ArteInventariado.BackupRunner.exe"
$cloudBackupRunnerExecutablePath = Join-Path $publishDirectory "ArteInventariado.CloudBackupRunner.exe"

if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "No se genero el ejecutable esperado."
}

if (-not (Test-Path -LiteralPath $updaterExecutablePath)) {
    throw "No se genero ArteInventariado.Updater.exe."
}

if (-not (Test-Path -LiteralPath $launcherExecutablePath)) {
    throw "No se genero ArteInventariado.Launcher.exe."
}

if (-not (Test-Path -LiteralPath $stationHelperExecutablePath)) {
    throw "No se genero ArteInventariado.StationHelper.exe."
}

if (-not (Test-Path -LiteralPath $backupRunnerExecutablePath)) {
    throw "No se genero ArteInventariado.BackupRunner.exe."
}

if (-not (Test-Path -LiteralPath $cloudBackupRunnerExecutablePath)) {
    throw "No se genero ArteInventariado.CloudBackupRunner.exe."
}

$forbiddenFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    Where-Object {
        $_.Extension.ToLowerInvariant() -in @(".db", ".sqlite", ".sqlite3", ".aibak", ".aibak.tmp", ".aibak.uploading", ".wal", ".shm", ".journal") -or
        $_.Name -match "(?i)(application-errors|station-audit|backup-status|current\.json|previous|station-settings|installationId|\.aibak|\.db-wal|\.db-shm|\.db-journal|\.log$|\.bak$)"
    }

if ($forbiddenFiles) {
    $names = $forbiddenFiles.FullName -join [Environment]::NewLine
    throw "El paquete contiene datos o diagnosticos locales que no deben distribuirse:$([Environment]::NewLine)$names"
}

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
if ($fileVersion.ProductVersion -ne $Version) {
    throw "ProductVersion '$($fileVersion.ProductVersion)' no coincide con la version solicitada '$Version'."
}

$updaterVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($updaterExecutablePath)
if ($updaterVersion.ProductVersion -ne $Version) {
    throw "ProductVersion del actualizador '$($updaterVersion.ProductVersion)' no coincide con '$Version'."
}

$launcherVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($launcherExecutablePath)
if ($launcherVersion.ProductVersion -ne $Version) {
    throw "ProductVersion del launcher '$($launcherVersion.ProductVersion)' no coincide con '$Version'."
}

$stationHelperVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($stationHelperExecutablePath)
if ($stationHelperVersion.ProductVersion -ne $Version) {
    throw "ProductVersion del helper de station '$($stationHelperVersion.ProductVersion)' no coincide con '$Version'."
}

$backupRunnerVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($backupRunnerExecutablePath)
if ($backupRunnerVersion.ProductVersion -ne $Version) {
    throw "ProductVersion del runner de respaldos '$($backupRunnerVersion.ProductVersion)' no coincide con '$Version'."
}

$cloudBackupRunnerVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cloudBackupRunnerExecutablePath)
if ($cloudBackupRunnerVersion.ProductVersion -ne $Version) {
    throw "ProductVersion del runner cloud '$($cloudBackupRunnerVersion.ProductVersion)' no coincide con '$Version'."
}

$versionLines = @(
    "Producto: ArteInventariado",
    "Version: $Version",
    "Runtime: $Runtime self-contained",
    "FileVersion: $($fileVersion.FileVersion)",
    "ProductVersion: $($fileVersion.ProductVersion)",
    "GeneradoUTC: $([DateTimeOffset]::UtcNow.ToString('O'))"
)
$versionLines | Set-Content -LiteralPath (Join-Path $publishDirectory "VERSION.txt") -Encoding UTF8

$hash = Get-FileHash -LiteralPath $executablePath -Algorithm SHA256
$updaterHash = Get-FileHash -LiteralPath $updaterExecutablePath -Algorithm SHA256
$launcherHash = Get-FileHash -LiteralPath $launcherExecutablePath -Algorithm SHA256
$stationHelperHash = Get-FileHash -LiteralPath $stationHelperExecutablePath -Algorithm SHA256
$backupRunnerHash = Get-FileHash -LiteralPath $backupRunnerExecutablePath -Algorithm SHA256
$cloudBackupRunnerHash = Get-FileHash -LiteralPath $cloudBackupRunnerExecutablePath -Algorithm SHA256
@(
    "$($hash.Hash)  $($hash.Path | Split-Path -Leaf)",
    "$($updaterHash.Hash)  $($updaterHash.Path | Split-Path -Leaf)",
    "$($launcherHash.Hash)  $($launcherHash.Path | Split-Path -Leaf)",
    "$($stationHelperHash.Hash)  $($stationHelperHash.Path | Split-Path -Leaf)",
    "$($backupRunnerHash.Hash)  $($backupRunnerHash.Path | Split-Path -Leaf)",
    "$($cloudBackupRunnerHash.Hash)  $($cloudBackupRunnerHash.Path | Split-Path -Leaf)"
) |
    Set-Content -LiteralPath (Join-Path $publishDirectory "SHA256SUMS.txt") -Encoding ASCII

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal

[pscustomobject]@{
    PublishDirectory = $publishDirectory
    ArchivePath = $archivePath
    ExecutablePath = $executablePath
    UpdaterExecutablePath = $updaterExecutablePath
    LauncherExecutablePath = $launcherExecutablePath
    StationHelperExecutablePath = $stationHelperExecutablePath
    BackupRunnerExecutablePath = $backupRunnerExecutablePath
    CloudBackupRunnerExecutablePath = $cloudBackupRunnerExecutablePath
    ProductVersion = $fileVersion.ProductVersion
    Sha256 = $hash.Hash
    UpdaterSha256 = $updaterHash.Hash
    LauncherSha256 = $launcherHash.Hash
    StationHelperSha256 = $stationHelperHash.Hash
    BackupRunnerSha256 = $backupRunnerHash.Hash
    CloudBackupRunnerSha256 = $cloudBackupRunnerHash.Hash
}
