import { useEffect, useState, type FormEvent } from 'react';
import { useAuth } from '../auth/AuthContext';
import { listarPedidos } from '../api/pedidos';
import { obtenerCredito } from '../api/credito';
import { ErrorMessage } from '../components/ErrorMessage';
import { estados, type ListaPedidos } from '../types/pedidos';
import type { Credito } from '../types/credito';
import { dinero, fecha, siguienteDia } from '../format';

export function PedidosPage() {
  const { sesion } = useAuth();
  const [filtros, setFiltros] = useState('');
  const [pagina, setPagina] = useState(1);
  const [lista, setLista] = useState<ListaPedidos | null>(null);
  const [credito, setCredito] = useState<Credito | null>(null);
  const [error, setError] = useState('');
  const [cargando, setCargando] = useState(true);
  const token = sesion!.token;
  const usuario = sesion!.usuario;

  useEffect(() => {
    let activo = true;
    setCargando(true);
    setError('');
    setLista(null);
    const query = new URLSearchParams(filtros);
    query.set('pagina', String(pagina));
    query.set('tamanoPagina', '10');
    async function cargar() {
      try {
        const [pedidos, saldo] = await Promise.all([
          listarPedidos(token, query),
          usuario.distribuidorId ? obtenerCredito(token, usuario.distribuidorId) : Promise.resolve(null),
        ]);
        if (activo) { setLista(pedidos); setCredito(saldo); }
      } catch (error) {
        if (activo) setError((error as Error).message);
      } finally {
        if (activo) setCargando(false);
      }
    }
    void cargar();
    return () => { activo = false; };
  }, [token, usuario.distribuidorId, filtros, pagina]);

  function filtrar(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const desde = String(data.get('desde') || '');
    const hasta = String(data.get('hasta') || '');
    if (desde && hasta && desde > hasta) { setError('Desde no puede ser posterior a Hasta.'); return; }
    const query = new URLSearchParams();
    for (const key of ['estado', 'distribuidorId']) {
      const value = String(data.get(key) || '').trim();
      if (value) query.set(key, value);
    }
    if (desde) query.set('desde', `${desde}T00:00:00-04:00`);
    if (hasta) query.set('hasta', `${siguienteDia(hasta)}T00:00:00-04:00`);
    setPagina(1);
    setFiltros(query.toString());
  }

  return <section>
    <div className="heading"><div><p className="eyebrow">Control de combustible</p><h1>Pedidos</h1></div>
      {usuario.rol === 'Distribuidor' && <a className="button" href="#/crear">Crear pedido</a>}
    </div>
    {credito && <p className="credit">Credito disponible <strong>{dinero(credito.creditoDisponible)}</strong>
      <span>Limite {dinero(credito.limiteCredito)} · Consumido {dinero(credito.creditoConsumido)}</span></p>}
    <form className="filters panel" onSubmit={filtrar}>
      <label>Estado<select name="estado" aria-label="Estado"><option value="">Todos</option>{estados.map(e => <option key={e}>{e}</option>)}</select></label>
      {usuario.rol === 'Operador' && <label>ID distribuidor<input name="distribuidorId" placeholder="GUID (opcional)" /></label>}
      <label>Desde<input type="date" name="desde" /></label>
      <label>Hasta<input type="date" name="hasta" /></label>
      <button disabled={cargando}>Filtrar</button>
    </form>
    <p className="muted">Filtro por fecha de creacion · Hora dominicana (UTC-04:00)</p>
    <ErrorMessage message={error} />
    {cargando && <p role="status">Cargando pedidos...</p>}
    {lista && <>
      {lista.items.length === 0 ? <p className="panel">No hay pedidos para estos filtros.</p> :
        <div className="table-scroll"><table><caption className="sr-only">Lista de pedidos</caption>
          <thead><tr><th>Distribuidor / pedido</th><th>Creacion</th><th>Entrega</th><th>Estado</th><th>Total</th></tr></thead>
          <tbody>{lista.items.map(p => <tr key={p.id}>
            <td><a href={`#/pedidos/${p.id}`}>{p.nombreDistribuidor}<small>{p.id.slice(0, 8)}</small></a></td>
            <td>{fecha(p.fechaCreacion)}</td><td>{fecha(p.fechaEntrega)}</td>
            <td><span className={`badge ${p.estado}`}>{p.estado}</span></td><td>{dinero(p.total)}</td>
          </tr>)}</tbody>
        </table></div>}
      <nav className="pagination" aria-label="Paginacion">
        <button className="secondary" disabled={pagina === 1} onClick={() => setPagina(pagina - 1)}>Anterior</button>
        <span>Pagina {pagina} de {Math.max(1, Math.ceil(lista.totalRegistros / 10))} · {lista.totalRegistros} pedidos</span>
        <button className="secondary" disabled={pagina * 10 >= lista.totalRegistros} onClick={() => setPagina(pagina + 1)}>Siguiente</button>
      </nav>
    </>}
  </section>;
}
