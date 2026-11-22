import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import type { CountriesGeoJson, CountryPointsGeoJson } from '../useCountriesGeo'

/** Valida os arquivos gerados por `npm run geo:fetch` (scripts/fetch-geo.mjs). */
const read = <T>(file: string): T =>
  JSON.parse(readFileSync(resolve(__dirname, '../../../public/geo', file), 'utf8')) as T

const polygons = read<CountriesGeoJson>('countries-50m.geojson')
const points = read<CountryPointsGeoJson>('country-points.geojson')
const polygonByIso = new Map(polygons.features.map((f) => [f.properties.iso, f]))
const pointIsos = new Set(points.features.map((f) => f.properties.iso))

/** Todas as coordenadas [lng, lat] de uma geometria. */
function positions(coordinates: unknown): number[][] {
  const flat: number[][] = []
  const walk = (node: unknown) => {
    if (Array.isArray(node) && typeof node[0] === 'number') flat.push(node as number[])
    else if (Array.isArray(node)) node.forEach(walk)
  }
  walk(coordinates)
  return flat
}

describe('countries-50m.geojson', () => {
  it('tem uma única feature por iso (alpha-3)', () => {
    const isos = polygons.features.map((f) => f.properties.iso)
    expect(new Set(isos).size).toBe(isos.length)
    expect(isos.every((iso) => /^[A-Z]{3}$/.test(iso))).toBe(true)
  })

  it('traz Maurício, Guiana Francesa e os demais territórios franceses separados', () => {
    for (const iso of ['MUS', 'GUF', 'GLP', 'MTQ', 'REU', 'MYT', 'XKX', 'SJM']) {
      expect(polygonByIso.has(iso), iso).toBe(true)
    }
    expect(polygonByIso.has('KOS')).toBe(false)
  })

  it('FRA é só a França metropolitana (+ Córsega), sem a Guiana e demais territórios', () => {
    const fra = positions(polygonByIso.get('FRA')!.geometry.coordinates)
    const lngs = fra.map((p) => p[0]!)
    const lats = fra.map((p) => p[1]!)
    expect(Math.min(...lngs)).toBeGreaterThan(-6) // Guiana ≈ -54; Guadalupe/Martinica ≈ -61
    expect(Math.max(...lngs)).toBeLessThan(10) // Reunião ≈ 55; Mayotte ≈ 45
    expect(Math.min(...lats)).toBeGreaterThan(41) // Guiana/Reunião/Mayotte < 6
    expect(Math.max(...lats)).toBeLessThan(52)
    expect(Math.max(...lngs)).toBeGreaterThan(9) // Córsega presente
  })

  it('GUF fica na costa norte da América do Sul', () => {
    const guf = positions(polygonByIso.get('GUF')!.geometry.coordinates)
    expect(guf.every(([lng, lat]) => lng! < -51 && lng! > -55 && lat! > 2 && lat! < 6)).toBe(true)
  })

  it('coordenadas são números finitos dentro dos limites', () => {
    for (const feature of polygons.features) {
      for (const [lng, lat] of positions(feature.geometry.coordinates)) {
        expect(Math.abs(lng!)).toBeLessThanOrEqual(180)
        expect(Math.abs(lat!)).toBeLessThanOrEqual(90)
      }
    }
  })

  it('mantém o arquivo leve (< 700 KB)', () => {
    const size = readFileSync(
      resolve(__dirname, '../../../public/geo/countries-50m.geojson'),
    ).length
    expect(size).toBeLessThan(700 * 1024)
  })
})

describe('country-points.geojson', () => {
  it('todo ponto é Point com iso, sem iso duplicado', () => {
    expect(points.features.length).toBeGreaterThan(50)
    for (const feature of points.features) {
      expect(feature.geometry.type).toBe('Point')
      expect(feature.properties.iso).toMatch(/^[A-Z]{3}$/)
      expect(feature.geometry.coordinates).toHaveLength(2)
    }
    expect(pointIsos.size).toBe(points.features.length)
  })

  it('tem marcador para países pequenos como Maurício e não para a Guiana Francesa', () => {
    for (const iso of ['MUS', 'SYC', 'MLT', 'SGP', 'MDV', 'BHR', 'CPV', 'STP', 'ATG']) {
      expect(pointIsos.has(iso), iso).toBe(true)
    }
    expect(pointIsos.has('GUF')).toBe(false)
    expect(pointIsos.has('BRA')).toBe(false)
  })

  it('o ponto de Maurício está no oceano Índico', () => {
    const [lng, lat] = points.features.find((f) => f.properties.iso === 'MUS')!.geometry.coordinates
    expect(lng).toBeGreaterThan(57)
    expect(lng).toBeLessThan(58)
    expect(lat).toBeLessThan(-19.9)
    expect(lat).toBeGreaterThan(-20.6)
  })

  it('todo ponto tem polígono ou é um território só de marcador', () => {
    const markerOnly = points.features.filter((f) => !polygonByIso.has(f.properties.iso))
    expect(markerOnly.map((f) => f.properties.iso).sort()).toEqual(['BES', 'GIB', 'TKL'])
  })
})
