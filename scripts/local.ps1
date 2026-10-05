param(
    [ValidateSet('Inicializar', 'Verificar', 'Ejecutar', 'Detener')]
    [string]$Accion = 'Ejecutar'
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $raiz '.env'

if ($Accion -eq 'Detener') {
    & docker compose --project-directory $raiz down
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo detener Compose.' }
    exit 0
}

if (-not (Test-Path -LiteralPath $envFile)) {
    throw 'Copia .env.example a .env y define las credenciales locales antes de continuar.'
}

$valores = @{}
foreach ($linea in Get-Content -LiteralPath $envFile) {
    if ([string]::IsNullOrWhiteSpace($linea) -or $linea.TrimStart().StartsWith('#')) { continue }
    $partes = $linea -split '=', 2
    if ($partes.Length -eq 2) { $valores[$partes[0].Trim()] = $partes[1].Trim() }
}
if (-not $valores['SQLSERVER_PASSWORD'] -or -not $valores['SEED_PASSWORD']) {
    throw '.env debe definir SQLSERVER_PASSWORD y SEED_PASSWORD.'
}

& docker compose --project-directory $raiz up -d --wait --wait-timeout 240 sqlserver
if ($LASTEXITCODE -ne 0) { throw 'SQL Server no pudo iniciarse. Revisa Docker Desktop y docker compose logs.' }

$conexionAnterior = $env:ConnectionStrings__Refidomsa
$passwordAnterior = $env:DatabaseSeed__Password
$entornoAnterior = $env:ASPNETCORE_ENVIRONMENT
try {
    # El builder escapa contrasenas con caracteres especiales en la cadena de conexion.
    $conexion = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $conexion['Data Source'] = 'tcp:127.0.0.1,14333'
    $conexion['Initial Catalog'] = 'Refidomsa'
    $conexion['User ID'] = 'sa'
    $conexion['Password'] = $valores['SQLSERVER_PASSWORD']
    $conexion['Encrypt'] = $true
    $conexion['TrustServerCertificate'] = $true
    $env:ConnectionStrings__Refidomsa = $conexion.ConnectionString
    $env:DatabaseSeed__Password = $valores['SEED_PASSWORD']
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $proyecto = Join-Path $raiz 'src\Refidomsa.Api\Refidomsa.Api.csproj'

    if ($Accion -eq 'Inicializar') {
        & dotnet run --project $proyecto -- --initialize-db
    } elseif ($Accion -eq 'Verificar') {
        & dotnet run --project $proyecto -- --verify-db
    } else {
        & dotnet run --project $proyecto -- --urls http://localhost:5080
    }
    if ($LASTEXITCODE -ne 0) { throw "La accion $Accion fallo." }
} finally {
    $env:ConnectionStrings__Refidomsa = $conexionAnterior
    $env:DatabaseSeed__Password = $passwordAnterior
    $env:ASPNETCORE_ENVIRONMENT = $entornoAnterior
}
