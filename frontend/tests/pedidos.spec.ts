import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { dinero } from '../src/format';

const password = process.env.QA_PASSWORD;
const api = process.env.QA_API_URL;
const norte = '11111111-1111-1111-1111-111111111111';
const productoId = 'aaaaaaaa-0000-0000-0000-000000000001';

function entrega(dia = 2) {
  const date = new Date(Date.now() + dia * 86_400_000);
  while (date.getUTCDay() === 0) date.setUTCDate(date.getUTCDate() + 1);
  return `${date.toISOString().slice(0, 10)}T12:00`;
}

async function login(page: Page, usuario: string) {
  await page.getByLabel('Usuario', { exact: true }).fill(usuario);
  await page.getByLabel('Contrasena').fill(password!);
  await page.getByRole('button', { name: 'Iniciar sesion', exact: true }).click();
}

async function token(request: APIRequestContext, nombreUsuario: string) {
  const response = await request.post(`${api}/api/auth/login`, { data: { nombreUsuario, password } });
  expect(response.status()).toBe(200);
  return (await response.json()).token as string;
}

async function create(request: APIRequestContext, auth: string, galones = 500) {
  const response = await request.post(`${api}/api/pedidos`, {
    headers: { Authorization: `Bearer ${auth}` },
    data: { fechaEntrega: `${entrega()}:00-04:00`, lineas: [{ productoId, galones }] },
  });
  expect(response.status()).toBe(201);
  return await response.json();
}

test.beforeAll(() => {
  if (!password || api !== 'http://127.0.0.1:5081' ||
      !/^Refidomsa_QA_[a-f0-9]{32}$/.test(process.env.QA_DATABASE || '')) {
    throw new Error('Run browser QA through scripts/qa-pedidos.ps1 -Bloque 5 -Frontend only.');
  }
});

test('real SQL/API: roles, creation, filtering, transitions, conflicts and session errors', async ({ page, request }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' && !/Failed to load resource|net::ERR_/.test(message.text())) errors.push(message.text());
  });
  await page.goto('/');
  await page.getByLabel('Usuario', { exact: true }).fill('distribuidor.norte');
  await page.getByLabel('Contrasena').fill('incorrecta');
  await page.getByRole('button', { name: 'Iniciar sesion', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  let logins = 0;
  await page.route('**/api/auth/login', async route => {
    logins++;
    await new Promise(resolve => setTimeout(resolve, 400));
    await route.continue();
  });
  await login(page, 'distribuidor.norte');
  await expect(page.getByRole('button', { name: 'Iniciando sesion...' })).toBeDisabled();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Pedidos', exact: true })).toBeVisible();
  expect(logins).toBe(1);
  await page.unroute('**/api/auth/login');
  await expect(page.getByText('Credito disponible', { exact: false })).toBeVisible();
  await expect(page.getByLabel('ID distribuidor')).toHaveCount(0);
  await page.getByRole('button', { name: 'Siguiente' }).click();
  await expect(page.getByText(/Pagina 2 de/)).toBeVisible();
  await page.getByRole('button', { name: 'Anterior' }).click();
  await expect(page.getByText(/Pagina 1 de/)).toBeVisible();
  const today = new Date(Date.now() - 4 * 3_600_000).toISOString().slice(0, 10);
  const yesterday = new Date(Date.now() - 28 * 3_600_000).toISOString().slice(0, 10);
  await page.getByLabel('Desde').fill(yesterday);
  await page.getByLabel('Hasta').fill(today);
  await page.getByRole('button', { name: 'Filtrar', exact: true }).click();
  await expect(page.locator('tbody tr')).toHaveCount(10);
  await page.getByLabel('Estado', { exact: true }).selectOption('Pendiente');
  await page.getByRole('button', { name: 'Filtrar', exact: true }).click();
  await expect(page.getByText(/Pagina 1 de/)).toBeVisible();
  await expect(page.locator('tbody tr')).toHaveCount(3);
  await page.getByLabel('Desde').fill('2099-01-01');
  await page.getByLabel('Hasta').fill('2099-01-02');
  await page.getByRole('button', { name: 'Filtrar', exact: true }).click();
  await expect(page.getByText('No hay pedidos para estos filtros.')).toBeVisible();
  await page.getByRole('link', { name: 'Crear pedido' }).click();
  await expect(page.getByRole('button', { name: 'Guardar pedido' })).toBeVisible();
  await page.getByLabel('Producto 1', { exact: true }).selectOption(productoId);
  await page.getByLabel('Fecha de entrega').fill(entrega());
  await page.getByRole('button', { name: 'Agregar linea' }).click();
  await expect(page.getByLabel('Producto 2')).toBeVisible();
  await expect(page.getByLabel('Producto 2').locator(`option[value="${productoId}"]`)).toHaveAttribute('disabled', '');
  await page.getByRole('button', { name: 'Quitar linea 2' }).click();
  await expect(page.getByRole('button', { name: 'Quitar linea 1' })).toBeDisabled();
  for (let i = 0; i < 3; i++) await page.getByRole('button', { name: 'Agregar linea' }).click();
  await expect(page.getByRole('button', { name: 'Agregar linea' })).toBeDisabled();
  const options = await page.getByLabel('Producto 1', { exact: true }).locator('option').evaluateAll(elements =>
    elements.map(el => (el as HTMLOptionElement).value).filter(Boolean));
  for (let i = 0; i < 4; i++) {
    await page.getByLabel(`Producto ${i + 1}`, { exact: true }).selectOption(options[i]);
    await page.getByLabel(`Galones ${i + 1}`).fill('3000');
  }
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByRole('alert')).toContainText('9000 galones');
  for (let i = 4; i > 1; i--) await page.getByRole('button', { name: `Quitar linea ${i}` }).click();
  await page.getByLabel('Producto 1', { exact: true }).selectOption(productoId);
  await page.getByLabel('Galones 1').fill('499');
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  expect(await page.getByLabel('Galones 1').evaluate((el: HTMLInputElement) => el.validity.rangeUnderflow)).toBe(true);
  await page.getByLabel('Galones 1').fill('500.0000001');
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  expect(await page.getByLabel('Galones 1').evaluate((el: HTMLInputElement) => el.validity.stepMismatch)).toBe(true);
  await page.getByLabel('Galones 1').fill('500');
  await expect(page.locator('.totals')).toContainText(dinero(145050));
  await page.getByLabel('Fecha de entrega').fill(new Date().toISOString().slice(0, 16));
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByRole('alert')).toContainText('24 horas');
  const sunday = new Date(Date.now() + 7 * 86_400_000);
  while (sunday.getUTCDay() !== 0) sunday.setUTCDate(sunday.getUTCDate() + 1);
  await page.getByLabel('Fecha de entrega').fill(`${sunday.toISOString().slice(0, 10)}T12:00`);
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByRole('alert')).toContainText('domingo');
  await page.getByLabel('Fecha de entrega').fill(entrega());

  // Delay the real request to verify loading and duplicate-submit protection.
  let posts = 0;
  await page.route('**/api/pedidos', async route => {
    if (route.request().method() === 'POST') { posts++; await new Promise(resolve => setTimeout(resolve, 400)); }
    await route.continue();
  });
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByRole('button', { name: 'Guardando pedido...' })).toBeDisabled();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Detalle del pedido' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cancelar pedido' })).toBeVisible();
  await expect(page.locator('.totals')).toContainText(dinero(1419800));
  expect(posts).toBe(1);
  const createdId = page.url().split('/').pop()!;
  await page.unroute('**/api/pedidos');
  await page.getByRole('button', { name: 'Cancelar pedido' }).click();
  await expect(page.locator('.badge')).toHaveText('Cancelado');
  await expect(page.locator('.totals')).toContainText(dinero(1564850));
  await expect(page.getByRole('button', { name: 'Cancelar pedido' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Cerrar sesion' }).click();
  await login(page, 'distribuidor.sur');
  await page.evaluate(id => { window.location.hash = `/pedidos/${id}`; }, createdId);
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cancelar pedido' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Cerrar sesion' })).toBeVisible();
  await page.getByRole('button', { name: 'Cerrar sesion' }).click();
  await login(page, 'operador');
  await expect(page.getByRole('link', { name: 'Crear pedido' })).toHaveCount(0);
  await page.getByLabel('ID distribuidor').fill(norte);
  await page.getByLabel('Estado', { exact: true }).selectOption('Pendiente');
  await page.getByRole('button', { name: 'Filtrar', exact: true }).click();
  await expect(page.locator('tbody tr')).toHaveCount(3);
  await page.locator('tbody a').first().click();
  await expect(page.getByRole('button', { name: 'Cancelar pedido' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Rechazar', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('motivo');
  await page.getByLabel('Motivo de rechazo', { exact: true }).fill('Revision QA real');
  await page.getByRole('button', { name: 'Rechazar', exact: true }).click();
  await expect(page.locator('.badge')).toHaveText('Rechazado');
  await expect(page.getByText('Revision QA real', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aprobar', exact: true })).toHaveCount(0);
  await page.getByRole('link', { name: /Volver a pedidos/ }).click();
  await page.getByLabel('Estado', { exact: true }).selectOption('Pendiente');
  await page.getByRole('button', { name: 'Filtrar', exact: true }).click();
  await page.locator('tbody a').first().click();
  let patches = 0;
  await page.route('**/estado', async route => {
    patches++;
    await new Promise(resolve => setTimeout(resolve, 400));
    await route.continue();
  });
  await page.getByRole('button', { name: 'Aprobar', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Aprobar', exact: true })).toBeDisabled();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('button', { name: 'Despachar', exact: true })).toBeVisible();
  expect(patches).toBe(1);
  await page.unroute('**/estado');
  await page.getByRole('button', { name: 'Despachar', exact: true }).click();
  await expect(page.locator('.badge')).toHaveText('Despachado');
  await expect(page.getByRole('button', { name: 'Despachar', exact: true })).toHaveCount(0);

  const auth = await token(request, 'distribuidor.norte');
  const stale = await create(request, auth);
  await page.evaluate(id => { window.location.hash = `/pedidos/${id}`; }, stale.id);
  await expect(page.getByRole('button', { name: 'Aprobar', exact: true })).toBeVisible();
  const operatorToken = await token(request, 'operador');
  const updated = await request.patch(`${api}/api/pedidos/${stale.id}/estado`, {
    headers: { Authorization: `Bearer ${operatorToken}` }, data: { nuevoEstado: 'Aprobado' },
  });
  expect(updated.status()).toBe(200);
  await page.getByRole('button', { name: 'Aprobar', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Conflicto');
  await expect(page.locator('.badge')).toHaveText('Aprobado');
  await expect(page.getByRole('button', { name: 'Cerrar sesion' })).toBeVisible();

  // Real backend validation error: alter only the outgoing PATCH payload, never mock a response.
  await page.route('**/estado', route => route.continue({ postData: JSON.stringify({ nuevoEstado: 'Invalido' }) }));
  await page.getByRole('button', { name: 'Despachar', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.locator('.badge')).toHaveText('Aprobado');
  await page.unroute('**/estado');
  await page.route('**/estado', route => route.continue({ postData: JSON.stringify({ nuevoEstado: 'Cancelado' }) }));
  await page.getByRole('button', { name: 'Despachar', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cerrar sesion' })).toBeVisible();
  await expect(page.locator('.badge')).toHaveText('Aprobado');
  await page.unroute('**/estado');
  await page.getByRole('button', { name: 'Cerrar sesion' }).click();
  await login(page, 'distribuidor.norte');
  await page.getByRole('link', { name: 'Crear pedido' }).click();
  await expect(page.getByRole('button', { name: 'Guardar pedido' })).toBeVisible();
  await page.getByLabel('Producto 1', { exact: true }).selectOption(productoId);
  await page.getByLabel('Fecha de entrega').fill(entrega());
  const balanceResponse = await request.get(`${api}/api/distribuidores/${norte}/credito`, { headers: { Authorization: `Bearer ${auth}` } });
  const balance = await balanceResponse.json();
  const gallons = Math.floor(balance.creditoDisponible / 290.1);
  await create(request, auth, gallons);
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await expect(page.getByRole('alert')).toContainText('saldo pudo cambiar');
  await expect(page.locator('.totals')).toContainText(dinero(balance.creditoDisponible - gallons * 290.1));
  await expect(page.getByRole('heading', { name: 'Crear pedido' })).toBeVisible();

  await page.getByRole('link', { name: /Volver a pedidos/ }).click();
  await page.context().setOffline(true);
  await page.getByRole('link', { name: 'Crear pedido' }).click();
  await expect(page.getByRole('alert')).toContainText('No se pudo conectar');
  await page.context().setOffline(false);
  await page.getByRole('link', { name: /Volver a pedidos/ }).click();
  await expect(page.getByRole('button', { name: 'Filtrar', exact: true })).toBeEnabled();
  await page.route('**/api/pedidos?*', route => route.continue({
    headers: { ...route.request().headers(), authorization: `Bearer ${process.env.QA_EXPIRED_TOKEN}` },
  }));
  await page.getByLabel('Estado', { exact: true }).selectOption('Cancelado');
  await page.getByRole('button', { name: 'Filtrar', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Gestion de pedidos' })).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('expirado');
  await page.unroute('**/api/pedidos?*');
  await login(page, 'operador');
  await page.reload();
  await expect(page.getByRole('button', { name: 'Iniciar sesion', exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByLabel('Usuario', { exact: true }).focus();
  await page.keyboard.press('Tab');
  await expect(page.getByLabel('Contrasena')).toBeFocused();
  await login(page, 'operador');
  await expect(page.getByRole('heading', { name: 'Pedidos', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});

test('delayed old requests do not expire or redirect a newer session', async ({ page }) => {
  await page.goto('/');
  await login(page, 'distribuidor.sur');
  await expect(page.getByRole('button', { name: 'Filtrar', exact: true })).toBeEnabled();
  let release!: () => void;
  let started!: () => void;
  const waiting = new Promise<void>(resolve => { release = resolve; });
  const intercepted = new Promise<void>(resolve => { started = resolve; });
  let first = true;
  await page.route('**/api/pedidos?*', async route => {
    if (!first) { await route.continue(); return; }
    first = false;
    started();
    await waiting;
    await route.continue({ headers: { ...route.request().headers(), authorization: `Bearer ${process.env.QA_EXPIRED_TOKEN}` } });
  });
  await page.getByLabel('Estado', { exact: true }).selectOption('Pendiente');
  await page.getByRole('button', { name: 'Filtrar', exact: true }).click();
  await intercepted;
  await page.getByRole('button', { name: 'Cerrar sesion' }).click();
  await login(page, 'operador');
  await expect(page.getByRole('heading', { name: 'Pedidos', exact: true })).toBeVisible();
  const expired = page.waitForResponse(response => response.url().includes('/api/pedidos?') && response.status() === 401);
  release();
  await expired;
  await expect(page.getByRole('button', { name: 'Cerrar sesion' })).toBeVisible();
  await expect(page.getByText('Operador Refidomsa', { exact: false })).toBeVisible();
  await page.unroute('**/api/pedidos?*');
  await page.getByRole('button', { name: 'Cerrar sesion' }).click();
  await login(page, 'distribuidor.sur');
  await page.getByRole('link', { name: 'Crear pedido' }).click();
  await expect(page.getByRole('button', { name: 'Guardar pedido' })).toBeVisible();
  await page.getByLabel('Producto 1', { exact: true }).selectOption(productoId);
  await page.getByLabel('Fecha de entrega').fill(entrega());
  let releasePost!: () => void;
  let startedPost!: () => void;
  const postWaiting = new Promise<void>(resolve => { releasePost = resolve; });
  const postIntercepted = new Promise<void>(resolve => { startedPost = resolve; });
  await page.route('**/api/pedidos', async route => { startedPost(); await postWaiting; await route.continue(); });
  await page.getByRole('button', { name: 'Guardar pedido' }).click();
  await postIntercepted;
  await page.getByRole('button', { name: 'Cerrar sesion' }).click();
  await login(page, 'operador');
  await expect(page.getByRole('heading', { name: 'Pedidos', exact: true })).toBeVisible();
  const created = page.waitForResponse(response => response.url().endsWith('/api/pedidos') && response.status() === 201);
  releasePost();
  await created;
  await expect(page.getByRole('heading', { name: 'Pedidos', exact: true })).toBeVisible();
  await expect(page).toHaveURL(/#\/pedidos$/);
});
