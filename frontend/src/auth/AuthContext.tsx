import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { request } from '../api/client';
import type { Sesion } from '../types/auth';

interface Auth {
  sesion: Sesion | null;
  aviso: string;
  login: (nombreUsuario: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<Auth | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [sesion, setSesion] = useState<Sesion | null>(null);
  const [aviso, setAviso] = useState('');
  const tokenActual = useRef('');

  function logout() {
    tokenActual.current = '';
    setSesion(null);
    setAviso('');
    window.location.hash = '/pedidos';
  }

  useEffect(() => {
    function expirar(event: Event) {
      if ((event as CustomEvent<string>).detail !== tokenActual.current) return;
      tokenActual.current = '';
      setSesion(null);
      setAviso('Tu sesion ha expirado. Inicia sesion de nuevo.');
    }
    window.addEventListener('sesion-expirada', expirar);
    return () => window.removeEventListener('sesion-expirada', expirar);
  }, []);

  async function login(nombreUsuario: string, password: string) {
    const resultado = await request<Sesion>('/auth/login', '', {
      method: 'POST', body: JSON.stringify({ nombreUsuario, password }),
    });
    setAviso('');
    tokenActual.current = resultado.token;
    setSesion(resultado);
  }

  return <AuthContext.Provider value={{ sesion, aviso, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('Falta AuthProvider.');
  return auth;
}
