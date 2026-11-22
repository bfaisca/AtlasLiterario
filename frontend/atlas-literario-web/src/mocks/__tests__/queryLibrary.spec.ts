import { describe, expect, it } from 'vitest'
import { createMockLibrary } from '../data'
import { queryLibrary } from '../mockApi'

const library = createMockLibrary()
const titles = (query: Parameters<typeof queryLibrary>[1]) =>
  queryLibrary(library, { ...query, pageSize: 100 }).items.map((i) => i.book.title)

describe('mock de GET /library?geoMissing=', () => {
  it('any / origin / author / both seguem o contrato', () => {
    const any = titles({ geoMissing: 'any' })
    const origin = titles({ geoMissing: 'origin' })
    const author = titles({ geoMissing: 'author' })
    const both = titles({ geoMissing: 'both' })
    expect(origin).toEqual(expect.arrayContaining(['O Processo', 'Ensaio sobre a Lucidez']))
    expect(author).toEqual(expect.arrayContaining(['Cartas de um Farol (demo)']))
    expect(both).toEqual(['As Mil e Uma Noites'])
    expect(new Set(any)).toEqual(new Set([...origin, ...author]))
    expect(titles({})).not.toHaveLength(any.length)
  })

  it('combina com os demais filtros (E)', () => {
    expect(titles({ geoMissing: 'any', status: ['Reading'] })).toEqual([])
    expect(titles({ geoMissing: 'origin', q: 'lucidez' })).toEqual(['Ensaio sobre a Lucidez'])
  })
})
