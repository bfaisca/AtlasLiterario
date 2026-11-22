<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { storeToRefs } from 'pinia'
import { useLibraryStore } from '@/stores/library'
import {
  defaultFilters,
  filtersFromRouteQuery,
  filtersToRouteQuery,
  hasActiveFilters,
  type LibraryFilters,
} from '@/stores/libraryFilters'
import { getErrorMessage } from '@/api/errors'
import {
  READING_STATUSES,
  type LibraryGeoMissing,
  type LibrarySort,
  type ReadingStatus,
} from '@/types/api'
import { GEO_MISSING_OPTIONS } from '@/utils/libraryGeo'
import { debounce } from '@/utils/debounce'
import { READING_STATUS_LABELS, pluralize } from '@/utils/labels'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseSelect from '@/components/ui/BaseSelect.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import BookCard from '@/components/books/BookCard.vue'

const route = useRoute()
const router = useRouter()
const library = useLibraryStore()
const { items, totalCount, loading, loadingMore, error, hasMore } = storeToRefs(library)

/** Estado local dos controles; a URL é a fonte da verdade dos filtros aplicados. */
const form = ref<LibraryFilters>(filtersFromRouteQuery(route.query))
const filtersOpen = ref(false)

const SORT_OPTIONS: Array<{ value: LibrarySort; label: string }> = [
  { value: 'createdAt', label: 'Adicionados recentemente' },
  { value: 'title', label: 'Título' },
  { value: 'author', label: 'Autor' },
  { value: 'finishedDate', label: 'Data de conclusão' },
  { value: 'rating', label: 'Avaliação' },
  { value: 'publicationYear', label: 'Ano de publicação' },
]
const RATING_OPTIONS: Array<{ value: number; label: string }> = [
  { value: 0, label: 'Qualquer nota' },
  { value: 3, label: '3 estrelas ou mais' },
  { value: 4, label: '4 estrelas ou mais' },
  { value: 5, label: '5 estrelas' },
]

const minRating = computed({
  get: () => form.value.minRating ?? 0,
  set: (value: number) => apply({ ...form.value, minRating: value || null }),
})
const geoMissing = computed({
  get: () => form.value.geoMissing ?? '',
  set: (value: LibraryGeoMissing | '') => apply({ ...form.value, geoMissing: value || null }),
})
const sort = computed({
  get: () => form.value.sort,
  set: (value: LibrarySort) => apply({ ...form.value, sort: value }),
})
const active = computed(() => hasActiveFilters(form.value))
/** Só o filtro de dados geográficos está ativo: a lista vazia tem mensagem própria. */
const onlyGeoFilter = computed(
  () => !!form.value.geoMissing && !hasActiveFilters({ ...form.value, geoMissing: null }),
)
const knownGenres = computed(() =>
  [...new Set(items.value.flatMap((i) => i.book.genres))].sort((a, b) =>
    a.localeCompare(b, 'pt-BR'),
  ),
)

function apply(next: LibraryFilters) {
  form.value = next
  void router.replace({ query: filtersToRouteQuery(next) })
}

const applyDebounced = debounce(() => apply({ ...form.value }), 350)
onBeforeUnmount(() => applyDebounced.cancel())

function toggleStatus(status: ReadingStatus) {
  const current = form.value.status
  apply({
    ...form.value,
    status: current.includes(status) ? current.filter((s) => s !== status) : [...current, status],
  })
}

function toggleOrder() {
  apply({ ...form.value, order: form.value.order === 'asc' ? 'desc' : 'asc' })
}

function clearFilters() {
  apply({ ...defaultFilters(), sort: form.value.sort, order: form.value.order })
}

// URL → store (inclui voltar/avançar do navegador e links com filtros)
watch(
  () => route.query,
  (query) => {
    if (route.name !== 'library') return
    const filters = filtersFromRouteQuery(query)
    // Preserva o texto digitado (ex.: espaço final) se equivale ao filtro aplicado.
    form.value = { ...filters, q: form.value.q.trim() === filters.q ? form.value.q : filters.q }
    void library.setFilters(filters)
  },
  { immediate: true },
)
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <p class="eyebrow">Biblioteca</p>
        <h1>Suas estantes</h1>
        <p class="muted count" aria-live="polite">{{ pluralize(totalCount, 'livro', 'livros') }}</p>
      </div>
      <BaseButton :to="{ name: 'search' }">Adicionar livro</BaseButton>
    </header>

    <section class="toolbar" aria-label="Filtros da biblioteca">
      <div class="toolbar__row">
        <div class="toolbar__search">
          <AppIcon name="search" :size="18" class="toolbar__search-icon" />
          <label for="library-q" class="sr-only">Buscar por título ou autor</label>
          <input
            id="library-q"
            v-model="form.q"
            type="search"
            class="toolbar__input"
            placeholder="Buscar por título ou autor"
            @input="applyDebounced()"
            @keydown.enter.prevent="applyDebounced.flush()"
          />
        </div>
        <button
          type="button"
          class="toolbar__toggle"
          :aria-expanded="filtersOpen"
          aria-controls="library-filters"
          @click="filtersOpen = !filtersOpen"
        >
          <AppIcon name="filter" :size="18" />
          Filtros
          <span v-if="active" class="toolbar__dot" aria-label="filtros ativos" />
        </button>
      </div>

      <div class="chips" role="group" aria-label="Status de leitura">
        <button
          v-for="status in READING_STATUSES"
          :key="status"
          type="button"
          class="chip"
          :class="{ 'is-active': form.status.includes(status) }"
          :aria-pressed="form.status.includes(status)"
          @click="toggleStatus(status)"
        >
          {{ READING_STATUS_LABELS[status] }}
        </button>
      </div>

      <div id="library-filters" class="filters" :class="{ 'is-open': filtersOpen }">
        <div class="filters__genre">
          <label for="library-genre" class="filters__label">Gênero</label>
          <input
            id="library-genre"
            v-model="form.genre"
            list="library-genres"
            class="filters__input"
            placeholder="Todos"
            @change="apply({ ...form })"
            @keydown.enter.prevent="apply({ ...form })"
          />
          <datalist id="library-genres">
            <option v-for="genre in knownGenres" :key="genre" :value="genre" />
          </datalist>
        </div>
        <BaseSelect v-model="minRating" label="Avaliação mínima" :options="RATING_OPTIONS" />
        <BaseSelect
          v-model="geoMissing"
          label="Dados geográficos"
          :options="GEO_MISSING_OPTIONS"
          data-testid="geo-missing-select"
        />
        <div class="filters__sort">
          <BaseSelect v-model="sort" label="Ordenar por" :options="SORT_OPTIONS" />
          <button
            type="button"
            class="filters__order"
            :aria-label="
              form.order === 'asc'
                ? 'Ordem crescente (alterar para decrescente)'
                : 'Ordem decrescente (alterar para crescente)'
            "
            @click="toggleOrder"
          >
            <AppIcon name="sort" :size="18" />
            {{ form.order === 'asc' ? 'Crescente' : 'Decrescente' }}
          </button>
        </div>
        <BaseButton v-if="active" variant="ghost" size="sm" @click="clearFilters"
          >Limpar filtros</BaseButton
        >
      </div>
    </section>

    <div v-if="loading && !items.length" class="center"><BaseSpinner size="lg" /></div>
    <EmptyState
      v-else-if="error && !items.length"
      icon="alert"
      title="Não foi possível carregar a biblioteca"
      :description="getErrorMessage(error)"
    >
      <BaseButton variant="secondary" @click="library.fetch({ force: true })"
        >Tentar novamente</BaseButton
      >
    </EmptyState>
    <EmptyState
      v-else-if="!items.length && onlyGeoFilter"
      icon="globe"
      title="Nenhum livro com dados geográficos faltando."
      description="Todos os livros que se encaixam já têm país conhecido. Dado ausente é só informação ainda não encontrada: assim que aparecer algum, ele será listado aqui."
    >
      <BaseButton variant="secondary" @click="clearFilters">Ver todos os livros</BaseButton>
    </EmptyState>
    <EmptyState
      v-else-if="!items.length && active"
      icon="filter"
      title="Nenhum livro com esses filtros"
      description="Experimente remover algum filtro ou buscar outro termo."
    >
      <BaseButton variant="secondary" @click="clearFilters">Limpar filtros</BaseButton>
    </EmptyState>
    <EmptyState
      v-else-if="!items.length"
      icon="globe"
      title="Sua primeira estante espera por você"
      description="Cada livro que você concluir acende um país no seu mapa. Comece pesquisando um título que você ama."
    >
      <BaseButton :to="{ name: 'search' }">Pesquisar livros</BaseButton>
    </EmptyState>

    <template v-else>
      <ul role="list" class="shelf" :aria-busy="loading">
        <li v-for="item in items" :key="item.id"><BookCard :item="item" /></li>
      </ul>
      <div v-if="hasMore" class="more">
        <BaseButton variant="secondary" :loading="loadingMore" @click="library.loadMore()">
          Carregar mais
        </BaseButton>
      </div>
    </template>
  </div>
</template>

<style scoped>
.center {
  display: grid;
  place-items: center;
  padding: var(--space-8);
}
.count {
  margin-top: var(--space-1);
  font-size: var(--text-sm);
}
.toolbar {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.toolbar__row {
  display: flex;
  gap: var(--space-2);
}
.toolbar__search {
  position: relative;
  flex: 1;
}
.toolbar__search-icon {
  position: absolute;
  left: 0.9rem;
  top: 50%;
  transform: translateY(-50%);
  color: var(--color-text-faint);
}
.toolbar__input,
.filters__input {
  width: 100%;
  padding: 0.7rem 0.9rem;
  background: var(--color-ocean);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}
.toolbar__input {
  padding-left: 2.6rem;
  border-radius: var(--radius-pill);
}
.toolbar__input:focus,
.filters__input:focus {
  outline: none;
  border-color: var(--color-sky);
  box-shadow: 0 0 0 3px var(--color-sky-soft);
}
.toolbar__toggle {
  position: relative;
  display: none;
  align-items: center;
  gap: var(--space-2);
  padding: 0 var(--space-4);
  border-radius: var(--radius-pill);
  border: 1px solid var(--color-border);
  color: var(--color-text-muted);
  font-size: var(--text-sm);
}
.toolbar__dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--color-brass);
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.chip {
  padding: 0.35rem 0.9rem;
  border-radius: var(--radius-pill);
  border: 1px solid var(--color-border);
  font-size: var(--text-sm);
  color: var(--color-text-muted);
  transition: all var(--duration-fast) var(--ease-out);
}
.chip:hover {
  color: var(--color-text);
  border-color: var(--color-border-strong);
}
.chip.is-active {
  color: var(--color-brass-strong);
  border-color: rgb(227 191 124 / 0.45);
  background: var(--color-brass-soft);
}
.filters {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  align-items: end;
  gap: var(--space-3);
}
.filters__label {
  display: block;
  margin-bottom: var(--space-2);
  font-size: var(--text-sm);
  font-weight: 500;
  color: var(--color-text-muted);
}
.filters__sort {
  grid-column: span 2;
  display: flex;
  align-items: flex-end;
  gap: var(--space-2);
}
.filters__sort > :first-child {
  flex: 1;
}
.filters__order {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  height: 2.75rem;
  padding: 0 var(--space-3);
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border);
  color: var(--color-text-muted);
  font-size: var(--text-sm);
  white-space: nowrap;
}
.filters__order:hover {
  color: var(--color-text);
}
@media (max-width: 899px) {
  .toolbar__toggle {
    display: inline-flex;
  }
  .filters {
    display: none;
    grid-template-columns: 1fr;
    padding: var(--space-4);
    border-radius: var(--radius-lg);
    background: var(--color-surface);
    border: 1px solid var(--color-hairline);
  }
  .filters.is-open {
    display: grid;
  }
  .filters__sort {
    grid-column: auto;
  }
}
.shelf {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(170px, 1fr));
  gap: var(--space-4) var(--space-3);
}
@media (max-width: 480px) {
  .shelf {
    grid-template-columns: repeat(2, 1fr);
    gap: var(--space-3) var(--space-2);
  }
}
.more {
  display: flex;
  justify-content: center;
}
</style>
