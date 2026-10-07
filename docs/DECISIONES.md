# Decisiones iniciales

## Docker Compose de desarrollo

Compose levanta SQL Server, un trabajo temporal `db-init`, la API y el frontend con `docker compose up --build --wait`. `db-init` reutiliza exactamente la imagen de la API y ejecuta `--initialize-db`; depende de la salud de SQL Server y la API depende de que termine exitosamente. Asi se conserva la decision de no migrar ni insertar datos durante cada arranque HTTP, pero una copia nueva queda lista con un solo comando.

La API conecta a `sqlserver,1433` dentro de la red de Compose. El frontend construye los archivos de Vite en una etapa Node y NGINX los sirve con un proxy de mismo origen para `/api` hacia `api:8080`; no se usa `vite preview`, no se expone la API y no se agrega CORS. Solo se publica `127.0.0.1:8080` para el navegador y se conserva el puerto SQL loopback existente para las herramientas locales. La imagen de la API instala `curl` unicamente para comprobar `/health/ready`, porque el runtime de ASP.NET no lo incluye por defecto.

El volumen SQL existente se mantiene y los secretos se interpolan desde `.env` al entorno de los contenedores, sin copiarlos en las imagenes. Es una configuracion de desarrollo: SQL Server Developer, `sa` y `TrustServerCertificate=True` no son decisiones de produccion.

## Bloque 5: frontend minimo

React/Vite/TypeScript, paginas y componentes con estado local y un contexto solo para autenticacion. Navegacion hash en vez de router porque son cuatro pantallas y no hacen falta rutas de servidor. Fetch nativo centraliza errores/token, los modulos API separan contratos de UI. No Redux, librerias de formulario/UI ni fuentes remotas. La decision inicial de no agregar contenedores se sustituyo despues por el Compose de desarrollo documentado arriba. UI industrial/utilitaria con controles HTML nativos, labels, foco y responsive, sin adornos innecesarios.

Token/usuario viven en memoria, password se limpia tras cada intento; reload exige login. Logout local no revoca JWT. La notificacion401 incluye token de origen y se compara con el actual para no cerrar la sesion de otro usuario tras una respuesta tardia. Las paginas descartan cargas obsoletas y una creacion completada tras unmount no redirige. Autorizacion/precios/credito definitivo permanecen en API; ocultar botones no es una barrera de seguridad.

Estimacion monetaria: BigInt con escala6 para precio/galones, producto redondeado por linea a centavos y suma de esos centavos. Prueba medio centavo500.005x1=>500.01 y dos lineas=>1000.02. No se envia precio/total. La serializacion numerica y formato visual usan number, asi que no se promete precision decimal arbitraria del cliente; el servidor conserva autoridad decimal. Hora dominicana explicita-04:00 al enviar entrega; filtros de dias inclusive/inclusive se convierten a limites inclusive/exclusive.

Playwright utiliza Chromium instalado por el paquete fijado y API/SQL reales de la DB GUID del harness existente. Credenciales solo entorno hijo, traces off, stdout/stderr saneados para secretos conocidos (no garantia universal). Pruebas negativas modifican peticion real, nunca fabrican respuesta backend. Se mantienen checks de integridad y cleanup originales; QA final independiente3tests/matriz150/80carreras/digests PASS. No afirma minimo privilegio SQL del harness existente (sa local), ni certifica produccion/accesibilidad completa. Docs/.sisyphus/.env permanecen fuera de cualquier futuro commit; en esta implementacion no se hizo staging/commit/push.

## Arquitectura proporcional

La primera version separaba dominio, API y pruebas en tres proyectos. Tras leer mejoras.txt, el usuario solicito un unico proyecto de aplicacion Refidomsa.Api y el proyecto separado Refidomsa.UnitTests. Adoptamos esa organizacion porque el reto es pequeno y las responsabilidades pueden separarse por carpetas sin multiplicar proyectos. Las reglas puras siguen sin utilizar ASP.NET Core ni el ORM, aunque ahora pertenecen al ensamblado de la API.

Models conserva los datos y protege las modificaciones; Rules contiene las validaciones y el calculo del credito; Security contiene JWT y UsuarioActual; Exceptions contiene la excepcion de negocio. Data implementa persistencia EF Core. Controllers, Services y DTOs contienen autenticacion, consultas de productos/credito, creacion transaccional y consultas autorizadas de pedidos; los servicios consultan AppDbContext directamente. No se agregan repositorios genericos, UnitOfWork, CQRS, MediatR ni interfaces artificiales. El manejo HTTP de excepciones del dominio ya se incorporo en el bloque 1 de consultas.

## Sintaxis y encapsulamiento

Por preferencia expresa del usuario, reemplazamos records y constructores primarios por clases con propiedades y constructores tradicionales, y dejamos un tipo por archivo. Actor se renombro UsuarioActual. No usamos sealed ni interfaces por defecto. ReglaNegocioException hereda de Exception porque se necesita propagar fallos de negocio con un codigo estable.

Pedido mantiene constructor privado, fabrica Crear y propiedades protegidas: asi no se pueden modificar sus estados ni construir un pedido sin validar. ReglasPedido es la unica fuente de validaciones de negocio, y Pedido la invoca antes de construirse o cambiar de estado; no duplica condiciones. LineaPedido copia los datos del producto y guarda su subtotal. SolicitudLinea es una entrada interna con producto resuelto, no un DTO HTTP: CrearLineaPedidoSolicitud recibe solo ID del producto y galones.

Las clases estaticas de reglas son calculos sin estado; no necesitan interfaces ni inyeccion. Async/await se utiliza para consultas y escritura en la base de datos, no para calculos puros. El proyecto de pruebas referencia la API pero no ejecuta Program ni levanta un servidor para probar las reglas.

## Persistencia elegida

El usuario aprobo SQL Server despues de considerar PostgreSQL. Ambos cumplen el reto. Implementamos EF Core 10.0.12 con el proveedor SQL Server y herramienta EF local de la misma version. SQL Server 2022 CU26 Developer usa una imagen fijada y un volumen Docker, con puerto limitado a 127.0.0.1:14333. No se requiere instalar un servicio SQL Server en Windows. No tocamos otros contenedores del equipo.

EF Core InMemory no es la persistencia de la aplicacion. Las pruebas unitarias siguen sin DB; el comando Verificar hace una comprobacion de integracion optativa en SQL real, no una sustitucion con InMemory.

## Mapeo y encapsulamiento EF

Los modelos persistidos tienen setters privados y constructor privado sin parametros, que EF usa para reconstruir datos guardados. Pedido.Crear y CambiarEstado siguen siendo las puertas para operaciones de negocio. La coleccion interna List de lineas se mapea por su campo privado _lineas; el codigo externo solo recibe una vista de lectura. No usamos proxies ni lazy loading.

LineaPedido es una entidad dependiente owned: se guarda en LineasPedido y no tiene un ciclo de vida independiente del pedido. La clave compuesta PedidoId + ProductoId tambien impide duplicados de producto en SQL. Las relaciones a productos/distribuidores restringen el borrado de registros referenciados. Las configuraciones implementan IEntityTypeConfiguration porque es el contrato real que utiliza EF, no una interfaz agregada solo para pruebas.

Las migraciones y snapshots conservan los nombres con fecha y archivos parciales generados por EF, necesarios para su tooling. Son la excepcion justificada al nombre simple de archivo; no se cambia su codigo generado para imitar clases manuales.

## Precision de almacenamiento

Precios y galones usan decimal(18,6), importes y limites decimal(28,2). Esto admite cantidades fraccionarias con hasta seis decimales, sin convertirlas a enteros. AppDbContext comprueba el rango y escala configurados antes de SaveChanges/SaveChangesAsync para evitar que SQL redondee silenciosamente y cambie snapshots o totales. Es una comprobacion tecnica de persistencia, no una consulta dentro de Rules. CrearLineaPedidoSolicitud informa estos limites antes de guardar. GalonesJsonConverter examina el token numerico antes de conversion CLR, evitando que decimales extremadamente largos se redondeen inadvertidamente; reconoce exponentes y ceros finales exactos. Su herencia JsonConverter es el contrato real del serializador, sin nueva dependencia. El criterio de redondeo de subtotal no cambia.

## Migracion y semilla explicitas

El arranque normal no modifica el esquema. El comando local Inicializar aplica migraciones y semilla en Development. Elegimos inicializacion separada en vez de migraciones automaticas por cada instancia de API para evitar efectos ocultos de arranque y ejecutar los usuarios de demostracion solo a proposito. La semilla usa IDs estables, verifica cada registro y guarda las inserciones en una transaccion Serializable; no borra ni sobrescribe catalogo/hashes al repetirse. Se ejecuta como proceso local, no es un mecanismo de despliegue concurrente de produccion.

Las contrasenas de demostracion provienen de configuracion local y se guardan con PasswordHasher de ASP.NET Core. La DB solo recibe hashes salados. Los precios/RNC son ficticios. .env se ignora en Git y .env.example contiene credenciales publicas desechables, no secretos reales. sa y certificados confiados sin validacion son solo para este entorno loopback local; en produccion se requiere usuario de aplicacion de minimo privilegio y certificados validos.

## Diagnostico y salud

VerificadorPersistencia es un comando local optativo, no un endpoint de negocio ni un servicio para el frontend. Guarda y lee datos reales y revierte todos sus cambios en una transaccion. Sirve para demostrar que el mapeo no rompe reglas o precios historicos. /health verifica vida del proceso sin SQL y /health/ready verifica conectividad; readiness no prueba que todo el esquema este actualizado. IHealthCheck es el contrato requerido por el framework.

## Fechas

Usamos DateTimeOffset y almacenamos las fechas del dominio en UTC. Domingo se determina con la hora de Republica Dominicana (UTC-04:00, sin cambio estacional). Las 24 horas son duracion real, no diferencia entre fechas de calendario. La creacion inicializa tambien la fecha de cambio de estado. El reloj se pasa explicitamente al dominio; PedidosService lo obtiene del servidor despues del lock, nunca de fechas de creacion del cliente.

## Cantidades, dinero y precios

Permitimos galones decimales porque el requisito no exige cantidades enteras. Usamos decimal, redondeamos cada subtotal a dos decimales con MidpointRounding.AwayFromZero y sumamos esos subtotales para el total. El precio unitario conserva su precision original. Cada linea copia el precio y el nombre del producto al crear el pedido. El catalogo utilizado para crear las lineas debe obtenerse del servidor, no de precios enviados por el cliente.

## Roles y estados

UsuarioActual representa el rol y distribuidor obtenidos por LectorUsuarioActual desde ClaimsPrincipal autenticado. No es un mecanismo de autenticacion ni un DTO enviado por el cliente. Crear deriva el distribuidor de ese contexto; no recibe un distribuidor de destino independiente. Cancelar exige propietario y estado Pendiente. Aprobar, rechazar y despachar requieren Operador y la transicion exacta del diagrama. Rechazar requiere motivo no vacio. Una operacion invalida no modifica el estado ni las fechas.

## Autenticacion minima y flujo de clases

Se reutilizan Usuario, SQL y PasswordHasher<Usuario>, sin Identity completo ni cambios de esquema. El recorrido implementado es LoginSolicitud -> AutenticacionController -> AutenticacionService -> consulta SQL de Usuario/PasswordHasher -> GeneradorToken -> LoginRespuesta. El controller solo coordina HTTP; los DTOs limitan entrada/salida; el servicio consulta sin tracking y obtiene la hora UTC del servidor. Normaliza solo nombre con Trim/ToLowerInvariant, nunca el password. Success y SuccessRehashNeeded se aceptan sin reescribir hashes; errores SQL no se ocultan como credenciales incorrectas.

ConfiguracionJwt es una clase convencional mutable para binding, validada antes de servir HTTP, no una configuracion para cambiar en caliente. Exige emisor/audiencia no blancos y clave de al menos 32 bytes UTF-8. CrearParametrosValidacion concentra los parametros compartidos por JwtBearer y pruebas: firma HS256, issuer/audience y vigencia obligatorios, con 30 segundos de tolerancia. Los 60 minutos se fijan al emitir el token; la validacion del framework no impone por separado una duracion maxima exp-iat. JwtBearer 10.0.12 usa MapInboundClaims=false, name y role sin renombrarlos. Los comandos DB no necesitan JWT.

GeneradorToken firma los datos del usuario persistido: sub, name, role y distribuidorId solo para Distribuidor, mas iss/aud/iat/nbf/exp. La respuesta HTTP separada incluye nombreUsuario y distribuidorId nullable, nunca la entidad ni su hash. El JWT esta firmado, no cifrado; no guarda credito ni secretos.

Para solicitudes protegidas: Authorization: Bearer -> JwtBearer -> ClaimsPrincipal -> LectorUsuarioActual -> UsuarioActual. TryCrear comparte la validacion de coherencia con OnTokenValidated: identidad autenticada, un sub Guid no vacio, un rol exacto y unico, Distribuidor con un distribuidorId Guid no vacio u Operador sin el claim. Rechaza claims duplicados/incompatibles y roles numericos. Obtener solo lee HttpContext.User, no datos del cuerpo. No hace una consulta SQL por solicitud.

POST /api/auth/login es anonimo: 200 con token/expiraEnUtc/usuario; 401 ProblemDetails generico para usuario desconocido o password incorrecto; 400 ValidationProblemDetails para entrada invalida. El filtro no-store se ejecuta antes del rechazo automatico de formato. Program mantiene UseExceptionHandler -> ErroresMiddleware -> UseAuthentication -> UseAuthorization -> endpoints y health checks anonimos. Challenge401 se comprobo en vivo en productos/credito; forbidden403 sigue sin accion HTTP aplicable en bloque 1.

La clave llega del entorno (Jwt__Clave); el script puede tomar JWT_KEY de .env solo para Ejecutar, conserva variables JWT preexistentes y las restaura en finally. .env.example usa una clave publica de desarrollo; la .env existente no se modifico y requiere agregar JWT_KEY manualmente o usar una variable temporal. Produccion exige HTTPS, clave fuerte protegida y permisos SQL minimos. No hay refresh, revocacion anticipada, registro, recuperacion, logout servidor ni /me. Esto no sustituye autorizacion por propietario ni refleja cambios de rol en tokens ya emitidos antes de que expiren.

## Credito

Solo Pendiente y Aprobado consumen credito, incluso si en un sistema contable real el despacho no significara pago. Respetamos la formula del reto. El calculo filtra por distribuidor. El dominio valida contra un saldo recibido; NO garantiza atomicidad frente a pedidos simultaneos. La persistencia debe calcular y reservar credito dentro de una operacion transaccional adecuada. Si el limite baja por debajo del consumo, Disponible devuelve el saldo negativo real; no lo oculta.

## Alcance

La API actual tiene login/JWT, lector de identidad, health checks, persistencia, comandos de semilla/diagnostico, productos, credito autorizado, POST de pedidos con proteccion transaccional, consultas paginadas/detalle y PATCH de estados autorizados/concurrentes. El bloque5 incorpora frontend React/Vite/TypeScript con login/lista/crear/detalle/acciones. El middleware traduce errores de negocio; la encapsulacion del modelo no sustituye autorizacion por recurso. La proteccion del credito en POST/PATCH pertenece a PedidosService, no a la semilla/diagnostico.

Verificacion historica del bloque de autenticacion: build Release sin errores/advertencias, 144 unitarias sin fallos/omisiones y QA real SQL/HTTP aprobado el 2026-10-06. Los conteos 3/4/3/0 y datos/hashes/precios/historial EF se conservaron. Inicializar y Verificar se ejecutaron sin JWT. En aquel bloque la validacion criptografica era unitaria y no habia recurso protegido para challenge en vivo. Las cinco revisiones independientes de autenticacion devolvieron PASS dentro del alcance minimo local; no certifican produccion ni sustituyen el okay del usuario. Como mejora no bloqueante, los mensajes genericos no eliminan la diferencia temporal entre un usuario inexistente y verificar un hash; una exposicion publica requeriria revisar esta limitacion y las protecciones antiabuso.

## Recorrido de creacion de pedido

Pedido.Crear recibe UsuarioActual, fechas del servidor, productos resueltos y credito disponible; valida permiso, lineas y entrega en ReglasPedido, copia las lineas con precio aplicado, calcula el total y comprueba el credito. Solo entonces devuelve un pedido Pendiente.

El recorrido HTTP de creacion implementado es: DTO de formato -> controller autorizado -> UsuarioActual autenticado -> servicio/transaccion/bloqueo -> catalogo/credito desde AppDbContext -> hora UTC despues de esperar -> Pedido.Crear/Rules -> SaveChanges/commit -> DTO de detalle. El controller devuelve HTTP; el servicio coordina datos y transaccion sin repetir reglas puras. El middleware traduce excepciones de negocio a ProblemDetails.

## Pautas para nuevas funcionalidades

AGENTS.md conserva las instrucciones de mejoras.txt dentro del repositorio, incluyendo organizacion de React en pages, components, api, auth y types. El bloque5 implementa esa estructura con componentes funcionales; en la reorganizacion inicial no se crearon carpetas vacias para aparentar avance.

## Bloque 1: productos y credito, 2026-10-06

Se adoptaron GET /api/productos y GET /api/distribuidores/{id}/credito porque el README no fija rutas/JSON. Catalogo autenticado para ambos roles; credito propio para Distribuidor y cualquiera para Operador como supuesto operativo minimo. CreditoService comprueba propietario antes de consultar y devuelve null tanto para ajeno como inexistente; controller produce el mismo 404. Guid vacio/invalido produce 400 de formato.

AppDbContext se usa directamente sin repositorios/interfaces adicionales. Productos proyecta DTOs ordenados; credito proyecta limite y SUM nullable correlacionado en una consulta SQL, sin materializar pedidos/lineas. ReglasCredito conserva una unica resta en la sobrecarga limite/consumido; la version de pedidos delega. Ninguna reserva ni garantia transaccional se atribuye a esta consulta.

ErroresMiddleware captura solo ReglaNegocioException y escribe ProblemDetails mediante el servicio del framework: reglas400, sin_permiso403 y conflictos de credito/transicion409. UseExceptionHandler conserva errores tecnicos500 con texto generico y registro servidor; no se captura cualquier InvalidOperationException/DbUpdateException como error del usuario. JSON enum string sin valores enteros queda configurado para los futuros contratos, sin cambiar el rol string del login.

En bloque1 el QA incremental se implemento solo para -Bloque 1. Usa System.Data.SqlClient y HttpClient en PowerShell5.1, DB con GUID unico, fixture con estados mezclados, dos procesos Release propios y credenciales temporales solo en hijos. Digests originales en memoria, finally con limpieza restringida y error nozero si falla. Release sin advertencias y 159 unitarias aprobadas; QA SQL/HTTP exit0 con saldos positivos/negativos/vacios,401/404/400 y catalogo exacto. Docs/ y notepads son locales, excluidos de futuros commits; no se realizo commit/staging/push en esta implementacion.

## Bloque 3: consultas autorizadas

La identidad limita la consulta antes de filtros, conteo y paginacion. Un filtro de propietario ajeno para Distribuidor devuelve 404 sin ejecutar la consulta; Operador puede filtrar cualquier ID y recibe lista vacia cuando no existe. Detalle utiliza el mismo scope para indistinguibilidad de ajeno/inexistente. No se agregan repositorios ni interfaces.

Lista proyecta solo columnas de resumen, incluyendo nombre del distribuidor, sin cargar lineas owned; detalle proyecta snapshots guardados y reutiliza los DTOs del POST. FechaCreacion descendente e Id descendente hacen estable el orden con empates. El rango usa instantes con offset explicito, desde inclusivo/hasta exclusivo; no interpreta una fecha sin zona usando la zona de Windows. Paginacion extrema se rechaza cuando su desplazamiento excede Int32, antes de consultar SQL.

Harness acumulativo bloque3 agrega diez pedidos aislados, ambos distribuidores, cinco estados y fechas repetidas. Comparacion contra SQL verifica orden/campos/totales, rangos y filtros combinados, paginas vacias, aislamiento y snapshots despues de cambios del catalogo QA. Se mantienen los casos de carreras y cancelacion del bloque2 sin modificarlos. Verificacion directa: diagnosticos C# sin errores, Release0warnings/errors,219unitarias PASS y QA acumulativo bloque3 exit0, veinte carreras previas incluidas. QA/procesos propios eliminados y digests Refidomsa/.env identicos. El primer intento corrigio el comparador de cero filas SQL (null en PS5.1), no la API; reejecucion completa PASS.

## Bloque 2: creacion transaccional, 2026-10-06

ReadCommitted solo no serializa lectura/validacion/insercion. Elegimos SELECT parametrizado por PK sobre Distribuidores WITH (UPDLOCK, HOLDLOCK), primera lectura dentro de la transaccion, antes del catalogo/SUM/reloj. Cada distribuidor tiene una fila aunque no existan pedidos: serializa API distintas sin mutex en memoria ni bloqueo global. AsNoTracking evita reutilizar limite/catalogo obsoletos. La transaccion se dispone async y revierte explicitamente los fallos con CancellationToken.None, incluso si el request fue cancelado. Futuras mutaciones de estados deben compartir este protocolo. No se promete seguridad frente a escritores SQL que lo ignoren.

DTOs validan formato/presencia/escala y rango decimal(18,6); Rules conserva cantidad, minimo/capacidad, duplicados, entrega y credito. Precios provienen del catalogo SQL, propietario de claims; extras JSON no forman parte de DTOs. El Operador se rechaza en autorizacion HTTP antes del caso de uso; servicio y dominio conservan la defensa pura. POST devuelve detalle/Location sin agregar GET prematuramente. No se requiere migracion ni dependencia nueva.

Build Release sin advertencias, 182 unitarias y QA acumulativo bloque2 final exit0 aprobados directamente. Veinte carreras 201/409 con SQL exacto, diez sin pedidos y diez con consumo, esperas demostradas mediante DMV/lock externo y Task.WhenAll en dos APIs. SQL puede encadenar el segundo waiter detras del primero: el harness sigue esa cadena, no exige dos bloqueos directos contra el holder. Sur completa mientras Norte espera, sin bloqueo global. Refidomsa/.env y volumen preservados. Limites/JSON malformado/precision extrema, reloj despues del lock y cancelacion mientras espera lock PASS. QA independiente repitio173tests/harness ampliado antes del ultimo ajuste converter; ejecucion directa final182tests tambien PASS. No ejecutar harnesses simultaneos porque usan los mismos puertos.

## Bloque 4: estados autorizados y concurrencia

PATCH recibe CambiarEstadoSolicitud con string exacto NuevoEstado y motivo opcional hasta1000: sigue la validacion legible de nombres de ListaPedidosSolicitud y rechaza numeros en JSON/string. DTO valida formato, dominio conserva permisos/transiciones y recorte del rechazo. El servicio resuelve propietario mediante proyeccion sin tracking para no reutilizar un estado viejo, inicia ReadCommitted, bloquea primero la fila distribuidor igual que CrearAsync y carga pedido tracked con owned lineas despues. El segundo scope comprueba visibilidad/propietario; CambiarEstado comprueba permisos/reglas antes de mutar y usa reloj servidor fresco. Misma404 para ajeno/inexistente,403 para rol prohibido y409 para transicion incompatible. No se agregaron repositorios ni contratos artificiales.

Estado usa IsConcurrencyToken sobre columna existente: EF UPDATE comprueba valor original. DbUpdateConcurrencyException revierte con token de limpieza no cancelado y produce ReglaNegocioException conflicto_concurrencia409 sin retry. El bloqueo por distribuidor protege el credito compartido de POST/PATCH hasta commit; el token tambien rechaza escritores EF obsoletos fuera de ese protocolo. No protege contra SQL arbitrario que omita esas comprobaciones. has-pending-model-changes informo que no hay cambios: sin migracion nueva ni edicion de Inicial/snapshot.

QA aislado acumulativo bloque4 aprobado directamente:150 combinaciones roles/owners/estados,40races de estados200/409 y20 crear/liberar con limite exacto; creditos y datos finales SQL coherentes, fallos sin escrituras, PATCH independiente entre distribuidores y cancelacion esperando lock. Veinte carreras POST previas y bloques1-3 conservados. El auxiliar QaConcurrencia.cs referencia el proyecto API como aplicacion de archivo .NET10, no es nueva infraestructura/capa de aplicacion ni unitarias con DB: solo harness, restringido a QA GUID. Dos contextos con mismo original demostraron exception SQL real y ganador intacto. AOT desactivado porque EF necesita construir modelo dinamico; primeros intentos detectaron esa incompatibilidad y posterior repeticion completaPASS. Release0warnings/errors,242unitarias0fail/skip y cleanup/digests originalesPASS.
