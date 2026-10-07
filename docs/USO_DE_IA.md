# Uso de herramientas de IA

Durante el desarrollo utilicé herramientas de IA como apoyo para acelerar algunas tareas, revisar alternativas y detectar posibles problemas. Las decisiones finales y la validación del código fueron realizadas por mí.

## Herramientas utilizadas

- **OpenCode** como entorno principal para trabajar con asistentes de IA sobre el proyecto.
- **Oh My OpenCode** como complemento de OpenCode para facilitar el uso de distintos agentes/modelos durante el desarrollo.
- **Modelos de OpenAI GPT-5.6** principalmente para generación y revisión de código, análisis de errores, propuestas de estructura y revisión de casos de prueba.

La IA se utilizó principalmente para:

- Proponer estructuras iniciales de clases y carpetas.
- Generar código repetitivo o base.
- Revisar reglas de negocio y posibles casos borde.
- Ayudar con pruebas unitarias y escenarios de concurrencia.
- Revisar consultas y uso de Entity Framework Core.
- Analizar posibles problemas de seguridad y autorización.
- Apoyar la implementación del frontend en React/TypeScript.
- Ayudar a documentar las decisiones tomadas durante el desarrollo.

## Prompts relevantes

No conservé cada prompt utilizado, pero algunos de los más importantes fueron similares a los siguientes:

### Organización del proyecto

> Organiza este proyecto ASP.NET Core de una manera sencilla y fácil de explicar. Evita agregar patrones o capas que no sean necesarias para el tamaño del proyecto.

### Reglas de negocio

> Revisa las reglas de negocio y dime si falta algún caso importante o validación.

### Concurrencia

> Dos solicitudes pueden intentar crear pedidos al mismo tiempo utilizando el mismo crédito disponible. Analiza cómo evitar que ambas validen el mismo saldo antes de guardar.

### Seguridad

> Revisa el flujo de autenticación y autorización con JWT. Valida que un Distribuidor no pueda acceder a información o pedidos pertenecientes a otro distribuidor.

### Pruebas

> Propón pruebas para las reglas de negocio, incluyendo casos válidos, inválidos, límites de crédito, cambios de estado y solicitudes concurrentes.

## Ejemplo donde no acepté la propuesta de la IA

Una de las primeras propuestas de la IA fue dividir la solución en varios proyectos y utilizar abstracciones adicionales como repositorios, interfaces y otras capas.

Aunque esa estructura puede ser válida en aplicaciones más grandes, consideré que para el alcance de este ejercicio agregaba complejidad innecesaria.

Decidí mantener una solución más sencilla, utilizando un proyecto principal organizado por responsabilidades mediante carpetas como `Controllers`, `Services`, `Models`, `Rules`, `DTOs` y `Data`, además del proyecto de pruebas.

También revisé código generado por IA que utilizaba construcciones más compactas como `record`, constructores primarios y varias clases en un mismo archivo. Preferí reemplazar parte de ese código por clases y constructores tradicionales porque me resultaban más claros de leer, mantener y explicar durante una revisión técnica.

## Decisiones tomadas por mí

Independientemente de las sugerencias de la IA, tomé decisiones como:

- Mantener una arquitectura proporcional al tamaño del ejercicio y evitar sobreingeniería.
- Utilizar ASP.NET Core, Entity Framework Core y SQL Server.
- Mantener los controladores simples y colocar la lógica en servicios y reglas de negocio.
- Obtener precios, crédito e identidad del usuario desde fuentes controladas por el backend y no confiar en esos valores enviados por el cliente.
- Utilizar JWT para autenticación y aplicar las validaciones de autorización nuevamente en el backend
- Utilizar React, TypeScript y Vite para el frontend sin agregar Redux u otras dependencias que no fueran necesarias.
- Revisar y ejecutar el código generado antes de considerarlo terminado.