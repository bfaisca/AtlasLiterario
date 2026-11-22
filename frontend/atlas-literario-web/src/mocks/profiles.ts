/**
 * Perfis públicos e seguidores em memória (contrato v1.12, §8.1) para `VITE_USE_MOCKS=true`.
 * Regras simuladas: só perfis `Public` são vistos por terceiros (`404 profile.not_found` caso
 * contrário), follow/unfollow idempotentes, `follow.self`, apelido único e validado.
 */
import { ApiError } from '@/api/errors'
import type { ProfilesApi } from '@/api/profiles'
import type {
  LibraryItemDto,
  MapVisibility,
  PagedResult,
  ProfileDto,
  ProfileSummaryDto,
  UpdateProfileRequest,
} from '@/types/api'
import { isValidHandle, normalizeHandle } from '@/utils/handle'
import {
  computeMapData,
  computeProfileCountryDetail,
  readItems,
  type CountryLookup,
} from './aggregation'
import { getMockCatalog, MOCK_DEMO_USER, mockLibraryItem, type ItemSeed } from './data'

const LATENCY_MS = 200

function delay<T>(value: T, ms = LATENCY_MS): Promise<T> {
  return new Promise((resolve) => setTimeout(() => resolve(value), ms))
}

/** Conta do "diretório" de perfis: pessoas fictícias ou usuários que fazem login nos mocks. */
export interface MockAccount {
  id: string
  handle: string | null
  name: string
  visibility: MapVisibility
  memberSince: string
  items: () => LibraryItemDto[]
}

const personId = (n: number) => `00000000-0000-4000-8000-0000000001${String(n).padStart(2, '0')}`

type BookKey = keyof ReturnType<typeof getMockCatalog>['books']
const read = (
  book: BookKey,
  finished: string,
  rating: number,
  review?: string,
): Omit<ItemSeed, 'book'> & { key: BookKey } => ({
  key: book,
  status: 'Read',
  finished,
  rating,
  review,
})

function library(
  seeds: Array<ReturnType<typeof read> | (Omit<ItemSeed, 'book'> & { key: BookKey })>,
) {
  const books = getMockCatalog().books
  return seeds.map(({ key, ...seed }) => mockLibraryItem({ ...seed, book: books[key] }))
}

export function createMockPeople(): MockAccount[] {
  const person = (
    n: number,
    handle: string,
    name: string,
    visibility: MapVisibility,
    memberSince: string,
    items: LibraryItemDto[],
  ): MockAccount => ({ id: personId(n), handle, name, visibility, memberSince, items: () => items })

  return [
    person(
      1,
      'marina_lima',
      'Marina Lima',
      'Public',
      '2024-03-12T10:00:00Z',
      library([
        read(
          'cemAnos',
          '2025-01-10',
          5,
          'Macondo é um país inteiro dentro de uma família. Li devagar para não acabar.',
        ),
        read(
          'processo',
          '2025-02-22',
          4,
          'Angustiante e engraçado ao mesmo tempo, como um pesadelo burocrático.',
        ),
        read('cegueira', '2025-04-03', 5, 'A cena do manicômio me tirou o sono por dias.'),
        read(
          'americanah',
          '2025-06-18',
          5,
          'Cabelo, raça e amor: o melhor retrato de quem muda de país que já li.',
        ),
        read('pedroParamo', '2025-08-09', 4),
        read('belasMaldicoes', '2025-09-30', 4, 'Ficção histórica sem pedir licença.'),
        read('estrangeiro', '2025-11-01', 3),
        { key: 'ulisses', status: 'Abandoned', start: '2025-05-04' },
      ]),
    ),
    person(
      2,
      'joaoribeiro',
      'João Ribeiro',
      'Public',
      '2024-06-01T10:00:00Z',
      library([
        read(
          'domCasmurro',
          '2025-03-14',
          5,
          'Capitu é, até hoje, a maior dúvida da literatura brasileira.',
        ),
        read('bras', '2025-05-02', 3),
        read('crimeCastigo', '2025-07-21', 5, 'Raskólnikov me acompanhou pelas ruas por semanas.'),
        read('ficcoes', '2025-09-12', 4, 'Cada conto é um labirinto de bolso.'),
        read('metamorfose', '2025-10-02', 4),
      ]),
    ),
    person(
      3,
      'clara_nunes',
      'Clara Nunes',
      'Public',
      '2024-09-20T10:00:00Z',
      library([
        read('lucidez', '2025-02-01', 4),
        read('pagli', '2025-03-15', 4, 'Uma história pequena sobre cuidado e fronteiras.'),
        read('pigments', '2025-05-20', 5, 'Prosa que se lê com os olhos fechados.'),
        read('farol', '2025-07-04', 3),
        read(
          'milEUmaNoites',
          '2025-08-25',
          4,
          'Contar histórias para adiar o fim: o melhor enredo possível.',
        ),
        read('norwegianWood', '2025-10-18', 4),
        read('cidadeCachorros', '2025-12-01', 3),
      ]),
    ),
    person(
      4,
      'tomas_arruda',
      'Tomás Arruda',
      'Public',
      '2025-05-05T10:00:00Z',
      library([
        read('cemAnos', '2025-06-01', 4),
        read('americanah', '2025-07-15', 5, 'Já virou presente.'),
      ]),
    ),
    person(
      5,
      'marcos_lima',
      'Marcos Lima',
      'Public',
      '2025-02-14T10:00:00Z',
      library([read('crimeCastigo', '2025-04-04', 4), read('farol', '2025-05-30', 2)]),
    ),
    person(
      6,
      'ana_vazia',
      'Ana Vazia',
      'Public',
      '2025-10-01T10:00:00Z',
      library([{ key: 'miramar', status: 'WantToRead' }]),
    ),
    person(
      7,
      'bia_prado',
      'Bia Prado',
      'Private',
      '2024-04-04T10:00:00Z',
      library([read('cegueira', '2025-01-02', 5, 'Resenha privada.')]),
    ),
    person(
      8,
      'ze_carlos',
      'Zé Carlos',
      'Private',
      '2024-04-05T10:00:00Z',
      library([read('processo', '2025-01-03', 4)]),
    ),
  ]
}

/** Arestas iniciais "seguidor → seguido" (ids). O usuário demo segue 2 pessoas e é seguido por 4. */
function initialFollows(): Array<[string, string]> {
  const demo = MOCK_DEMO_USER.id
  return [
    [demo, personId(1)],
    [demo, personId(3)],
    [personId(1), demo],
    [personId(2), demo],
    [personId(8), demo], // conta privada: omitida das listas
    [personId(4), demo],
    [personId(2), personId(1)],
    [personId(3), personId(1)],
    [personId(5), personId(1)],
    [personId(1), personId(3)],
    [personId(6), personId(3)],
    [personId(4), personId(2)],
  ]
}

const notFound = () =>
  new ApiError({ status: 404, title: 'Perfil não encontrado', code: 'profile.not_found' })

function validation(errors: Record<string, string[]>, code = 'validation.failed') {
  return new ApiError({
    status: 400,
    title: 'Um ou mais erros de validação ocorreram.',
    code,
    errors,
  })
}

function normalizeText(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .trim()
}

export interface MockProfilesDeps {
  /** Usuário autenticado (lança 401 sem sessão). */
  currentUserId: () => string
  /** Usuários que fazem login nos mocks (demo, moderador, contas registradas). */
  userAccounts: () => MockAccount[]
  countries: CountryLookup
}

export function createMockProfiles(deps: MockProfilesDeps) {
  const people = createMockPeople()
  const follows = new Set(initialFollows().map(([a, b]) => `${a}>${b}`))
  const edge = (follower: string, followed: string) => `${follower}>${followed}`

  const accounts = () => [...deps.userAccounts(), ...people]
  const byId = (id: string) => accounts().find((a) => a.id === id)
  const byHandle = (handle: string) => {
    const normalized = normalizeHandle(handle)
    return accounts().find((a) => a.handle === normalized)
  }
  /** Perfil visível para o usuário logado: `Public` ou o próprio. */
  function visible(handle: string, me: string): MockAccount {
    const account = byHandle(handle)
    if (!account || (account.visibility !== 'Public' && account.id !== me)) throw notFound()
    return account
  }
  const followersOf = (id: string) =>
    [...follows].filter((e) => e.endsWith(`>${id}`)).map((e) => e.split('>')[0]!)
  const followingOf = (id: string) =>
    [...follows].filter((e) => e.startsWith(`${id}>`)).map((e) => e.split('>')[1]!)

  function summary(account: MockAccount, me: string): ProfileSummaryDto {
    return {
      handle: account.handle!,
      name: account.name,
      followersCount: followersOf(account.id).length,
      readBooksCount: readItems(account.items()).length,
      isFollowing: follows.has(edge(me, account.id)),
      followsYou: follows.has(edge(account.id, me)),
    }
  }

  function page<T>(all: T[], params: { page?: number; pageSize?: number } = {}): PagedResult<T> {
    const pageSize = Math.min(Math.max(params.pageSize ?? 20, 1), 100)
    const pageNumber = Math.max(params.page ?? 1, 1)
    return {
      items: all.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
      page: pageNumber,
      pageSize,
      totalCount: all.length,
      totalPages: Math.ceil(all.length / pageSize),
    }
  }

  /** Lista de ids → resumos de contas descobríveis (`Public` com apelido), por nome. */
  function summaries(ids: string[], me: string): ProfileSummaryDto[] {
    return ids
      .map(byId)
      .filter((a): a is MockAccount => !!a && !!a.handle && a.visibility === 'Public')
      .sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'))
      .map((a) => summary(a, me))
  }

  const api: ProfilesApi = {
    async search(q, limit = 10, signal) {
      const me = deps.currentUserId()
      const errors: Record<string, string[]> = {}
      const term = normalizeText(q ?? '')
      if (term.length < 2) errors.q = ['Informe ao menos 2 caracteres.']
      if (limit < 1 || limit > 20) errors.limit = ['O limite deve estar entre 1 e 20.']
      if (Object.keys(errors).length) throw validation(errors)
      await delay(null, 150)
      if (signal?.aborted) throw new DOMException('Requisição cancelada', 'AbortError')
      const handleTerm = term.replace(/^@/, '')
      return accounts()
        .filter((a) => a.id !== me && a.visibility === 'Public' && a.handle)
        .map((a) => {
          const words = normalizeText(a.name).split(/\s+/)
          const rank =
            a.handle === handleTerm
              ? 0
              : a.handle!.startsWith(handleTerm)
                ? 1
                : words.some((w) => w.startsWith(term))
                  ? 2
                  : -1
          return { a, rank }
        })
        .filter(({ rank }) => rank >= 0)
        .sort((x, y) => x.rank - y.rank || x.a.name.localeCompare(y.a.name, 'pt-BR'))
        .slice(0, limit)
        .map(({ a }) => summary(a, me))
    },
    async get(handle) {
      const me = deps.currentUserId()
      await delay(null)
      const account = visible(handle, me)
      const isSelf = account.id === me
      const items = account.items()
      const dto: ProfileDto = {
        ...summary(account, me),
        followingCount: followingOf(account.id).length,
        exploredCountriesAuthors: computeMapData(items, 'Authors', deps.countries)
          .exploredCountries,
        exploredCountriesOrigins: computeMapData(items, 'Origins', deps.countries)
          .exploredCountries,
        memberSince: account.memberSince,
        isSelf,
      }
      return isSelf ? { ...dto, isFollowing: false, followsYou: false } : dto
    },
    async follow(handle) {
      const me = deps.currentUserId()
      await delay(null, 150)
      if (byHandle(handle)?.id === me) {
        throw new ApiError({ status: 400, title: 'Operação inválida', code: 'follow.self' })
      }
      follows.add(edge(me, visible(handle, me).id))
    },
    async unfollow(handle) {
      const me = deps.currentUserId()
      await delay(null, 150)
      const account = byHandle(handle)
      if (account) follows.delete(edge(me, account.id))
    },
    async followers(handle, params) {
      const me = deps.currentUserId()
      await delay(null)
      return page(summaries(followersOf(visible(handle, me).id), me), params)
    },
    async following(handle, params) {
      const me = deps.currentUserId()
      await delay(null)
      return page(summaries(followingOf(visible(handle, me).id), me), params)
    },
    async map(handle, mapType) {
      const me = deps.currentUserId()
      await delay(null)
      return computeMapData(visible(handle, me).items(), mapType, deps.countries, 'Read')
    },
    async countryDetail(handle, mapType, iso) {
      const me = deps.currentUserId()
      await delay(null, 150)
      const account = visible(handle, me)
      const code = iso.toUpperCase()
      if (!deps.countries.has(code)) {
        throw new ApiError({ status: 400, title: 'País desconhecido', code: 'country.unknown_iso' })
      }
      return computeProfileCountryDetail(account.items(), mapType, code, deps.countries)
    },
    async meFollowers(params) {
      const me = deps.currentUserId()
      await delay(null)
      return page(summaries(followersOf(me), me), params)
    },
    async meFollowing(params) {
      const me = deps.currentUserId()
      await delay(null)
      return page(summaries(followingOf(me), me), params)
    },
  }

  /**
   * Regras de apelido de `PUT /auth/me` (§2 / §8.1): omitido mantém; `null`/"" remove; formato,
   * unicidade sem diferenciar caixa e obrigatoriedade com visibilidade `Public`.
   */
  function resolveHandle(userId: string, current: string | null, request: UpdateProfileRequest) {
    let handle = current
    if (request.handle !== undefined) {
      const raw = (request.handle ?? '').trim().toLowerCase()
      if (!raw) handle = null
      else {
        if (!isValidHandle(raw)) {
          throw validation({ handle: ['Apelido inválido.'] }, 'profile.handle_invalid')
        }
        const owner = byHandle(raw)
        if (owner && owner.id !== userId) {
          throw new ApiError({
            status: 409,
            title: 'Apelido em uso',
            code: 'profile.handle_taken',
          })
        }
        handle = raw
      }
    }
    if (request.mapVisibility === 'Public' && !handle) {
      throw new ApiError({
        status: 400,
        title: 'Apelido obrigatório',
        code: 'profile.handle_required',
      })
    }
    return handle
  }

  /** Contagens de seguidores/seguindo (para `UserProfileDto`). */
  function counts(userId: string) {
    return {
      followersCount: followersOf(userId).length,
      followingCount: followingOf(userId).length,
    }
  }

  return { api, resolveHandle, counts }
}
