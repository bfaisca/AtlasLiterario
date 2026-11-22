import type { Continent, GeoSource, MapType, ReadingStatus } from '@/types/api'

export const READING_STATUS_LABELS: Record<ReadingStatus, string> = {
  WantToRead: 'Quero ler',
  Reading: 'Lendo',
  Read: 'Lido',
  Abandoned: 'Abandonado',
}

export const CONTINENT_LABELS: Record<Continent, string> = {
  Africa: 'África',
  Asia: 'Ásia',
  Europe: 'Europa',
  NorthAmerica: 'América do Norte',
  SouthAmerica: 'América do Sul',
  Oceania: 'Oceania',
  Antarctica: 'Antártida',
}

export const MAP_TYPE_LABELS: Record<MapType, string> = {
  Authors: 'Nacionalidades dos autores',
  Origins: 'Origem das obras',
}

export const MAP_TYPE_SHORT_LABELS: Record<MapType, string> = {
  Authors: 'Autores',
  Origins: 'Obras',
}

export const GEO_SOURCE_LABELS: Record<GeoSource, string> = {
  Manual: 'Correção manual',
  Wikidata: 'Wikidata',
  OpenLibrary: 'Open Library',
  GoogleBooks: 'Google Books',
}

export function pluralize(count: number, singular: string, plural: string): string {
  return `${count.toLocaleString('pt-BR')} ${count === 1 ? singular : plural}`
}
