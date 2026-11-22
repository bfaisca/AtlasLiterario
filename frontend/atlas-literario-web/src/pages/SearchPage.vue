<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '@/api'
import { getErrorMessage, toApiError, type ApiError } from '@/api/errors'
import type { BookSearchResultDto, LibraryItemDto, SearchType } from '@/types/api'
import { useUiStore } from '@/stores/ui'
import { debounce } from '@/utils/debounce'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseBadge from '@/components/ui/BaseBadge.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import AddToLibraryDialog from '@/components/books/AddToLibraryDialog.vue'
import type { AddOutcome } from '@/utils/addRecovery'
import BookCover from '@/components/books/BookCover.vue'

const MIN_QUERY_LENGTH = 2
const DEBOUNCE_MS = 400

const TYPES: Array<{ value: SearchType; label: string }> = [
  { value: 'all', label: 'Tudo' },
  { value: 'title', label: 'Título' },
  { value: 'author', label: 'Autor' },
  { value: 'isbn', label: 'ISBN' },
]

const route = useRoute()
const router = useRouter()
const ui = useUiStore()

const query = ref(typeof route.query.q === 'string' ? route.query.q : '')
const type = ref<SearchType>(
  TYPES.some((t) => t.value === route.query.type) ? (route.query.type as SearchType) : 'all',
)
const results = ref<BookSearchResultDto[] | null>(null)
const loading = ref(false)
const error = ref<ApiError | null>(null)
const selected = ref<BookSearchResultDto | null>(null)
const dialogOpen = ref(false)
let seq = 0

const trimmed = computed(() => query.value.trim())
const externalUnavailable = computed(
  () => error.value?.code === 'external.unavailable' || error.value?.status === 502,
)

async function runSearch() {
  const q = trimmed.value
  void router.replace({
    query: q ? { q, ...(type.value !== 'all' ? { type: type.value } : {}) } : {},
  })
  if (q.length < MIN_QUERY_LENGTH) {
    results.value = null
    error.value = null
    loading.value = false
    return
  }
  const current = ++seq
  loading.value = true
  error.value = null
  try {
    const data = await api.books.search({ q, type: type.value, limit: 20 })
    if (current === seq) results.value = data
  } catch (e) {
    if (current === seq) {
      error.value = toApiError(e)
      results.value = null
    }
  } finally {
    if (current === seq) loading.value = false
  }
}

const debouncedSearch = debounce(runSearch, DEBOUNCE_MS)
watch(query, () => debouncedSearch())
watch(type, () => {
  debouncedSearch.cancel()
  void runSearch()
})
onBeforeUnmount(() => debouncedSearch.cancel())
if (trimmed.value) void runSearch()

function submitNow() {
  debouncedSearch.cancel()
  void runSearch()
}

function choose(result: BookSearchResultDto) {
  selected.value = result
  dialogOpen.value = true
}

function onAdded(item: LibraryItemDto, outcome: AddOutcome) {
  ui.success(
    outcome === 'duplicate'
      ? `“${item.book.title}” já estava na sua biblioteca.`
      : `“${item.book.title}” foi adicionado à sua biblioteca.`,
  )
  void router.push({ name: 'library-item', params: { id: item.id } })
}

function sourceLabel(result: BookSearchResultDto): string {
  return result.source.provider === 'Local'
    ? 'Catálogo'
    : result.source.provider === 'GoogleBooks'
      ? 'Google Books'
      : result.source.provider === 'OpenLibrary'
        ? 'Open Library'
        : result.source.provider
}
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <p class="eyebrow">Adicionar livros</p>
        <h1>Pesquisar</h1>
      </div>
      <BaseButton :to="{ name: 'book-new' }" variant="secondary">Cadastrar manualmente</BaseButton>
    </header>

    <form class="search" role="search" @submit.prevent="submitNow">
      <div class="search__types" role="radiogroup" aria-label="Pesquisar por">
        <button
          v-for="option in TYPES"
          :key="option.value"
          type="button"
          role="radio"
          class="search__type"
          :class="{ 'is-active': type === option.value }"
          :aria-checked="type === option.value"
          @click="type = option.value"
        >
          {{ option.label }}
        </button>
      </div>
      <div class="search__field">
        <AppIcon name="search" class="search__icon" />
        <label for="search-input" class="sr-only">Título, autor ou ISBN</label>
        <input
          id="search-input"
          v-model="query"
          type="search"
          class="search__input"
          :placeholder="type === 'isbn' ? 'ISBN-10 ou ISBN-13' : 'Título, autor ou ISBN'"
          autocomplete="off"
          enterkeyhint="search"
        />
        <BaseSpinner v-if="loading" size="sm" class="search__spinner" label="Pesquisando" />
      </div>
    </form>

    <div aria-live="polite">
      <EmptyState
        v-if="!trimmed"
        icon="search"
        title="Que livro você quer guardar no seu atlas?"
        description="Pesquise pelo título, pelo nome do autor ou pelo ISBN. Os dados vêm do catálogo do Atlas, do Google Books e da Open Library."
      />
      <p v-else-if="trimmed.length < MIN_QUERY_LENGTH" class="muted">
        Digite ao menos 2 caracteres.
      </p>
      <div v-else-if="loading && !results" class="center">
        <BaseSpinner size="lg" label="Pesquisando" />
      </div>
      <EmptyState
        v-else-if="error"
        icon="alert"
        :title="
          externalUnavailable ? 'Fontes externas indisponíveis' : 'Não foi possível pesquisar'
        "
        :description="getErrorMessage(error)"
      >
        <div class="cluster">
          <BaseButton variant="secondary" @click="submitNow">Tentar novamente</BaseButton>
          <BaseButton v-if="externalUnavailable" :to="{ name: 'book-new' }" variant="ghost">
            Cadastrar manualmente
          </BaseButton>
        </div>
      </EmptyState>
      <EmptyState
        v-else-if="results && !results.length"
        icon="book"
        title="Nenhum resultado"
        description="Tente outro termo, o nome completo do autor ou o ISBN."
      >
        <BaseButton :to="{ name: 'book-new' }" variant="secondary"
          >Cadastrar manualmente</BaseButton
        >
      </EmptyState>

      <template v-else-if="results">
        <p class="muted results__count">
          {{ results.length }} {{ results.length === 1 ? 'resultado' : 'resultados' }}
        </p>
        <ul role="list" class="results" :aria-busy="loading">
          <li v-for="result in results" :key="result.resultKey" class="result">
            <BookCover
              :title="result.title"
              :author="result.authors[0]"
              :cover-url="result.coverUrl"
              size="sm"
            />
            <div class="result__body">
              <h2 class="result__title">
                {{ result.title }}
                <span v-if="result.subtitle" class="result__subtitle">{{ result.subtitle }}</span>
              </h2>
              <p class="result__authors">
                {{ result.authors.join(', ') || 'Autoria desconhecida' }}
              </p>
              <p class="result__meta">
                <span v-if="result.publicationYear">{{ result.publicationYear }}</span>
                <span v-if="result.publisher">{{ result.publisher }}</span>
                <span>{{ sourceLabel(result) }}</span>
              </p>
            </div>
            <RouterLink
              v-if="result.inLibrary && result.libraryItemId"
              class="result__in-library"
              :to="{ name: 'library-item', params: { id: result.libraryItemId } }"
              :aria-label="`Abrir ${result.title} na biblioteca`"
            >
              <BaseBadge tone="success">Já na biblioteca</BaseBadge>
            </RouterLink>
            <BaseBadge v-else-if="result.inLibrary" tone="success">Já na biblioteca</BaseBadge>
            <BaseButton
              v-else
              size="sm"
              variant="secondary"
              :aria-label="`Adicionar ${result.title}`"
              @click="choose(result)"
            >
              Adicionar
            </BaseButton>
          </li>
        </ul>
        <p class="manual-link muted">
          Não encontrou?
          <RouterLink :to="{ name: 'book-new' }">Cadastrar manualmente</RouterLink>
        </p>
      </template>
    </div>

    <AddToLibraryDialog v-model:open="dialogOpen" :result="selected" @added="onAdded" />
  </div>
</template>

<style scoped>
.center {
  display: grid;
  place-items: center;
  padding: var(--space-7);
}
.search {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.search__types {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.search__type {
  padding: 0.35rem 0.9rem;
  border-radius: var(--radius-pill);
  border: 1px solid var(--color-border);
  font-size: var(--text-sm);
  color: var(--color-text-muted);
}
.search__type.is-active {
  color: var(--color-brass-strong);
  border-color: rgb(227 191 124 / 0.45);
  background: var(--color-brass-soft);
}
.search__field {
  position: relative;
}
.search__icon {
  position: absolute;
  left: 1rem;
  top: 50%;
  transform: translateY(-50%);
  color: var(--color-text-faint);
}
.search__spinner {
  position: absolute;
  right: 1rem;
  top: 50%;
  transform: translateY(-50%);
}
.search__input {
  width: 100%;
  padding: 0.95rem 3rem;
  font-size: var(--text-lg);
  background: var(--color-ocean);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-pill);
}
.search__input:focus {
  outline: none;
  border-color: var(--color-sky);
  box-shadow: 0 0 0 3px var(--color-sky-soft);
}
.results__count {
  font-size: var(--text-sm);
  margin-bottom: var(--space-2);
}
.results {
  display: flex;
  flex-direction: column;
}
.result {
  display: flex;
  align-items: center;
  gap: var(--space-4);
  padding: var(--space-3) 0;
  border-bottom: 1px solid var(--color-hairline);
}
.result__body {
  flex: 1;
  min-width: 0;
}
.result__title {
  font-size: var(--text-lg);
  line-height: 1.25;
}
.result__subtitle {
  display: block;
  font-family: var(--font-body);
  font-size: var(--text-sm);
  color: var(--color-text-muted);
}
.result__authors {
  font-size: var(--text-sm);
  color: var(--color-text-muted);
}
.result__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1) var(--space-3);
  font-size: var(--text-xs);
  color: var(--color-text-faint);
}
.manual-link {
  margin-top: var(--space-5);
  font-size: var(--text-sm);
}
</style>
