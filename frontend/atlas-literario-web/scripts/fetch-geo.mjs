#!/usr/bin/env node
/**
 * Baixa o Natural Earth 1:110m Admin 0 – Countries (domínio público), mantém apenas as
 * propriedades necessárias e normaliza o código ISO 3166-1 alpha-3 na propriedade `iso`.
 *
 * Saída: public/geo/countries-110m.geojson (servido estaticamente e carregado via fetch
 * pelo componente WorldMap — não entra no bundle JS).
 *
 * Uso: npm run geo:fetch   (opcional: GEO_SOURCE_URL=<url> para outra fonte)
 */
import { mkdir, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const SOURCE_URL =
  process.env.GEO_SOURCE_URL ??
  'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_admin_0_countries.geojson'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const OUTPUT = resolve(root, 'public/geo/countries-110m.geojson')

/** Códigos do Natural Earth (ADM0_A3) que divergem do identificador usado no seed de países. */
const ISO_OVERRIDES = {
  KOS: 'XKX', // Kosovo: código atribuído pelo usuário (XKX), não oficial ISO
}

/** Arredonda coordenadas (3 casas ≈ 100 m — suficiente para 1:110m) para reduzir o arquivo. */
function roundCoords(coords) {
  if (typeof coords[0] === 'number') {
    return coords.map((n) => Math.round(n * 1000) / 1000)
  }
  return coords.map(roundCoords)
}

function normalizeIso(props) {
  const candidates = [props.ISO_A3, props.ISO_A3_EH, props.ADM0_A3]
  const code = candidates.find((c) => typeof c === 'string' && /^[A-Z]{3}$/.test(c))
  if (!code) return null
  return ISO_OVERRIDES[code] ?? code
}

async function main() {
  console.log(`Baixando ${SOURCE_URL} ...`)
  const response = await fetch(SOURCE_URL)
  if (!response.ok) {
    throw new Error(`Falha no download: HTTP ${response.status}`)
  }
  const source = await response.json()
  if (source?.type !== 'FeatureCollection' || !Array.isArray(source.features)) {
    throw new Error('Conteúdo inesperado: não é um FeatureCollection GeoJSON.')
  }

  const skipped = []
  const features = []
  for (const feature of source.features) {
    const props = feature.properties ?? {}
    const iso = normalizeIso(props)
    if (!iso || !feature.geometry) {
      skipped.push(props.NAME ?? '(sem nome)')
      continue
    }
    features.push({
      type: 'Feature',
      properties: {
        iso,
        name: props.NAME ?? props.ADMIN ?? iso,
        namePt: props.NAME_PT ?? null,
      },
      geometry: {
        type: feature.geometry.type,
        coordinates: roundCoords(feature.geometry.coordinates),
      },
    })
  }

  if (features.length < 150) {
    throw new Error(`Apenas ${features.length} países encontrados — arquivo de origem inválido?`)
  }

  const output = {
    type: 'FeatureCollection',
    // Atribuição: Made with Natural Earth. Free vector and raster map data @ naturalearthdata.com
    metadata: { source: 'Natural Earth 1:110m Admin 0 – Countries', license: 'Public domain' },
    features,
  }

  await mkdir(dirname(OUTPUT), { recursive: true })
  const json = JSON.stringify(output)
  await writeFile(OUTPUT, json)
  console.log(
    `OK: ${features.length} países gravados em ${OUTPUT} (${(json.length / 1024).toFixed(0)} kB).`,
  )
  if (skipped.length) console.warn(`Ignorados (sem código ISO válido): ${skipped.join(', ')}`)
}

main().catch((error) => {
  console.error(`Erro: ${error.message}`)
  process.exitCode = 1
})
