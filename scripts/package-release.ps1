param(
	[string]$Configuration = "Release",
	[string]$Framework = "net10.0",
	[string]$Runtime = "win-x64",
	[bool]$SelfContained = $true,
	[string]$OutputRoot = ".\artifacts\NdjsonErrorCollector"
)

$ErrorActionPreference = "Stop"

$projectPath = ".\NdjsonErrorCollector\NdjsonErrorCollector.csproj"
$publishPath = [System.IO.Path]::GetFullPath($OutputRoot)
$programsPath = Join-Path $publishPath "programs"
$dataPath = Join-Path $publishPath "data"

if (Test-Path $publishPath)
{
	Remove-Item $publishPath -Recurse -Force
}

Write-Host "Publishing NdjsonErrorCollector to $publishPath"
dotnet publish $projectPath -c $Configuration -f $Framework -r $Runtime --self-contained $SelfContained -o $publishPath

New-Item -ItemType Directory -Force -Path $programsPath, $dataPath | Out-Null

Get-ChildItem $publishPath | Where-Object { $_.Name -notin @("programs", "data", "configuration") } | Move-Item -Destination $programsPath

Write-Host "Release package created: $publishPath"
Write-Host "Run from: $(Join-Path $programsPath 'NdjsonErrorCollector.exe')"
