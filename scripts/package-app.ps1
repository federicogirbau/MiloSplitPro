param (
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$PublishDir = Join-Path $RootDir "build\publish"
$DotNetExe = "$env:LOCALAPPDATA\dotnet\dotnet.exe"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Milo Split Pro - Packaging Pipeline" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Clean output directories
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

# 2. Publish WPF App in Release
Write-Host "`n[1/4] Publicando aplicación WPF en $Configuration..." -ForegroundColor Yellow
& $DotNetExe publish "$RootDir\src\MiloSplitPro.App\MiloSplitPro.App.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $PublishDir

# 3. Copy Engine scripts
Write-Host "`n[2/4] Empaquetando motor Python y componentes de IA..." -ForegroundColor Yellow
$EngineTarget = Join-Path $PublishDir "engine"
New-Item -ItemType Directory -Path $EngineTarget -Force | Out-Null
Copy-Item "$RootDir\src\MiloSplitPro.Engine\*" -Destination $EngineTarget -Recurse -Force

# 4. Copy Manifests and Legal Notices
Write-Host "`n[3/4] Incorporando manifiestos, licencias y SBOM..." -ForegroundColor Yellow
Copy-Item "$RootDir\src\MiloSplitPro.Core\Manifests\models.manifest.json" -Destination $PublishDir -Force
Copy-Item "$RootDir\src\MiloSplitPro.Core\Manifests\PROVENANCE.json" -Destination $PublishDir -Force
Copy-Item "$RootDir\THIRD_PARTY_NOTICES.txt" -Destination $PublishDir -Force
Copy-Item "$RootDir\bom.cyclonedx.json" -Destination $PublishDir -Force

# 5. Compute SHA-256 hashes of published binaries
Write-Host "`n[4/4] Verificando integridad de binarios publicados..." -ForegroundColor Yellow
$ExePath = Join-Path $PublishDir "MiloSplitPro.App.exe"
if (Test-Path $ExePath) {
    $hash = Get-FileHash -Path $ExePath -Algorithm SHA256
    Write-Host "Ejecutable principal generado con éxito:" -ForegroundColor Green
    Write-Host "  Ruta: $ExePath"
    Write-Host "  SHA-256: $($hash.Hash)"
}

Write-Host "`n¡Empaquetado completado con éxito en: $PublishDir!" -ForegroundColor Green
