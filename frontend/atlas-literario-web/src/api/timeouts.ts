/** Timeout padrão do cliente HTTP (ver `http.ts`). */
export const DEFAULT_TIMEOUT_MS = 20_000
/**
 * Chamadas que podem consultar fontes externas (Wikidata etc.) e, por isso, demoram mais:
 * POST /library, POST /books (cadastro manual), POST /books/{id}/enrich e o envio de capa
 * (POST /books/{id}/cover, upload multipart).
 * Aplicado por requisição na camada de API, nunca globalmente.
 */
export const SLOW_REQUEST_TIMEOUT_MS = 45_000
