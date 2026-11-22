import { describe, expect, it, vi } from 'vitest'
import {
  CoverError,
  MAX_OUTPUT_BYTES,
  detectType,
  fitWithin,
  prepareCover,
  validateCoverFile,
  type CanvasLike,
  type DecodedImage,
  type ResizeDeps,
} from '../imageResize'

interface FakeOptions {
  width?: number
  height?: number
  webp?: boolean
  /** Bytes produzidos para (largura, altura, qualidade, tipo). */
  bytes?: (w: number, h: number, q: number, type: string) => number
  decodeFails?: boolean
  /** Simula um navegador sem WebP: devolve PNG quando pedem WebP. */
  webpFallsBackToPng?: boolean
}

function fake(options: FakeOptions = {}) {
  const { width = 3000, height = 4000, webp = true, bytes = () => 50_000 } = options
  const events: string[] = []
  const canvases: Array<{ w: number; h: number; type?: string; quality?: number }> = []
  const close = vi.fn()
  const image: DecodedImage = {
    width,
    height,
    draw: () => events.push('draw'),
    close,
  }
  const deps: ResizeDeps = {
    decode: async () => {
      if (options.decodeFails) throw new Error('decode')
      return image
    },
    createCanvas: (w, h): CanvasLike => {
      const record: { w: number; h: number; type?: string; quality?: number } = { w, h }
      canvases.push(record)
      const context = {
        set fillStyle(value: string) {
          events.push(`fill:${value}`)
        },
        get fillStyle() {
          return ''
        },
        fillRect: () => events.push('fillRect'),
      }
      return {
        getContext: () => context,
        toBlob: (callback, type = '', quality = 0) => {
          record.type = type
          record.quality = quality
          const size = bytes(w, h, quality, type)
          const outType = options.webpFallsBackToPng && type === 'image/webp' ? 'image/png' : type
          callback(new Blob([new Uint8Array(size)], { type: outType }))
        },
      }
    },
    supportsWebp: () => webp,
  }
  return { deps, events, canvases, close }
}

const file = (name: string, type: string, size = 1000) =>
  new File([new Uint8Array(size)], name, { type })

describe('fitWithin', () => {
  it('reduz mantendo a proporção para caber em 800×1200', () => {
    expect(fitWithin(3000, 4000)).toEqual({ width: 800, height: 1067 })
    expect(fitWithin(4000, 3000)).toEqual({ width: 800, height: 600 })
    expect(fitWithin(1600, 4800)).toEqual({ width: 400, height: 1200 })
  })
  it('não amplia imagens pequenas', () => {
    expect(fitWithin(300, 450)).toEqual({ width: 300, height: 450 })
  })
  it('aplica a escala extra dos passos de redução', () => {
    expect(fitWithin(800, 1200, 800, 1200, 0.5)).toEqual({ width: 400, height: 600 })
  })
})

describe('validação do arquivo de entrada', () => {
  it('aceita JPEG, PNG e WebP (inclusive pela extensão quando o tipo vem vazio)', () => {
    expect(validateCoverFile(file('a.jpg', 'image/jpeg'))).toBe('image/jpeg')
    expect(validateCoverFile(file('a.png', 'image/png'))).toBe('image/png')
    expect(validateCoverFile(file('a.webp', 'image/webp'))).toBe('image/webp')
    expect(detectType(file('CAPA.JPEG', ''))).toBe('image/jpeg')
  })
  it('recusa SVG e GIF com mensagem própria', () => {
    for (const f of [
      file('a.svg', 'image/svg+xml'),
      file('a.gif', 'image/gif'),
      file('a.svg', ''),
    ]) {
      expect(() => validateCoverFile(f)).toThrow(/SVG e GIF não são aceitas/)
    }
  })
  it('recusa outros tipos', () => {
    expect(() => validateCoverFile(file('a.pdf', 'application/pdf'))).toThrow(/Formato não aceito/)
    expect(() => validateCoverFile(file('semextensao', ''))).toThrow(CoverError)
  })
  it('recusa entrada acima de 15 MB antes de decodificar', async () => {
    const big = file('a.png', 'image/png', 15 * 1024 * 1024 + 1)
    const { deps } = fake()
    const decode = vi.spyOn(deps, 'decode')
    await expect(prepareCover(big, deps)).rejects.toThrow(/máximo de 15 MB/)
    expect(decode).not.toHaveBeenCalled()
  })
})

describe('prepareCover', () => {
  it('reduz para 800×1200 mantendo a proporção e exporta JPEG com fundo branco', async () => {
    const { deps, canvases, events, close } = fake({ width: 3000, height: 4000 })
    const out = await prepareCover(file('foto.png', 'image/png'), deps)
    expect(canvases[0]).toMatchObject({ w: 800, h: 1067, type: 'image/jpeg', quality: 0.85 })
    expect(out.type).toBe('image/jpeg')
    expect(out.name).toBe('foto.jpg')
    expect(out.size).toBeLessThanOrEqual(MAX_OUTPUT_BYTES)
    // fundo branco pintado ANTES de desenhar a imagem
    expect(events).toEqual(['fill:#ffffff', 'fillRect', 'draw'])
    expect(close).toHaveBeenCalled()
  })

  it('não amplia uma imagem pequena', async () => {
    const { deps, canvases } = fake({ width: 300, height: 450 })
    await prepareCover(file('a.jpg', 'image/jpeg'), deps)
    expect(canvases[0]).toMatchObject({ w: 300, h: 450 })
  })

  it('mantém WebP quando o original é WebP e o navegador suporta', async () => {
    const { deps } = fake({ webp: true })
    const out = await prepareCover(file('a.webp', 'image/webp'), deps)
    expect(out.type).toBe('image/webp')
    expect(out.name).toBe('a.webp')
  })

  it('usa JPEG para WebP quando o navegador não exporta WebP', async () => {
    const { deps } = fake({ webp: false })
    const out = await prepareCover(file('a.webp', 'image/webp'), deps)
    expect(out.type).toBe('image/jpeg')
    expect(out.name).toBe('a.jpg')
  })

  it('PNG e JPEG sempre saem como JPEG', async () => {
    const { deps } = fake({ webp: true })
    expect((await prepareCover(file('a.png', 'image/png'), deps)).type).toBe('image/jpeg')
  })

  it('se passar de 2 MB, reduz qualidade e depois o tamanho, em passos', async () => {
    // 4 bytes por pixel × qualidade: 800×1067 a 0,85 ≈ 2,9 MB
    const bytes = (w: number, h: number, q: number) => Math.round(w * h * q * 4)
    const { deps, canvases } = fake({ bytes })
    const out = await prepareCover(file('a.png', 'image/png'), deps)
    expect(out.size).toBeLessThanOrEqual(MAX_OUTPUT_BYTES)
    expect(canvases.length).toBeGreaterThan(1)
    expect(canvases[0]!.quality).toBe(0.85)
    expect(canvases[1]!.quality).toBeLessThan(0.85)
    expect(canvases[1]!.w).toBe(canvases[0]!.w) // 1º reduz qualidade, mesma dimensão
  })

  it('reduz também as dimensões quando só a qualidade não basta', async () => {
    const bytes = (w: number, h: number) => w * h * 5 // independente da qualidade
    const { deps, canvases } = fake({ bytes })
    const out = await prepareCover(file('a.png', 'image/png'), deps)
    expect(out.size).toBeLessThanOrEqual(MAX_OUTPUT_BYTES)
    expect(canvases.at(-1)!.w).toBeLessThan(800)
  })

  it('falha com mensagem amigável se nunca couber em 2 MB', async () => {
    const { deps } = fake({ bytes: () => MAX_OUTPUT_BYTES + 1 })
    await expect(prepareCover(file('a.png', 'image/png'), deps)).rejects.toThrow(/2 MB/)
  })

  it('falha se a imagem não puder ser decodificada', async () => {
    const { deps } = fake({ decodeFails: true })
    await expect(prepareCover(file('a.png', 'image/png'), deps)).rejects.toThrow(/ler esta imagem/)
  })

  it('não aceita WebP que o navegador converteu em PNG', async () => {
    const { deps } = fake({ webpFallsBackToPng: true })
    await expect(prepareCover(file('a.webp', 'image/webp'), deps)).rejects.toMatchObject({
      code: 'encode_failed',
    })
  })
})
