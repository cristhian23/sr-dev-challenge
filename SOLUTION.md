# Solucion: gestion de pedidos de combustible

Verificacion final independiente del bloque 2 (2026-10-06): Release 0 advertencias/errores; 182 unitarias aprobadas, 0 fallos/omisiones; `qa-pedidos.ps1 -Bloque 2` exit0 con20carreras201/409, precision JSON extrema400 sin escrituras, reloj posterior al lock, cancelacion mientras espera y cleanup/digests PASS. Review-work de las cinco areas PASS dentro del alcance de creacion. Cambios concurrentes de bloques posteriores no forman parte de esta evidencia.

## Estado actual

Backend .NET 10 con modelos encapsulados, reglas puras, EF Core 10.0.12, SQL Server 2022 en Docker, migracion inicial, datos semilla y autenticacion minima con JwtBearer 10.0.12. Hay 242 pruebas unitarias, diagnostico de persistencia y QA aislado SQL/HTTP reproducible para productos, credito, creacion transaccional, consultas y cambios de estado autorizados de pedidos.

Los tres usuarios semilla pueden iniciar sesion y consultar productos. Cada distribuidor consulta solo su credito, crea y consulta pedidos propios y cancela solo propios Pendiente; el operador consulta credito y pedidos de cualquiera, aprueba/rechaza Pendiente y despacha Aprobado, pero no crea ni cancela. Todavia NO hay frontend. No es una entrega completa del reto.

## Requisitos locales

- Windows con PowerShell 5.1, SDK .NET 10.0.101 (o parche compatible con global.json).
- Docker Desktop iniciado en modo de contenedores Linux. SQL Server necesita recursos suficientes (al menos 2 GB de RAM disponibles para el motor).
- Puerto IPv4 14333 disponible para SQL Server y 5080 para la API.
- Edicion Developer, exclusivamente para desarrollo/pruebas; Compose acepta la EULA del contenedor.

## Preparar y ejecutar

Desde la raiz del repositorio, en una copia nueva:

```powershell
Copy-Item .env.example .env
```

Revisa los valores de .env. Son credenciales desechables de prueba, no secretos de produccion. .env esta excluido de Git; el script admite entradas KEY=valor sin comillas. Cambia SQLSERVER_PASSWORD y SEED_PASSWORD si lo deseas ANTES de inicializar por primera vez. JWT_KEY en .env.example es una clave publica de demostracion, nunca debe reutilizarse en produccion.

```powershell
.\scripts\local.ps1 -Accion Inicializar
.\scripts\local.ps1 -Accion Ejecutar
```

Inicializar levanta SQL Server, espera a que responda, aplica migraciones e inserta los datos semilla que faltan. Puede repetirse sin duplicarlos ni sobrescribir precios o hashes existentes. Ejecutar levanta/verifica SQL Server y arranca la API en http://localhost:5080; no aplica migraciones implicitamente. En este equipo ya se inicializo la base.

La .env existente de este equipo NO se actualizo y no contiene JWT_KEY. No la reemplaces: agrega manualmente JWT_KEY con al menos 32 bytes UTF-8, o usa una variable temporal. Ejemplo publico solo para desarrollo, no es la clave usada en QA:

```powershell
$claveAnterior = $env:Jwt__Clave
try {
    $env:Jwt__Clave = 'Refidomsa_JWT_PUBLICA_SOLO_DESARROLLO_2026!'
    .\scripts\local.ps1 -Accion Ejecutar
} finally {
    $env:Jwt__Clave = $claveAnterior
}
```

Si la politica local impide ejecutar scripts, puedes usar `powershell -ExecutionPolicy Bypass -File .\scripts\local.ps1 -Accion Inicializar` para esa ejecucion; no es necesario cambiar la politica global.

El script configura temporalmente ConnectionStrings__Refidomsa, DatabaseSeed__Password y ASPNETCORE_ENVIRONMENT=Development, y restaura los valores previos en finally. Solo Ejecutar necesita JWT: conserva Jwt__Clave, Jwt__Emisor y Jwt__Audiencia preexistentes; si faltan, toma JWT_KEY de .env y usa Refidomsa.Api/Refidomsa.Client. Tambien restaura esas variables al terminar. Inicializar/Verificar no requieren JWT y conservan sus comandos DB.

Para ejecutar dotnet run directamente, configura la conexion y Jwt__Clave, Jwt__Emisor=Refidomsa.Api y Jwt__Audiencia=Refidomsa.Client en el entorno. La API no lee .env por si sola: .env es utilizado por Compose y el script. El arranque HTTP rechaza clave ausente/corta o emisor/audiencia blancos antes de abrir el puerto.

- `/health`: vida del proceso; no consulta SQL.
- `/health/ready`: conectividad a la base SQL; 200 si conecta, 503 si no. No sustituye la comprobacion de migraciones.

## Usuarios semilla

| Nombre de usuario | Rol | Distribuidor |
| --- | --- | --- |
| distribuidor.norte | Distribuidor | Norte |
| distribuidor.sur | Distribuidor | Sur |
| operador | Operador | Ninguno |

Con .env.example la contrasena de prueba para los tres es `PruebaRefidomsa_2026!`. Si cambias SEED_PASSWORD antes de inicializar, se utiliza tu valor. Se almacena un hash salado con PasswordHasher de ASP.NET Core, nunca el texto plano. Repetir Inicializar NO cambia contrasenas: modificar .env despues no actualiza hashes ya guardados.

Los RNC, distribuidores y precios son ficticios. Los productos son Gasolina Premium, Gasolina Regular, Gasoil Optimo (con acento en la base) y Gasoil Regular. No representan precios oficiales actuales.

## Contrato de autenticacion

`POST /api/auth/login` es anonimo y recibe JSON con solo `nombreUsuario` y `password`, ambos obligatorios y no blancos. El nombre se normaliza con Trim/ToLowerInvariant; el password se verifica exactamente como llega, sin recortarlo ni cambiarlo. Rol y distribuidor provienen de SQL, no del cliente.

- 200: `token`, `expiraEnUtc` y `usuario` con `id`, `nombre`, `nombreUsuario`, `rol` y `distribuidorId` (null para Operador). Nunca incluye password/hash.
- 401 ProblemDetails: respuesta generica identica para usuario desconocido o password incorrecto. El texto del contrato indica credenciales invalidas, sin distinguir la causa.
- 400 ValidationProblemDetails: campos ausentes/blancos/nulos, cuerpo nulo o JSON malformado.
- `Cache-Control: no-store` en respuestas de login, incluidos los errores de entrada y credenciales comprobados.

JWT firmado con HS256, no cifrado: su contenido es legible. Vigencia fija de 60 minutos, tolerancia de reloj de 30 segundos, emisor Refidomsa.Api y audiencia Refidomsa.Client. Incluye `sub` (ID del usuario), `name`, `role`, `distribuidorId` solo para Distribuidor, y `iss`/`aud`/`iat`/`nbf`/`exp`. No contiene password/hash ni credito. Productos y credito requieren `Authorization: Bearer <token>`.

HTTP loopback y la clave publica son solo para desarrollo. Produccion necesita HTTPS, una clave de firma fuerte y protegida y acceso SQL de minimo privilegio. No hay revocacion anticipada, refresh tokens, registro, recuperacion, logout servidor ni /me.

## Pruebas y comprobacion real

```powershell
dotnet build Refidomsa.slnx --configuration Release
dotnet test Refidomsa.slnx --configuration Release --no-build
.\scripts\local.ps1 -Accion Verificar
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\qa-pedidos.ps1 -Bloque 1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\qa-pedidos.ps1 -Bloque 2
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\qa-pedidos.ps1 -Bloque 3
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\qa-pedidos.ps1 -Bloque 4
```

Las 242 pruebas no necesitan una base de datos ni un servidor HTTP. Cubren reglas, mapeo/precision EF, DTOs, hashes, emision/validacion criptografica JWT, identidad, saldo negativo, traduccion de errores de negocio, formato de filtros/paginacion, solicitud de cambio de estado y metadata de su token de concurrencia. Comprueban que credito/filtro de pedidos ajeno retorna antes de consultar y que Operador no abre la transaccion de creacion. GalonesJsonConverter comprueba el token numerico antes de convertir a decimal para rechazar precision que CLR podria redondear; admite exponentes y ceros finales sin perdida. La configuracion de validacion y la comprobacion de claims son compartidas con produccion.

Verificar es un diagnostico de integracion local, separado de las unitarias. Comprueba semilla, hashes, guardado/lectura de un pedido con dos lineas, cantidades decimales, fechas, total, precio historico tras cambiar el catalogo y cambio de estado. Revierte su transaccion y comprueba que no quedan pedido ni precio alterado. No expone un endpoint y solo se admite en Development.

Resultados historicos de autenticacion del 2026-10-06: build Release repetido sin errores/advertencias y 144 pruebas aprobadas, 0 fallos/omisiones. QA SQL/HTTP aprobado: Verificar sin JWT; login 200 de los tres usuarios y nombre con espacios/mayusculas; password con espacios no recortado; 401 genericos iguales; entradas invalidas 400; no-store y health checks anonimos 200. Clave ausente/corta y emisor/audiencia blancos causaron salida no cero sin listener. Datos, hashes, precios e historial EF quedaron sin cambios, con conteos 3/4/3/0. La API de QA se detuvo y SQL quedo saludable; .env y variables de proceso se conservaron. Evidencia: `.sisyphus/evidence/task-4-qa-20261006-182014.txt`.

En aquel bloque la validacion criptografica se comprobo en unitarias; el QA HTTP decodifico claims, no verifico firmas. Todavia no habia recursos protegidos para probar challenge/autorizacion en vivo. Inicializar tambien se ejecuto sin JWT. Los resultados de reinicio SQL y readiness 503 son historicos. Las cinco revisiones finales independientes de autenticacion devolvieron PASS; resumen en `.sisyphus/evidence/revision-autenticacion.md`. Esto no certifica produccion ni sustituye el okay y la comprension del candidato. Frontend no implementado ni probado.

## Productos y credito (bloque 1)

El README exige operaciones, no URLs exactas; estos son los contratos minimos adoptados:

- `GET /api/productos`: ambos roles autenticados; array `{id,nombre,precioPorGalon}`, orden SQL nombre/id.
- `GET /api/distribuidores/{id}/credito`: `{distribuidorId,limiteCredito,creditoConsumido,creditoDisponible}`. Distribuidor solo propio; Operador cualquiera. Ajeno/inexistente devuelve el mismo 404; Guid invalido/vacio 400 ValidationProblemDetails.
- Credito consumido: suma SQL de Pendiente/Aprobado del propietario, agregado vacio = 0. Disponible utiliza la formula pura `ReglasCredito.CalcularDisponible(limite, consumido)`; no carga pedidos ni lineas y no oculta saldo negativo. Esta lectura informativa NO reserva credito ni protege creaciones concurrentes.
- JWT ausente, malformado o firma alterada: 401 ProblemDetails y `WWW-Authenticate: Bearer`. Errores de negocio: middleware central con `codigo`, 400 para reglas, 403 sin_permiso, 409 credito_insuficiente/transicion_invalida. Fallos tecnicos siguen hacia UseExceptionHandler con 500 generico; no se convierten en 400. En este bloque no hay una accion HTTP que produzca 403/409: su traduccion esta probada unitariamente.

`qa-pedidos.ps1 -Bloque 1|2|3|4` requiere SQL healthy existente y puertos QA 5081/5082/5173 libres. Crea exclusivamente `Refidomsa_QA_<GuidN>`, inicializa/migra con el comando Development existente y arranca dos APIs Release propias. Password/clave temporales solo en entornos hijos; no usa el password semilla de produccion ni imprime secretos. Finalmente cierra procesos propios, elimina solo su DB y compara digests en memoria de Refidomsa (incluye hashes, catalogo, pedidos, lineas e historial EF) y .env. No modifica Compose, volumen ni variables del padre. No ejecutar dos harnesses simultaneamente: comparten puertos QA, aunque sus bases sean distintas.

Ejecucion real 2026-10-06: diagnosticos C# sin errores, Release 0 errores/advertencias, 159 unitarias aprobadas sin fallos/omisiones y harness exit0. HTTP: cuatro productos para los tres usuarios, credito propio/operador, consumo mixto exacto 290100 y saldo -90099.75, agregado vacio, 404 ajeno/inexistente, 401 ausente/invalido/firma alterada, 400 IDs invalidos/vacios y segunda API. Limpieza QA y digests originales identicos confirmados.

## Crear pedidos (bloque 2)

`POST /api/pedidos` requiere Bearer de Distribuidor y recibe solo `{fechaEntrega,lineas:[{productoId,galones}]}`. Devuelve 201 con Location `/api/pedidos/{id}` y detalle: id, distribuidorId, nombreDistribuidor, fechaEntrega, fechaCreacion, fechaCambioEstado, total, estado literal, motivoRechazo y lineas con productoId/nombreProducto/galones/precioPorGalon/subtotal. Location identifica el recurso consultable mediante GET detalle.

El flujo es DTO de formato -> controller/identidad autenticada -> PedidosService -> transaccion ReadCommitted -> primera lectura parametrizada de la fila Distribuidores por PK con UPDLOCK/HOLDLOCK -> catalogo sin tracking -> SUM Pendiente/Aprobado -> hora UTC actual -> Pedido.Crear/Rules -> SaveChangesAsync -> commit -> DTO. El bloqueo por distribuidor se conserva hasta commit/rollback y funciona entre instancias distintas, incluso sin pedidos previos. La hora se obtiene despues de esperar; no se carga una entidad tracked obsoleta. Fallos y cancelacion revierten la transaccion, usando un token no cancelado para la limpieza. Futuras mutaciones deben seguir el mismo protocolo.

Formato invalido/precision mayor de seis decimales/extremos: 400; reglas de lineas/producto/fecha: 400 con codigo; credito insuficiente: 409 credito_insuficiente; Operador: 403 sin_permiso antes del caso de uso. Campos extra de distribuidor, rol, credito, precios, subtotales, total y creacion se ignoran y nunca son autoridad. Las restricciones de negocio siguen solamente en Rules; DTOs comprueban presencia, identificadores y representacion decimal(18,6).

Evidencia final directa 2026-10-06: Release sin errores/advertencias, 182 pruebas aprobadas, diagnosticos individuales C# sin errores y QA acumulativo SQL/HTTP exit0 con 201/Location/detalle, snapshots, galones fraccionarios, limites 4 lineas/9000, 23 rechazos400 sin escrituras (incluido token numerico de precision extrema),401/403 y credito exacto/exceso409. Veinte carreras sincronizadas mediante lock SQL externo (10 sin pedidos previos, 10 con consumo previo), dos APIs, dieron exactamente un201/un409 y conteos/importes SQL exactos. Sur completo mientras Norte seguia bloqueado; fecha de creacion posterior a liberar lock. Cancelacion mientras esperaba lock no dejo escrituras y una nueva creacion demostro el bloqueo liberado. Refidomsa/.env identicos y DB/procesos QA propios eliminados. QA independiente ejecuto tambien build/173tests/harness ampliado PASS antes del ajuste final de precision; la repeticion final directa con182tests volvio a pasar.

## Consultar pedidos (bloque 3)

Verificacion directa 2026-10-06: diagnosticos de los seis archivos C# nuevos/modificados sin errores, Release sin errores/advertencias y 219 unitarias aprobadas (0 fallos/omisiones). `qa-pedidos.ps1 -Bloque 3` acumulativo exit0: bloques1/2 conservados, veinte carreras201/409, independencia y cancelacion, fixture de diez pedidos/ambos distribuidores/cinco estados, fechas con empates, orden SQL/totales/campos exactos, filtros individuales/combinados y limites inclusivo/exclusivo con offsets equivalentes, paginas sin duplicados/vacias, parametros invalidos400, aislamiento404, JWT401 y detalle snapshot estable despues de cambiar catalogo QA. Cleanup de DB/procesos propios y digests Refidomsa/.env identicos confirmados. Un primer intento detecto un fallo del harness al contar resultado SQL vacio como una fila null; corregido y repetido completamente con PASS.

- `GET /api/pedidos?pagina=1&tamanoPagina=10&estado=&distribuidorId=&desde=&hasta=`: los filtros opcionales se omiten cuando no se usan. Respuesta `{items,pagina,tamanoPagina,totalRegistros}`. Pagina >=1, tamano 1-100; desplazamiento que excede Int32 produce 400 antes de consultar. Pagina valida fuera del total devuelve 200 con items vacios.
- Orden fijo SQL `FechaCreacion DESC, Id DESC`. Resumen: id, distribuidorId, nombreDistribuidor, fechaEntrega, fechaCreacion, fechaCambioEstado, total y estado literal. No carga la coleccion de lineas para listar.
- Rango sobre FechaCreacion: desde inclusivo, hasta exclusivo, ISO 8601 con offset explicito (`Z` o `+/-HH:mm`); limites iguales/invertidos producen 400. Codificar `+` como `%2B` en query strings. Estado desconocido/numerico, identificador invalido/vacio y paginacion invalida producen 400 ValidationProblemDetails.
- Scope de identidad antes de filtros, Count, Skip y Take. Distribuidor solo propio; filtro de otro ID devuelve el mismo 404 que un filtro inaccesible/inexistente. Operador permite cualquier ID, incluso inexistente (200 lista vacia).
- `GET /api/pedidos/{id}`: detalle con snapshots owned y nombre del distribuidor; ajeno e inexistente devuelven el mismo 404, Guid invalido/vacio 400. Ambas consultas exigen JWT y usan proyecciones sin tracking y cancelacion, sin exponer entidades, usuarios ni hashes.

## Cambiar estado (bloque 4)

`PATCH /api/pedidos/{id}/estado` exige Bearer y recibe `{nuevoEstado,motivo?}`; devuelve 200 con detalle actualizado. NuevoEstado es un nombre exacto del enum (no numero, string numerico, combinacion ni nombre desconocido). Cuerpo vacio/nulo/malformado, ID invalido/vacio y formato invalido producen 400. Motivo solo es obligatorio y no puede estar en blanco para Rechazado. Cualquier motivo suministrado admite hasta 1000 caracteres, tambien para otros destinos; el modelo recorta espacios al guardar el rechazo e ignora el motivo para otros destinos.

Distribuidor solo cancela propios Pendiente. Operador solo aprueba/rechaza Pendiente y despacha Aprobado. Un recurso ajeno/inexistente produce el mismo404 antes de evaluar la accion; accion de rol prohibida sobre pedido visible produce403 sin_permiso; transicion invalida409 transicion_invalida. La validacion de formato HTTP precede al caso de uso. Ningun campo de entidad se asigna fuera de Pedido.CambiarEstado.

Flujo: resolver propietario visible sin tracking -> transaccion ReadCommitted -> bloquear primero Distribuidores por PK con el mismo UPDLOCK/HOLDLOCK del POST -> cargar pedido fresco tracked con lineas owned -> revalidar scope/permiso/transicion -> CambiarEstado con reloj UTC posterior al lock -> SaveChanges -> commit -> detalle. Aprobado sigue consumiendo; Rechazado/Cancelado/Despachado liberan credito solo al commit. Fallos/cancelacion revierten con CancellationToken.None. Estado es token EF aplicativo sobre su columna existente: el UPDATE compara el estado original; DbUpdateConcurrencyException revierte y se traduce a409 conflicto_concurrencia sin retry. Dos mutaciones que respetan el bloqueo se serializan y la segunda transicion incompatible produce409 transicion_invalida. No se promete proteccion frente a SQL arbitrario que ignore el protocolo/token.

Verificacion directa 2026-10-06: Release0warnings/errors,242unitarias0fail/skip, diagnosticos C# sin errores y `dotnet ef migrations has-pending-model-changes --project src/Refidomsa.Api -- --verify-db` sin cambios pendientes; no se agrego migracion ni se edito Inicial. QA acumulativo bloque4 exit0: matriz150 combinaciones (3 identidades x2 propietarios x5 origenes x5 destinos),400/401/403/404/409 sin escrituras, fechas/motivo/snapshots/credito SQL;40 carreras de estados (10 por aprobar/aprobar, aprobar/rechazar, cancelar/aprobar, despachar/despachar) exactamente un200/un409;20 carreras crear/cancelar o crear/despachar con limite para un pedido y resultados seriales sin sobreconsumo. Los20 races de creacion previos y bloques1-3 tambien pasaron; PATCH Sur independiente y cancelacion Norte mientras espera bloqueo, sin escrituras y lock liberado.

El harness ejecuta `scripts/QaConcurrencia.cs` como aplicacion de archivo .NET10 con referencia al proyecto API, sin proyecto/capa/paquete nuevo y AOT desactivado porque EF construye el modelo dinamicamente. Solo acepta catalogo QA con GUID. Dos AppDbContext leen el mismo Pendiente; el primero aprueba y el segundo intento de rechazo lanza DbUpdateConcurrencyException real contra SQL, preservando ganador/fecha/motivo/total. Dos primeros intentos completos fallaron solo en ese auxiliar por el default AOT del SDK; se corrigio y se repitio acumulativo con PASS. En todos los intentos cleanup y digests de Refidomsa/.env fueron identicos. Esto no sustituye revision independiente del parent ni aprobacion humana.

Review-work bloque4: cinco areas finales PASS. Cumplimiento/calidad/seguridad/contexto mediante inspeccion; QA independiente ejecuto Release0warnings/errors,242tests y harness acumulativoexit0. Seguridad encontro propagacion de errores tecnicos SQL sin saneamiento en el harness: catch global ahora informa fallo fijo sin excepcion original ni logs hijos; prueba negativa de puerto ocupado exitnozero y mensaje fijoPASS, re-review seguridadPASS y nueva ejecucion directa completa bloque4exit0. LSP C# individual sin errores; LSP .ps1 no disponible, parser5.1 y ejecucion realPASS. La prueba de dos contextos ejecuta el token EF, y las unitarias traducen su codigo HTTP; el catch de concurrencia del servicio fue verificado por inspeccion, no forzado mediante HTTP.

## Persistencia y migraciones

La base es `Refidomsa`, en `tcp:127.0.0.1,14333`. El volumen `refidomsa_sqlserver-data` conserva los datos. El acceso con sa y TrustServerCertificate se utiliza solo para desarrollo local, no es una configuracion de produccion.

La herramienta EF esta fijada en el manifiesto local, no requiere instalacion global:

```powershell
dotnet tool restore
# Con ConnectionStrings__Refidomsa configurada:
dotnet ef migrations add NombreDelCambio --project src/Refidomsa.Api --output-dir Data/Migrations
```

Despues, Inicializar aplica la migracion. Los cambios futuros deben generar una nueva migracion; no editar una migracion ya aplicada.

Para detener el contenedor sin borrar datos:

```powershell
.\scripts\local.ps1 -Accion Detener
```

No ejecutar `docker compose down -v` salvo que quieras eliminar deliberadamente la base local y sus datos. Cambiar SQLSERVER_PASSWORD en .env no actualiza automaticamente la contrasena de una instancia existente en el volumen.

## Organizacion y decisiones

- Models y Models/Enums: datos y comportamiento protegido.
- Rules: condiciones puras, sin HTTP ni SQL.
- Data/AppDbContext, Configurations, Migrations y DatosSemilla: persistencia.
- Controllers/AutenticacionController, DTOs/Autenticacion y Services/AutenticacionService: contrato HTTP y login sobre SQL/hashes existentes.
- Controllers/ProductosController y DistribuidoresController, DTOs/Productos y Credito, Services/ProductosService y CreditoService: consultas protegidas proyectadas sin tracking.
- Controllers/PedidosController, DTOs/Pedidos y Services/PedidosService: POST autorizado, formato/precision exacta JSON, creacion transaccional con snapshots, GETs de lista paginada/detalle con scope por identidad y PATCH de estados con bloqueo/token de concurrencia.
- Middlewares/ErroresMiddleware: traduccion exclusiva de excepciones de negocio; UseExceptionHandler conserva los fallos inesperados.
- Security/ConfiguracionJwt, GeneradorToken y LectorUsuarioActual: configuracion, firma/validacion e identidad autenticada para UsuarioActual.
- Exceptions: errores de negocio con codigo estable.
- tests/Refidomsa.UnitTests: pruebas sin DB.
- scripts/local.ps1 y compose.yaml: entorno local.
- AGENTS.md: pautas del usuario; docs/DECISIONES.md y docs/USO_DE_IA.md: decisiones y uso real de IA.

Precios y galones usan decimal(18,6); importes y credito decimal(28,2). Se rechazan valores fuera de rango/escala antes de guardar en lugar de permitir redondeo SQL silencioso. El subtotal se redondea a dos decimales AwayFromZero, y el total suma subtotales. Fechas DateTimeOffset en UTC; domingo en hora dominicana UTC-04:00.

## Pendientes deliberados

1. Frontend React y Compose de API/frontend (actualmente solo SQL esta contenerizado).
2. Suite de navegador/e2e y CI segun tiempo; SQL/HTTP backend ya dispone de harness aislado acumulativo.

La creacion protege el credito mediante el pedido Pendiente guardado, sin balance mutable ni reserva adicional. No incluimos registro de usuarios, recuperacion de contrasenas ni IA dentro del producto en el alcance inicial.
