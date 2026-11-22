/** Contratos de eventos do componente de mapa (independentes da biblioteca visual). */
export interface MapCountryDatum {
  iso: string
  name: string
  bookCount: number
  authorCount?: number
}

export interface CountryHoverEvent {
  iso: string
  /** Nome em português do dado, ou do GeoJSON quando o país não tem livros. */
  name: string
  /** `null` = país sem livros neste mapa. */
  datum: MapCountryDatum | null
  /** Posição em pixels relativa ao contêiner do mapa (para tooltip). */
  point: { x: number; y: number }
}
