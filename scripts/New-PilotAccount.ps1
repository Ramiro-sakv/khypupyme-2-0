param([Parameter(Mandatory)][string]$Email)

$ErrorActionPreference = 'Stop'
if (-not $env:ConnectionStrings__KhypuPyme -or -not $env:Pilot__AllowedEmails) {
    throw 'Configura la conexión del propietario de Neon y Pilot__AllowedEmails en esta terminal.'
}
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'server/KhypuPyme.Api'
$api = Join-Path $project 'bin/Release/net10.0/KhypuPyme.Api.dll'
if (-not (Test-Path -LiteralPath $api)) { throw 'Primero compila .NET en Release.' }

$secret = Read-Host 'Contraseña nueva para la cuenta de prueba' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
$previousEnvironment = $env:DOTNET_ENVIRONMENT
$previousAspnet = $env:ASPNETCORE_ENVIRONMENT
$previousPilot = $env:Pilot__Enabled
$previousEncoding = $OutputEncoding
try {
    $env:DOTNET_ENVIRONMENT = 'Production'
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:Pilot__Enabled = 'false'
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
    @($Email, [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)) | dotnet $api --create-pilot-account
    if ($LASTEXITCODE -ne 0) { throw 'La cuenta del piloto no se creó.' }
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    $secret.Dispose()
    $env:DOTNET_ENVIRONMENT = $previousEnvironment
    $env:ASPNETCORE_ENVIRONMENT = $previousAspnet
    $env:Pilot__Enabled = $previousPilot
    $OutputEncoding = $previousEncoding
}
