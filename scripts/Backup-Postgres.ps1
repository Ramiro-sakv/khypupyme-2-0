#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^age1[0-9a-z]+$')]
    [string]$Recipient,

    [ValidateSet('Development', 'Test')]
    [string]$Database = 'Development',

    [string]$Directory = (Join-Path $env:LOCALAPPDATA 'KhypuPyme2.0\backups')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Postgres.Local.ps1')

$age = Get-Command age.exe -CommandType Application -ErrorAction SilentlyContinue
if (-not $age) { throw 'Instala age y agrega age.exe al listado de programas disponibles.' }
$target = if ($Database -eq 'Test') { 'khypupyme2_test' } else { 'khypupyme2_dev' }
$role = $target + '_owner'
$folder = [IO.Directory]::CreateDirectory($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Directory))
$name = $target + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
$backup = Join-Path $folder.FullName ($name + '.pgdump.age')
$staging = $backup + '.partial'

function New-BackupProcess([string]$File, [string[]]$Arguments) {
    $process = [Diagnostics.Process]::new()
    $process.StartInfo.FileName = $File
    $process.StartInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) { $process.StartInfo.ArgumentList.Add($argument) }
    return $process
}

$dump = New-BackupProcess (Get-PostgresTool 'pg_dump') @(
    '-w', '-h', $script:PostgresHost, '-p', [string]$script:PostgresPort, '-U', $role,
    '-d', $target, '--format=custom', '--schema=khypupyme'
)
$encrypt = New-BackupProcess $age.Source @('--encrypt', '--recipient', $Recipient, '--output', $staging)
$dump.StartInfo.RedirectStandardOutput = $true
$encrypt.StartInfo.RedirectStandardInput = $true
$encrypt.StartInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
# La contraseña se entrega solo a pg_dump y no aparece como argumento ni se copia a age.
$dump.StartInfo.Environment['PGPASSWORD'] = Get-DatabasePassword (Read-DatabaseCredentials) $role
$null = $encrypt.StartInfo.Environment.Remove('PGPASSWORD')

$dumpStarted = $false
$encryptStarted = $false
try {
    $encryptStarted = $encrypt.Start()
    $dumpStarted = $dump.Start()
    # Copia bytes entre procesos. Solo el archivo cifrado toca el disco.
    $null = $dump.StandardOutput.BaseStream.CopyToAsync($encrypt.StandardInput.BaseStream).GetAwaiter().GetResult()
    $encrypt.StandardInput.Close()
    $dump.WaitForExit()
    $encrypt.WaitForExit()
    if ($dump.ExitCode -ne 0 -or $encrypt.ExitCode -ne 0) {
        throw "No se generó el respaldo completo (pg_dump $($dump.ExitCode), age $($encrypt.ExitCode))."
    }

    # Publica el archivo definitivo solo cuando ambas operaciones terminan correctamente.
    [IO.File]::Move($staging, $backup)
    Write-Output $backup
} finally {
    try {
        if ($dumpStarted -and -not $dump.HasExited) { $dump.Kill(); $dump.WaitForExit() }
        if ($encryptStarted -and -not $encrypt.HasExited) { $encrypt.Kill(); $encrypt.WaitForExit() }
    } finally {
        $dump.Dispose()
        $encrypt.Dispose()
        [IO.File]::Delete($staging)
    }
}
