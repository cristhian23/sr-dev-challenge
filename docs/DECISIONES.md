# Decisiones de arquitectura y diseño

Para este proyecto intenté mantener una arquitectura sencilla, acorde al tamaño de la solución, evitando agregar capas o patrones que no fueran necesarios.

## Arquitectura

Utilicé una API en ASP.NET Core organizada por carpetas como `Controllers`, `Services`, `Models`, `DTOs`, `Data` y `Rules`.

Consideré separar la solución en más proyectos y utilizar patrones como Repository o CQRS, pero para el alcance actual entendí que agregaban complejidad innecesaria.

## Persistencia

Elegí SQL Server con Entity Framework Core.

También consideré PostgreSQL, pero ambos cumplían con los requerimientos y SQL Server encajaba bien con el stack de .NET utilizado.

## Reglas de negocio

Mantuve las validaciones importantes dentro del dominio y los servicios, evitando colocar lógica de negocio directamente en los controladores.

## Concurrencia

Para operaciones que pueden afectar el crédito disponible de un distribuidor utilicé transacciones y bloqueo a nivel de base de datos.

Esto evita que dos solicitudes simultáneas utilicen el mismo crédito disponible.

## Frontend

Utilicé React con TypeScript y Vite.

Como la aplicación tiene pocas pantallas, mantuve el manejo de estado simple y evité agregar Redux u otras librerías que no eran necesarias.

# Supuestos realizados

Ante algunos puntos que no estaban completamente definidos en el requerimiento, asumí que:

- Los galones pueden contener valores decimales.
- Solo los pedidos `Pendientes` y `Aprobados` consumen crédito.
- Los precios utilizados para crear un pedido siempre deben obtenerse desde la base de datos y no desde el cliente.
- Un Operador puede trabajar con pedidos de diferentes distribuidores.

Estas decisiones buscaron mantener la solución fácil de entender, segura y suficientemente robusta sin agregar complejidad que no aportara valor al alcance solicitado.