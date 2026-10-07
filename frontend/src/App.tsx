import { useEffect, useState } from 'react';
import { useAuth } from './auth/AuthContext';
import { LoginPage } from './pages/LoginPage';
import { PedidosPage } from './pages/PedidosPage';
import { CrearPedidoPage } from './pages/CrearPedidoPage';
import { PedidoDetallePage } from './pages/PedidoDetallePage';

export function App() {
  const { sesion, logout } = useAuth();
  const [ruta, setRuta] = useState(window.location.hash.slice(1) || '/pedidos');
  useEffect(() => {
    const cambiar = () => setRuta(window.location.hash.slice(1) || '/pedidos');
    window.addEventListener('hashchange', cambiar);
    return () => window.removeEventListener('hashchange', cambiar);
  }, []);
  const detalle = /^\/pedidos\/([^/]+)$/.exec(ruta);
  return <>
    <header><a className="brand" href="#/pedidos">REFIDOMSA <span>Pedidos de combustible</span></a>
      {sesion && <div className="user"><span>{sesion.usuario.nombre} · {sesion.usuario.rol}</span>
        <button className="secondary" onClick={logout}>Cerrar sesion</button></div>}</header>
    <main>{!sesion ? <LoginPage /> : detalle ? <PedidoDetallePage key={detalle[1]} id={detalle[1]} /> :
      ruta === '/crear' && sesion.usuario.rol === 'Distribuidor' ? <CrearPedidoPage /> : <PedidosPage />}</main>
    <footer>Gestion de pedidos · RD$ · Hora dominicana (UTC-04:00)</footer>
  </>;
}
