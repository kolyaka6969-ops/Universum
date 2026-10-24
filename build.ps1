# Собирает Universum в один exe (не требует установленного .NET): publish\Universum.exe
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Test-Path Assets\universum.ico)) { & .\make-icon.ps1 }
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
Write-Host "Готово: $PSScriptRoot\publish\Universum.exe"
