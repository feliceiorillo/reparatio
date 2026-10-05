param([ValidateSet('Update','Script','Test','AddMigration')][string]$Action = 'Test', [string]$Name)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.local\dotnet'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.local\nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.local\nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $projectRoot '.local\nuget-plugins'
if (($Action -eq 'Update' -or $Action -eq 'Test') -and [string]::IsNullOrWhiteSpace($env:REPARATIO_SQL_CONNECTION)) {
    throw 'Set REPARATIO_SQL_CONNECTION in the current process. No credentials are saved by this script.'
}
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
