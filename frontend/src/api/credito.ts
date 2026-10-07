import { request } from './client';
import type { Credito } from '../types/credito';

export function obtenerCredito(token: string, id: string) {
  return request<Credito>(`/distribuidores/${encodeURIComponent(id)}/credito`, token);
}
