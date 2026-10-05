# Solucion: gestion de pedidos de combustible

## Estado actual

Backend .NET 10 con modelos encapsulados, reglas puras, EF Core 10.0.12, SQL Server 2022 en Docker, migracion inicial y datos semilla. Hay 66 pruebas unitarias y un diagnostico reproducible contra la base real.

Todavia NO hay login, tokens, endpoints de pedidos, servicios de casos de uso ni frontend. Los usuarios existen en la base, pero aun no pueden iniciar sesion por HTTP. No es una entrega completa del reto.

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

Revisa los valores de .env. Son credenciales desechables de prueba, no secretos de produccion. .env esta excluido de Git; el script admite entradas KEY=valor sin comillas. Cambia SQLSERVER_PASSWORD y SEED_PASSWORD si lo deseas ANTES de inicializar por primera vez.

```powershell
.\scripts\local.ps1 -Accion Inicializar
.\scripts\local.ps1 -Accion Ejecutar
```

Inicializar levanta SQL Server, espera a que responda, aplica migraciones e inserta los datos semilla que faltan. Puede repetirse sin duplicarlos ni sobrescribir precios o hashes existentes. Ejecutar levanta/verifica SQL Server y arranca la API en http://localhost:5080; no aplica migraciones implicitamente. En este equipo ya se inicializo la base.

Si la politica local impide ejecutar scripts, puedes usar `powershell -ExecutionPolicy Bypass -File .\scripts\local.ps1 -Accion Inicializar` para esa ejecucion; no es necesario cambiar la politica global.

El script configura temporalmente ConnectionStrings__Refidomsa, DatabaseSeed__Password y ASPNETCORE_ENVIRONMENT=Development, y restaura los valores previos al terminar. Tambien puedes configurar esas variables directamente y ejecutar dotnet run. La API no lee .env por si sola: .env es utilizado por Compose y el script.

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

## Pruebas y comprobacion real

```powershell
dotnet build Refidomsa.slnx --configuration Release
dotnet test Refidomsa.slnx --configuration Release --no-build
.\scripts\local.ps1 -Accion Verificar
```

Las 66 pruebas no necesitan una base de datos ni un servidor HTTP. Las pruebas de mapeo inspeccionan metadatos EF y las de precision rechazan valores antes de intentar conectarse.

Verificar es un diagnostico de integracion local, separado de las unitarias. Comprueba semilla, hashes, guardado/lectura de un pedido con dos lineas, cantidades decimales, fechas, total, precio historico tras cambiar el catalogo y cambio de estado. Revierte su transaccion y comprueba que no quedan pedido ni precio alterado. No expone un endpoint y solo se admite en Development.

Resultados comprobados: compilacion sin errores/advertencias, 66 pruebas aprobadas, inicializacion repetida sin duplicados, diagnostico real aprobado tambien despues de reiniciar el contenedor, health checks 200 y readiness 503 con conexion no disponible. Conteo final: 3 distribuidores, 4 productos, 3 usuarios, 0 pedidos residuales.

La revision independiente por subagentes sigue limitada por la configuracion de modelos descrita en docs/USO_DE_IA.md; no se presenta como aprobada. No se han ejecutado pruebas de login o frontend, pues aun no existen.

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
- Security/UsuarioActual: contexto de identidad; autenticacion pendiente.
- Exceptions: errores de negocio con codigo estable.
- tests/Refidomsa.UnitTests: pruebas sin DB.
- scripts/local.ps1 y compose.yaml: entorno local.
- AGENTS.md: pautas del usuario; docs/DECISIONES.md y docs/USO_DE_IA.md: decisiones y uso real de IA.

Precios y galones usan decimal(18,6); importes y credito decimal(28,2). Se rechazan valores fuera de rango/escala antes de guardar en lugar de permitir redondeo SQL silencioso. El subtotal se redondea a dos decimales AwayFromZero, y el total suma subtotales. Fechas DateTimeOffset en UTC; domingo en hora dominicana UTC-04:00.

## Pendientes deliberados

1. Login/JWT, identidad autenticada y autorizacion por recurso.
2. DTOs, servicios y controllers de pedidos, filtros y paginacion.
3. Consultar y reservar credito transaccionalmente, proteger cambios de estado concurrentes y mapear errores a HTTP.
4. Frontend React y Compose de API/frontend (actualmente solo SQL esta contenerizado).
5. Suite automatizada de integracion/e2e y CI segun tiempo.

La transaccion del diagnostico y la de semilla NO implementan aun la reserva de credito para solicitudes de pedidos. No incluimos registro de usuarios, recuperacion de contrasenas ni IA dentro del producto en el alcance inicial.
