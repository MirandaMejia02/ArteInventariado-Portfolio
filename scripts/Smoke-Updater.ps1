[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [string]$OutputPath,
    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
} else {
    [System.IO.Path]::GetFullPath($RepositoryRoot)
}
$publishRoot = [System.IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw "No existe el publish '$publishRoot'."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\smoke\v1.11.0-updater\updater-smoke.log"
}
$output = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null

$temporaryRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) "ArteInventariado-published-smoke-$([Guid]::NewGuid().ToString('N'))"))
$expectedPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $temporaryRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "La ruta temporal calculada no pertenece al directorio temporal del sistema."
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $publishRoot "*") -Destination $temporaryRoot -Recurse -Force
    $updater = Join-Path $temporaryRoot "ArteInventariado.Updater.exe"
    $application = Join-Path $temporaryRoot "ArteInventariado.WinForms.exe"
    if (-not (Test-Path -LiteralPath $updater) -or -not (Test-Path -LiteralPath $application)) {
        throw "El publish externo no contiene ambos ejecutables requeridos."
    }

    & $updater `
        --smoke `
        --publish-directory $temporaryRoot `
        --output $output `
        --repo-root $repoRoot
    if ($LASTEXITCODE -ne 0) {
        throw "El smoke del actualizador termino con codigo $LASTEXITCODE. Revise '$output'."
    }

    Get-Content -LiteralPath $output
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
