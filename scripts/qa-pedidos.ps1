param(
    [ValidateSet(1, 2, 3, 4, 5)]
    [int]$Bloque = 1,
    [switch]$Frontend
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Web.Extensions
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

function Start-ApiProceso([string]$argumentos, [switch]$PruebaConcurrencia) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'dotnet'
    $info.Arguments = '"' + $dll + '" ' + $argumentos
    if ($PruebaConcurrencia) {
        $info.Arguments = 'run --file "' + (Join-Path $PSScriptRoot 'QaConcurrencia.cs') + '" --configuration Release'
        $info.EnvironmentVariables['QA_PEDIDO_ID'] = $argumentos
    }
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
    [int]$puerto = 5081, [switch]$RawJson, [switch]$Patch) {
    $metodo = [System.Net.Http.HttpMethod]::Get
    if ($null -ne $cuerpo) { $metodo = [System.Net.Http.HttpMethod]::Post }
    if ($Patch) { $metodo = New-Object System.Net.Http.HttpMethod('PATCH') }
    $solicitud = New-Object System.Net.Http.HttpRequestMessage($metodo, "http://127.0.0.1:$puerto$ruta")
    try {
        if ($token) { $solicitud.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $token) }
        if ($null -ne $cuerpo) {
            $json = $cuerpo | ConvertTo-Json -Depth 10
            if ($RawJson) { $json = [string]$cuerpo }
            $solicitud.Content = New-Object System.Net.Http.StringContent($json,
                [Text.Encoding]::UTF8, 'application/json')
        }
        $respuesta = $cliente.SendAsync($solicitud).GetAwaiter().GetResult()
        try {
            Assert-QA ([int]$respuesta.StatusCode -eq $status) "$ruta esperaba HTTP $status, recibio $([int]$respuesta.StatusCode)."
            $texto = $respuesta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($status -eq 201) {
                $detalle = $texto | ConvertFrom-Json
                Assert-QA ($respuesta.Headers.Location.ToString() -eq "/api/pedidos/$($detalle.id)") 'Location de creacion incorrecto.'
            }
            if ($status -ge 400) {
                Assert-QA ($respuesta.Content.Headers.ContentType.MediaType -eq 'application/problem+json') 'Error sin ProblemDetails.'
                # PS5.1 ConvertFrom-Json rechaza la clave vacia de errores de validacion del body.
                $serializador = New-Object System.Web.Script.Serialization.JavaScriptSerializer
                $problema = $serializador.DeserializeObject($texto)
                Assert-QA ($problema.status -eq $status -and $problema.title -and $problema.instance -eq ($ruta -split '\?', 2)[0]) 'Contrato ProblemDetails incorrecto.'
                if ($status -ne 400) { Assert-QA ($problema.detail) 'ProblemDetails sin detail.' }
                if ($status -eq 401) { Assert-QA ($respuesta.Headers.WwwAuthenticate.ToString() -eq 'Bearer') 'Falta WWW-Authenticate Bearer.' }
            }
            Write-Host "PASS HTTP $status $ruta"
            if ($status -ge 400) { return $problema }
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

function Get-QARows([string]$sql, [hashtable]$parametros = @{}) {
    $filas = (Invoke-Sql $qaNombre ($sql + ' FOR JSON PATH, INCLUDE_NULL_VALUES') $parametros -Texto) | ConvertFrom-Json
    if ($null -eq $filas) { return }
    return $filas
}

function Get-PedidosDigest {
    $pedidos = Invoke-Sql $qaNombre 'SELECT * FROM Pedidos ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES' -Texto
    $lineas = Invoke-Sql $qaNombre 'SELECT * FROM LineasPedido ORDER BY PedidoId, ProductoId FOR JSON PATH' -Texto
    return Get-Digest ([Text.Encoding]::UTF8.GetBytes($pedidos + $lineas))
}

function Reset-Pedidos([decimal]$limite = 145050) {
    Invoke-Sql $qaNombre 'DELETE FROM LineasPedido; DELETE FROM Pedidos; UPDATE Distribuidores SET LimiteCredito = @limite' @{ '@limite' = $limite }
}

function Assert-NoEscritura($cuerpo, [int]$status = 400, [string]$token = $tokens['distribuidor.norte'], [switch]$RawJson) {
    $antes = Get-PedidosDigest
    $errorHttp = Invoke-Http '/api/pedidos' $status $token -cuerpo $cuerpo -RawJson:$RawJson
    Assert-QA ((Get-PedidosDigest) -eq $antes) 'Una solicitud rechazada modifico pedidos/lineas.'
    if ($status -eq 409) { Assert-QA ($errorHttp.codigo -eq 'credito_insuficiente') 'Conflicto sin codigo de credito.' }
    if ($status -eq 403) { Assert-QA ($errorHttp.codigo -eq 'sin_permiso') '403 sin codigo de permiso.' }
}

function Assert-PedidoSQL($detalle) {
    $filas = @(Get-QARows 'SELECT * FROM Pedidos WHERE Id = @id' @{ '@id' = [Guid]$detalle.id })
    Assert-QA ($filas.Count -eq 1) 'Pedido creado no encontrado en SQL.'
    $fila = $filas[0]
    Assert-QA ($fila.DistribuidorId -eq $detalle.distribuidorId -and $fila.Estado -eq 'Pendiente' -and
        [decimal]$fila.Total -eq [decimal]$detalle.total -and $null -eq $fila.MotivoRechazo) 'Cabecera SQL incorrecta.'
    foreach ($campo in @('FechaCreacion', 'FechaEntrega', 'FechaCambioEstado')) {
        Assert-QA ([DateTimeOffset]$fila.$campo -eq [DateTimeOffset]$detalle.$campo) "Fecha SQL $campo incorrecta."
        Assert-QA (([DateTimeOffset]$fila.$campo).Offset -eq [TimeSpan]::Zero) 'Fecha persistida no UTC.'
    }
    Assert-QA ($detalle.estado -eq 'Pendiente' -and $detalle.fechaCreacion -eq $detalle.fechaCambioEstado -and
        $null -eq $detalle.motivoRechazo) 'Estado inicial incorrecto.'
    $distribuidor = @(Get-QARows 'SELECT Nombre FROM Distribuidores WHERE Id = @id' @{ '@id' = [Guid]$detalle.distribuidorId })
    Assert-QA ($detalle.nombreDistribuidor -eq $distribuidor[0].Nombre) 'Nombre distribuidor incorrecto.'
    Assert-QA ((@($detalle.PSObject.Properties.Name | Sort-Object) -join ',') -eq
        'distribuidorId,estado,fechaCambioEstado,fechaCreacion,fechaEntrega,id,lineas,motivoRechazo,nombreDistribuidor,total') 'Detalle fuera de contrato.'
    $lineasSQL = @(Get-QARows 'SELECT * FROM LineasPedido WHERE PedidoId = @id' @{ '@id' = [Guid]$detalle.id })
    Assert-QA ($lineasSQL.Count -eq $detalle.lineas.Count) 'Cantidad de lineas SQL incorrecta.'
    [decimal]$total = 0
    foreach ($linea in $detalle.lineas) {
        $guardada = @($lineasSQL | Where-Object { $_.ProductoId -eq $linea.productoId })
        Assert-QA ($guardada.Count -eq 1) 'Linea no encontrada en SQL.'
        foreach ($campo in @('NombreProducto', 'Galones', 'PrecioPorGalon', 'Subtotal')) {
            Assert-QA ($guardada[0].$campo -eq $linea.$campo) "Snapshot SQL $campo difiere de respuesta."
        }
        $subtotal = [decimal]::Round(([decimal]$linea.galones * [decimal]$linea.precioPorGalon), 2, [MidpointRounding]::AwayFromZero)
        Assert-QA ($subtotal -eq [decimal]$linea.subtotal) 'Redondeo de subtotal incorrecto.'
        $total += $subtotal
        Assert-QA ((@($linea.PSObject.Properties.Name | Sort-Object) -join ',') -eq
            'galones,nombreProducto,precioPorGalon,productoId,subtotal') 'Linea fuera de contrato.'
    }
    Assert-QA ($total -eq [decimal]$detalle.total) 'Total no suma subtotales.'
}

function Start-PostPedido([object]$cuerpo, [string]$token, [int]$puerto,
    [System.Threading.CancellationToken]$cancelacion = [System.Threading.CancellationToken]::None) {
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "http://127.0.0.1:$puerto/api/pedidos")
    $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $token)
    $request.Content = New-Object System.Net.Http.StringContent(($cuerpo | ConvertTo-Json -Depth 10), [Text.Encoding]::UTF8, 'application/json')
    return [PSCustomObject]@{ Request = $request; Task = $cliente.SendAsync($request, $cancelacion) }
}

function Complete-PostPedido($pendiente) {
    $respuesta = $pendiente.Task.GetAwaiter().GetResult()
    try {
        $detalle = $respuesta.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        $status = [int]$respuesta.StatusCode
        if ($status -eq 201) {
            Assert-QA ($respuesta.Headers.Location.ToString() -eq "/api/pedidos/$($detalle.id)") 'Location de carrera incorrecto.'
            Assert-PedidoSQL $detalle
        } elseif ($status -eq 409) {
            Assert-QA ($detalle.codigo -eq 'credito_insuficiente' -and $detalle.status -eq 409 -and
                $respuesta.Content.Headers.ContentType.MediaType -eq 'application/problem+json') '409 de carrera incorrecto.'
        } else { throw "Carrera recibio HTTP $status inesperado." }
        return $status
    } finally { $respuesta.Dispose() }
}

function Open-DistribuidorLock([string]$id) {
    $conexion = New-Object System.Data.SqlClient.SqlConnection($conexionQA)
    try {
        $conexion.Open()
        $transaccion = $conexion.BeginTransaction([System.Data.IsolationLevel]::ReadCommitted)
        $comando = $conexion.CreateCommand()
        try {
            $comando.Transaction = $transaccion
            $comando.CommandText = 'SELECT Id FROM Distribuidores WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id; SELECT @@SPID;'
            [void]$comando.Parameters.AddWithValue('@id', [Guid]$id)
            $lector = $comando.ExecuteReader()
            try {
                Assert-QA ($lector.Read()) 'Distribuidor de lock no existe.'
                [void]$lector.NextResult()
                Assert-QA ($lector.Read()) 'Falta session ID del lock.'
                # @@SPID es smallint; GetInt32 lanza InvalidCastException en PS5.1.
                $spid = $lector.GetInt16(0)
            } finally { $lector.Dispose() }
        } finally { $comando.Dispose() }
        return [PSCustomObject]@{ Conexion = $conexion; Transaccion = $transaccion; Spid = $spid }
    } catch { $conexion.Dispose(); throw }
}

function Wait-LockRequests($bloqueo, [int]$cantidad) {
    $limite = [DateTime]::UtcNow.AddSeconds(5)
    while ([DateTime]::UtcNow -lt $limite) {
        # SQL puede encadenar el segundo waiter detras del primero, no directamente detras del holder.
        $filas = @(Get-QARows @'
WITH Esperas AS (
 SELECT session_id FROM sys.dm_exec_requests WHERE blocking_session_id = @spid AND database_id = DB_ID(@db)
 UNION ALL
 SELECT r.session_id FROM sys.dm_exec_requests r JOIN Esperas e ON r.blocking_session_id = e.session_id
 WHERE r.database_id = DB_ID(@db)
)
SELECT COUNT(*) AS cantidad FROM Esperas
'@ @{ '@spid' = $bloqueo.Spid; '@db' = $qaNombre })
        if ($filas[0].cantidad -ge $cantidad) { return }
        Start-Sleep -Milliseconds 40
    }
    throw 'No se comprobo que ambas APIs esperan el lock SQL por distribuidor.'
}

function Invoke-CarreraCredito([bool]$consumoPrevio, [int]$iteracion) {
    Reset-Pedidos
    if ($consumoPrevio) {
        Invoke-Sql $qaNombre 'UPDATE Distribuidores SET LimiteCredito = 290100 WHERE Id = @id' @{ '@id' = [Guid]$norte }
        $previo = Invoke-Http '/api/pedidos' 201 $tokens['distribuidor.norte'] -cuerpo $pedidoValido
        Assert-PedidoSQL $previo
    }
    $bloqueo = Open-DistribuidorLock $norte
    $peticiones = @()
    try {
        $peticiones += Start-PostPedido $pedidoValido $tokens['distribuidor.norte'] 5081
        $peticiones += Start-PostPedido $pedidoValido $tokens['distribuidor.norte'] 5082
        Wait-LockRequests $bloqueo 2
        Assert-QA (-not $peticiones[0].Task.IsCompleted -and -not $peticiones[1].Task.IsCompleted) 'Creacion evadio lock SQL.'
        $bloqueo.Transaccion.Commit()
        [void][System.Threading.Tasks.Task]::WhenAll([System.Threading.Tasks.Task[]]@($peticiones[0].Task, $peticiones[1].Task)).GetAwaiter().GetResult()
        $status = @($peticiones | ForEach-Object { Complete-PostPedido $_ } | Sort-Object)
        Assert-QA (($status -join ',') -eq '201,409') 'Carrera no produjo exactamente 201/409.'
        $resumen = @(Get-QARows 'SELECT COUNT(*) AS cantidad, SUM(Total) AS consumido FROM Pedidos WHERE DistribuidorId = @id' @{ '@id' = [Guid]$norte })
        $esperados = 1
        if ($consumoPrevio) { $esperados = 2 }
        Assert-QA ($resumen[0].cantidad -eq $esperados -and [decimal]$resumen[0].consumido -eq 145050 * $esperados) 'Sobreconsumo o escrituras extra en carrera.'
        $lineas = @(Get-QARows 'SELECT COUNT(*) AS cantidad FROM LineasPedido')
        Assert-QA ($lineas[0].cantidad -eq $esperados) 'Lineas extra o faltantes en carrera.'
        Write-Host "PASS carrera $iteracion consumoPrevio=$consumoPrevio HTTP201/409 y SQL exacto"
    } finally {
        $bloqueo.Transaccion.Dispose()
        $bloqueo.Conexion.Dispose()
        foreach ($peticion in $peticiones) {
            try { $peticion.Task.GetAwaiter().GetResult().Dispose() } catch [System.OperationCanceledException] { }
            $peticion.Request.Dispose()
        }
    }
}

function Assert-ListaSQL([string]$query = '', [string]$usuario = 'operador', [string]$where = '1=1',
    [hashtable]$parametros = @{}, [int]$pagina = 1, [int]$tamano = 10) {
    $ruta = '/api/pedidos'
    if ($query) { $ruta += '?' + $query }
    $lista = Invoke-Http $ruta 200 $tokens[$usuario]
    Assert-QA ((@($lista.PSObject.Properties.Name | Sort-Object) -join ',') -eq
        'items,pagina,tamanoPagina,totalRegistros') 'Lista fuera de contrato.'
    $esperados = @(Get-QARows ("SELECT p.Id, p.DistribuidorId, d.Nombre AS NombreDistribuidor, p.FechaEntrega, p.FechaCreacion, p.FechaCambioEstado, p.Total, p.Estado FROM Pedidos p JOIN Distribuidores d ON d.Id = p.DistribuidorId WHERE $where ORDER BY p.FechaCreacion DESC, p.Id DESC") $parametros)
    Assert-QA ($lista.pagina -eq $pagina -and $lista.tamanoPagina -eq $tamano -and
        $lista.totalRegistros -eq $esperados.Count) 'Paginacion o total incorrecto/ajeno.'
    $offset = ($pagina - 1) * $tamano
    $cantidad = [Math]::Max(0, [Math]::Min($tamano, $esperados.Count - $offset))
    Assert-QA (@($lista.items).Count -eq $cantidad) 'Cantidad paginada incorrecta.'
    for ($i = 0; $i -lt $cantidad; $i++) {
        $actual = $lista.items[$i]
        $sql = $esperados[$offset + $i]
        Assert-QA ((@($actual.PSObject.Properties.Name | Sort-Object) -join ',') -eq
            'distribuidorId,estado,fechaCambioEstado,fechaCreacion,fechaEntrega,id,nombreDistribuidor,total') 'Resumen expone lineas/campos no requeridos.'
        foreach ($campo in @('id', 'distribuidorId', 'nombreDistribuidor', 'estado', 'total')) {
            Assert-QA ($actual.$campo -eq $sql.$campo) "Resumen $campo u orden difiere de SQL."
        }
        foreach ($campo in @('fechaEntrega', 'fechaCreacion', 'fechaCambioEstado')) {
            Assert-QA ([DateTimeOffset]$actual.$campo -eq [DateTimeOffset]$sql.$campo) "Resumen $campo difiere de SQL."
        }
    }
    return $lista
}

function Test-Bloque3 {
    Reset-Pedidos 10000000
    $estados = @('Pendiente', 'Aprobado', 'Despachado', 'Rechazado', 'Cancelado')
    $fixtures = @()
    for ($i = 0; $i -lt 10; $i++) {
        $propietario = $norte
        if ($i -ge 5) { $propietario = $sur }
        $id = [Guid]::NewGuid()
        # Fechas repetidas obligan a comprobar el desempate Id DESC de SQL Server.
        $creacion = [DateTimeOffset]::Parse('2026-09-01T04:00:00Z').AddDays($i % 3)
        $estado = $estados[$i % 5]
        Invoke-Sql $qaNombre @'
INSERT INTO Pedidos (Id, DistribuidorId, FechaEntrega, FechaCreacion, FechaCambioEstado, Total, Estado, MotivoRechazo)
VALUES (@id, @distribuidor, @entrega, @creacion, @cambio, 145050, @estado,
 CASE WHEN @estado = 'Rechazado' THEN 'QA motivo historico' ELSE NULL END);
INSERT INTO LineasPedido (PedidoId, ProductoId, NombreProducto, Galones, PrecioPorGalon, Subtotal)
SELECT @id, Id, Nombre, 500, PrecioPorGalon, 145050 FROM Productos WHERE Id = @producto;
'@ @{ '@id' = $id; '@distribuidor' = [Guid]$propietario; '@entrega' = $creacion.AddDays(7);
        '@creacion' = $creacion; '@cambio' = $creacion.AddHours(1); '@estado' = $estado;
        '@producto' = [Guid]'aaaaaaaa-0000-0000-0000-000000000001' }
        $fixtures += [PSCustomObject]@{ Id = $id.ToString(); Propietario = $propietario; Estado = $estado }
    }
    [void](Assert-ListaSQL)
    foreach ($usuario in @('distribuidor.norte', 'distribuidor.sur')) {
        $propietario = $norte
        if ($usuario -eq 'distribuidor.sur') { $propietario = $sur }
        $params = @{ '@owner' = [Guid]$propietario }
        [void](Assert-ListaSQL '' $usuario 'p.DistribuidorId = @owner' $params)
        [void](Assert-ListaSQL "distribuidorId=$propietario" $usuario 'p.DistribuidorId = @owner' $params)
        foreach ($estado in $estados) {
            [void](Assert-ListaSQL "estado=$estado" $usuario 'p.DistribuidorId = @owner AND p.Estado = @estado' @{ '@owner' = [Guid]$propietario; '@estado' = $estado })
        }
    }
    foreach ($estado in $estados) {
        [void](Assert-ListaSQL "estado=$estado" 'operador' 'p.Estado = @estado' @{ '@estado' = $estado })
    }
    foreach ($propietario in @($norte, $sur, [Guid]::NewGuid().ToString())) {
        [void](Assert-ListaSQL "distribuidorId=$propietario" 'operador' 'p.DistribuidorId = @id' @{ '@id' = [Guid]$propietario })
    }
    $desde = [Uri]::EscapeDataString('2026-09-01T00:00:00-04:00')
    $hasta = [Uri]::EscapeDataString('2026-09-02T00:00:00-04:00')
    $rango = @{ '@desde' = [DateTimeOffset]::Parse('2026-09-01T04:00:00Z'); '@hasta' = [DateTimeOffset]::Parse('2026-09-02T04:00:00Z') }
    [void](Assert-ListaSQL "desde=$desde" 'operador' 'p.FechaCreacion >= @desde' @{ '@desde' = $rango['@desde'] })
    [void](Assert-ListaSQL "hasta=$hasta" 'operador' 'p.FechaCreacion < @hasta' @{ '@hasta' = $rango['@hasta'] })
    [void](Assert-ListaSQL "desde=$desde&hasta=$hasta" 'operador' 'p.FechaCreacion >= @desde AND p.FechaCreacion < @hasta' $rango)
    $desdePositivo = [Uri]::EscapeDataString('2026-09-01T06:00:00+02:00')
    [void](Assert-ListaSQL "desde=$desdePositivo&hasta=$hasta" 'operador' 'p.FechaCreacion >= @desde AND p.FechaCreacion < @hasta' $rango)
    [void](Assert-ListaSQL "desde=$desde&hasta=$hasta&distribuidorId=$sur&estado=Rechazado" 'operador' `
        'p.FechaCreacion >= @desde AND p.FechaCreacion < @hasta AND p.DistribuidorId = @id AND p.Estado = @estado' `
        @{ '@desde' = $rango['@desde']; '@hasta' = $rango['@hasta']; '@id' = [Guid]$sur; '@estado' = 'Rechazado' })
    [void](Assert-ListaSQL "desde=$desde&hasta=$hasta&estado=Pendiente" 'distribuidor.norte' `
        'p.FechaCreacion >= @desde AND p.FechaCreacion < @hasta AND p.DistribuidorId = @id AND p.Estado = @estado' `
        @{ '@desde' = $rango['@desde']; '@hasta' = $rango['@hasta']; '@id' = [Guid]$norte; '@estado' = 'Pendiente' })
    $ids = @()
    for ($pagina = 1; $pagina -le 4; $pagina++) {
        $lista = Assert-ListaSQL "pagina=$pagina&tamanoPagina=3" 'operador' '1=1' @{} $pagina 3
        $ids += @($lista.items | ForEach-Object { $_.id })
    }
    Assert-QA ($ids.Count -eq 10 -and @($ids | Select-Object -Unique).Count -eq 10) 'Paginas duplican/omiten pedidos.'
    for ($pagina = 1; $pagina -le 4; $pagina++) {
        [void](Assert-ListaSQL "pagina=$pagina&tamanoPagina=2" 'distribuidor.norte' 'p.DistribuidorId = @id' @{ '@id' = [Guid]$norte } $pagina 2)
    }
    [void](Assert-ListaSQL 'pagina=5&tamanoPagina=3' 'operador' '1=1' @{} 5 3)
    [void](Assert-ListaSQL 'tamanoPagina=1' 'operador' '1=1' @{} 1 1)
    [void](Assert-ListaSQL 'tamanoPagina=100' 'operador' '1=1' @{} 1 100)
    [void](Assert-ListaSQL 'pagina=2147483647&tamanoPagina=1' 'operador' '1=1' @{} 2147483647 1)
    foreach ($query in @('pagina=0', 'pagina=-1', 'pagina=no', 'pagina=2147483648', 'pagina=2147483647',
        'tamanoPagina=0', 'tamanoPagina=101', 'tamanoPagina=-1', 'tamanoPagina=no',
        'estado=Inexistente', 'estado=0', 'estado=999', 'estado=Pendiente,Aprobado',
        'distribuidorId=no', 'distribuidorId=00000000-0000-0000-0000-000000000000',
        'desde=no', 'hasta=no', 'desde=2026-09-01', 'hasta=2026-09-01T00:00:00',
        "desde=$hasta&hasta=$desde", "desde=$desde&hasta=$desde")) {
        [void](Invoke-Http "/api/pedidos?$query" 400 $tokens['operador'])
    }
    $otro = Invoke-Http "/api/pedidos?distribuidorId=$sur" 404 $tokens['distribuidor.norte']
    $missing = [Guid]::NewGuid().ToString()
    $noExiste = Invoke-Http "/api/pedidos?distribuidorId=$missing" 404 $tokens['distribuidor.norte']
    Assert-QA ($otro.title -eq $noExiste.title -and $otro.detail -eq $noExiste.detail) 'Filtro ajeno revela existencia.'
    foreach ($fixture in $fixtures) {
        $detalle = Invoke-Http "/api/pedidos/$($fixture.Id)" 200 $tokens['operador']
        Assert-QA ($detalle.id -eq $fixture.Id -and $detalle.distribuidorId -eq $fixture.Propietario -and
            $detalle.estado -eq $fixture.Estado -and $detalle.total -eq 145050 -and $detalle.lineas.Count -eq 1) 'Detalle incorrecto.'
        Assert-QA ((@($detalle.PSObject.Properties.Name | Sort-Object) -join ',') -eq
            'distribuidorId,estado,fechaCambioEstado,fechaCreacion,fechaEntrega,id,lineas,motivoRechazo,nombreDistribuidor,total') 'Detalle expone datos fuera de contrato.'
        $sql = @(Get-QARows 'SELECT d.Nombre, p.MotivoRechazo FROM Pedidos p JOIN Distribuidores d ON d.Id = p.DistribuidorId WHERE p.Id = @id' @{ '@id' = [Guid]$fixture.Id })
        Assert-QA ($detalle.nombreDistribuidor -eq $sql[0].Nombre -and $detalle.motivoRechazo -eq $sql[0].MotivoRechazo) 'Detalle nombre/motivo incorrecto.'
        $usuario = 'distribuidor.norte'
        if ($fixture.Propietario -eq $sur) { $usuario = 'distribuidor.sur' }
        [void](Invoke-Http "/api/pedidos/$($fixture.Id)" 200 $tokens[$usuario])
    }
    $ajenoId = $fixtures[5].Id
    $ajeno = Invoke-Http "/api/pedidos/$ajenoId" 404 $tokens['distribuidor.norte']
    $inexistente = Invoke-Http "/api/pedidos/$missing" 404 $tokens['distribuidor.norte']
    Assert-QA ($ajeno.title -eq $inexistente.title -and $ajeno.detail -eq $inexistente.detail) 'Detalle ajeno revela existencia.'
    [void](Invoke-Http "/api/pedidos/$($fixtures[0].Id)" 404 $tokens['distribuidor.sur'])
    [void](Invoke-Http "/api/pedidos/$missing" 404 $tokens['operador'])
    foreach ($id in @('no-es-guid', [Guid]::Empty.ToString())) {
        [void](Invoke-Http "/api/pedidos/$id" 400 $tokens['operador'])
    }
    foreach ($ruta in @('/api/pedidos', "/api/pedidos/$($fixtures[0].Id)")) {
        [void](Invoke-Http $ruta 401)
        [void](Invoke-Http $ruta 401 'token-invalido')
    }
    $rutaSnapshot = "/api/pedidos/$($fixtures[0].Id)"
    $historico = Invoke-Http $rutaSnapshot 200 $tokens['distribuidor.norte']
    Invoke-Sql $qaNombre 'UPDATE Productos SET Nombre = @nombre, PrecioPorGalon = 999 WHERE Id = @id' `
        @{ '@nombre' = 'QA catalogo nuevo'; '@id' = [Guid]'aaaaaaaa-0000-0000-0000-000000000001' }
    $actual = Invoke-Http $rutaSnapshot 200 $tokens['distribuidor.norte']
    Assert-QA (($historico | ConvertTo-Json -Depth 10 -Compress) -eq ($actual | ConvertTo-Json -Depth 10 -Compress)) 'GET modifico snapshots por cambios de catalogo.'
    $lineaSQL = @(Get-QARows 'SELECT ProductoId, NombreProducto, Galones, PrecioPorGalon, Subtotal FROM LineasPedido WHERE PedidoId = @id' @{ '@id' = [Guid]$fixtures[0].Id })
    foreach ($campo in @('productoId', 'nombreProducto', 'galones', 'precioPorGalon', 'subtotal')) {
        Assert-QA ($actual.lineas[0].$campo -eq $lineaSQL[0].$campo) 'Snapshot detalle difiere de SQL.'
    }
    Assert-QA ((@($actual.lineas[0].PSObject.Properties.Name | Sort-Object) -join ',') -eq
        'galones,nombreProducto,precioPorGalon,productoId,subtotal') 'Linea expone entidad.'
    Write-Host 'PASS bloque3 scope antes de paginar, filtros/rangos, snapshots y contratos SQL/HTTP'
}

function New-PedidoEstado([string]$estado = 'Pendiente', [string]$propietario = $norte) {
    $id = [Guid]::NewGuid()
    Invoke-Sql $qaNombre @'
INSERT INTO Pedidos (Id, DistribuidorId, FechaEntrega, FechaCreacion, FechaCambioEstado, Total, Estado, MotivoRechazo)
VALUES (@id, @owner, '2026-09-08T12:00:00Z', '2026-09-01T12:00:00Z', '2026-09-01T13:00:00Z',
 145050, @estado, CASE WHEN @estado = 'Rechazado' THEN 'QA historico' ELSE NULL END);
INSERT INTO LineasPedido (PedidoId, ProductoId, NombreProducto, Galones, PrecioPorGalon, Subtotal)
SELECT @id, Id, Nombre, 500, 290.1, 145050 FROM Productos WHERE Id = @producto;
'@ @{ '@id' = $id; '@owner' = [Guid]$propietario; '@estado' = $estado;
        '@producto' = [Guid]'aaaaaaaa-0000-0000-0000-000000000001' }
    return $id.ToString()
}

function Assert-EstadoSQL($detalle, [string]$esperado, [string]$motivo, $anterior, [DateTimeOffset]$inicio) {
    $fila = @(Get-QARows 'SELECT * FROM Pedidos WHERE Id = @id' @{ '@id' = [Guid]$detalle.id })[0]
    Assert-QA ($detalle.estado -eq $esperado -and $fila.Estado -eq $esperado -and
        $detalle.motivoRechazo -eq $fila.MotivoRechazo) 'Estado/motivo HTTP difiere de SQL.'
    if ($esperado -eq 'Rechazado') {
        Assert-QA ($fila.MotivoRechazo -eq $motivo.Trim()) 'Motivo rechazo no corresponde al ganador.'
    } else { Assert-QA ($null -eq $fila.MotivoRechazo) 'Motivo persistido fuera de rechazo.' }
    $fecha = [DateTimeOffset]$fila.FechaCambioEstado
    Assert-QA ($fecha -eq [DateTimeOffset]$detalle.fechaCambioEstado -and $fecha -ge $inicio -and
        $fecha -le [DateTimeOffset]::UtcNow -and $fecha.Offset -eq [TimeSpan]::Zero) 'Fecha cambio no usa reloj UTC fresco.'
    foreach ($campo in @('id', 'distribuidorId', 'nombreDistribuidor', 'total', 'fechaEntrega', 'fechaCreacion')) {
        Assert-QA ($detalle.$campo -eq $anterior.$campo) "PATCH modifico campo inmutable $campo."
    }
    Assert-QA (($detalle.lineas | ConvertTo-Json -Depth 10 -Compress) -eq
        ($anterior.lineas | ConvertTo-Json -Depth 10 -Compress)) 'PATCH modifico snapshots owned.'
    $lineas = @(Get-QARows 'SELECT ProductoId, NombreProducto, Galones, PrecioPorGalon, Subtotal FROM LineasPedido WHERE PedidoId = @id' @{ '@id' = [Guid]$detalle.id })
    Assert-QA ($lineas.Count -eq $detalle.lineas.Count) 'PATCH modifico cantidad lineas SQL.'
    foreach ($campo in @('productoId', 'nombreProducto', 'galones', 'precioPorGalon', 'subtotal')) {
        Assert-QA ($lineas[0].$campo -eq $detalle.lineas[0].$campo) 'PATCH altero snapshot SQL.'
    }
}

function Assert-CreditoSQL([string]$propietario = $norte) {
    $sql = @(Get-QARows @'
SELECT d.LimiteCredito, COALESCE(SUM(p.Total), 0) AS Consumido FROM Distribuidores d
LEFT JOIN Pedidos p ON p.DistribuidorId = d.Id AND p.Estado IN ('Pendiente', 'Aprobado')
WHERE d.Id = @id GROUP BY d.LimiteCredito
'@ @{ '@id' = [Guid]$propietario })[0]
    Assert-QA ([decimal]$sql.Consumido -le [decimal]$sql.LimiteCredito) 'Sobreconsumo en SQL.'
    Assert-Credito (Invoke-Http "/api/distribuidores/$propietario/credito" 200 $tokens['operador']) `
        $propietario $sql.LimiteCredito $sql.Consumido ($sql.LimiteCredito - $sql.Consumido)
}

function Assert-PatchFallo([string]$id, $cuerpo, [int]$status, [string]$usuario = 'operador',
    [string]$codigo = '', [switch]$RawJson) {
    $digest = Get-PedidosDigest
    $errorHttp = Invoke-Http "/api/pedidos/$id/estado" $status $tokens[$usuario] -cuerpo $cuerpo -Patch -RawJson:$RawJson
    Assert-QA ((Get-PedidosDigest) -eq $digest) 'PATCH fallido modifico cabeceras/lineas.'
    if ($codigo) { Assert-QA ($errorHttp.codigo -eq $codigo) 'Codigo PATCH incorrecto.' }
    return $errorHttp
}

function Start-PatchEstado([string]$id, [string]$estado, [string]$usuario, [int]$puerto,
    [System.Threading.CancellationToken]$cancelacion = [System.Threading.CancellationToken]::None) {
    $request = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod('PATCH')), "http://127.0.0.1:$puerto/api/pedidos/$id/estado")
    $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $tokens[$usuario])
    $request.Content = New-Object System.Net.Http.StringContent((@{ nuevoEstado = $estado; motivo = '  QA carrera  ' } | ConvertTo-Json), [Text.Encoding]::UTF8, 'application/json')
    return [PSCustomObject]@{ Request = $request; Task = $cliente.SendAsync($request, $cancelacion); Estado = $estado }
}

function Complete-PatchEstado($pendiente) {
    $respuesta = $pendiente.Task.GetAwaiter().GetResult()
    try {
        $status = [int]$respuesta.StatusCode
        $body = $respuesta.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Assert-QA ($status -eq 200 -or $status -eq 409) "Carrera PATCH recibio HTTP $status."
        if ($status -eq 409) {
            Assert-QA ($body.codigo -eq 'transicion_invalida' -and $body.status -eq 409 -and
                $respuesta.Content.Headers.ContentType.MediaType -eq 'application/problem+json') 'Conflicto de carrera PATCH incorrecto.'
        }
        return [PSCustomObject]@{ Status = $status; Body = $body; Estado = $pendiente.Estado }
    } finally { $respuesta.Dispose() }
}

function Invoke-CarreraEstado([string]$origen, [string]$destino1, [string]$destino2, [int]$iteracion,
    [switch]$Crear) {
    Reset-Pedidos 145050
    $id = New-PedidoEstado $origen
    $anterior = Invoke-Http "/api/pedidos/$id" 200 $tokens['operador']
    $usuario1 = 'operador'
    if ($destino1 -eq 'Cancelado') { $usuario1 = 'distribuidor.norte' }
    $bloqueo = Open-DistribuidorLock $norte
    $peticiones = @()
    try {
        # Alternar quien entra primero ejercita ambos ordenes seriales en carreras con creacion.
        if ($Crear -and $iteracion % 2 -eq 0) {
            $peticiones += Start-PostPedido $pedidoValido $tokens['distribuidor.norte'] 5082
            Wait-LockRequests $bloqueo 1
        }
        $patch = Start-PatchEstado $id $destino1 $usuario1 5081
        $peticiones += $patch
        if ($Crear) {
            if ($iteracion % 2 -ne 0) {
                Wait-LockRequests $bloqueo 1
                $peticiones += Start-PostPedido $pedidoValido $tokens['distribuidor.norte'] 5082
            }
        } else { $peticiones += Start-PatchEstado $id $destino2 'operador' 5082 }
        Wait-LockRequests $bloqueo 2
        Assert-QA (-not $peticiones[0].Task.IsCompleted -and -not $peticiones[1].Task.IsCompleted) 'Mutacion evadio lock distribuidor.'
        $inicio = [DateTimeOffset]::UtcNow
        $bloqueo.Transaccion.Commit()
        [void][System.Threading.Tasks.Task]::WhenAll([System.Threading.Tasks.Task[]]@($peticiones[0].Task, $peticiones[1].Task)).GetAwaiter().GetResult()
        if ($Crear) {
            $resultado = Complete-PatchEstado $patch
            Assert-QA ($resultado.Status -eq 200) 'Liberacion perdio cambio de estado.'
            $post = @($peticiones | Where-Object { $_ -ne $patch })[0]
            $postStatus = Complete-PostPedido $post
            Assert-QA ($postStatus -eq 201 -or $postStatus -eq 409) 'Creacion no tiene resultado serial.'
            $filas = @(Get-QARows 'SELECT COUNT(*) AS Cantidad, SUM(CASE WHEN Estado IN (''Pendiente'', ''Aprobado'') THEN Total ELSE 0 END) AS Consumido FROM Pedidos')
            $cantidad = 1; $consumido = 0
            if ($postStatus -eq 201) { $cantidad = 2; $consumido = 145050 }
            Assert-QA ($filas[0].Cantidad -eq $cantidad -and $filas[0].Consumido -eq $consumido) 'Creacion/liberacion incoherente.'
        } else {
            $resultados = @($peticiones | ForEach-Object { Complete-PatchEstado $_ })
            Assert-QA ((@($resultados.Status | Sort-Object) -join ',') -eq '200,409') 'Carrera estados no produjo exactamente un200/un409.'
            $resultado = @($resultados | Where-Object { $_.Status -eq 200 })[0]
            $filas = @(Get-QARows 'SELECT COUNT(*) AS Cantidad FROM Pedidos')
            Assert-QA ($filas[0].Cantidad -eq 1) 'Carrera estados altero cantidad pedidos.'
        }
        Assert-EstadoSQL $resultado.Body $resultado.Estado 'QA carrera' $anterior $inicio
        Assert-CreditoSQL
        Write-Host "PASS carrera estado $origen $destino1/$destino2 crear=$Crear iteracion=$iteracion ganador/SQL/credito coherentes"
    } finally {
        $bloqueo.Transaccion.Dispose(); $bloqueo.Conexion.Dispose()
        foreach ($peticion in $peticiones) {
            try { $peticion.Task.GetAwaiter().GetResult().Dispose() } finally { $peticion.Request.Dispose() }
        }
    }
}

function Test-Bloque4 {
    # Bloque3 cambia solo el catalogo QA: restaurar precio para carreras de limite exacto.
    Invoke-Sql $qaNombre 'UPDATE Productos SET PrecioPorGalon = 290.1 WHERE Id = @id' @{ '@id' = [Guid]'aaaaaaaa-0000-0000-0000-000000000001' }
    $estados = @('Pendiente', 'Aprobado', 'Despachado', 'Rechazado', 'Cancelado')
    foreach ($usuario in @('operador', 'distribuidor.norte', 'distribuidor.sur')) {
        foreach ($owner in @($norte, $sur)) {
            foreach ($origen in $estados) {
                foreach ($destino in $estados) {
                    Reset-Pedidos 145050
                    $id = New-PedidoEstado $origen $owner
                    $cuerpo = @{ nuevoEstado = $destino; motivo = '  QA matriz  ' }
                    $visible = $usuario -eq 'operador' -or ($usuario -eq 'distribuidor.norte' -and $owner -eq $norte) -or
                        ($usuario -eq 'distribuidor.sur' -and $owner -eq $sur)
                    $permiso = ($usuario -eq 'operador' -and $destino -ne 'Cancelado') -or
                        ($usuario -ne 'operador' -and $destino -eq 'Cancelado')
                    $transicion = ($origen -eq 'Pendiente' -and $destino -in @('Aprobado', 'Rechazado', 'Cancelado')) -or
                        ($origen -eq 'Aprobado' -and $destino -eq 'Despachado')
                    if (-not $visible) { [void](Assert-PatchFallo $id $cuerpo 404 $usuario) }
                    elseif (-not $permiso) { [void](Assert-PatchFallo $id $cuerpo 403 $usuario 'sin_permiso') }
                    elseif (-not $transicion) { [void](Assert-PatchFallo $id $cuerpo 409 $usuario 'transicion_invalida') }
                    else {
                        $anterior = Invoke-Http "/api/pedidos/$id" 200 $tokens[$usuario]
                        $inicio = [DateTimeOffset]::UtcNow
                        $detalle = Invoke-Http "/api/pedidos/$id/estado" 200 $tokens[$usuario] -cuerpo $cuerpo -Patch
                        Assert-EstadoSQL $detalle $destino 'QA matriz' $anterior $inicio
                        $consumido = 0
                        if ($destino -eq 'Aprobado') { $consumido = 145050 }
                        Assert-Credito (Invoke-Http "/api/distribuidores/$owner/credito" 200 $tokens['operador']) $owner 145050 $consumido (145050 - $consumido)
                    }
                }
            }
        }
    }
    Write-Host 'PASS matriz completa 3 identidades/2 propietarios/5 origenes/5 destinos, fallos sin escrituras'
    Reset-Pedidos 145050
    $id = New-PedidoEstado
    foreach ($cuerpo in @(@{}, @{ nuevoEstado = $null }, @{ nuevoEstado = '' }, @{ nuevoEstado = ' ' },
        @{ nuevoEstado = '0' }, @{ nuevoEstado = '999' }, @{ nuevoEstado = 'aprobado' }, @{ nuevoEstado = ' Aprobado ' },
        @{ nuevoEstado = 'Aprobado,Rechazado' }, @{ nuevoEstado = 'Inexistente' }, @{ nuevoEstado = 'Rechazado' },
        @{ nuevoEstado = 'Rechazado'; motivo = '' }, @{ nuevoEstado = 'Rechazado'; motivo = '  ' },
        @{ nuevoEstado = 'Rechazado'; motivo = ('x' * 1001) }, @{ nuevoEstado = 'Aprobado'; motivo = ('x' * 1001) })) {
        [void](Assert-PatchFallo $id $cuerpo 400)
    }
    foreach ($json in @('null', '{', '', '{"nuevoEstado":1}', '{"nuevoEstado":true}', '{"nuevoEstado":{}}', '{"nuevoEstado":"Rechazado","motivo":1}')) {
        [void](Assert-PatchFallo $id $json 400 -RawJson)
    }
    foreach ($invalido in @('no-es-guid', [Guid]::Empty.ToString())) {
        [void](Assert-PatchFallo $invalido @{ nuevoEstado = 'Aprobado' } 400)
    }
    $missing = [Guid]::NewGuid().ToString()
    $ajeno = Assert-PatchFallo $id @{ nuevoEstado = 'Cancelado' } 404 'distribuidor.sur'
    $noExiste = Assert-PatchFallo $missing @{ nuevoEstado = 'Cancelado' } 404 'distribuidor.sur'
    Assert-QA ($ajeno.title -eq $noExiste.title -and $ajeno.detail -eq $noExiste.detail) 'PATCH ajeno revela existencia.'
    [void](Assert-PatchFallo $missing @{ nuevoEstado = 'Aprobado' } 404)
    foreach ($token in @('', 'token-invalido')) {
        $digest = Get-PedidosDigest
        [void](Invoke-Http "/api/pedidos/$id/estado" 401 $token -cuerpo @{ nuevoEstado = 'Aprobado' } -Patch)
        Assert-QA ((Get-PedidosDigest) -eq $digest) 'PATCH sin identidad escribio.'
    }
    $anterior = Invoke-Http "/api/pedidos/$id" 200 $tokens['operador']
    $inicio = [DateTimeOffset]::UtcNow
    $detalle = Invoke-Http "/api/pedidos/$id/estado" 200 $tokens['operador'] -Patch -cuerpo @{ nuevoEstado = 'Rechazado'; motivo = ('x' * 1000) }
    Assert-EstadoSQL $detalle 'Rechazado' ('x' * 1000) $anterior $inicio
    foreach ($accion in @('Aprobado', 'Cancelado')) {
        Reset-Pedidos 145050; $id = New-PedidoEstado
        $usuario = 'operador'; if ($accion -eq 'Cancelado') { $usuario = 'distribuidor.norte' }
        [void](Invoke-Http "/api/pedidos/$id/estado" 200 $tokens[$usuario] -Patch -cuerpo @{ nuevoEstado = $accion })
        Assert-CreditoSQL
        if ($accion -eq 'Aprobado') {
            [void](Invoke-Http "/api/pedidos/$id/estado" 200 $tokens['operador'] -Patch -cuerpo @{ nuevoEstado = 'Despachado' })
            Assert-CreditoSQL
        }
    }
    foreach ($carrera in @(@('Pendiente', 'Aprobado', 'Aprobado'), @('Pendiente', 'Aprobado', 'Rechazado'),
        @('Pendiente', 'Cancelado', 'Aprobado'), @('Aprobado', 'Despachado', 'Despachado'))) {
        for ($i = 1; $i -le 10; $i++) { Invoke-CarreraEstado $carrera[0] $carrera[1] $carrera[2] $i }
    }
    foreach ($carrera in @(@('Pendiente', 'Cancelado'), @('Aprobado', 'Despachado'))) {
        for ($i = 1; $i -le 10; $i++) { Invoke-CarreraEstado $carrera[0] $carrera[1] '' $i -Crear }
    }
    Reset-Pedidos 145050
    $id = New-PedidoEstado
    $surId = New-PedidoEstado 'Pendiente' $sur
    $digest = Get-PedidosDigest
    $bloqueo = Open-DistribuidorLock $norte
    $cancelacion = New-Object System.Threading.CancellationTokenSource
    $pendiente = $null
    try {
        $pendiente = Start-PatchEstado $id 'Aprobado' 'operador' 5081 $cancelacion.Token
        Wait-LockRequests $bloqueo 1
        [void](Invoke-Http "/api/pedidos/$surId/estado" 200 $tokens['distribuidor.sur'] -cuerpo @{ nuevoEstado = 'Cancelado' } -Patch -puerto 5082)
        Assert-QA (-not $pendiente.Task.IsCompleted) 'PATCH no demostro independencia entre distribuidores.'
        $digest = Get-PedidosDigest
        $cancelacion.Cancel()
        $cancelado = $false
        try { [void]$pendiente.Task.GetAwaiter().GetResult() } catch [System.OperationCanceledException] { $cancelado = $true }
        Assert-QA $cancelado 'PATCH esperando bloqueo no cancelo.'
        Start-Sleep -Milliseconds 250
        $bloqueo.Transaccion.Commit()
        Assert-QA ((Get-PedidosDigest) -eq $digest) 'PATCH cancelado dejo escrituras.'
        [void](Invoke-Http "/api/pedidos/$id/estado" 200 $tokens['operador'] -cuerpo @{ nuevoEstado = 'Aprobado' } -Patch -puerto 5082)
        Assert-CreditoSQL
        Write-Host 'PASS PATCH Sur independiente y cancelacion Norte: sin escrituras, rollback y lock liberado'
    } finally {
        $bloqueo.Transaccion.Dispose(); $bloqueo.Conexion.Dispose(); $cancelacion.Dispose()
        if ($pendiente) { $pendiente.Request.Dispose() }
    }
    Reset-Pedidos 145050
    $id = New-PedidoEstado
    $prueba = Start-ApiProceso $id -PruebaConcurrencia
    Assert-QA ($prueba.WaitForExit(120000)) 'Timeout prueba dos contextos EF.'
    Assert-QA ($prueba.ExitCode -eq 0) 'Prueba dos contextos EF fallo (logs privados en memoria).'
    $final = @(Get-QARows 'SELECT Estado, MotivoRechazo FROM Pedidos WHERE Id = @id' @{ '@id' = [Guid]$id })[0]
    Assert-QA ($final.Estado -eq 'Aprobado' -and $null -eq $final.MotivoRechazo) 'Token EF no preservo ganador SQL.'
    Assert-CreditoSQL
    Write-Host 'PASS dos AppDbContext SQL: segundo SaveChanges lanza DbUpdateConcurrencyException y ganador intacto'
}

function Test-Frontend {
    Assert-QA $Frontend 'Bloque 5 requiere -Frontend para no omitir navegador.'
    Reset-Pedidos 2000000
    Invoke-Sql $qaNombre 'UPDATE Productos SET PrecioPorGalon = 290.1 WHERE Id = @id' @{ '@id' = [Guid]'aaaaaaaa-0000-0000-0000-000000000001' }
    # Suficientes filas propias para paginar, estados coherentes y credito para crear/cancelar.
    for ($i = 0; $i -lt 15; $i++) {
        $estado = 'Cancelado'
        if ($i -lt 3) { $estado = 'Pendiente' }
        Invoke-Sql $qaNombre @'
INSERT INTO Pedidos (Id, DistribuidorId, FechaEntrega, FechaCreacion, FechaCambioEstado, Total, Estado, MotivoRechazo)
VALUES (@id, @distribuidor, @entrega, @creacion, @creacion, 145050, @estado, NULL);
INSERT INTO LineasPedido (PedidoId, ProductoId, NombreProducto, Galones, PrecioPorGalon, Subtotal)
SELECT @id, Id, Nombre, 500, PrecioPorGalon, 145050 FROM Productos WHERE Id = @producto;
'@ @{ '@id' = [Guid]::NewGuid(); '@distribuidor' = [Guid]$norte; '@estado' = $estado;
      '@entrega' = [DateTimeOffset]::UtcNow.AddDays(3); '@creacion' = [DateTimeOffset]::UtcNow.AddMinutes(-$i);
      '@producto' = [Guid]'aaaaaaaa-0000-0000-0000-000000000001' }

    }
    # Firmado por la clave temporal QA pero expirado: el navegador recibe 401 real.
    function Convert-Base64Url([byte[]]$bytes) { return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
    $header = Convert-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $payload = @{ sub = '99999999-9999-9999-9999-999999999999'; name = 'QA'; role = 'Operador';
        iss = 'Refidomsa.QA'; aud = 'Refidomsa.QA.Client';
        nbf = [DateTimeOffset]::UtcNow.AddHours(-2).ToUnixTimeSeconds(); exp = [DateTimeOffset]::UtcNow.AddHours(-1).ToUnixTimeSeconds() } | ConvertTo-Json -Compress
    $body = Convert-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload))
    $hmac = New-Object Security.Cryptography.HMACSHA256
    try {
        $hmac.Key = [Text.Encoding]::UTF8.GetBytes($claveQA)
        $signature = Convert-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$header.$body")))
    } finally { $hmac.Dispose() }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = 'node'
    $info.Arguments = 'node_modules/@playwright/test/cli.js test'
    $info.WorkingDirectory = Join-Path $raiz 'frontend'
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables['QA_API_URL'] = 'http://127.0.0.1:5081'
    $info.EnvironmentVariables['QA_BASE_URL'] = 'http://127.0.0.1:5173'
    $info.EnvironmentVariables['QA_DATABASE'] = $qaNombre
    $info.EnvironmentVariables['QA_PASSWORD'] = $passwordQA
    $info.EnvironmentVariables['QA_EXPIRED_TOKEN'] = "$header.$body.$signature"
    $proceso = New-Object Diagnostics.Process
    $proceso.StartInfo = $info
    Assert-QA ($proceso.Start()) 'No se pudo iniciar Playwright.'
    $entrada = [PSCustomObject]@{ Proceso = $proceso; Salida = $proceso.StandardOutput.ReadToEndAsync(); Error = $proceso.StandardError.ReadToEndAsync() }
    $procesos.Add($entrada)
    try {
        Assert-QA ($proceso.WaitForExit(180000)) 'Timeout navegador QA.'
        # Fallos de acciones del navegador pueden incluir argumentos: sanear incluso credenciales temporales.
        $salida = $entrada.Salida.GetAwaiter().GetResult() + $entrada.Error.GetAwaiter().GetResult()
        foreach ($secreto in @($passwordQA, $claveQA, $conexionQA, "$header.$body.$signature") + @($tokens.Values)) {
            if ($secreto) { $salida = $salida.Replace([string]$secreto, '[REDACTED]') }
        }
        Write-Host $salida
        Assert-QA ($proceso.ExitCode -eq 0) 'Playwright QA fallo.'
        $listeners = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object { $_.Port -eq 5173 })
        Assert-QA ($listeners.Count -eq 0) 'Listener frontend QA residual; no se detienen procesos ajenos.'
        Write-Host 'PASS Playwright navegador real contra API/SQL aislados'
    } finally {
        if (-not $proceso.HasExited) { & taskkill /PID $proceso.Id /T /F | Out-Null; $proceso.WaitForExit(10000) | Out-Null }
    }
}

function Test-Bloque2 {
    Reset-Pedidos 10000000
    $producto = 'aaaaaaaa-0000-0000-0000-000000000001'
    $fecha = [DateTimeOffset]::UtcNow.AddDays(3).ToOffset([TimeSpan]::FromHours(-4))
    while ($fecha.DayOfWeek -eq [DayOfWeek]::Sunday) { $fecha = $fecha.AddDays(1) }
    $script:pedidoValido = @{ fechaEntrega = $fecha.ToString('o'); lineas = @(@{ productoId = $producto; galones = 500 }) }
    $antes = [DateTimeOffset]::UtcNow
    $detalle = Invoke-Http '/api/pedidos' 201 $tokens['distribuidor.norte'] -cuerpo $pedidoValido
    Assert-PedidoSQL $detalle
    Assert-QA ($detalle.distribuidorId -eq $norte -and [decimal]$detalle.total -eq 145050 -and
        [DateTimeOffset]$detalle.fechaCreacion -ge $antes -and [DateTimeOffset]$detalle.fechaCreacion -le [DateTimeOffset]::UtcNow) 'No se uso identidad/catalogo/reloj servidor.'
    $snapshot = Get-PedidosDigest
    Invoke-Sql $qaNombre 'UPDATE Productos SET PrecioPorGalon = 999, Nombre = @nombre WHERE Id = @id' @{ '@id' = [Guid]$producto; '@nombre' = 'QA precio cambiado' }
    Assert-QA ((Get-PedidosDigest) -eq $snapshot) 'Cambio de catalogo altero snapshot del pedido.'
    Invoke-Sql $qaNombre 'UPDATE Productos SET PrecioPorGalon = 290.1, Nombre = @nombre WHERE Id = @id' @{ '@id' = [Guid]$producto; '@nombre' = $detalle.lineas[0].nombreProducto }
    $extra = @{ fechaEntrega = $fecha.ToString('o'); distribuidorId = $sur; total = 1; creditoDisponible = 1;
        rol = 'Operador'; fechaCreacion = '2000-01-01T00:00:00Z'; lineas = @(@{ productoId = $producto; galones = 500; precioPorGalon = 0.01; subtotal = 1 }) }
    $conExtra = Invoke-Http '/api/pedidos' 201 $tokens['distribuidor.norte'] -cuerpo $extra
    Assert-PedidoSQL $conExtra
    Assert-QA ($conExtra.distribuidorId -eq $norte -and $conExtra.total -eq 145050 -and [DateTimeOffset]$conExtra.fechaCreacion -ge $antes) 'Campos extra influyen en creacion.'
    $fraccion = Invoke-Http '/api/pedidos' 201 $tokens['distribuidor.norte'] -cuerpo @{ fechaEntrega = $fecha.ToString('o'); lineas = @(@{ productoId = $producto; galones = [decimal]500.000123 }) }
    Assert-PedidoSQL $fraccion
    Assert-QA ($fraccion.lineas[0].galones -eq [decimal]500.000123 -and $fraccion.total -eq [decimal]145050.04) 'Precision fraccional alterada.'
    foreach ($cantidad in @([decimal]499, [decimal]9001, [decimal]500.1234567, [decimal]::MaxValue, [decimal]::MinValue)) {
        Assert-NoEscritura @{ fechaEntrega = $fecha.ToString('o'); lineas = @(@{ productoId = $producto; galones = $cantidad }) }
    }
    foreach ($id in @([Guid]::NewGuid().ToString(), [Guid]::Empty.ToString(), 'no-es-guid')) {
        Assert-NoEscritura @{ fechaEntrega = $fecha.ToString('o'); lineas = @(@{ productoId = $id; galones = 500 }) }
    }
    foreach ($lineas in @(@(), @(@{ productoId = $producto; galones = 500 }, @{ productoId = $producto; galones = 500 }),
        @(@{ productoId = $producto; galones = 500 }, @{ productoId = $producto; galones = 500 }, @{ productoId = $producto; galones = 500 }, @{ productoId = $producto; galones = 500 }, @{ productoId = $producto; galones = 500 }))) {
        Assert-NoEscritura @{ fechaEntrega = $fecha.ToString('o'); lineas = @($lineas) }
    }
    $domingo = $fecha
    while ($domingo.DayOfWeek -ne [DayOfWeek]::Sunday) { $domingo = $domingo.AddDays(1) }
    foreach ($entrega in @($domingo.ToString('o'), [DateTimeOffset]::UtcNow.AddHours(23).ToString('o'), 'no-es-fecha')) {
        Assert-NoEscritura @{ fechaEntrega = $entrega; lineas = @(@{ productoId = $producto; galones = 500 }) }
    }
    foreach ($invalido in @(@{}, @{ fechaEntrega = $null; lineas = $null },
        @{ fechaEntrega = $fecha.ToString('o'); lineas = @($null) },
        @{ fechaEntrega = $fecha.ToString('o'); lineas = @(@{ productoId = $producto }) })) {
        Assert-NoEscritura $invalido
    }
    foreach ($json in @('null', '{', '{"fechaEntrega":"2026-10-08T12:00:00Z","lineas":[{"productoId":"aaaaaaaa-0000-0000-0000-000000000001","galones":1e100}]}',
        '{"fechaEntrega":"2026-10-08T12:00:00Z","lineas":[{"productoId":"aaaaaaaa-0000-0000-0000-000000000001","galones":500.00000000000000000000000000001}]}')) {
        Assert-NoEscritura $json -RawJson
    }
    Assert-NoEscritura @{ fechaEntrega = $fecha.ToString('o'); lineas = @(
        @{ productoId = $producto; galones = 5000 },
        @{ productoId = 'aaaaaaaa-0000-0000-0000-000000000002'; galones = 5000 }) }
    Assert-NoEscritura $pedidoValido 403 $tokens['operador']
    Assert-NoEscritura $pedidoValido 401 ''
    Assert-NoEscritura $pedidoValido 401 'token-invalido'
    foreach ($cantidadValida in @(9000, 500)) {
        $lineasValidas = @(@{ productoId = $producto; galones = $cantidadValida })
        if ($cantidadValida -eq 500) {
            $lineasValidas = @($catalogoSQL | ForEach-Object { @{ productoId = $_.id; galones = 500 } })
        }
        $limiteValido = Invoke-Http '/api/pedidos' 201 $tokens['distribuidor.norte'] -cuerpo @{ fechaEntrega = $fecha.ToString('o'); lineas = $lineasValidas }
        Assert-PedidoSQL $limiteValido
    }
    Reset-Pedidos 145050
    $exacto = Invoke-Http '/api/pedidos' 201 $tokens['distribuidor.norte'] -cuerpo $pedidoValido
    Assert-PedidoSQL $exacto
    Assert-Credito (Invoke-Http "/api/distribuidores/$norte/credito" 200 $tokens['distribuidor.norte']) $norte 145050 145050 0
    Assert-NoEscritura $pedidoValido 409
    Reset-Pedidos 145049.99
    Assert-NoEscritura $pedidoValido 409
    foreach ($previo in @($false, $true)) {
        for ($iteracion = 1; $iteracion -le 10; $iteracion++) { Invoke-CarreraCredito $previo $iteracion }
    }
    Reset-Pedidos
    $bloqueo = Open-DistribuidorLock $norte
    $pendiente = $null
    try {
        $pendiente = Start-PostPedido $pedidoValido $tokens['distribuidor.norte'] 5081
        Wait-LockRequests $bloqueo 1
        $independiente = Invoke-Http '/api/pedidos' 201 $tokens['distribuidor.sur'] -cuerpo $pedidoValido -puerto 5082
        Assert-PedidoSQL $independiente
        Assert-QA (-not $pendiente.Task.IsCompleted) 'No se demostro independencia de distribuidores.'
        $liberacion = [DateTimeOffset]::UtcNow
        $bloqueo.Transaccion.Commit()
        $respuestaPendiente = $pendiente.Task.GetAwaiter().GetResult()
        $detallePendiente = $respuestaPendiente.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        Assert-QA ([DateTimeOffset]$detallePendiente.fechaCreacion -ge $liberacion) 'Hora de creacion obtenida antes de esperar lock.'
        Assert-QA ((Complete-PostPedido $pendiente) -eq 201) 'Norte no completo tras liberar lock.'
        Write-Host 'PASS Sur completa mientras Norte espera: bloqueo por distribuidor, no global'
    } finally {
        $bloqueo.Transaccion.Dispose(); $bloqueo.Conexion.Dispose()
        if ($pendiente) { $pendiente.Request.Dispose() }
    }
    Reset-Pedidos
    $bloqueo = Open-DistribuidorLock $norte
    $cancelacion = New-Object System.Threading.CancellationTokenSource
    $pendiente = $null
    try {
        $pendiente = Start-PostPedido $pedidoValido $tokens['distribuidor.norte'] 5081 $cancelacion.Token
        Wait-LockRequests $bloqueo 1
        $cancelacion.Cancel()
        $cancelado = $false
        try { [void]$pendiente.Task.GetAwaiter().GetResult() } catch [System.OperationCanceledException] { $cancelado = $true }
        Assert-QA $cancelado 'Request no cancelo.'
        Start-Sleep -Milliseconds 250
        $bloqueo.Transaccion.Commit()
        # Esperar una nueva creacion verifica que la cancelada libero su transaccion/bloqueo.
        $despues = Invoke-Http '/api/pedidos' 201 $tokens['distribuidor.norte'] -cuerpo $pedidoValido -puerto 5082
        Assert-PedidoSQL $despues
        $filas = @(Get-QARows 'SELECT COUNT(*) AS cantidad FROM Pedidos')
        Assert-QA ($filas[0].cantidad -eq 1) 'Cancelacion dejo escrituras persistidas.'
        Write-Host 'PASS cancelacion esperando lock: rollback sin escrituras y bloqueo liberado'
    } finally {
        $bloqueo.Transaccion.Dispose(); $bloqueo.Conexion.Dispose(); $cancelacion.Dispose()
        if ($pendiente) { $pendiente.Request.Dispose() }
    }
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
        # TIME_WAIT de una ejecucion previa no es un listener ocupado.
        $ocupados = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
            Where-Object { $_.Port -eq $puerto })
        Assert-QA ($ocupados.Count -eq 0) "Puerto QA $puerto ocupado; no se detienen procesos ajenos."
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
    if ($Bloque -ge 2) { Test-Bloque2 }
    if ($Bloque -ge 3) { Test-Bloque3 }
    if ($Bloque -ge 4) { Test-Bloque4 }
    if ($Bloque -eq 5) { Test-Frontend }
    Write-Host "PASS bloque $Bloque SQL/HTTP real"
} catch {
    # No propagar errores tecnicos de conexion/comando ni logs hijos que puedan contener secretos.
    throw 'QA de pedidos fallo. Revisa el ultimo caso PASS; detalles tecnicos sensibles omitidos.'
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
