// Cliente mínimo da API REST do Atlas Literário (somente o que a ferramenta usa).
const sleepDefault = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

export class ApiError extends Error {
  constructor(status, body, message) {
    super(message ?? body?.detail ?? body?.title ?? `HTTP ${status}`);
    this.status = status;
    this.body = body;
    this.code = body?.code ?? null;
  }
}

const RETRYABLE = new Set([429, 502, 503, 504]);

export class AtlasClient {
  constructor({ baseUrl, email, password, fetchImpl = fetch, sleep = sleepDefault, onRetry = () => {} }) {
    this.baseUrl = baseUrl.replace(/\/+$/, '');
    this.email = email;
    this.password = password;
    this.fetch = fetchImpl;
    this.sleep = sleep;
    this.onRetry = onRetry;
    this.auth = null;
  }

  async login() {
    const res = await this.#send('POST', '/api/auth/login', { body: { email: this.email, password: this.password }, auth: false, timeoutMs: 20000 });
    this.auth = { token: res.accessToken, expiresAt: Date.parse(res.accessTokenExpiresAt) };
    return res.user;
  }

  async #ensureToken() {
    if (!this.auth || this.auth.expiresAt - Date.now() < 120000) await this.login(); // tokens duram 60 min
  }

  search(q, type = 'all', limit = 20) {
    return this.request('GET', '/api/books/search', { query: { q, type, limit }, timeoutMs: 45000 });
  }

  /** Adiciona à biblioteca. Retorna `{ item, attempts }`; 409 vira `ApiError` com `libraryItemId`. */
  async addToLibrary(payload) {
    return this.request('POST', '/api/library', { body: payload, timeoutMs: 60000, withAttempts: true });
  }

  getLibraryItem(id) {
    return this.request('GET', `/api/library/${id}`, { timeoutMs: 20000 });
  }

  enrich(bookId) {
    return this.request('POST', `/api/books/${bookId}/enrich`, { timeoutMs: 60000, retries: 1 });
  }

  async request(method, path, { body, query, timeoutMs = 30000, retries = 3, withAttempts = false } = {}) {
    await this.#ensureToken();
    const delays = [2000, 6000, 15000];
    let attempt = 0;
    for (;;) {
      attempt++;
      try {
        const data = await this.#send(method, path, { body, query, timeoutMs });
        return withAttempts ? { item: data, attempts: attempt } : data;
      } catch (err) {
        const transient = err instanceof ApiError ? RETRYABLE.has(err.status) : true; // rede/timeout
        if (err instanceof ApiError && err.status === 401 && attempt === 1) {
          this.auth = null;
          await this.#ensureToken();
          continue;
        }
        if (!transient || attempt > retries) {
          if (err instanceof ApiError && err.status === 409) err.attempts = attempt;
          throw err;
        }
        const wait = Math.min(err.retryAfterMs ?? delays[attempt - 1] ?? 15000, 30000);
        this.onRetry({ method, path, attempt, wait, reason: err.message });
        await this.sleep(wait);
      }
    }
  }

  async #send(method, path, { body, query, auth = true, timeoutMs }) {
    const url = new URL(this.baseUrl + path);
    for (const [k, v] of Object.entries(query ?? {})) url.searchParams.set(k, String(v));
    const headers = { Accept: 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (auth) headers.Authorization = `Bearer ${this.auth.token}`;
    let res;
    try {
      res = await this.fetch(url, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: AbortSignal.timeout(timeoutMs),
      });
    } catch (err) {
      throw new Error(err?.name === 'TimeoutError' ? `tempo esgotado em ${method} ${path}` : `falha de rede em ${method} ${path}: ${err?.cause?.code ?? err?.message}`);
    }
    const text = await res.text();
    let json = null;
    try { json = text ? JSON.parse(text) : null; } catch { /* resposta não-JSON */ }
    if (!res.ok) {
      const error = new ApiError(res.status, json);
      const retryAfter = Number(res.headers.get('retry-after'));
      if (retryAfter > 0) error.retryAfterMs = retryAfter * 1000;
      throw error;
    }
    return json;
  }
}
