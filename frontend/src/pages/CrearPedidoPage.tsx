import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useAuth } from '../auth/AuthContext';
import { obtenerProductos } from '../api/productos';
import { obtenerCredito } from '../api/credito';
import { crearPedido } from '../api/pedidos';
import { ApiError } from '../api/client';
import { ErrorMessage } from '../components/ErrorMessage';
import { PedidoLineasForm, type LineaForm } from '../components/PedidoLineasForm';
import type { Producto } from '../types/productos';
import type { Credito } from '../types/credito';
import { decimal6, dinero, subtotalCentavos } from '../format';

export function CrearPedidoPage() {
  const { sesion } = useAuth();
  const [productos, setProductos] = useState<Producto[]>([]);
  const [credito, setCredito] = useState<Credito | null>(null);
  const [lineas, setLineas] = useState<LineaForm[]>([{ productoId: '', galones: '500' }]);
  const [entrega, setEntrega] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const enviando = useRef(false);
  const activo = useRef(true);
  const token = sesion!.token;
  const distribuidorId = sesion!.usuario.distribuidorId!;

  useEffect(() => {
    let cargando = true;
    activo.current = true;
    Promise.all([obtenerProductos(token), obtenerCredito(token, distribuidorId)])
      .then(([productos, credito]) => { if (cargando) { setProductos(productos); setCredito(credito); } })
      .catch(error => { if (cargando) setError(error.message); })
      .finally(() => { if (cargando) setLoading(false); });
    return () => { cargando = false; activo.current = false; };
  }, [token, distribuidorId]);

  let total = 0n;
  let cantidadesValidas = true;
  try {
    for (const linea of lineas) {
      const producto = productos.find(p => p.id === linea.productoId);
      if (producto) total += subtotalCentavos(linea.galones, producto.precioPorGalon);
      decimal6(linea.galones);
    }
  } catch { cantidadesValidas = false; }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (enviando.current) return;
    setError('');
    try {
      if (!cantidadesValidas) throw new Error('Galones: usa hasta seis decimales.');
      const cantidades = lineas.map(l => decimal6(l.galones));
      if (cantidades.some(g => g < 500_000_000n)) throw new Error('Cada linea requiere al menos 500 galones.');
      if (cantidades.reduce((a, b) => a + b, 0n) > 9_000_000_000n) throw new Error('El pedido no puede superar 9000 galones.');
      if (lineas.some(l => !l.productoId) || new Set(lineas.map(l => l.productoId)).size !== lineas.length)
        throw new Error('Selecciona productos distintos.');
      const fechaEntrega = `${entrega}:00-04:00`;
      const instant = new Date(fechaEntrega);
      if (!Number.isFinite(instant.getTime()) || instant.getTime() < Date.now() + 86_400_000)
        throw new Error('La entrega debe ser al menos 24 horas despues de ahora.');
      if (new Date(`${entrega.slice(0, 10)}T00:00:00Z`).getUTCDay() === 0)
        throw new Error('No se permiten entregas en domingo.');
      if (credito && total > BigInt(Math.round(credito.creditoDisponible * 100)))
        throw new Error('El total estimado supera el credito disponible.');
      enviando.current = true;
      setBusy(true);
      const pedido = await crearPedido(token, { fechaEntrega,
        lineas: lineas.map(l => ({ productoId: l.productoId, galones: Number(l.galones) })),
      });
      if (activo.current) window.location.hash = `/pedidos/${pedido.id}`;
    } catch (error) {
      let mensaje = (error as Error).message;
      if (error instanceof ApiError && error.status === 409) {
        mensaje += ' El saldo pudo cambiar; revisa el credito actualizado antes de intentar de nuevo.';
        try { setCredito(await obtenerCredito(token, distribuidorId)); }
        catch (refreshError) { mensaje += ` ${(refreshError as Error).message}`; }
      }
      setError(mensaje);
    } finally {
      enviando.current = false;
      setBusy(false);
    }
  }

  return <section><a href="#/pedidos">← Volver a pedidos</a><h1>Crear pedido</h1>
    <ErrorMessage message={error} />
    {loading ? <p role="status">Cargando productos y credito...</p> : credito && <form className="panel" onSubmit={submit}>
      <fieldset disabled={busy}>
        <legend>Entrega y combustible</legend>
        <label>Fecha de entrega (hora dominicana)<input type="datetime-local" required value={entrega}
          onChange={e => setEntrega(e.target.value)} /></label>
        <p className="muted">Minimo 24 horas · No domingo · 1 a 4 productos · 500 galones por linea · Maximo 9000</p>
        <PedidoLineasForm productos={productos} lineas={lineas} onChange={setLineas} />
        <div className="totals"><p>Total estimado <strong>{cantidadesValidas ? dinero(Number(total) / 100) : 'Revisa los galones'}</strong></p>
          <p>Credito disponible <strong>{dinero(credito.creditoDisponible)}</strong></p></div>
        <p className="muted">Precios del catalogo. El servidor confirma los importes y el credito al guardar.</p>
        <button disabled={busy}>{busy ? 'Guardando pedido...' : 'Guardar pedido'}</button>
      </fieldset>
    </form>}
  </section>;
}
