using Refidomsa.Api.Exceptions;
using Refidomsa.Api.Models;
using Refidomsa.Api.Models.Enums;
using Refidomsa.Api.Security;

namespace Refidomsa.Api.Rules;

public static class ReglasPedido
{
    private static readonly TimeSpan ZonaDominicana = TimeSpan.FromHours(-4);

    public static void ValidarUsuarioParaCrear(UsuarioActual usuarioActual)
    {
        ArgumentNullException.ThrowIfNull(usuarioActual);
        bool esDistribuidor = usuarioActual.Rol == Rol.Distribuidor;
        bool tieneDistribuidor = usuarioActual.DistribuidorId.HasValue
            && usuarioActual.DistribuidorId.Value != Guid.Empty;

        if (!esDistribuidor || !tieneDistribuidor)
        {
            throw new ReglaNegocioException("sin_permiso",
                "Solo un distribuidor puede crear pedidos para su propia cuenta.");
        }
    }

    public static void ValidarLineas(IReadOnlyCollection<SolicitudLinea> lineas)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        if (lineas.Count < 1 || lineas.Count > 4)
        {
            throw new ReglaNegocioException("cantidad_lineas", "El pedido debe tener entre 1 y 4 lineas.");
        }

        ValidarProductos(lineas);
        ValidarGalones(lineas);
    }

    private static void ValidarProductos(IReadOnlyCollection<SolicitudLinea> lineas)
    {
        foreach (var linea in lineas)
        {
            if (linea == null || linea.Producto == null)
            {
                throw new ReglaNegocioException("producto_invalido", "Todas las lineas deben tener un producto.");
            }

            var producto = linea.Producto;
            if (producto.Id == Guid.Empty || string.IsNullOrWhiteSpace(producto.Nombre) || producto.PrecioPorGalon <= 0)
            {
                throw new ReglaNegocioException("producto_invalido",
                    "El producto debe tener identificador, nombre y precio positivo.");
            }
        }

        var productos = new HashSet<Guid>();
        foreach (var linea in lineas)
        {
            if (!productos.Add(linea.Producto.Id))
            {
                throw new ReglaNegocioException("producto_repetido", "No se permiten productos repetidos.");
            }
        }

    }

    private static void ValidarGalones(IReadOnlyCollection<SolicitudLinea> lineas)
    {
        if (lineas.Any(linea => linea.Galones < 500))
        {
            throw new ReglaNegocioException("minimo_galones", "Cada linea debe tener al menos 500 galones.");
        }

        // Comprobar cada linea primero evita desbordar la suma con cantidades extremas.
        if (lineas.Any(linea => linea.Galones > 9000) || lineas.Sum(linea => linea.Galones) > 9000)
        {
            throw new ReglaNegocioException("capacidad_camion", "El pedido no puede superar 9000 galones.");
        }
    }

    public static void ValidarFechaEntrega(DateTimeOffset fechaEntrega, DateTimeOffset fechaCreacion)
    {
        if (fechaEntrega < fechaCreacion || fechaEntrega - fechaCreacion < TimeSpan.FromHours(24))
        {
            throw new ReglaNegocioException("anticipacion_entrega",
                "La entrega debe ser al menos 24 horas despues de la creacion.");
        }

        if (fechaEntrega.ToOffset(ZonaDominicana).DayOfWeek == DayOfWeek.Sunday)
        {
            throw new ReglaNegocioException("entrega_domingo",
                "No se permiten entregas en domingo en Republica Dominicana.");
        }
    }

    public static void ValidarCredito(decimal total, decimal creditoDisponible)
    {
        if (total > creditoDisponible)
        {
            throw new ReglaNegocioException("credito_insuficiente", "El total supera el credito disponible.");
        }
    }

    public static void ValidarCambioEstado(Pedido pedido, EstadoPedido nuevoEstado,
        UsuarioActual usuarioActual, DateTimeOffset fechaCambio, string? motivo)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentNullException.ThrowIfNull(usuarioActual);
        ValidarPermisoParaCambiarEstado(pedido, nuevoEstado, usuarioActual);

        if (!EsTransicionPermitida(pedido.Estado, nuevoEstado))
        {
            throw new ReglaNegocioException("transicion_invalida", "La transicion de estado no esta permitida.");
        }

        if (nuevoEstado == EstadoPedido.Rechazado && string.IsNullOrWhiteSpace(motivo))
        {
            throw new ReglaNegocioException("motivo_requerido", "Rechazar un pedido requiere un motivo.");
        }

        if (fechaCambio < pedido.FechaCambioEstado)
        {
            throw new ReglaNegocioException("fecha_estado_invalida", "La fecha de cambio no puede retroceder.");
        }
    }

    private static void ValidarPermisoParaCambiarEstado(Pedido pedido, EstadoPedido nuevoEstado,
        UsuarioActual usuarioActual)
    {
        bool tienePermiso;
        if (nuevoEstado == EstadoPedido.Cancelado)
        {
            tienePermiso = usuarioActual.Rol == Rol.Distribuidor
                && usuarioActual.DistribuidorId == pedido.DistribuidorId;
        }
        else
        {
            tienePermiso = usuarioActual.Rol == Rol.Operador;
        }

        if (!tienePermiso)
        {
            throw new ReglaNegocioException("sin_permiso", "No tienes permiso para realizar esta accion.");
        }
    }

    private static bool EsTransicionPermitida(EstadoPedido estadoActual, EstadoPedido nuevoEstado)
    {
        if (estadoActual == EstadoPedido.Pendiente)
        {
            return nuevoEstado == EstadoPedido.Aprobado
                || nuevoEstado == EstadoPedido.Rechazado
                || nuevoEstado == EstadoPedido.Cancelado;
        }

        return estadoActual == EstadoPedido.Aprobado && nuevoEstado == EstadoPedido.Despachado;
    }
}
