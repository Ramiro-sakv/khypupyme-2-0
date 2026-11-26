$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Postgres.Local.ps1')
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'server/KhypuPyme.Api'
$api = Join-Path $project 'bin/Release/net10.0/KhypuPyme.Api.dll'
$setup = 'http://127.0.0.1:5653'
$lan = 'https://127.0.0.1:5654'
if (Get-NetTCPConnection -State Listen -LocalPort 5653,5654 -ErrorAction SilentlyContinue) {
    throw 'Los puertos 5653 y 5654 deben estar libres para las pruebas.'
}
$directory = Join-Path $env:TEMP ("khypupyme-lan-test-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$testEnvironment = @{
    ConnectionStrings__KhypuPyme = Get-LocalConnectionString 'khypupyme2_test' 'app'
    ASPNETCORE_ENVIRONMENT = 'Development'
    DOTNET_ENVIRONMENT = 'Development'
    Email__MailboxPath = $directory
    Google__ClientId = ''
    Google__ClientSecret = ''
    Pilot__Enabled = 'false'
    KHYPU_TEST_PSQL = Get-PostgresTool 'psql'
}
$previous = @{}
$processes = @()
try {
    foreach ($name in $testEnvironment.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $testEnvironment[$name])
    }
    $processes += Start-Process dotnet.exe -ArgumentList @(('"' + $api + '"'), '--urls', $setup) `
        -WorkingDirectory $project -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $directory 'setup.log') -RedirectStandardError (Join-Path $directory 'setup-errors.log')
    $launcher = Join-Path $PSScriptRoot 'Start-Lan.ps1'
    $processes += Start-Process (Get-Process -Id $PID).Path -ArgumentList @(
        '-NoProfile', '-File', ('"' + $launcher + '"'), '-Database', 'Test', '-Port', '5654'
    ) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $directory 'lan.log') -RedirectStandardError (Join-Path $directory 'lan-errors.log')
    $credentials = @{
        owner = Get-DatabasePassword (Read-DatabaseCredentials) 'khypupyme2_test_owner'
        setup = $setup
        lan = $lan
        certificate = Join-Path $env:LOCALAPPDATA 'KhypuPyme2.0/lan/Test/KhypuPyme-Red-Local.cer'
    }
    $credentials | ConvertTo-Json -Compress | node (Join-Path $PSScriptRoot 'test-lan.mjs')
    if ($LASTEXITCODE -ne 0) { throw "Fallo del recorrido. Consulta $directory." }
}
finally {
    foreach ($process in $processes) {
        if ($process -and -not $process.HasExited) { taskkill.exe /PID $process.Id /T /F | Out-Null }
    }
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
