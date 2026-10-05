# Pautas obligatorias del proyecto

Estas pautas vienen de `mejoras.txt`, proporcionado por el usuario. Aplican a todo codigo nuevo y a refactorizaciones en este repositorio. Leer tambien README.md, SOLUTION.md y docs/DECISIONES.md antes de cambiar comportamiento.

## Estilo C#

- Clases convencionales con propiedades explicitas y constructores tradicionales. No usar record ni constructores primarios.
- Un tipo por archivo (clase, interfaz o enum); nombre del archivo igual al tipo y namespace correspondiente a su carpeta.
- Nombres descriptivos y consistentes. Usar UsuarioActual para la identidad autenticada, no Actor.
- No agregar sealed, herencia, interfaces ni abstracciones sin una justificacion concreta.
- Metodos legibles, responsabilidades acotadas; evitar expresiones demasiado compactas. Usar LINQ, async/await e inyeccion de dependencias cuando simplifiquen el codigo. No introducir async en calculos puros.
- Comentarios para decisiones o reglas no obvias, no para repetir instrucciones.
- Mantener encapsulamiento: no permitir modificar estado, totales ni lineas saltandose las reglas.

## Backend

Un solo proyecto de aplicacion, src/Refidomsa.Api, organizado por responsabilidades:

- Controllers: recibir HTTP, aplicar autorizacion general y devolver respuestas. Controladores pequenos.
- Services/Interfaces: contratos solo cuando aporten valor real.
- Services: casos de uso, consultas y guardado. Pueden usar AppDbContext directamente.
- Models: entidades y entradas internas del negocio.
- Models/Enums: roles y estados.
- DTOs: solicitudes y respuestas HTTP agrupadas por funcionalidad.
- Rules: reglas puras sin HTTP ni consultas a base de datos.
- Data: EF Core, configuraciones, semilla y migraciones.
- Security: tokens y datos del usuario autenticado.
- Exceptions: excepciones de negocio.
- Middlewares: errores centralizados con respuestas consistentes.

Crear solo archivos/carpetas necesarios para funcionalidades reales. No agregar repositorios genericos, UnitOfWork propio, CQRS, MediatR ni proyectos de capas adicionales sin necesidad concreta.

## Reglas y seguridad

- Separar formato de entrada HTTP de reglas del negocio; una sola fuente de cada regla.
- El backend calcula precios, subtotales, totales y credito; copiar precio aplicado en cada linea del pedido.
- El cliente no determina rol, distribuidor, precios, credito ni fecha de creacion. UsuarioActual se construye desde identidad autenticada.
- Verificar permisos sobre cada recurso al leer o modificar: distribuidores nunca acceden a pedidos ajenos.
- Mantener todas las restricciones y transiciones del README.
- Decimal para dinero; documentar redondeo, precision y fechas.
- No afirmar que validar un saldo aislado protege la concurrencia: la persistencia debe garantizar la operacion transaccional.

## Pruebas

- Proyecto tests/Refidomsa.UnitTests en la misma solucion, referencia a la API.
- Unitarias sin base de datos real ni arranque HTTP; probar reglas directamente o mediante modelos.
- Casos validos, invalidos y limites para galones, productos repetidos, fechas, credito, roles y estados.
- Nombres claros; no crear interfaces solo para probar calculos puros.

## Frontend futuro

React organizado en pages, components, api, auth y types. Separar consumo de API de componentes visuales. Formularios legibles con carga, errores y acciones segun rol. No agregar un frontend vacio para aparentar avance.

## Forma de trabajar

- Inspeccionar lo existente y mostrar brevemente la estructura objetivo antes de refactorizar.
- Implementar incrementalmente, actualizar referencias, no pedir confirmacion por cada archivo.
- No eliminar funcionalidades ni requisitos para simplificar.
- Compilar y ejecutar pruebas, reportar limitaciones con claridad. No inventar verificaciones, errores de IA ni decisiones humanas.
- Actualizar SOLUTION.md, docs/DECISIONES.md y docs/USO_DE_IA.md con hechos reales.
- Explicar responsabilidades, flujo de creacion y decisiones que el candidato debe defender.
- No hacer commits, push ni Pull Requests sin solicitud explicita.
