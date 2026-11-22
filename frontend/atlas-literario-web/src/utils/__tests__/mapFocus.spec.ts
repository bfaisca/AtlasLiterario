import { describe, expect, it } from 'vitest'
import { buildFocusIndex, mainBounds, POINT_FOCUS_ZOOM } from '../mapFocus'

const square = (w: number, s: number, e: number, n: number) => [
  [
    [w, s],
    [e, s],
    [e, n],
    [w, n],
    [w, s],
  ],
]

describe('mainBounds', () => {
  it('usa o bbox de um polígono simples', () => {
    expect(mainBounds({ type: 'Polygon', coordinates: square(10, 20, 30, 40) })).toEqual([
      [10, 20],
      [30, 40],
    ])
  })

  it('une partes relevantes e ignora ilhas distantes minúsculas', () => {
    const geometry = {
      type: 'MultiPolygon',
      coordinates: [
        square(-120, 30, -70, 48), // território principal
        square(-165, 55, -140, 68), // Alasca: relevante (≥ 10% do maior)
        square(-160, 19, -159, 20), // Havaí: ignorado
      ],
    }
    expect(mainBounds(geometry)).toEqual([
      [-165, 30],
      [-70, 68],
    ])
  })

  it('devolve null para geometria sem polígonos', () => {
    expect(mainBounds({ type: 'Point', coordinates: [0, 0] })).toBeNull()
  })
})

describe('buildFocusIndex', () => {
  const polygons = {
    features: [
      {
        properties: { iso: 'BRA', name: 'Brazil', namePt: 'Brasil' },
        geometry: { type: 'Polygon', coordinates: square(-74, -34, -34, 5) },
      },
      {
        properties: { iso: 'MUS', name: 'Mauritius', namePt: 'Maurícia' },
        geometry: { type: 'Polygon', coordinates: square(57.3, -20.5, 57.8, -20) },
      },
    ],
  } as unknown as Parameters<typeof buildFocusIndex>[0]
  const points = {
    features: [
      {
        properties: { iso: 'MUS', name: 'Mauritius', namePt: null },
        geometry: { type: 'Point', coordinates: [57.58, -20.3] },
      },
    ],
  } as unknown as Parameters<typeof buildFocusIndex>[1]

  it('país com marcador recebe foco em ponto; os demais, em bounds', () => {
    const index = buildFocusIndex(polygons, points)
    expect(index.get('BRA')).toEqual({
      kind: 'bounds',
      bounds: [
        [-74, -34],
        [-34, 5],
      ],
    })
    expect(index.get('MUS')).toEqual({
      kind: 'point',
      center: [57.58, -20.3],
      zoom: POINT_FOCUS_ZOOM,
    })
  })

  it('funciona sem marcadores', () => {
    expect(buildFocusIndex(polygons, null).get('MUS')?.kind).toBe('bounds')
  })

  it('país desconhecido não tem alvo', () => {
    expect(buildFocusIndex(polygons, points).get('XXX')).toBeUndefined()
  })
})
