# Solucion: gestion de pedidos de combustible

## Estado actual

Primer bloque del backend: dominio de pedidos y pruebas unitarias. La API es una base minima con `/health`; todavia NO expone endpoints de pedidos, autenticacion ni persistencia. El frontend y SQL Server estan pendientes. No es aun una entrega completa del reto.

## Requisitos y ejecucion

- SDK .NET 10.0.101 (o parche posterior compatible con `global.json`).
- Desde la raiz del repositorio:

```powershell
dotnet build Refidomsa.slnx
dotnet test Refidomsa.slnx
dotnet run --project src/Refidomsa.Api --urls http://localhost:5080
```

La comprobacion de vida esta en `http://localhost:5080/health`. Por ahora solo comprueba que el proceso responde, no una conexion a base de datos.

## Organizacion

- `src/Refidomsa.Api/Models`: modelos de negocio con encapsulamiento.
- `src/Refidomsa.Api/Models/Enums`: roles y estados.
- `src/Refidomsa.Api/Rules`: validaciones puras y calculo de credito, sin HTTP ni SQL Server.
- `src/Refidomsa.Api/Security`: contexto UsuarioActual, pendiente de conectar a autenticacion.
- `src/Refidomsa.Api/Exceptions`: excepciones de negocio con codigo estable.
- `src/Refidomsa.Api/Program.cs`: arranque HTTP y health check.
- `tests/Refidomsa.UnitTests`: pruebas de limites, precios, credito, roles y estados, sin base de datos ni servidor.
- `docs/DECISIONES.md`: decisiones y supuestos.
- `docs/USO_DE_IA.md`: uso real de IA durante el desarrollo.
- `AGENTS.md`: pautas de estilo y organizacion para futuros cambios.

Hay un unico proyecto backend y uno de pruebas. Controllers, Services, DTOs, Data y Middlewares se agregaran cuando se implementen sus responsabilidades. La reorganizacion de mejoras.txt conserva las 59 pruebas existentes y reemplaza records y constructores primarios por clases convencionales.

## Pendientes

1. Persistencia con EF Core y SQL Server, migraciones y datos semilla.
2. Autenticacion, autorizacion por recurso y endpoints con errores consistentes.
3. Validacion transaccional del credito al crear pedidos y proteccion de cambios concurrentes.
4. Frontend React, filtros y paginacion.
5. Docker Compose, credenciales de prueba y pruebas de integracion.

No hay usuarios de prueba todavia. No se han incluido secretos ni creado commits automaticamente. Registro, recuperacion de contrasena y funcionalidades de IA en el producto quedan fuera del alcance inicial.

## Verificacion del primer bloque

En el entorno local con SDK 10.0.101, la compilacion termino sin errores ni advertencias y pasaron 59 pruebas unitarias. Se arranco la API y `/health` respondio HTTP 200 con `Healthy`; despues se detuvo el proceso de comprobacion. La revision independiente por subagentes no pudo ejecutarse por problemas de configuracion de los modelos; no debe confundirse con una revision aprobada.

Despues de aplicar mejoras.txt se repitieron las verificaciones con la estructura nueva:

```powershell
dotnet build Refidomsa.slnx --configuration Release
dotnet test Refidomsa.slnx --configuration Release --no-build
```

Resultado: cero errores, cero advertencias, 59 pruebas aprobadas y ninguna omitida. La API de Release tambien respondio `200 Healthy`. No se ejecutaron pruebas de integracion con SQL Server ni pruebas de frontend porque esas funcionalidades aun no existen.
