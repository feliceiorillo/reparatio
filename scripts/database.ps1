param([ValidateSet('Update','Script','Test','AddMigration')][string]$Action = 'Test', [string]$Name)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver = 'false'
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.local\dotnet' }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $projectRoot '.local\nuget' }
if (-not $env:NUGET_HTTP_CACHE_PATH) { $env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.local\nuget-http' }
if (-not $env:NUGET_PLUGINS_CACHE_PATH) { $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $projectRoot '.local\nuget-plugins' }
Push-Location $projectRoot
try {
    dotnet restore Reparatio.slnx --disable-parallel -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
    dotnet build Reparatio.slnx --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    switch ($Action) {
        'Update' { dotnet ef database update --project src/Reparatio.Repairs.Infrastructure --no-build }
        'Script' { dotnet ef migrations script --idempotent --project src/Reparatio.Repairs.Infrastructure --no-build --output scripts/repairs-schema.sql }
        'AddMigration' {
            if ([string]::IsNullOrWhiteSpace($Name)) { throw 'Migration name is required.' }
            dotnet ef migrations add $Name --project src/Reparatio.Repairs.Infrastructure --no-build
        }
        'Test' { dotnet test Reparatio.slnx --no-build --no-restore -m:1 --blame-hang-timeout 45s }
    }
    if ($LASTEXITCODE -ne 0) { throw "$Action failed." }
} finally { Pop-Location }
