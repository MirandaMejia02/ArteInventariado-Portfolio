[CmdletBinding()]
param(
    [ValidateSet("Debug")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Test-PathEqual {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right
    )

    return [string]::Equals(
        (Resolve-FullPath $Left).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar),
        (Resolve-FullPath $Right).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-PathWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Child,
        [Parameter(Mandatory = $true)][string]$Parent
    )

    $childPath = (Resolve-FullPath $Child).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $parentPath = (Resolve-FullPath $Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $prefix = $parentPath + [System.IO.Path]::DirectorySeparatorChar
    return $childPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-GitText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $result = & git -C $repoRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') terminó con código $LASTEXITCODE."
    }

    return ($result -join [Environment]::NewLine).Trim()
}

function Get-VisualDevProcess {
    param([Parameter(Mandatory = $true)][string]$ExecutablePath)

    $expected = Resolve-FullPath $ExecutablePath
    $name = [System.IO.Path]::GetFileNameWithoutExtension($expected)
    $processes = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    foreach ($process in $processes) {
        $candidate = $null
        try {
            $candidate = $process.MainModule.FileName
        }
        catch {
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            [string]::Equals(
                (Resolve-FullPath $candidate),
                $expected,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            $process
        }
    }
}

function Stop-PreviousVisualDev {
    param([Parameter(Mandatory = $true)][string]$ExecutablePath)

    $previous = @(Get-VisualDevProcess $ExecutablePath)
    foreach ($process in $previous) {
        Write-Host "Cerrando DEV anterior PID $($process.Id) ..." -ForegroundColor Yellow
        $closed = $false
        try {
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                $closed = $process.CloseMainWindow()
            }
        }
        catch {
            $closed = $false
        }

        try {
            if ($closed -and $process.WaitForExit(4000)) {
                continue
            }
        }
        catch {
            # The process may have exited between the checks.
        }

        if (-not $process.HasExited) {
            # This PID was resolved by its exact source-tree executable path;
            # never apply this fallback to the installed machine-wide EXE.
            Write-Host "DEV anterior no cerró de forma cooperativa; finalizando solo ese proceso DEV." -ForegroundColor Yellow
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(3000)
        }
    }

    if (@(Get-VisualDevProcess $ExecutablePath).Count -gt 0) {
        throw "No se pudo cerrar la instancia DEV anterior del working tree."
    }
}

function Find-SeedDatabase {
    param([Parameter(Mandatory = $true)][string]$Root)

    $rootPath = Resolve-FullPath $Root
    $candidates = @(
        (Join-Path $rootPath "Data\arteinventariado_v1.db"),
        (Join-Path $rootPath "Data\arteinventariado.db"),
        (Join-Path $rootPath "arteinventariado_v1.db"),
        (Join-Path $rootPath "arteinventariado.db")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [pscustomobject]@{
                Root = $rootPath
                Database = (Resolve-FullPath $candidate)
            }
        }
    }

    return $null
}

function Copy-SeedDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        return $false
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
    return $true
}

function Ensure-VisualDevData {
    param(
        [Parameter(Mandatory = $true)][string]$DataRoot,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot
    )

    $databasePath = Join-Path $DataRoot "Data\arteinventariado_v1.db"
    $stationPath = Join-Path $DataRoot "Station\station-settings.json"

    if ((Test-Path -LiteralPath $databasePath -PathType Leaf) -and
        (Test-Path -LiteralPath $stationPath -PathType Leaf)) {
        return [pscustomobject]@{
            Created = $false
            Source = "persisted DEV data"
            Database = (Resolve-FullPath $databasePath)
        }
    }

    if (Test-Path -LiteralPath $DataRoot -PathType Container) {
        $existingEntries = @(Get-ChildItem -LiteralPath $DataRoot -Force)
        if ($existingEntries.Count -gt 0) {
            throw "La raíz DEV '$DataRoot' está incompleta; no se eliminarán sus datos. Revísala antes de continuar."
        }
    }

    $seedCandidates = @(
        (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "ArteInventariado"),
        (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) "ArteInventariado"),
        $RepositoryRoot,
        (Join-Path $RepositoryRoot "ArteInventariado.WinForms")
    )
    $seed = $null
    foreach ($candidateRoot in $seedCandidates) {
        $found = Find-SeedDatabase $candidateRoot
        if ($null -ne $found) {
            $seed = $found
            break
        }
    }

    if ($null -eq $seed) {
        throw "No se encontró una base de datos para inicializar DEV."
    }

    $dataParent = Split-Path -Parent $DataRoot
    New-Item -ItemType Directory -Path $dataParent -Force | Out-Null
    $stagingRoot = Join-Path $dataParent ".VisualDev-seed-$([Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
        $stagingDatabaseDirectory = Join-Path $stagingRoot "Data"
        New-Item -ItemType Directory -Path $stagingDatabaseDirectory -Force | Out-Null
        $stagingDatabase = Join-Path $stagingDatabaseDirectory "arteinventariado_v1.db"
        Copy-Item -LiteralPath $seed.Database -Destination $stagingDatabase -Force

        foreach ($suffix in @("-wal", "-shm")) {
            $sourceSidecar = "$($seed.Database)$suffix"
            if (Test-Path -LiteralPath $sourceSidecar -PathType Leaf) {
                Copy-Item -LiteralPath $sourceSidecar -Destination "$stagingDatabase$suffix" -Force
            }
        }

        foreach ($directoryName in @("Assets", "BusinessAssets")) {
            $sourceDirectory = Join-Path $seed.Root $directoryName
            $destinationDirectory = Join-Path $stagingRoot $directoryName
            [void](Copy-SeedDirectory -Source $sourceDirectory -Destination $destinationDirectory)
        }

        $stationDirectory = Join-Path $stagingRoot "Station"
        New-Item -ItemType Directory -Path $stationDirectory -Force | Out-Null
        $stationDocument = [ordered]@{
            schemaVersion = 5
            installationId = ([Guid]::NewGuid().ToString())
            installationMode = "PRINCIPAL"
            isFirstRunCompleted = $true
            backup = $null
            reports = $null
            pos = [ordered]@{ quickPosEnabled = $true }
            peripherals = $null
        }
        $stationDocument |
            ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath (Join-Path $stationDirectory "station-settings.json") -Encoding UTF8

        Move-Item -LiteralPath $stagingRoot -Destination $DataRoot
        return [pscustomobject]@{
            Created = $true
            Source = $seed.Database
            Database = (Resolve-FullPath $databasePath)
        }
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }
}

$repoRoot = Resolve-FullPath (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "ArteInventariado.WinForms\ArteInventariado.WinForms.csproj"
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "No se encontró el proyecto WinForms en '$projectPath'."
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
$targetFramework = [string]$projectXml.Project.PropertyGroup.TargetFramework | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "No se pudo resolver TargetFramework desde '$projectPath'."
}

$projectDirectory = Split-Path -Parent $projectPath
$outputDirectory = Join-Path $projectDirectory "bin\$Configuration\$targetFramework"
$executablePath = Join-Path $outputDirectory "ArteInventariado.WinForms.exe"
$localApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$commonApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$devDataRoot = Join-Path $localApplicationData "ArteInventariado\VisualDev"
$normalDataRoot = Join-Path $localApplicationData "ArteInventariado"
$programFilesRoot = Join-Path $programFiles "ArteInventariado"
$programDataRoot = Join-Path $commonApplicationData "ArteInventariado"
$devRootIsUnsafe = (Test-PathEqual $devDataRoot $normalDataRoot) -or (Test-PathWithin $devDataRoot $programFilesRoot) -or (Test-PathWithin $devDataRoot $programDataRoot)

if ($devRootIsUnsafe) {
    throw "La raíz DEV calculada no está aislada de los datos protegidos o normales: '$devDataRoot'."
}

$branch = Invoke-GitText @("branch", "--show-current")
if ([string]::IsNullOrWhiteSpace($branch)) {
    $branch = "(detached HEAD)"
}
$head = Invoke-GitText @("rev-parse", "HEAD")
$status = Invoke-GitText @("status", "--porcelain")
$hasUncommittedChanges = -not [string]::IsNullOrWhiteSpace($status)

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "VISUAL DEV — current working tree" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Rama:                  $branch"
Write-Host "HEAD:                  $head"
if ($hasUncommittedChanges) {
    $changedLines = @($status -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
    Write-Host ("Cambios sin commit:     SI ({0} entradas)" -f $changedLines) -ForegroundColor Yellow
}
else {
    Write-Host "Cambios sin commit:     NO"
}
Write-Host ""

Stop-PreviousVisualDev -ExecutablePath $executablePath

Write-Host ("Compilando WinForms actual ({0}, incremental)..." -f $Configuration) -ForegroundColor Gray
& dotnet build $projectPath --configuration $Configuration --nologo --verbosity minimal
$buildExitCode = $LASTEXITCODE
if ($buildExitCode -ne 0) {
    throw "Build falló con código $buildExitCode. No se iniciará ningún binario anterior."
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Build terminó correctamente pero no generó el ejecutable esperado '$executablePath'."
}

$data = Ensure-VisualDevData -DataRoot $devDataRoot -RepositoryRoot $repoRoot
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
$productVersion = if ([string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) {
    "desconocida"
}
else {
    $versionInfo.ProductVersion
}
$buildTime = (Get-Item -LiteralPath $executablePath).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = Resolve-FullPath $executablePath
$startInfo.WorkingDirectory = Resolve-FullPath $outputDirectory
$startInfo.UseShellExecute = $false
$startEnvironment = $startInfo.EnvironmentVariables
$startEnvironment["ARTE_DEV_VISUAL"] = "1"
$startEnvironment["ARTE_DEV_DATA_ROOT"] = Resolve-FullPath $devDataRoot

foreach ($name in @(
    "ARTE_UPDATE_REDIRECTED",
    "ARTE_UPDATE_VERIFY",
    "ARTE_STABLE_LAUNCHED",
    "ARTE_LAUNCHER_RECOVERY_ATTEMPT",
    "ARTE_FULLSCREEN_SMOKE",
    "ARTE_POS_BARCODE_SMOKE",
    "ARTE_OPERATIONAL_RECOVERY_SMOKE",
    "ARTE_BACKUP_LAYOUT_SMOKE",
    "ARTE_BACKUP_RESTORE_SMOKE",
    "ARTE_PROTECTED_RESTORE_SMOKE",
    "ARTE_ACCOUNT_PAYMENT_LAYOUT_SMOKE",
    "ARTE_PRODUCT_LAYOUT_SMOKE",
    "ARTE_PERIPHERAL_DOCUMENT_SMOKE",
    "ARTE_BARCODE_HARDENING_SMOKE",
    "ARTE_H2_DISASTER_RECOVERY_SMOKE"
)) {
    [void]$startEnvironment.Remove($name)
}

$process = [System.Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
    throw "No se pudo iniciar el ejecutable DEV recién compilado."
}

Start-Sleep -Milliseconds 400
if ($process.HasExited) {
    throw "El ejecutable DEV terminó inmediatamente con código $($process.ExitCode)."
}

Write-Host ""
Write-Host "Ruta exacta EXE:       $($startInfo.FileName)" -ForegroundColor Green
Write-Host "ProductVersion/build:  $productVersion / $Configuration ($buildTime)"
Write-Host "Datos DEV aislados:    $(Resolve-FullPath $devDataRoot)"
Write-Host "Base usada:            $($data.Database)"
Write-Host "PID DEV:               $($process.Id)"
Write-Host "No se usó redirect ni launcher estable." -ForegroundColor Green
Write-Host "VISUAL DEV — current working tree" -ForegroundColor Cyan
