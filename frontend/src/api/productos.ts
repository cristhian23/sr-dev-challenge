import { request } from './client';
import type { Producto } from '../types/productos';

export function obtenerProductos(token: string) {
  return request<Producto[]>('/productos', token);
}
