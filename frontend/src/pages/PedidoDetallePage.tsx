import { useEffect, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { obtenerPedido, cambiarEstado } from '../api/pedidos';
import { obtenerCredito } from '../api/credito';
import { ApiError } from '../api/client';
import { ErrorMessage } from '../components/ErrorMessage';
import type { Estado, PedidoDetalle } from '../types/pedidos';
import type { Credito } from '../types/credito';
import { dinero, fecha } from '../format';

export function PedidoDetallePage({ id }: { id: string }) {
  const { sesion } = useAuth();
  const [pedido, setPedido] = useState<PedidoDetalle | null>(null);
  const [credito, setCredito] = useState<Credito | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [motivo, setMotivo] = useState('');
  const enviando = useRef(false);
  const token = sesion!.token;
  const usuario = sesion!.usuario;

  useEffect(() => {
    let activo = true;
    async function cargar() {
      try {
        const detalle = await obtenerPedido(token, id);
        const saldo = await obtenerCredito(token, detalle.distribuidorId);
        if (activo) { setPedido(detalle); setCredito(saldo); }
      } catch (error) { if (activo) setError((error as Error).message); }
      finally { if (activo) setLoading(false); }
    }
    void cargar();
    return () => { activo = false; };
  }, [token, id]);

  async function actuar(estado: Estado) {
    if (enviando.current || !pedido) return;
    if (estado === 'Rechazado' && !motivo.trim()) { setError('Escribe el motivo del rechazo.'); return; }
    enviando.current = true;
    setBusy(true);
    setError('');
    try {
      const actualizado = await cambiarEstado(token, id, estado, estado === 'Rechazado' ? motivo : undefined);
      setPedido(actualizado);
      setCredito(await obtenerCredito(token, actualizado.distribuidorId));
    } catch (error) {
      let mensaje = (error as Error).message;
      if (error instanceof ApiError && error.status === 409) {
        mensaje += ' Conflicto: se consultaron de nuevo el pedido y el credito. Revisa el estado antes de actuar.';
        try {
          const actualizado = await obtenerPedido(token, id);
          setPedido(actualizado);
          setCredito(await obtenerCredito(token, actualizado.distribuidorId));
        } catch (refreshError) { mensaje += ` ${(refreshError as Error).message}`; }
      }
      setError(mensaje);
    } finally { enviando.current = false; setBusy(false); }
  }

  return <section><a href="#/pedidos">← Volver a pedidos</a><h1>Detalle del pedido</h1>
    <ErrorMessage message={error} />
    {loading && <p role="status">Cargando detalle...</p>}
    {pedido && <div className="panel">
      <div className="heading"><h2>{pedido.nombreDistribuidor}</h2><span className={`badge ${pedido.estado}`}>{pedido.estado}</span></div>
      <p className="muted id">{pedido.id}</p>
      <dl className="facts"><div><dt>Creacion</dt><dd>{fecha(pedido.fechaCreacion)}</dd></div>
        <div><dt>Entrega</dt><dd>{fecha(pedido.fechaEntrega)}</dd></div>
        <div><dt>Cambio de estado</dt><dd>{fecha(pedido.fechaCambioEstado)}</dd></div></dl>
      {pedido.motivoRechazo && <p><strong>Motivo de rechazo:</strong> {pedido.motivoRechazo}</p>}
      <div className="table-scroll"><table><caption>Precios aplicados al crear el pedido</caption>
        <thead><tr><th>Producto</th><th>Galones</th><th>Precio / galon</th><th>Subtotal</th></tr></thead>
        <tbody>{pedido.lineas.map(l => <tr key={l.productoId}><td>{l.nombreProducto}</td><td>{l.galones}</td>
          <td>{dinero(l.precioPorGalon)}</td><td>{dinero(l.subtotal)}</td></tr>)}</tbody></table></div>
      <div className="totals"><p>Total <strong>{dinero(pedido.total)}</strong></p>
        {credito && <p>Credito disponible <strong>{dinero(credito.creditoDisponible)}</strong></p>}</div>
      <div className="actions" aria-busy={busy}>
        {usuario.rol === 'Distribuidor' && usuario.distribuidorId === pedido.distribuidorId && pedido.estado === 'Pendiente' &&
          <button disabled={busy} className="danger" onClick={() => void actuar('Cancelado')}>Cancelar pedido</button>}
        {usuario.rol === 'Operador' && pedido.estado === 'Pendiente' && <>
          <button disabled={busy} onClick={() => void actuar('Aprobado')}>Aprobar</button>
          <label>Motivo de rechazo<textarea maxLength={1000} value={motivo} disabled={busy} onChange={e => setMotivo(e.target.value)} /></label>
          <button disabled={busy} className="danger" onClick={() => void actuar('Rechazado')}>Rechazar</button>
        </>}
        {usuario.rol === 'Operador' && pedido.estado === 'Aprobado' &&
          <button disabled={busy} onClick={() => void actuar('Despachado')}>Despachar</button>}
        {busy && <p role="status">Actualizando pedido...</p>}
      </div>
    </div>}
  </section>;
}
