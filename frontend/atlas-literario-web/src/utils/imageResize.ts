/**
 * Preparação de capas no cliente: valida o tipo, reduz (máx. 800×1200, sem ampliar), compõe sobre
 * fundo branco e reexporta em JPEG (ou WebP, se o original for WebP e o navegador suportar) com
 * resultado ≤ 2 MB. O backend valida de novo pelos bytes; isto só poupa banda e evita o 413.
 * A parte de navegador (decodificar/criar canvas) é injetável para teste.
 */

export const MAX_INPUT_BYTES = 15 * 1024 * 1024
export const MAX_OUTPUT_BYTES = 2 * 1024 * 1024
export const MAX_COVER_WIDTH = 800
export const MAX_COVER_HEIGHT = 1200
export const ACCEPT_ATTRIBUTE = 'image/jpeg,image/png,image/webp'

const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp']
const REJECTED_TYPES = ['image/svg+xml', 'image/gif']
const EXTENSION_TYPES: Record<string, string> = {
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  png: 'image/png',
  webp: 'image/webp',
  svg: 'image/svg+xml',
  gif: 'image/gif',
}
/** Qualidades tentadas na dimensão máxima; depois reduz a dimensão em passos. */
const QUALITY_STEPS = [0.85, 0.75, 0.65, 0.55]
const SCALE_STEPS = [0.85, 0.7, 0.55, 0.4]
const FALLBACK_QUALITY = 0.7

export type CoverErrorCode =
  'invalid_type' | 'unsupported_type' | 'input_too_large' | 'read_failed' | 'encode_failed'

const MESSAGES: Record<CoverErrorCode, string> = {
  invalid_type: 'Formato não aceito. Use uma imagem JPEG, PNG ou WebP.',
  unsupported_type: 'Imagens SVG e GIF não são aceitas. Use uma imagem JPEG, PNG ou WebP.',
  input_too_large: 'O arquivo é muito grande (máximo de 15 MB). Escolha uma imagem menor.',
  read_failed: 'Não foi possível ler esta imagem. Tente outro arquivo.',
  encode_failed: 'Não foi possível reduzir a imagem para o tamanho permitido (2 MB). Tente outra.',
}

export class CoverError extends Error {
  constructor(readonly code: CoverErrorCode) {
    super(MESSAGES[code])
    this.name = 'CoverError'
  }
}

export interface DecodedImage {
  width: number
  height: number
  draw(context: CanvasLike2D, width: number, height: number): void
  close?(): void
}

export interface CanvasLike2D {
  fillStyle: string | CanvasGradient | CanvasPattern
  fillRect(x: number, y: number, w: number, h: number): void
}

export interface CanvasLike {
  getContext(type: '2d'): CanvasLike2D | null
  toBlob(callback: (blob: Blob | null) => void, type?: string, quality?: number): void
}

export interface ResizeDeps {
  decode(file: Blob): Promise<DecodedImage>
  createCanvas(width: number, height: number): CanvasLike
  /** O navegador exporta WebP? */
  supportsWebp(): boolean
}

/** Tipo efetivo: `file.type`, ou a extensão quando o navegador não informa. */
export function detectType(file: File): string {
  if (file.type) return file.type.toLowerCase()
  const extension = file.name.split('.').pop()?.toLowerCase() ?? ''
  return EXTENSION_TYPES[extension] ?? ''
}

/** Valida tipo e tamanho de entrada; lança `CoverError` com mensagem amigável. */
export function validateCoverFile(file: File): string {
  const type = detectType(file)
  if (REJECTED_TYPES.includes(type)) throw new CoverError('unsupported_type')
  if (!ACCEPTED_TYPES.includes(type)) throw new CoverError('invalid_type')
  if (file.size > MAX_INPUT_BYTES) throw new CoverError('input_too_large')
  return type
}

/** Reduz mantendo a proporção para caber em `maxW × maxH`, sem ampliar; `scale` reduz mais. */
export function fitWithin(
  width: number,
  height: number,
  maxW = MAX_COVER_WIDTH,
  maxH = MAX_COVER_HEIGHT,
  scale = 1,
): { width: number; height: number } {
  const ratio = Math.min(1, maxW / width, maxH / height) * scale
  return {
    width: Math.max(1, Math.round(width * ratio)),
    height: Math.max(1, Math.round(height * ratio)),
  }
}

function toBlob(canvas: CanvasLike, type: string, quality: number): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, quality))
}

function renamed(name: string, outputType: string): string {
  const base = name.replace(/\.[^./\\]+$/, '') || 'capa'
  return `${base}.${outputType === 'image/webp' ? 'webp' : 'jpg'}`
}

/**
 * Devolve o arquivo pronto para envio (≤ 2 MB). Lança `CoverError` se o tipo for inválido, o
 * arquivo for grande demais, não puder ser lido ou não couber em 2 MB.
 */
export async function prepareCover(file: File, deps: ResizeDeps = browserDeps()): Promise<File> {
  const inputType = validateCoverFile(file)
  const outputType = inputType === 'image/webp' && deps.supportsWebp() ? 'image/webp' : 'image/jpeg'

  let image: DecodedImage
  try {
    image = await deps.decode(file)
  } catch {
    throw new CoverError('read_failed')
  }
  if (!(image.width > 0 && image.height > 0)) {
    image.close?.()
    throw new CoverError('read_failed')
  }

  try {
    const attempts: Array<{ scale: number; quality: number }> = [
      ...QUALITY_STEPS.map((quality) => ({ scale: 1, quality })),
      ...SCALE_STEPS.map((scale) => ({ scale, quality: FALLBACK_QUALITY })),
    ]
    for (const { scale, quality } of attempts) {
      const { width, height } = fitWithin(
        image.width,
        image.height,
        MAX_COVER_WIDTH,
        MAX_COVER_HEIGHT,
        scale,
      )
      const canvas = deps.createCanvas(width, height)
      const context = canvas.getContext('2d')
      if (!context) throw new CoverError('read_failed')
      // Fundo branco: PNG com transparência viraria preto em JPEG.
      context.fillStyle = '#ffffff'
      context.fillRect(0, 0, width, height)
      image.draw(context, width, height)
      const blob = await toBlob(canvas, outputType, quality)
      if (!blob) throw new CoverError('encode_failed')
      // Um navegador sem suporte a WebP devolve PNG: nesse caso o resultado não vale.
      if (blob.type && blob.type !== outputType) throw new CoverError('encode_failed')
      if (blob.size <= MAX_OUTPUT_BYTES) {
        return new File([blob], renamed(file.name, outputType), { type: outputType })
      }
    }
    throw new CoverError('encode_failed')
  } finally {
    image.close?.()
  }
}

/** Implementação real (navegador): createImageBitmap, com fallback para <img>. */
export function browserDeps(): ResizeDeps {
  return {
    async decode(file) {
      if (typeof createImageBitmap === 'function') {
        const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
        return {
          width: bitmap.width,
          height: bitmap.height,
          draw: (context, w, h) =>
            (context as unknown as CanvasRenderingContext2D).drawImage(bitmap, 0, 0, w, h),
          close: () => bitmap.close(),
        }
      }
      const url = URL.createObjectURL(file)
      try {
        const element = await new Promise<HTMLImageElement>((resolve, reject) => {
          const img = new Image()
          img.onload = () => resolve(img)
          img.onerror = () => reject(new Error('decode'))
          img.src = url
        })
        return {
          width: element.naturalWidth,
          height: element.naturalHeight,
          draw: (context, w, h) =>
            (context as unknown as CanvasRenderingContext2D).drawImage(element, 0, 0, w, h),
        }
      } finally {
        URL.revokeObjectURL(url)
      }
    },
    createCanvas(width, height) {
      const canvas = document.createElement('canvas')
      canvas.width = width
      canvas.height = height
      return canvas as unknown as CanvasLike
    },
    supportsWebp() {
      const canvas = document.createElement('canvas')
      canvas.width = canvas.height = 1
      return canvas.toDataURL('image/webp').startsWith('data:image/webp')
    },
  }
}
