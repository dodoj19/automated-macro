# Gradi Automated Macro i sprema AutomatedMacro.exe u ovu mapu.
#
#   powershell -ExecutionPolicy Bypass -File .\build.ps1          prijenosni exe (radi na svakom Windows 10/11 x64, bez instalacije)
#   powershell -ExecutionPolicy Bypass -File .\build.ps1 -Small   mali exe (trazi instaliran .NET 10 Desktop Runtime)
param([switch]$Small)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\AutomatedMacro\AutomatedMacro.csproj'
# WPF build ne voli izlaz u roditeljsku mapu projekta, pa objavljujemo u bin\publish i kopiramo exe.
$staging = Join-Path $root 'src\AutomatedMacro\bin\publish'
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }

$publishArgs = @(
    'publish', $project, '-c', 'Release', '-r', 'win-x64', '-o', $staging,
    '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false'
)
if ($Small) {
    $publishArgs += '--self-contained', 'false'
} else {
    # .NET i WPF-ove nativne biblioteke idu unutar exe datoteke (komprimirano).
    $publishArgs += '--self-contained', 'true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true'
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Build nije uspio (exit $LASTEXITCODE)." }

$exe = Join-Path $root 'AutomatedMacro.exe'
Copy-Item (Join-Path $staging 'AutomatedMacro.exe') $exe -Force
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Gotovo: $exe ($sizeMb MB, $(if ($Small) { 'treba .NET 10' } else { 'prijenosni' }))" -ForegroundColor Green
