#!/usr/bin/env node
/**
 * Gera os dois arquivos geográficos do mapa a partir do Natural Earth 1:50m (domínio público):
 *
 *  - public/geo/countries-50m.geojson   — polígonos (propriedades `iso`, `name`, `namePt`);
 *  - public/geo/country-points.geojson  — marcadores (Points, propriedades `iso`, `name`, `namePt`)
 *    para países/territórios pequenos demais para serem vistos como polígono no mapa-múndi.
 *
 * `iso` = ISO 3166-1 alpha-3 = `GeoJsonId` do backend (Kosovo: KOS → XKX). Cada `iso` aparece em
 * UMA única feature por arquivo (partes extras viram MultiPolygon).
 *
 * Fontes (nvkelso/natural-earth-vector):
 *  - ne_50m_admin_0_countries     → base de todos os países;
 *  - ne_50m_admin_0_map_units     → usado SÓ para separar territórios que o dataset base embute em
 *                                   outro país (ver SPLITS). Não é base geral: ele também divide o
 *                                   Reino Unido, etc.;
 *  - ne_50m_admin_0_tiny_countries → posição dos marcadores de países minúsculos.
 *
 * Critério dos marcadores (POINT_MAX_AREA_DEG2): recebe ponto todo país/território cuja área
 * (graus², com correção de cos(latitude)) é menor que 1,0 — menos que ~1° × 1° (≈ 110 × 110 km),
 * ou seja, ≲ 3 px na largura do mapa-múndi — mais todo `iso` que o dataset `tiny_countries` traz
 * e que não tem polígono (ex.: Gibraltar, Tokelau). O polígono continua existindo quando há (o
 * ponto só garante visibilidade e alvo de clique). A Guiana Francesa (≈ 6,8°²) NÃO recebe ponto.
 *
 * Uso: npm run geo:fetch   (opcional: GEO_SOURCE_BASE_URL=<url base> para outro espelho)
 */
import { mkdir, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const BASE_URL =
  process.env.GEO_SOURCE_BASE_URL ??
  'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const POLYGONS_OUTPUT = resolve(root, 'public/geo/countries-50m.geojson')
const POINTS_OUTPUT = resolve(root, 'public/geo/country-points.geojson')

/** Códigos do Natural Earth (ADM0_A3) que divergem do identificador usado no seed de países. */
const ISO_OVERRIDES = {
  KOS: 'XKX', // Kosovo: código atribuído pelo usuário (XKX), não oficial ISO
}

/**
 * Territórios que o dataset `countries` embute dentro de outro país. `unit` = GU_A3 em
 * `map_units`. `replaces: true` troca a geometria da feature base (que perde o território);
 * senão a feature é nova.
 */
const SPLITS = [
  { iso: 'FRA', unit: 'FXX', replaces: true }, // França metropolitana + Córsega (sem ultramar)
  { iso: 'GUF', unit: 'GUF' },
  { iso: 'GLP', unit: 'GLP' },
  { iso: 'MTQ', unit: 'MTQ' },
  { iso: 'REU', unit: 'REU' },
  { iso: 'MYT', unit: 'MYT' },
  { iso: 'NOR', unit: 'NOR', replaces: true }, // Noruega sem Svalbard
  { iso: 'SJM', unit: 'NSV' }, // Svalbard e Jan Mayen
  { iso: 'CXR', unit: 'CXR' }, // Ilha Christmas
  { iso: 'CCK', unit: 'CCK' }, // Ilhas Cocos
]
/** Features de `countries` substituídas pelos splits acima (ADM0_A3): "Indian Ocean Ter." = CXR + CCK. */
const DROPPED_BASE_ADM0 = new Set(['IOA'])

/** Territórios disputados sem código ISO (Chipre do Norte, Somalilândia, Sianchen): sem marcador. */
const NO_MARKER = new Set(['CYN', 'SOL', 'KAS'])
/** Países/territórios com área (°²) abaixo disso recebem marcador. */
const POINT_MAX_AREA_DEG2 = 1.0
/** Tolerância de simplificação (graus): mínima para ilhas, máxima para fronteiras grandes. */
const SIMPLIFY_MIN_TOLERANCE = 0.002
const SIMPLIFY_MAX_TOLERANCE = 0.035
/** Anéis menores que isso (°²) são descartados se a feature tiver anel bem maior (ilhotas). */
const MIN_RING_AREA_DEG2 = 0.0004
const DECIMALS = 100 // 2 casas ≈ 1 km (≈ 1 px no zoom máximo do mapa)

// ------------------------------------------------------------------ geometria

/** Área (°²) de um anel, com correção de cos(latitude média). */
function ringArea(ring) {
  let sum = 0
  let lat = 0
  for (let i = 0; i < ring.length - 1; i++) {
    sum += ring[i][0] * ring[i + 1][1] - ring[i + 1][0] * ring[i][1]
    lat += ring[i][1]
  }
  const meanLat = lat / Math.max(1, ring.length - 1)
  return (Math.abs(sum) / 2) * Math.cos((meanLat * Math.PI) / 180)
}

/** Área de um polígono (anel externo menos buracos). */
const polygonArea = (polygon) =>
  polygon.reduce((total, ring, i) => total + (i === 0 ? 1 : -1) * ringArea(ring), 0)

function geometryToPolygons(geometry) {
  if (geometry.type === 'Polygon') return [geometry.coordinates]
  if (geometry.type === 'MultiPolygon') return geometry.coordinates
  return []
}

function distanceToSegment(p, a, b) {
  const dx = b[0] - a[0]
  const dy = b[1] - a[1]
  const lengthSq = dx * dx + dy * dy
  const t =
    lengthSq === 0
      ? 0
      : Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / lengthSq))
  return Math.hypot(p[0] - (a[0] + t * dx), p[1] - (a[1] + t * dy))
}

/** Douglas–Peucker iterativo (evita estouro de pilha em anéis longos). */
function simplifyLine(points, tolerance) {
  const keep = new Uint8Array(points.length)
  keep[0] = 1
  keep[points.length - 1] = 1
  const stack = [[0, points.length - 1]]
  while (stack.length) {
    const [start, end] = stack.pop()
    let maxDistance = 0
    let index = -1
    for (let i = start + 1; i < end; i++) {
      const d = distanceToSegment(points[i], points[start], points[end])
      if (d > maxDistance) {
        maxDistance = d
        index = i
      }
    }
    if (index !== -1 && maxDistance > tolerance) {
      keep[index] = 1
      stack.push([start, index], [index, end])
    }
  }
  return points.filter((_, i) => keep[i])
}

const roundPoint = ([x, y]) => [
  Math.round(x * DECIMALS) / DECIMALS,
  Math.round(y * DECIMALS) / DECIMALS,
]

/** Simplifica e arredonda um anel; `null` se degenerar (< 4 posições distintas ou área nula). */
function processRing(ring) {
  const area = ringArea(ring)
  const tolerance = Math.min(
    SIMPLIFY_MAX_TOLERANCE,
    Math.max(SIMPLIFY_MIN_TOLERANCE, Math.sqrt(area) * 0.05),
  )
  const rounded = simplifyLine(ring, tolerance).map(roundPoint)
  const deduped = rounded.filter(
    (p, i) => i === 0 || p[0] !== rounded[i - 1][0] || p[1] !== rounded[i - 1][1],
  )
  return deduped.length >= 4 && ringArea(deduped) > 0 ? deduped : null
}

function processPolygons(polygons) {
  const largest = Math.max(...polygons.map(polygonArea))
  const result = []
  for (const polygon of polygons) {
    const area = polygonArea(polygon)
    // Ilhotas insignificantes ao lado de uma massa bem maior somem; a feature nunca fica vazia.
    if (area < MIN_RING_AREA_DEG2 && largest > area * 50) continue
    const [outer, ...holes] = polygon
    const processedOuter = processRing(outer)
    if (!processedOuter) continue
    result.push([processedOuter, ...holes.map(processRing).filter(Boolean)])
  }
  return result.length ? result : null
}

/** Centro do bbox do maior polígono (posição do marcador quando o dataset tiny não traz uma). */
function representativePoint(polygons) {
  const [largest] = [...polygons].sort((a, b) => polygonArea(b) - polygonArea(a))
  const xs = largest[0].map((p) => p[0])
  const ys = largest[0].map((p) => p[1])
  return [(Math.min(...xs) + Math.max(...xs)) / 2, (Math.min(...ys) + Math.max(...ys)) / 2]
}

// ------------------------------------------------------------------ propriedades

function normalizeIso(props) {
  const candidates = [props.ISO_A3, props.ISO_A3_EH, props.ADM0_A3]
  const code = candidates.find((c) => typeof c === 'string' && /^[A-Z]{3}$/.test(c))
  if (!code) return null
  return ISO_OVERRIDES[code] ?? code
}

/** ISO só do campo ISO_A3 (tiny_countries traz pontos de regiões sem código próprio, ex.: Açores). */
function strictIso(props) {
  const code = props.ISO_A3
  return typeof code === 'string' && /^[A-Z]{3}$/.test(code) ? (ISO_OVERRIDES[code] ?? code) : null
}

const nameOf = (props, iso) => ({
  name: props.NAME ?? props.ADMIN ?? iso,
  namePt: props.NAME_PT ?? null,
})

// ------------------------------------------------------------------ download

async function download(dataset) {
  const url = `${BASE_URL}/${dataset}.geojson`
  console.log(`Baixando ${url} ...`)
  const response = await fetch(url)
  if (!response.ok) throw new Error(`Falha no download de ${dataset}: HTTP ${response.status}`)
  const data = await response.json()
  if (data?.type !== 'FeatureCollection' || !Array.isArray(data.features)) {
    throw new Error(`${dataset}: não é um FeatureCollection GeoJSON.`)
  }
  return data.features
}

// ------------------------------------------------------------------ construção

function buildCountries(countries, mapUnits) {
  /** iso → { name, namePt, polygons } */
  const byIso = new Map()
  const skipped = []

  const add = (iso, props, polygons, replace = false) => {
    const existing = byIso.get(iso)
    if (existing && !replace) existing.polygons.push(...polygons)
    else byIso.set(iso, { ...nameOf(props, iso), polygons: [...polygons] })
  }

  for (const feature of countries) {
    const props = feature.properties ?? {}
    if (DROPPED_BASE_ADM0.has(props.ADM0_A3)) continue
    const iso = normalizeIso(props)
    if (!iso || !feature.geometry) {
      skipped.push(props.NAME ?? '(sem nome)')
      continue
    }
    add(iso, props, geometryToPolygons(feature.geometry))
  }

  const unitByCode = new Map(mapUnits.map((f) => [f.properties?.GU_A3, f]))
  for (const { iso, unit, replaces } of SPLITS) {
    const feature = unitByCode.get(unit)
    if (!feature) throw new Error(`map_units sem a unidade ${unit} (${iso}).`)
    const props = replaces
      ? (byIso.get(iso) ?? nameOf(feature.properties, iso))
      : feature.properties
    // `replaces` preserva o nome da feature base (ex.: "France"); novas usam o nome da unidade.
    const base = replaces ? { NAME: props.name, NAME_PT: props.namePt } : props
    add(iso, base, geometryToPolygons(feature.geometry), replaces)
  }
  return { byIso, skipped }
}

function buildPolygonFeatures(byIso) {
  const features = []
  for (const [iso, { name, namePt, polygons }] of [...byIso].sort(([a], [b]) =>
    a.localeCompare(b),
  )) {
    const processed = processPolygons(polygons)
    if (!processed) continue
    features.push({
      type: 'Feature',
      properties: { iso, name, namePt },
      geometry:
        processed.length === 1
          ? { type: 'Polygon', coordinates: processed[0] }
          : { type: 'MultiPolygon', coordinates: processed },
    })
  }
  return features
}

function buildPointFeatures(byIso, tinyCountries) {
  const tinyPosition = new Map()
  for (const feature of tinyCountries) {
    const iso = strictIso(feature.properties ?? {})
    if (iso && feature.geometry?.type === 'Point' && !tinyPosition.has(iso)) {
      tinyPosition.set(iso, {
        coordinates: feature.geometry.coordinates,
        props: feature.properties,
      })
    }
  }

  const features = []
  const reasons = new Map()
  for (const [iso, { name, namePt, polygons }] of byIso) {
    const area = polygons.reduce((total, polygon) => total + polygonArea(polygon), 0)
    const small = area < POINT_MAX_AREA_DEG2
    if (!small || NO_MARKER.has(iso)) continue
    reasons.set(iso, 'area')
    const tiny = tinyPosition.get(iso)
    features.push({
      type: 'Feature',
      properties: { iso, name, namePt },
      geometry: {
        type: 'Point',
        coordinates: roundPoint(tiny?.coordinates ?? representativePoint(polygons)),
      },
    })
  }
  // Presentes em tiny_countries mas sem polígono nem área medida (ex.: Gibraltar, Tokelau).
  for (const [iso, { coordinates, props }] of tinyPosition) {
    if (reasons.has(iso) || byIso.has(iso)) continue
    reasons.set(iso, 'tiny')
    features.push({
      type: 'Feature',
      properties: { iso, ...nameOf(props, iso) },
      geometry: { type: 'Point', coordinates: roundPoint(coordinates) },
    })
  }
  features.sort((a, b) => a.properties.iso.localeCompare(b.properties.iso))
  return { features, reasons }
}

async function writeCollection(path, source, features) {
  const json = JSON.stringify({
    type: 'FeatureCollection',
    // Atribuição: Made with Natural Earth. Free vector and raster map data @ naturalearthdata.com
    metadata: { source, license: 'Public domain' },
    features,
  })
  await mkdir(dirname(path), { recursive: true })
  await writeFile(path, json)
  return json.length
}

async function main() {
  const [countries, mapUnits, tiny] = await Promise.all([
    download('ne_50m_admin_0_countries'),
    download('ne_50m_admin_0_map_units'),
    download('ne_50m_admin_0_tiny_countries'),
  ])

  const { byIso, skipped } = buildCountries(countries, mapUnits)
  const polygonFeatures = buildPolygonFeatures(byIso)
  if (polygonFeatures.length < 200) {
    throw new Error(
      `Apenas ${polygonFeatures.length} países encontrados — arquivo de origem inválido?`,
    )
  }
  const { features: pointFeatures, reasons } = buildPointFeatures(byIso, tiny)

  const polygonBytes = await writeCollection(
    POLYGONS_OUTPUT,
    'Natural Earth 1:50m Admin 0 – Countries (+ Map Units para territórios separados)',
    polygonFeatures,
  )
  const pointBytes = await writeCollection(
    POINTS_OUTPUT,
    `Natural Earth 1:50m – marcadores de países pequenos (área < ${POINT_MAX_AREA_DEG2} °² ou tiny_countries)`,
    pointFeatures,
  )

  console.log(
    `OK: ${polygonFeatures.length} polígonos em ${POLYGONS_OUTPUT} (${(polygonBytes / 1024).toFixed(0)} kB).`,
  )
  console.log(
    `OK: ${pointFeatures.length} marcadores em ${POINTS_OUTPUT} (${(pointBytes / 1024).toFixed(1)} kB; ` +
      `${[...reasons.values()].filter((r) => r === 'tiny').length} só por tiny_countries).`,
  )
  if (skipped.length) console.warn(`Ignorados (sem código ISO válido): ${skipped.join(', ')}`)
}

main().catch((error) => {
  console.error(`Erro: ${error.message}`)
  process.exitCode = 1
})
