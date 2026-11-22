import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import {
  colorForCount,
  computeIntensity,
  hexToRgb,
  interpolateColor,
  legendTicks,
  MAP_COLOR_STOPS,
  UNEXPLORED_COLOR,
  useColorScale,
} from '../useColorScale'

describe('computeIntensity', () => {
  it('retorna 0 para países sem livros ou máximo zero', () => {
    expect(computeIntensity(0, 10)).toBe(0)
    expect(computeIntensity(3, 0)).toBe(0)
  })

  it('usa log1p(n) / log1p(max)', () => {
    expect(computeIntensity(10, 10)).toBe(1)
    expect(computeIntensity(3, 15)).toBeCloseTo(Math.log1p(3) / Math.log1p(15), 10)
    expect(computeIntensity(3, 15)).toBeCloseTo(0.5, 10) // log(4)/log(16)
  })

  it('normalização logarítmica: país com poucos livros não some perto de um país dominante', () => {
    expect(computeIntensity(1, 100)).toBeGreaterThan(0.14) // linear daria 0.01
  })

  it('limita a [0, 1]', () => {
    expect(computeIntensity(50, 10)).toBe(1)
  })
})

describe('interpolateColor', () => {
  it('retorna exatamente as paradas da identidade visual', () => {
    for (const stop of MAP_COLOR_STOPS) {
      expect(interpolateColor(stop.at)).toBe(stop.color)
    }
    expect(interpolateColor(0)).toBe('#13294b')
    expect(interpolateColor(1)).toBe('#bfe0ff')
  })

  it('interpola linearmente em RGB entre duas paradas', () => {
    const stops = [
      { at: 0, color: '#000000' },
      { at: 1, color: '#ff8040' },
    ]
    expect(interpolateColor(0.5, stops)).toBe('#804020')
  })

  it('fica entre as paradas vizinhas no meio de um intervalo', () => {
    const [r, g, b] = hexToRgb(interpolateColor(0.175))
    const low = hexToRgb('#13294b')
    const high = hexToRgb('#1f4e8c')
    expect(r).toBeCloseTo((low[0] + high[0]) / 2, -0.5)
    expect(g).toBeCloseTo((low[1] + high[1]) / 2, -0.5)
    expect(b).toBeCloseTo((low[2] + high[2]) / 2, -0.5)
  })

  it('satura fora do intervalo', () => {
    expect(interpolateColor(-1)).toBe('#13294b')
    expect(interpolateColor(2)).toBe('#bfe0ff')
  })
})

describe('colorForCount / useColorScale', () => {
  it('usa a cor de inexplorado quando n = 0', () => {
    expect(colorForCount(0, 5)).toBe(UNEXPLORED_COLOR)
  })

  it('usa a cor mais clara para o país com o máximo', () => {
    expect(colorForCount(7, 7)).toBe('#bfe0ff')
  })

  it('reage à mudança do máximo', () => {
    const max = ref(1)
    const scale = useColorScale(max)
    expect(scale.colorFor(1)).toBe('#bfe0ff')
    max.value = 15
    expect(scale.intensityFor(3)).toBeCloseTo(0.5, 10)
    expect(scale.colorFor(1)).not.toBe('#bfe0ff')
  })
})

const distance = (a: string, b: string) => {
  const [r1, g1, b1] = hexToRgb(a)
  const [r2, g2, b2] = hexToRgb(b)
  return Math.hypot(r1 - r2, g1 - g2, b1 - b2)
}

describe('legendTicks', () => {
  it('gera 5 pontos de 1 até o máximo, crescentes e únicos', () => {
    const ticks = legendTicks(50)
    expect(ticks).toHaveLength(5)
    expect(ticks[0]!.count).toBe(1)
    expect(ticks[4]!.count).toBe(50)
    expect(ticks.map((t) => t.count)).toEqual(
      [...new Set(ticks.map((t) => t.count))].sort((a, b) => a - b),
    )
  })

  it('usa exatamente as cores do mapa para cada valor', () => {
    for (const tick of legendTicks(50)) expect(tick.color).toBe(colorForCount(tick.count, 50))
  })

  it('remove valores repetidos quando o máximo é pequeno', () => {
    expect(legendTicks(3).map((t) => t.count)).toEqual([1, 2, 3])
    expect(legendTicks(1).map((t) => t.count)).toEqual([1])
    expect(legendTicks(0)).toEqual([])
  })
})

describe('legibilidade da escala', () => {
  it('um país com 50 livros não apaga os de 1, 2 e 3 livros (cores distintas entre si)', () => {
    const colors = [1, 2, 3, 50].map((n) => colorForCount(n, 50))
    expect(new Set(colors).size).toBe(4)
    for (let i = 1; i < colors.length; i++) {
      expect(distance(colors[i - 1]!, colors[i]!)).toBeGreaterThan(8)
    }
  })

  it('1 livro continua distinguível de país inexplorado', () => {
    expect(distance(colorForCount(1, 50), UNEXPLORED_COLOR)).toBeGreaterThan(10)
    expect(colorForCount(0, 50)).toBe(UNEXPLORED_COLOR)
  })

  it('país inexplorado continua distinguível do oceano', () => {
    expect(distance(UNEXPLORED_COLOR, '#060b14')).toBeGreaterThan(20)
  })
})
