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
Write-Host "`n[4/5] Verificando integridad de binarios publicados..." -ForegroundColor Yellow
$ExePath = Join-Path $PublishDir "MiloSplitPro.App.exe"
if (Test-Path $ExePath) {
    $hash = Get-FileHash -Path $ExePath -Algorithm SHA256
    Write-Host "Ejecutable principal generado con éxito:" -ForegroundColor Green
    Write-Host "  Ruta: $ExePath"
    Write-Host "  SHA-256: $($hash.Hash)"
}

# 6. Build Inno Setup Single-File Installer
Write-Host "`n[5/5] Compilando instalador ejecutable único con Inno Setup..." -ForegroundColor Yellow
$IsccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$IsccExe = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($IsccExe) {
    $DistDir = Join-Path $RootDir "dist"
    if (!(Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir -Force | Out-Null }
    
    $IssScript = Join-Path $RootDir "installer\MiloSplitPro.iss"
    & $IsccExe $IssScript
    
    $InstallerExe = Join-Path $DistDir "MiloSplitPro_Setup_v1.0.0_x64.exe"
    if (Test-Path $InstallerExe) {
        $instHash = Get-FileHash -Path $InstallerExe -Algorithm SHA256
        Write-Host "`n=========================================" -ForegroundColor Green
        Write-Host "¡Instalador único generado con éxito!" -ForegroundColor Green
        Write-Host "  Archivo: $InstallerExe" -ForegroundColor Cyan
        Write-Host "  Tamaño: $([math]::Round((Get-Item $InstallerExe).Length / 1MB, 2)) MB" -ForegroundColor Cyan
        Write-Host "  SHA-256: $($instHash.Hash)" -ForegroundColor Cyan
        Write-Host "=========================================" -ForegroundColor Green
    }
} else {
    Write-Host "Advertencia: No se encontró el compilador de Inno Setup (ISCC.exe)." -ForegroundColor Yellow
}

Write-Host "`n¡Empaquetado completado con éxito!" -ForegroundColor Green

