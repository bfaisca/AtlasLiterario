import type { MapRouteType, MapType } from '@/types/api'

export function toRouteMapType(mapType: MapType): MapRouteType {
  return mapType === 'Authors' ? 'authors' : 'origins'
}

export function fromRouteMapType(value: unknown): MapType {
  return value === 'origins' ? 'Origins' : 'Authors'
}
