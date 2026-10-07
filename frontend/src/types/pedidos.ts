export const estados = ['Pendiente', 'Aprobado', 'Despachado', 'Rechazado', 'Cancelado'] as const;
export type Estado = typeof estados[number];

export interface PedidoResumen {
  id: string;
  distribuidorId: string;
  nombreDistribuidor: string;
  fechaEntrega: string;
  fechaCreacion: string;
  fechaCambioEstado: string;
  total: number;
  estado: Estado;
}

export interface PedidoDetalle extends PedidoResumen {
  motivoRechazo: string | null;
  lineas: {
    productoId: string;
    nombreProducto: string;
    galones: number;
    precioPorGalon: number;
    subtotal: number;
  }[];
}

export interface ListaPedidos {
  items: PedidoResumen[];
  pagina: number;
  tamanoPagina: number;
  totalRegistros: number;
}

export interface CrearPedido {
  fechaEntrega: string;
  lineas: { productoId: string; galones: number }[];
}
