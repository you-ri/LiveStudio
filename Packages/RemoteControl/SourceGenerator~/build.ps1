# Copyright (c) You-Ri, 2026
# Build Lilium.RemoteControl.SourceGenerator and stage the DLL into the
# package's Plugins/ folder. Run this after editing the generator source,
# then commit both the source change and the updated DLL.

$ErrorActionPreference = "Stop"

$src = Join-Path $PSScriptRoot "Lilium.RemoteControl.SourceGenerator"
$out = Join-Path $src "bin/Release/netstandard2.0/Lilium.RemoteControl.SourceGenerator.dll"
$dst = Join-Path $PSScriptRoot "../Plugins/Lilium.RemoteControl.SourceGenerator.dll"

Write-Host "[build-generator] Building generator..."
Push-Location $src
try {
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed (exit code $LASTEXITCODE)"
    }
} finally {
    Pop-Location
}

if (-not (Test-Path $out)) {
    throw "Build output not found: $out"
}

$dstDir = Split-Path $dst -Parent
if (-not (Test-Path $dstDir)) {
    New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
}

Copy-Item $out $dst -Force
Write-Host "[build-generator] DLL copied to $dst"
Write-Host "[build-generator] Commit the updated DLL to git."
