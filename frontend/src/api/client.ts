import type { Problem } from '../types/problem';

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function request<T>(path: string, token = '', options: RequestInit = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      ...options,
      headers: {
        ...(options.body ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...options.headers,
      },
    });
  } catch {
    throw new ApiError(0, 'No se pudo conectar con el servidor. Revisa la conexion e intenta de nuevo.');
  }
  if (!response.ok) {
    const problem: Problem = await response.json().catch(() => ({}));
    const messages = Object.values(problem.errors || {}).flat();
    if (response.status === 401 && token) {
      window.dispatchEvent(new CustomEvent('sesion-expirada', { detail: token }));
    }
    throw new ApiError(response.status, messages.length ? messages.join(' ') :
      problem.detail || problem.title || `Error del servidor (${response.status}).`);
  }
  return response.json() as Promise<T>;
}
