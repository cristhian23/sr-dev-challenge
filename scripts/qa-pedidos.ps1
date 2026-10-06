param(
    [ValidateSet(1)]
    [int]$Bloque = 1
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$raiz = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $raiz '.env'
$dll = Join-Path $raiz 'src\Refidomsa.Api\bin\Release\net10.0\Refidomsa.Api.dll'
$qaNombre = 'Refidomsa_QA_' + [Guid]::NewGuid().ToString('N')
$procesos = New-Object System.Collections.Generic.List[object]
$qaCreada = $false
$cliente = New-Object System.Net.Http.HttpClient
$cliente.Timeout = [TimeSpan]::FromSeconds(10)

function Assert-QA($condicion, [string]$mensaje) {
    if (-not $condicion) { throw $mensaje }
}

function Get-Digest([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToBase64String($sha.ComputeHash($bytes)) } finally { $sha.Dispose() }
}

function Invoke-Sql([string]$catalogo, [string]$sql, [hashtable]$parametros = @{}, [switch]$Texto) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($conexionBase)
    $builder['Initial Catalog'] = $catalogo
    $conexion = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
    try {
        $conexion.Open()
        $comando = $conexion.CreateCommand()
        $comando.CommandText = $sql
        $comando.CommandTimeout = 30
        foreach ($nombre in $parametros.Keys) { [void]$comando.Parameters.AddWithValue($nombre, $parametros[$nombre]) }
        try {
            if ($Texto) {
                $lector = $comando.ExecuteReader()
                try {
                    $contenido = New-Object System.Text.StringBuilder
                    while ($lector.Read()) { [void]$contenido.Append($lector.GetString(0)) }
                    return $contenido.ToString()
                } finally { $lector.Dispose() }
            }
            [void]$comando.ExecuteNonQuery()
        } finally { $comando.Dispose() }
    } finally { $conexion.Dispose() }
}

function Get-ProduccionDigest {
    $texto = New-Object System.Text.StringBuilder
    foreach ($tabla in @('Distribuidores', 'Productos', 'Usuarios', 'Pedidos', 'LineasPedido', '__EFMigrationsHistory')) {
        $orden = 'Id'
        if ($tabla -eq 'LineasPedido') { $orden = 'PedidoId, ProductoId' }
        if ($tabla -eq '__EFMigrationsHistory') { $orden = 'MigrationId' }
        [void]$texto.Append($tabla)
        [void]$texto.Append((Invoke-Sql 'Refidomsa' "SELECT * FROM [$tabla] ORDER BY $orden FOR JSON PATH, INCLUDE_NULL_VALUES" -Texto))
    }
    return Get-Digest ([Text.Encoding]::UTF8.GetBytes($texto.ToString()))
}

function Start-ApiProceso([string]$argumentos) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'dotnet'
    $info.Arguments = '"' + $dll + '" ' + $argumentos
    $info.WorkingDirectory = $raiz
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    # Solo el entorno hijo recibe credenciales QA; el entorno del padre nunca cambia.
    $info.EnvironmentVariables['ConnectionStrings__Refidomsa'] = $conexionQA
    $info.EnvironmentVariables['DatabaseSeed__Password'] = $passwordQA
    $info.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $info.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Development'
    $info.EnvironmentVariables['Jwt__Clave'] = $claveQA
    $info.EnvironmentVariables['Jwt__Emisor'] = 'Refidomsa.QA'
    $info.EnvironmentVariables['Jwt__Audiencia'] = 'Refidomsa.QA.Client'
    $proceso = New-Object System.Diagnostics.Process
    $proceso.StartInfo = $info
    Assert-QA ($proceso.Start()) 'No se pudo iniciar el proceso propio.'
    $entrada = [PSCustomObject]@{ Proceso = $proceso; Salida = $null; Error = $null }
    $procesos.Add($entrada)
    # Drenar ambos streams en memoria evita bloqueos; nunca imprimir logs con posibles secretos.
    $entrada.Salida = $proceso.StandardOutput.ReadToEndAsync()
    $entrada.Error = $proceso.StandardError.ReadToEndAsync()
    return $proceso
}

function Invoke-Http([string]$ruta, [int]$status, [string]$token = '', [object]$cuerpo = $null,
    [int]$puerto = 5081) {
    $metodo = [System.Net.Http.HttpMethod]::Get
    if ($null -ne $cuerpo) { $metodo = [System.Net.Http.HttpMethod]::Post }
    $solicitud = New-Object System.Net.Http.HttpRequestMessage($metodo, "http://127.0.0.1:$puerto$ruta")
    try {
        if ($token) { $solicitud.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $token) }
        if ($null -ne $cuerpo) {
            $solicitud.Content = New-Object System.Net.Http.StringContent(($cuerpo | ConvertTo-Json -Depth 10),
                [Text.Encoding]::UTF8, 'application/json')
        }
        $respuesta = $cliente.SendAsync($solicitud).GetAwaiter().GetResult()
        try {
            Assert-QA ([int]$respuesta.StatusCode -eq $status) "$ruta esperaba HTTP $status, recibio $([int]$respuesta.StatusCode)."
            $texto = $respuesta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($status -ge 400) {
                Assert-QA ($respuesta.Content.Headers.ContentType.MediaType -eq 'application/problem+json') 'Error sin ProblemDetails.'
                $problema = $texto | ConvertFrom-Json
                Assert-QA ($problema.status -eq $status -and $problema.title -and $problema.instance -eq $ruta) 'Contrato ProblemDetails incorrecto.'
                if ($status -ne 400) { Assert-QA ($problema.detail) 'ProblemDetails sin detail.' }
                if ($status -eq 401) { Assert-QA ($respuesta.Headers.WwwAuthenticate.ToString() -eq 'Bearer') 'Falta WWW-Authenticate Bearer.' }
            }
            Write-Host "PASS HTTP $status $ruta"
            return ($texto | ConvertFrom-Json)
        } finally { $respuesta.Dispose() }
    } finally { $solicitud.Dispose() }
}

function Wait-Api([System.Diagnostics.Process]$proceso, [int]$puerto) {
    $limite = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $limite) {
        Assert-QA (-not $proceso.HasExited) 'La API QA termino antes de estar lista.'
        try {
            $respuesta = $cliente.GetAsync("http://127.0.0.1:$puerto/health/ready").GetAwaiter().GetResult()
            try { if ([int]$respuesta.StatusCode -eq 200) { return } } finally { $respuesta.Dispose() }
        } catch [System.Net.Http.HttpRequestException] { }
        Start-Sleep -Milliseconds 250
    }
    throw 'Timeout esperando readiness de la API QA.'
}

function Assert-Credito($respuesta, [string]$id, [decimal]$limite, [decimal]$consumido, [decimal]$disponible) {
    Assert-QA ($respuesta.distribuidorId -eq $id -and $respuesta.limiteCredito -eq $limite -and
        $respuesta.creditoConsumido -eq $consumido -and $respuesta.creditoDisponible -eq $disponible) 'Importes de credito incorrectos.'
    Assert-QA ((@($respuesta.PSObject.Properties.Name | Sort-Object) -join ',') -eq
        'creditoConsumido,creditoDisponible,distribuidorId,limiteCredito') 'Credito expone campos fuera del contrato.'
}

try {
    Assert-QA (Test-Path -LiteralPath $envFile) 'Falta .env existente.'
    Assert-QA (Test-Path -LiteralPath $dll) 'Ejecuta primero el build Release.'
    $envDigest = Get-Digest ([IO.File]::ReadAllBytes($envFile))
    $valores = @{}
    foreach ($linea in [IO.File]::ReadAllLines($envFile)) {
        if ([string]::IsNullOrWhiteSpace($linea) -or $linea.TrimStart().StartsWith('#')) { continue }
        $partes = $linea -split '=', 2
        if ($partes.Length -eq 2) { $valores[$partes[0].Trim()] = $partes[1].Trim() }
    }
    Assert-QA ($valores['SQLSERVER_PASSWORD']) 'Falta password SQL local.'
    $healthy = & docker inspect --format '{{.State.Health.Status}}' refidomsa-sqlserver-1
    Assert-QA ($LASTEXITCODE -eq 0 -and $healthy -eq 'healthy') 'SQL Server debe estar healthy; no se altera Compose.'
    foreach ($puerto in @(5081, 5082, 5173)) {
        $listener = New-Object System.Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $puerto)
        try { $listener.Start() } finally { $listener.Stop() }
    }
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = 'tcp:127.0.0.1,14333'
    $builder['User ID'] = 'sa'
    $builder['Password'] = $valores['SQLSERVER_PASSWORD']
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = $true
    $conexionBase = $builder.ConnectionString
    $produccionDigest = Get-ProduccionDigest
    Assert-QA ($qaNombre -cmatch '^Refidomsa_QA_[a-f0-9]{32}$') 'Nombre QA inseguro.'
    Invoke-Sql 'master' "CREATE DATABASE [$qaNombre]"
    $qaCreada = $true
    $builder['Initial Catalog'] = $qaNombre
    $conexionQA = $builder.ConnectionString
    $passwordQA = 'QA_' + [Guid]::NewGuid().ToString('N') + '!'
    $claveQA = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $inicializador = Start-ApiProceso '--initialize-db'
    Assert-QA ($inicializador.WaitForExit(60000)) 'Timeout al inicializar QA.'
    Assert-QA ($inicializador.ExitCode -eq 0) 'Inicializacion QA fallo (logs privados en memoria).'
    Write-Host 'PASS migracion y semilla aisladas'
    $norte = '11111111-1111-1111-1111-111111111111'
    $sur = '22222222-2222-2222-2222-222222222222'
    $este = '33333333-3333-3333-3333-333333333333'
    Invoke-Sql $qaNombre 'UPDATE Distribuidores SET LimiteCredito = 200000.25 WHERE Id = @id' @{ '@id' = [Guid]$norte }
    # Estados mezclados con lineas coherentes: solo Pendiente/Aprobado cuentan; Sur se aisla de Norte.
    foreach ($estado in @('Pendiente', 'Aprobado', 'Despachado', 'Rechazado', 'Cancelado', 'Pendiente')) {
        $propietario = $norte
        if ($estado -eq 'Pendiente' -and $ultimoPendiente) { $propietario = $sur }
        if ($estado -eq 'Pendiente') { $ultimoPendiente = $true }
        $pedidoId = [Guid]::NewGuid()
        Invoke-Sql $qaNombre @'
INSERT INTO Pedidos (Id, DistribuidorId, FechaEntrega, FechaCreacion, FechaCambioEstado, Total, Estado, MotivoRechazo)
VALUES (@pedido, @distribuidor, '2026-10-08T12:00:00+00:00', '2026-10-06T12:00:00+00:00',
 '2026-10-06T12:00:00+00:00', 145050.00, @estado, CASE WHEN @estado = 'Rechazado' THEN 'QA' ELSE NULL END);
INSERT INTO LineasPedido (PedidoId, ProductoId, NombreProducto, Galones, PrecioPorGalon, Subtotal)
SELECT @pedido, Id, Nombre, 500, PrecioPorGalon, 145050.00 FROM Productos WHERE Id = @producto;
'@ @{ '@pedido' = $pedidoId; '@distribuidor' = [Guid]$propietario; '@estado' = $estado;
      '@producto' = [Guid]'aaaaaaaa-0000-0000-0000-000000000001' }
    }
    $api1 = Start-ApiProceso '--urls http://127.0.0.1:5081'
    $api2 = Start-ApiProceso '--urls http://127.0.0.1:5082'
    Wait-Api $api1 5081
    Wait-Api $api2 5082
    $tokens = @{}
    foreach ($nombre in @('distribuidor.norte', 'distribuidor.sur', 'operador')) {
        $login = Invoke-Http '/api/auth/login' 200 -cuerpo @{ nombreUsuario = $nombre; password = $passwordQA }
        $tokens[$nombre] = $login.token
    }
    foreach ($nombre in $tokens.Keys) {
        $productos = @(Invoke-Http '/api/productos' 200 $tokens[$nombre])
        Assert-QA ($productos.Count -eq 4) 'Catalogo no contiene cuatro productos.'
        $catalogoSQL = (Invoke-Sql $qaNombre 'SELECT Id AS id, Nombre AS nombre, PrecioPorGalon AS precioPorGalon FROM Productos ORDER BY Nombre, Id FOR JSON PATH' -Texto) | ConvertFrom-Json
        Assert-QA (($productos | ConvertTo-Json -Compress) -eq ($catalogoSQL | ConvertTo-Json -Compress)) 'Catalogo difiere de SQL o expone campos adicionales.'
    }
    foreach ($id in @($norte, $sur, $este)) {
        $credito = Invoke-Http "/api/distribuidores/$id/credito" 200 $tokens['operador']
        if ($id -eq $norte) { Assert-Credito $credito $id 200000.25 290100 -90099.75 }
        elseif ($id -eq $sur) { Assert-Credito $credito $id 2000000 145050 1854950 }
        else { Assert-Credito $credito $id 1000000 0 1000000 }
    }
    Assert-Credito (Invoke-Http "/api/distribuidores/$norte/credito" 200 $tokens['distribuidor.norte']) $norte 200000.25 290100 -90099.75
    Assert-Credito (Invoke-Http "/api/distribuidores/$sur/credito" 200 $tokens['distribuidor.sur']) $sur 2000000 145050 1854950
    $ajeno = Invoke-Http "/api/distribuidores/$sur/credito" 404 $tokens['distribuidor.norte']
    [void](Invoke-Http "/api/distribuidores/$norte/credito" 404 $tokens['distribuidor.sur'])
    $inexistenteId = [Guid]::NewGuid().ToString()
    $inexistente = Invoke-Http "/api/distribuidores/$inexistenteId/credito" 404 $tokens['distribuidor.norte']
    Assert-QA ($ajeno.title -eq $inexistente.title -and $ajeno.detail -eq $inexistente.detail) '404 ajeno revela existencia.'
    [void](Invoke-Http "/api/distribuidores/$inexistenteId/credito" 404 $tokens['operador'])
    foreach ($ruta in @('/api/productos', "/api/distribuidores/$norte/credito")) {
        [void](Invoke-Http $ruta 401)
        [void](Invoke-Http $ruta 401 'token-invalido')
        $token = $tokens['operador']
        $partesToken = $token.Split('.')
        $firma = $partesToken[2]
        $letra = 'A'
        if ($firma[0] -eq 'A') { $letra = 'B' }
        $alterado = $partesToken[0] + '.' + $partesToken[1] + '.' + $letra + $firma.Substring(1)
        [void](Invoke-Http $ruta 401 $alterado)
    }
    [void](Invoke-Http '/api/distribuidores/no-es-guid/credito' 400 $tokens['operador'])
    [void](Invoke-Http '/api/distribuidores/00000000-0000-0000-0000-000000000000/credito' 400 $tokens['operador'])
    [void](Invoke-Http '/api/productos' 200 $tokens['operador'] -puerto 5082)
    Write-Host "PASS bloque $Bloque SQL/HTTP real"
} finally {
    $erroresLimpieza = New-Object System.Collections.Generic.List[string]
    foreach ($entrada in $procesos) {
        try {
            $proceso = $entrada.Proceso
            if (-not $proceso.HasExited) {
                $proceso.Kill()
                Assert-QA ($proceso.WaitForExit(10000)) 'Proceso QA no termino.'
            }
            $proceso.Dispose()
        } catch { $erroresLimpieza.Add('No se pudo detener un proceso propio QA.') }
    }
    $cliente.Dispose()
    [System.Data.SqlClient.SqlConnection]::ClearAllPools()
    if ($qaCreada) {
        try {
            Assert-QA ($qaNombre -cmatch '^Refidomsa_QA_[a-f0-9]{32}$') 'Nombre QA inseguro durante limpieza.'
            Invoke-Sql 'master' "ALTER DATABASE [$qaNombre] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$qaNombre];"
            Write-Host 'PASS base QA eliminada y procesos propios detenidos'
        } catch { $erroresLimpieza.Add("Base QA residual: $qaNombre. No se borro ningun otro catalogo.") }
    }
    try {
        if ($produccionDigest) {
            Assert-QA ((Get-ProduccionDigest) -eq $produccionDigest) 'La base Refidomsa cambio durante QA.'
            Write-Host 'PASS Refidomsa intacta'
        } else { Write-Host 'NO VERIFICADO Refidomsa: preflight sin baseline' }
        if ($envDigest) {
            Assert-QA ((Get-Digest ([IO.File]::ReadAllBytes($envFile))) -eq $envDigest) '.env cambio durante QA.'
            Write-Host 'PASS .env intacto; entorno padre sin cambios'
        } else { Write-Host 'NO VERIFICADO .env: preflight sin baseline' }
    } catch { $erroresLimpieza.Add('No se pudo verificar integridad de Refidomsa/.env.') }
    if ($erroresLimpieza.Count -gt 0) { throw ($erroresLimpieza -join ' ') }
}
