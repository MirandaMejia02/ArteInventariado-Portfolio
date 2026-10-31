[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.16.3",
    [string]$PackagePath = "..\artifacts\ArteInventariado-1.16.3-PC1-20261011-recoveryfix-win-x64.zip",
    [string]$ManifestPath = "..\artifacts\ArteInventariado-1.16.3-PC1-20261011-recoveryfix-win-x64.manifest.json",
    [string]$OutputName = "ArteInventariado-1.16.3-PC1-SetupRecuperacion-20261011-recoveryfix-win-x64",
    [string]$InstructionsPath = "..\docs\ARTEINVENTARIADO_SETUP_RECUPERACION_PC1_1.16.3.md",
    [string]$ArtifactsRoot
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version debe ser estable y tener formato X.Y.Z."
}
if ($Runtime -ne "win-x64") {
    throw "El Setup de recuperación solo se publica como win-x64."
}
if ([System.IO.Path]::GetFileName($OutputName) -ne $OutputName -or
    $OutputName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw "OutputName debe ser un nombre simple de archivo/carpeta."
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
$packagePath = if ([System.IO.Path]::IsPathRooted($PackagePath)) {
    [System.IO.Path]::GetFullPath($PackagePath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PackagePath))
}
$manifestPath = if ([System.IO.Path]::IsPathRooted($ManifestPath)) {
    [System.IO.Path]::GetFullPath($ManifestPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $ManifestPath))
}
$setupProject = Join-Path $repoRoot "ArteInventariado.Setup\ArteInventariado.Setup.csproj"
$instructionsPath = if ([System.IO.Path]::IsPathRooted($InstructionsPath)) {
    [System.IO.Path]::GetFullPath($InstructionsPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $InstructionsPath))
}
$outputZip = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "$OutputName.zip"))
$verificationPath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "$OutputName.verification.txt"))

foreach ($path in @($packagePath, $manifestPath, $setupProject, $instructionsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "No existe el archivo requerido: $path"
    }
}
foreach ($path in @($outputZip, $verificationPath)) {
    if (Test-Path -LiteralPath $path) {
        throw "La salida ya existe y no se sobrescribirá: $path"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version) {
    throw "El manifest declara $($manifest.version), pero Setup es $Version."
}
if ($manifest.deploymentTarget -ne "MachineWide") {
    throw "El manifest no declara DeploymentTarget=MachineWide."
}
if ($manifest.packagePath -ne [System.IO.Path]::GetFileName($packagePath)) {
    throw "PackagePath del manifest no coincide con el ZIP seleccionado."
}
if ((Get-Item -LiteralPath $packagePath).Length -ne [long]$manifest.sizeBytes) {
    throw "El tamaño del ZIP no coincide con el manifest."
}
$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
if (-not [string]::Equals($packageHash, [string]$manifest.sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "El SHA-256 del ZIP no coincide con el manifest."
}

$probeRoot = Join-Path $artifactsRoot ".recovery-setup-probe-$([Guid]::NewGuid().ToString('N'))"
$setupPublishRoot = Join-Path $artifactsRoot ".recovery-setup-publish-$([Guid]::NewGuid().ToString('N'))"
$bundleStage = Join-Path $artifactsRoot ".recovery-setup-stage-$([Guid]::NewGuid().ToString('N'))"
$completed = $false

try {
    New-Item -ItemType Directory -Path $probeRoot,$setupPublishRoot,$bundleStage -Force | Out-Null

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entry = $archive.GetEntry("ArteInventariado.WinForms.exe")
        if ($null -eq $entry) {
            throw "El ZIP no contiene ArteInventariado.WinForms.exe en su raíz."
        }
        $probeExecutable = Join-Path $probeRoot "ArteInventariado.WinForms.exe"
        $inputStream = $entry.Open()
        try {
            $outputStream = [System.IO.File]::Open($probeExecutable, [System.IO.FileMode]::CreateNew)
            try { $inputStream.CopyTo($outputStream) }
            finally { $outputStream.Dispose() }
        }
        finally { $inputStream.Dispose() }
    }
    finally { $archive.Dispose() }

    $payloadVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($probeExecutable).ProductVersion
    if ($payloadVersion -ne $Version) {
        throw "ProductVersion del ejecutable dentro del ZIP es '$payloadVersion', no '$Version'."
    }

    $versionProperties = @(
        "-p:Version=$Version",
        "-p:VersionPrefix=$Version",
        "-p:InformationalVersion=$Version",
        "-p:AssemblyVersion=${Version}.0",
        "-p:FileVersion=${Version}.0"
    )
    $publishArguments = @(
        "publish", $setupProject,
        "-c", $Configuration,
        "-r", $Runtime,
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-o", $setupPublishRoot
    ) + $versionProperties
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish de Setup terminó con código $LASTEXITCODE."
    }

    $publishedSetup = Join-Path $setupPublishRoot "ArteInventariado.Setup.exe"
    if (-not (Test-Path -LiteralPath $publishedSetup -PathType Leaf)) {
        throw "No se generó ArteInventariado.Setup.exe."
    }
    $setupProductVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($publishedSetup).ProductVersion
    if ($setupProductVersion -ne $Version) {
        throw "ProductVersion del Setup publicado es '$setupProductVersion', no '$Version'."
    }

    $updateDirectory = Join-Path $bundleStage "update"
    New-Item -ItemType Directory -Path $updateDirectory | Out-Null
    Get-ChildItem -LiteralPath $setupPublishRoot -File |
        Where-Object { $_.Extension -ne ".pdb" } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $bundleStage }
    Rename-Item -LiteralPath (Join-Path $bundleStage "ArteInventariado.Setup.exe") -NewName "setup.exe"
    Copy-Item -LiteralPath $packagePath -Destination (Join-Path $updateDirectory ([System.IO.Path]::GetFileName($packagePath)))
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $updateDirectory "manifest.json")
    Copy-Item -LiteralPath $instructionsPath -Destination (Join-Path $bundleStage "INSTRUCCIONES.md")

    Compress-Archive -Path (Join-Path $bundleStage "*") -DestinationPath $outputZip -CompressionLevel Optimal

    $bundleArchive = [System.IO.Compression.ZipFile]::OpenRead($outputZip)
    try {
        foreach ($requiredEntry in @(
            "setup.exe",
            "update/manifest.json",
            "update/$([System.IO.Path]::GetFileName($packagePath))",
            "INSTRUCCIONES.md"
        )) {
            if ($null -eq $bundleArchive.GetEntry($requiredEntry)) {
                throw "El bundle publicado no contiene la ruta requerida '$requiredEntry'."
            }
        }
        if ($null -ne $bundleArchive.GetEntry("setup/setup.exe")) {
            throw "El bundle publicó setup.exe en una subcarpeta; update/ debe quedar junto al ejecutable."
        }
    }
    finally { $bundleArchive.Dispose() }

    $bundleHash = (Get-FileHash -LiteralPath $outputZip -Algorithm SHA256).Hash
    $summary = @(
        "Setup de recuperación ArteInventariado $Version",
        "Destino: MachineWide / win-x64",
        "Actualización incluida: $([System.IO.Path]::GetFileName($packagePath))",
        "SHA-256 payload: $packageHash",
        "SHA-256 bundle: $bundleHash",
        "El bundle conserva la DB y la identidad local. No usar instalación limpia ni reinicio de DB."
    )
    Set-Content -LiteralPath $verificationPath -Value $summary -Encoding utf8
    $completed = $true

    [pscustomobject]@{
        BundleZip = $outputZip
        Verification = $verificationPath
        BundleSha256 = $bundleHash
        PayloadZip = $packagePath
        PayloadSha256 = $packageHash
        Version = $Version
    }
}
finally {
    foreach ($temporaryPath in @($probeRoot, $setupPublishRoot, $bundleStage)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            $resolved = [System.IO.Path]::GetFullPath($temporaryPath)
            if (-not $resolved.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Se rechazó limpiar una ruta temporal fuera de artifacts: $resolved"
            }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }

    if (-not $completed -and (Test-Path -LiteralPath $outputZip)) {
        throw "La publicación quedó incompleta. Se conserva el ZIP parcial para auditoría: $outputZip"
    }
}
