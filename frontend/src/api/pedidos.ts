import { request } from './client';
import type { CrearPedido, Estado, ListaPedidos, PedidoDetalle } from '../types/pedidos';

export function listarPedidos(token: string, filtros: URLSearchParams) {
  return request<ListaPedidos>(`/pedidos?${filtros}`, token);
}

export function obtenerPedido(token: string, id: string) {
  return request<PedidoDetalle>(`/pedidos/${encodeURIComponent(id)}`, token);
}

export function crearPedido(token: string, pedido: CrearPedido) {
  return request<PedidoDetalle>('/pedidos', token, { method: 'POST', body: JSON.stringify(pedido) });
}

export function cambiarEstado(token: string, id: string, nuevoEstado: Estado, motivo?: string) {
  return request<PedidoDetalle>(`/pedidos/${encodeURIComponent(id)}/estado`, token, {
    method: 'PATCH', body: JSON.stringify({ nuevoEstado, motivo }),
  });
}
