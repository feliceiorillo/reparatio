param(
    [string]$Server = 'tcp:127.0.0.1,62081',
    [string]$Database = 'Reparatio',
    [string]$UserName = 'reparatio',
    [Security.SecureString]$Password,
    [string]$Path
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Codex\.secrets\reparatio\sql.dpapi'
}
if ($null -eq $Password) { $Password = Read-Host 'SQL password' -AsSecureString }
$builder = New-Object System.Data.Common.DbConnectionStringBuilder
$builder['Server'] = $Server
$builder['Database'] = $Database
$builder['User ID'] = $UserName
$builder['Password'] = [Net.NetworkCredential]::new('', $Password).Password
$builder['Encrypt'] = 'True'
$builder['TrustServerCertificate'] = 'True'
$builder['Persist Security Info'] = 'False'
$builder['MultipleActiveResultSets'] = 'False'
$builder['Application Name'] = 'Reparatio'
$builder['Connect Timeout'] = '15'
$builder['Command Timeout'] = '30'
$bytes = [Text.Encoding]::UTF8.GetBytes($builder.ConnectionString)
try {
    $protected = [Security.Cryptography.ProtectedData]::Protect($bytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    New-Item -ItemType Directory -Force -Path (Split-Path $Path -Parent) | Out-Null
    [IO.File]::WriteAllBytes($Path, $protected)
    Write-Output 'Local encrypted SQL secret saved.'
} finally {
    [Array]::Clear($bytes, 0, $bytes.Length)
    $builder.Clear()
}
