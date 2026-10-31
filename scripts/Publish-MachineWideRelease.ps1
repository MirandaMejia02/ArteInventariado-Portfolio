[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.16.2",
    [string]$MinimumCompatibleVersion = "1.14.2",
    [switch]$ReuseExistingUpdatePackage
)

$ErrorActionPreference = "Stop"

$Version = $Version.Trim()
$MinimumCompatibleVersion = $MinimumCompatibleVersion.Trim()
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version debe tener formato semver simple, por ejemplo 1.16.2 o 1.16.2-rc1."
}
if ($MinimumCompatibleVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "MinimumCompatibleVersion debe tener formato semver simple."
}
if ($Runtime -ne "win-x64") {
    throw "El Setup machine-wide de este bundle solo admite win-x64."
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
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$payloadName = "ArteInventariado-$Version-$Runtime"
$payloadDirectory = Join-Path $artifactsRoot $payloadName
$updatePackagePath = Join-Path $artifactsRoot "$payloadName.zip"
$bundleArchivePath = Join-Path $artifactsRoot "ArteInventariado-$Version-PC1-reset-bundle.zip"
$publishScript = Join-Path $PSScriptRoot "Publish-V1.ps1"
$setupProject = Join-Path $repoRoot "ArteInventariado.Setup\ArteInventariado.Setup.csproj"
$stageRoot = Join-Path $artifactsRoot ".machine-wide-release-stage-$([Guid]::NewGuid().ToString('N'))"

$payloadDirectoryExists = Test-Path -LiteralPath $payloadDirectory
$updatePackageExists = Test-Path -LiteralPath $updatePackagePath
if ($payloadDirectoryExists -ne $updatePackageExists) {
    throw "Existe solo una parte del paquete update ($payloadDirectory o $updatePackagePath); inspección manual requerida."
}
if ($payloadDirectoryExists -and -not $ReuseExistingUpdatePackage) {
    throw "El paquete update ya existe. Use -ReuseExistingUpdatePackage para verificarlo y reutilizarlo, o elija otra versión."
}
if (Test-Path -LiteralPath $bundleArchivePath) {
    throw "Ya existe '$bundleArchivePath'. No se sobrescribió ningún artefacto."
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stageRoot | Out-Null

try {
    if (-not $ReuseExistingUpdatePackage) {
        & $publishScript `
            -Configuration $Configuration `
            -Runtime $Runtime `
            -Version $Version `
            -PackageName $payloadName
        if ($LASTEXITCODE -ne 0) {
            throw "Publish-V1.ps1 terminó con código $LASTEXITCODE."
        }
    } else {
        $versionFile = Get-Content -LiteralPath (Join-Path $payloadDirectory "VERSION.txt") -Raw
        if ($versionFile -notmatch [regex]::Escape("Version: $Version") -or
            $versionFile -notmatch [regex]::Escape("ProductVersion: $Version")) {
            throw "El paquete existente no declara la versión esperada '$Version'."
        }
        foreach ($executable in @(
            "ArteInventariado.WinForms.exe",
            "ArteInventariado.Updater.exe",
            "ArteInventariado.Launcher.exe",
            "ArteInventariado.StationHelper.exe",
            "ArteInventariado.BackupRunner.exe",
            "ArteInventariado.CloudBackupRunner.exe"
        )) {
            $path = Join-Path $payloadDirectory $executable
            if (-not (Test-Path -LiteralPath $path) -or
                [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion -ne $Version) {
                throw "El paquete existente no tiene el ejecutable '$executable' con ProductVersion '$Version'."
            }
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($updatePackagePath)
        try {
            if (-not ($archive.Entries | Where-Object { $_.FullName -ceq "ArteInventariado.WinForms.exe" })) {
                throw "El ZIP existente no contiene ArteInventariado.WinForms.exe en su raíz."
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    if (-not (Test-Path -LiteralPath $updatePackagePath)) {
        throw "No se generó el paquete del actualizador esperado: $updatePackagePath"
    }

    $setupPublish = Join-Path $stageRoot "setup-publish"
    dotnet publish $setupProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=false `
        @versionProperties `
        -o $setupPublish
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish de Setup terminó con código $LASTEXITCODE."
    }

    $setupExecutable = Join-Path $setupPublish "ArteInventariado.Setup.exe"
    if (-not (Test-Path -LiteralPath $setupExecutable)) {
        throw "No se generó ArteInventariado.Setup.exe."
    }
    $setupVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($setupExecutable).ProductVersion
    if ($setupVersion -ne $Version) {
        throw "Setup declara ProductVersion '$setupVersion'; se esperaba '$Version'."
    }

    $bundleRoot = Join-Path $stageRoot "bundle"
    $setupRoot = Join-Path $bundleRoot "setup"
    $updateRoot = Join-Path $bundleRoot "update"
    New-Item -ItemType Directory -Path $setupRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $updateRoot -Force | Out-Null

    Get-ChildItem -LiteralPath $setupPublish -Force |
        Where-Object { $_.Name -ne "ArteInventariado.Setup.exe" } |
        Copy-Item -Destination $setupRoot -Recurse
    $bundledSetupExecutable = Join-Path $setupRoot "setup.exe"
    Copy-Item -LiteralPath $setupExecutable -Destination $bundledSetupExecutable

    $updateHash = (Get-FileHash -LiteralPath $updatePackagePath -Algorithm SHA256).Hash
    $updateSize = (Get-Item -LiteralPath $updatePackagePath).Length
    $manifestName = "$payloadName.manifest.json"
    $manifestPath = Join-Path $updateRoot $manifestName
    $manifest = [ordered]@{
        version = $Version
        minimumCompatibleVersion = $MinimumCompatibleVersion
        maximumCompatibleVersion = $null
        packagePath = "$payloadName.zip"
        deploymentTarget = "MachineWide"
        sha256 = $updateHash
        sizeBytes = $updateSize
        releaseNotes = "ArteInventariado $Version. Incluye el paquete de actualización y Setup para renovar únicamente la DB después de actualizar. La certificación operativa debe completarse por separado."
        publishedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Copy-Item -LiteralPath $updatePackagePath -Destination (Join-Path $updateRoot "$payloadName.zip")

    $instructions = @"
ArteInventariado $Version - bundle PC1

Contenido:
- update\$payloadName.zip y ${manifestName}: paquete para Update Center.
- setup\setup.exe y sus dependencias: utilidad para reinicio protegido de DB $Version.

Ruta prevista para conservar la instalación y los servicios protegidos:
1. En PC1, usar Update Center para aplicar el ZIP junto con su manifest. No usar
   setup.exe --clean-install ni desinstalar la aplicación.
2. Confirmar que la aplicación quedó en $Version y que inicia normalmente.
3. Cerrar ArteInventariado y sus ventanas auxiliares.
4. Ejecutar setup\setup.exe --reset-database-only con consentimiento UAC.
   Leer el aviso y escribir exactamente REINICIAR DB cuando Setup lo solicite.
5. Completar en la interfaz oficial la creación del nuevo Administrador Principal.
   No escribir contraseñas en comandos, logs ni mensajes.
6. Verificar InstallationId y modo PRINCIPAL, inicio de sesión y las tareas
   protegidas BackupProtegido y CloudBackupProtegido bajo SYSTEM.

El reinicio protegido crea y valida un respaldo, conserva la identidad/configuración
de estación, assets, archivos de instalación y tareas programadas; reemplaza solo
la DB del negocio. Si el proceso se interrumpe, detenerse y usar el procedimiento
de recuperación protegido --recover-database-reset; no borrar archivos manualmente.

Este bundle no autoriza una instalación limpia ni certifica por sí solo el
funcionamiento en PC1. Si Update Center, UAC, provisión del administrador o
verificación de tareas falla, detenerse y conservar los diagnósticos sin repetir
el reinicio a ciegas.
"@
    Set-Content -LiteralPath (Join-Path $bundleRoot "INSTRUCCIONES-PC1.txt") -Value $instructions -Encoding UTF8

    $forbiddenFiles = Get-ChildItem -LiteralPath $bundleRoot -Recurse -File |
        Where-Object {
            $_.Extension.ToLowerInvariant() -in @(".db", ".sqlite", ".sqlite3", ".aibak", ".wal", ".shm", ".journal") -or
            $_.Name -match "(?i)(backup-status|station-settings|current\.json|application-errors|station-audit|\.db-wal|\.db-shm|\.db-journal)"
        }
    if ($forbiddenFiles) {
        throw "El bundle contiene datos locales prohibidos: $($forbiddenFiles.FullName -join ', ')"
    }

    Compress-Archive -Path (Join-Path $bundleRoot "*") -DestinationPath $bundleArchivePath -CompressionLevel Optimal
    $bundleHash = (Get-FileHash -LiteralPath $bundleArchivePath -Algorithm SHA256).Hash
    $setupHash = (Get-FileHash -LiteralPath (Join-Path $setupRoot "setup.exe") -Algorithm SHA256).Hash

    [pscustomobject]@{
        Status = "PACKAGE_BUILT - OPERATIONAL_CERTIFICATION_PENDING"
        Version = $Version
        BundleArchivePath = $bundleArchivePath
        BundleSizeBytes = (Get-Item -LiteralPath $bundleArchivePath).Length
        BundleSha256 = $bundleHash
        UpdatePackageInsideBundle = "update\$payloadName.zip"
        UpdatePackageSha256 = $updateHash
        UpdateManifestInsideBundle = "update\$manifestName"
        SetupPathInsideBundle = "setup\setup.exe"
        SetupSha256 = $setupHash
    }
}
finally {
    $resolvedArtifactsRoot = [System.IO.Path]::GetFullPath($artifactsRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedStageRoot = [System.IO.Path]::GetFullPath($stageRoot)
    if (-not $resolvedStageRoot.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "La carpeta temporal calculada salió de artifacts; no se limpió: $resolvedStageRoot"
    }
    if (Test-Path -LiteralPath $resolvedStageRoot) {
        Remove-Item -LiteralPath $resolvedStageRoot -Recurse -Force
    }
}
