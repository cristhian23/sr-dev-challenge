import { useRef, useState, type FormEvent } from 'react';
import { useAuth } from '../auth/AuthContext';
import { ErrorMessage } from '../components/ErrorMessage';

export function LoginPage() {
  const { login, aviso } = useAuth();
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const enviando = useRef(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (enviando.current) return;
    const form = event.currentTarget;
    const data = new FormData(form);
    enviando.current = true;
    setBusy(true);
    setError('');
    try {
      await login(String(data.get('usuario')), String(data.get('password')));
    } catch (error) {
      setError((error as Error).message);
    } finally {
      form.reset();
      enviando.current = false;
      setBusy(false);
    }
  }

  return <section className="login panel">
    <p className="eyebrow">Acceso al sistema</p>
    <h1>Gestion de pedidos</h1>
    <p>Combustibles · Distribuidores y operaciones</p>
    <ErrorMessage message={error || aviso} />
    <form onSubmit={submit}>
      <label>Usuario<input name="usuario" autoComplete="username" required autoFocus /></label>
      <label>Contrasena<input name="password" type="password" autoComplete="current-password" required /></label>
      <button disabled={busy}>{busy ? 'Iniciando sesion...' : 'Iniciar sesion'}</button>
    </form>
    <p className="muted">La sesion se guarda solo en memoria. Recargar requiere iniciar sesion.</p>
  </section>;
}
