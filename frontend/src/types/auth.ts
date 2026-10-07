export interface Usuario {
  id: string;
  nombre: string;
  nombreUsuario: string;
  rol: 'Distribuidor' | 'Operador';
  distribuidorId: string | null;
}

export interface Sesion {
  token: string;
  expiraEnUtc: string;
  usuario: Usuario;
}
