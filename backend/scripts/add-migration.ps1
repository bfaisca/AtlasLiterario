# Usage: ./scripts/add-migration.ps1 -Name <MigrationName>
param([Parameter(Mandatory = $true)][string]$Name)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet tool restore
dotnet ef migrations add $Name `
  --project src/AtlasLiterario.Infrastructure `
  --startup-project src/AtlasLiterario.API `
  --output-dir Persistence/Migrations
