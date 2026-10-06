# Solucion: gestion de pedidos de combustible

## Estado actual

Backend .NET 10 con modelos encapsulados, reglas puras, EF Core 10.0.12, SQL Server 2022 en Docker, migracion inicial, datos semilla y autenticacion minima con JwtBearer 10.0.12. Hay 144 pruebas unitarias, un diagnostico reproducible contra la base real y QA de login por HTTP.

Los tres usuarios semilla pueden iniciar sesion y recibir un JWT. Todavia NO hay endpoints de pedidos/productos/credito, autorizacion por recurso ni frontend. No es una entrega completa del reto.

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

JWT firmado con HS256, no cifrado: su contenido es legible. Vigencia fija de 60 minutos, tolerancia de reloj de 30 segundos, emisor Refidomsa.Api y audiencia Refidomsa.Client. Incluye `sub` (ID del usuario), `name`, `role`, `distribuidorId` solo para Distribuidor, y `iss`/`aud`/`iat`/`nbf`/`exp`. No contiene password/hash ni credito. Las futuras solicitudes protegidas usaran `Authorization: Bearer <token>`; no hay todavia recursos protegidos.

HTTP loopback y la clave publica son solo para desarrollo. Produccion necesita HTTPS, una clave de firma fuerte y protegida y acceso SQL de minimo privilegio. No hay revocacion anticipada, refresh tokens, registro, recuperacion, logout servidor ni /me.

## Pruebas y comprobacion real

```powershell
dotnet build Refidomsa.slnx --configuration Release
dotnet test Refidomsa.slnx --configuration Release --no-build
.\scripts\local.ps1 -Accion Verificar
```

Las 144 pruebas no necesitan una base de datos ni un servidor HTTP. Cubren reglas, mapeo/precision EF, DTOs, hashes, emision/validacion criptografica JWT e identidad. La configuracion de validacion y la comprobacion de claims son compartidas con produccion.

Verificar es un diagnostico de integracion local, separado de las unitarias. Comprueba semilla, hashes, guardado/lectura de un pedido con dos lineas, cantidades decimales, fechas, total, precio historico tras cambiar el catalogo y cambio de estado. Revierte su transaccion y comprueba que no quedan pedido ni precio alterado. No expone un endpoint y solo se admite en Development.

Resultados actuales del 2026-10-06: build Release repetido sin errores/advertencias y 144 pruebas aprobadas, 0 fallos/omisiones. QA SQL/HTTP aprobado: Verificar sin JWT; login 200 de los tres usuarios y nombre con espacios/mayusculas; password con espacios no recortado; 401 genericos iguales; entradas invalidas 400; no-store y health checks anonimos 200. Clave ausente/corta y emisor/audiencia blancos causaron salida no cero sin listener. Datos, hashes, precios e historial EF quedaron sin cambios, con conteos 3/4/3/0. La API de QA se detuvo y SQL quedo saludable; .env y variables de proceso se conservaron. Evidencia: `.sisyphus/evidence/task-4-qa-20261006-182014.txt`.

La validacion criptografica se comprobo en unitarias; el QA HTTP decodifico claims, no verifico firmas. No se probo challenge del middleware ni 403/autorizacion por recurso en vivo, pues no existe un recurso protegido. Inicializar tambien se ejecuto sin JWT y termino correctamente en este bloque. Los resultados anteriores de reinicio SQL y readiness 503 son historicos. Las cinco revisiones finales independientes devolvieron PASS (cumplimiento, calidad, seguridad, QA ejecutado y contexto/documentacion); resumen en `.sisyphus/evidence/revision-autenticacion.md`. Esto no certifica produccion ni sustituye el okay y la comprension del candidato. Frontend no implementado ni probado.

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
- Security/ConfiguracionJwt, GeneradorToken y LectorUsuarioActual: configuracion, firma/validacion e identidad autenticada para UsuarioActual.
- Exceptions: errores de negocio con codigo estable.
- tests/Refidomsa.UnitTests: pruebas sin DB.
- scripts/local.ps1 y compose.yaml: entorno local.
- AGENTS.md: pautas del usuario; docs/DECISIONES.md y docs/USO_DE_IA.md: decisiones y uso real de IA.

Precios y galones usan decimal(18,6); importes y credito decimal(28,2). Se rechazan valores fuera de rango/escala antes de guardar en lugar de permitir redondeo SQL silencioso. El subtotal se redondea a dos decimales AwayFromZero, y el total suma subtotales. Fechas DateTimeOffset en UTC; domingo en hora dominicana UTC-04:00.

## Pendientes deliberados

1. Autorizacion por recurso al implementar los endpoints de negocio; login/JWT e identidad ya implementados.
2. DTOs, servicios y controllers de pedidos, filtros y paginacion.
3. Consultar y reservar credito transaccionalmente, proteger cambios de estado concurrentes y mapear errores a HTTP.
4. Frontend React y Compose de API/frontend (actualmente solo SQL esta contenerizado).
5. Suite automatizada de integracion/e2e y CI segun tiempo.

La transaccion del diagnostico y la de semilla NO implementan aun la reserva de credito para solicitudes de pedidos. No incluimos registro de usuarios, recuperacion de contrasenas ni IA dentro del producto en el alcance inicial.
