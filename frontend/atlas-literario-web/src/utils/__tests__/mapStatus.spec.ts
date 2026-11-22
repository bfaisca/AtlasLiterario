import { describe, expect, it } from 'vitest'
import {
  booksCountLabel,
  mapStatusToApiParam,
  mapStatusToQuery,
  MAP_STATUS_OPTIONS,
  parseMapStatusQuery,
} from '../mapStatus'

describe('filtro de status do mapa', () => {
  it('ordem e rótulos do controle', () => {
    expect(MAP_STATUS_OPTIONS.map((o) => o.label)).toEqual([
      'Todos',
      'Quero ler',
      'Lendo',
      'Lido',
      'Abandonado',
    ])
  })

  it('?status= válido é aceito; ausente/inválido vira Read', () => {
    expect(parseMapStatusQuery('All')).toBe('All')
    expect(parseMapStatusQuery(['Reading', 'Read'])).toBe('Reading')
    expect(parseMapStatusQuery(undefined)).toBe('Read')
    expect(parseMapStatusQuery('read')).toBe('Read')
    expect(parseMapStatusQuery('xyz')).toBe('Read')
  })

  it('omite o padrão na URL e na API', () => {
    expect(mapStatusToQuery('Read')).toBeUndefined()
    expect(mapStatusToApiParam('Read')).toBeUndefined()
    expect(mapStatusToQuery('WantToRead')).toBe('WantToRead')
    expect(mapStatusToApiParam('All')).toBe('All')
  })

  it('textos de contagem refletem o filtro', () => {
    expect(booksCountLabel(13, 'Read')).toBe('13 livros lidos')
    expect(booksCountLabel(1, 'Read')).toBe('1 livro lido')
    expect(booksCountLabel(2, 'All')).toBe('2 livros')
    expect(booksCountLabel(3, 'WantToRead')).toBe('3 livros que quero ler')
    expect(booksCountLabel(1, 'Reading')).toBe('1 livro em leitura')
    expect(booksCountLabel(2, 'Abandoned')).toBe('2 livros abandonados')
  })
})
