import type { LocationQueryValue } from 'vue-router'
import type { MapCountryDatum } from '@/components/map/types'

/** `?country=` → código ISO alpha-3 em maiúsculas, ou `null` se ausente/malformado. */
export function parseCountryQuery(value: LocationQueryValue | LocationQueryValue[] | undefined) {
  const raw = Array.isArray(value) ? value[0] : value
  const iso = raw?.trim().toUpperCase()
  return iso && /^[A-Z]{3}$/.test(iso) ? iso : null
}

/** Lista alternativa ao mapa: mais livros primeiro; empate por nome (pt-BR). */
export function sortCountriesForList<T extends MapCountryDatum>(countries: readonly T[]): T[] {
  return [...countries].sort(
    (a, b) => b.bookCount - a.bookCount || a.name.localeCompare(b.name, 'pt-BR'),
  )
}
