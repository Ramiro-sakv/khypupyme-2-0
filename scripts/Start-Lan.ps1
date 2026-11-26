param(
    [string]$InterfaceAlias,
    [ValidateRange(1024, 65535)][int]$Port = 5643,
    [ValidateSet('Development', 'Test')][string]$Database = 'Development',
    [switch]$ConfigurePrivateFirewall
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Postgres.Local.ps1')

if ($Database -eq 'Test') {
    if ($InterfaceAlias -or $ConfigurePrivateFirewall) { throw 'Las pruebas de red quedan en la PC.' }
    $address = '127.0.0.1'
    $subnet = '127.0.0.1/32'
    $target = 'khypupyme2_test'
} else {
    $profiles = @(Get-NetConnectionProfile | Where-Object { -not $InterfaceAlias -or $_.InterfaceAlias -eq $InterfaceAlias })
    if ($profiles.Count -ne 1) { throw 'Indica con -InterfaceAlias el adaptador conectado a la red del negocio.' }
    $profile = $profiles[0]
    $addresses = @(Get-NetIPAddress -InterfaceIndex $profile.InterfaceIndex -AddressFamily IPv4 |
        Where-Object { $_.AddressState -eq 'Preferred' -and -not $_.SkipAsSource -and
            $_.IPAddress -match '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.)' })
    if ($addresses.Count -ne 1) { throw 'El adaptador debe tener una sola dirección IPv4 privada activa.' }
    $address = $addresses[0].IPAddress
    $prefix = $addresses[0].PrefixLength
    $bytes = [Net.IPAddress]::Parse($address).GetAddressBytes()
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $networkBits = [Math]::Min(8, [Math]::Max(0, $prefix - 8 * $index))
        $bytes[$index] = $bytes[$index] -band (256 - (1 -shl (8 - $networkBits)))
    }
    $subnet = "$([Net.IPAddress]::new($bytes))/$prefix"
    $target = 'khypupyme2_dev'
}

$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
if ($ConfigurePrivateFirewall) {
    if ($profile.NetworkCategory -ne 'Private') { throw 'Windows debe identificar la red del negocio como Privada.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Abre PowerShell como administrador para configurar la regla privada.'
    }
    $rule = @{
        Name = "KhypuPyme2-Lan-$Port"
        Direction = 'Inbound'
        Action = 'Allow'
        Enabled = 'True'
        Profile = 'Private'
        Protocol = 'TCP'
        LocalPort = $Port
        LocalAddress = $address
        RemoteAddress = $subnet
        Program = $dotnet
        InterfaceAlias = $profile.InterfaceAlias
        EdgeTraversalPolicy = 'Block'
    }
    if (Get-NetFirewallRule -Name $rule.Name -ErrorAction SilentlyContinue) {
        Set-NetFirewallRule @rule | Out-Null
    } else {
        New-NetFirewallRule @rule -DisplayName "KhypuPyme 2.0 · red privada ($Port)" | Out-Null
    }
    Write-Host "Conexiones autorizadas en $subnet hacia ${address}:$Port en redes privadas."
    return
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'server/KhypuPyme.Api'
$api = Join-Path $project 'bin/Release/net10.0/KhypuPyme.Api.dll'
$web = Join-Path $root 'web/dist'
if (-not (Test-Path -LiteralPath $api) -or -not (Test-Path -LiteralPath (Join-Path $web 'index.html'))) {
    throw 'Primero compila la web y .NET en Release.'
}
$connection = Get-LocalConnectionString $target 'app'
$lanFolder = Join-Path $env:LOCALAPPDATA "KhypuPyme2.0/lan/$Database"
[IO.Directory]::CreateDirectory($lanFolder) | Out-Null
$rootFile = Join-Path $lanFolder 'KhypuPyme-Red-Local.cer'
$now = Get-Date
$certificateOptions = @{
    Type = 'Custom'
    CertStoreLocation = 'Cert:\CurrentUser\My'
    KeyAlgorithm = 'RSA'
    KeyLength = 2048
    HashAlgorithm = 'SHA256'
    KeyExportPolicy = 'NonExportable'
    NotBefore = $now.AddHours(-1)
}
$ca = $null
if (Test-Path -LiteralPath $rootFile) {
    $public = [Security.Cryptography.X509Certificates.X509Certificate2]::new($rootFile)
    try { $ca = Get-Item -LiteralPath "Cert:\CurrentUser\My\$($public.Thumbprint)" -ErrorAction SilentlyContinue }
    finally { $public.Dispose() }
}
if (-not $ca -or -not $ca.HasPrivateKey -or $ca.NotAfter -lt $now.AddDays(30)) {
    $id = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $ca = New-SelfSignedCertificate @certificateOptions -Subject "CN=KhypuPyme Red $id" -NotAfter $now.AddYears(3) `
        -KeyUsage CertSign,CRLSign -TextExtension '2.5.29.19={critical}{text}ca=true&pathlength=0'
    Export-Certificate -Cert $ca -FilePath $rootFile -Force | Out-Null
}

$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=$address" -and
    $_.Issuer -eq $ca.Subject -and $_.HasPrivateKey -and $_.NotAfter -gt $now.AddDays(7) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $certificate) {
    $expires = @($now.AddDays(365), $ca.NotAfter.AddDays(-1)) | Sort-Object | Select-Object -First 1
    $certificate = New-SelfSignedCertificate @certificateOptions -Subject "CN=$address" -Signer $ca -NotAfter $expires `
        -KeyUsage DigitalSignature,KeyEncipherment -TextExtension @(
            "2.5.29.17={text}IPAddress=$address",
            '2.5.29.37={text}1.3.6.1.5.5.7.3.1'
        )
}

$configuration = @{
    ConnectionStrings__KhypuPyme = $connection
    ASPNETCORE_ENVIRONMENT = 'LocalNetwork'
    DOTNET_ENVIRONMENT = 'LocalNetwork'
    LocalNetwork__Address = $address
    LocalNetwork__Subnet = $subnet
    LocalNetwork__Port = "$Port"
    LocalNetwork__CertificateThumbprint = $certificate.Thumbprint
}
$previous = @{}
try {
    foreach ($name in $configuration.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $configuration[$name])
    }
    Write-Host "KhypuPyme compartido · https://${address}:$Port"
    Write-Host "Certificado público para los dispositivos de confianza · $rootFile"
    if ($Database -ne 'Test' -and $profile.NetworkCategory -ne 'Private') {
        Write-Warning 'Windows clasifica esta red como pública. El firewall puede impedir el acceso desde otros equipos.'
    }
    & $dotnet $api --contentRoot $project --webroot $web
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
