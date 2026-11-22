<script setup lang="ts">
/**
 * Painel de um país do mapa: drawer lateral (desktop) ou bottom sheet (mobile, ≤ 899 px).
 * Não é modal: o mapa continua clicável (clicar em outro país troca o painel; clicar no oceano
 * ou fora fecha). Foco: vai para o título ao abrir e volta ao elemento anterior ao fechar.
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import type { ApiError } from '@/api/errors'
import { getErrorMessage } from '@/api/errors'
import BookCover from '@/components/books/BookCover.vue'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import StarRating from '@/components/ui/StarRating.vue'
import type { CountryDetailDto, CountryMapDatum } from '@/types/api'
import { formatDate } from '@/utils/format'
import { CONTINENT_LABELS, pluralize } from '@/utils/labels'

const props = defineProps<{
  /** Resumo vindo do mapa: exibido enquanto o detalhe carrega ou se ele falhar. */
  summary: CountryMapDatum | null
  detail: CountryDetailDto | null
  loading: boolean
  error: ApiError | null
}>()

const emit = defineEmits<{ close: []; retry: [] }>()

const root = ref<HTMLElement | null>(null)
const heading = ref<HTMLElement | null>(null)
let previouslyFocused: HTMLElement | null = null

const name = computed(() => props.detail?.name ?? props.summary?.name ?? 'País')
const continent = computed(() => props.detail?.continent ?? props.summary?.continent ?? null)
const bookCount = computed(() => props.detail?.bookCount ?? props.summary?.bookCount ?? null)
const authorCount = computed(() => props.detail?.authorCount ?? props.summary?.authorCount ?? null)

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.stopPropagation()
    emit('close')
  }
}

/** Clique fora fecha, exceto no mapa e na lista de países (que têm a própria seleção). */
function onPointerDown(event: PointerEvent) {
  const target = event.target as Element | null
  if (!target || root.value?.contains(target)) return
  if (target.closest('[data-keep-panel]')) return
  emit('close')
}

onMounted(async () => {
  const active = document.activeElement
  previouslyFocused = active instanceof HTMLElement && active !== document.body ? active : null
  document.addEventListener('keydown', onKeydown)
  document.addEventListener('pointerdown', onPointerDown)
  await nextTick()
  heading.value?.focus({ preventScroll: true })
})

onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown)
  document.removeEventListener('pointerdown', onPointerDown)
  if (previouslyFocused?.isConnected) previouslyFocused.focus({ preventScroll: true })
})
</script>

<template>
  <aside
    ref="root"
    class="panel"
    role="dialog"
    aria-modal="false"
    aria-labelledby="country-panel-title"
    data-testid="country-panel"
  >
    <span class="panel__grip" aria-hidden="true" />
    <header class="panel__header">
      <div>
        <p v-if="continent" class="eyebrow">{{ CONTINENT_LABELS[continent] }}</p>
        <h2 id="country-panel-title" ref="heading" tabindex="-1">{{ name }}</h2>
        <p v-if="bookCount !== null" class="muted panel__counts">
          {{ pluralize(bookCount, 'livro', 'livros') }}
          <template v-if="authorCount !== null">
            · {{ pluralize(authorCount, 'autor', 'autores') }}
          </template>
        </p>
      </div>
      <button type="button" class="panel__close" aria-label="Fechar painel" @click="emit('close')">
        <AppIcon name="close" :size="20" />
      </button>
    </header>

    <div class="panel__body">
      <div v-if="loading" class="panel__state" role="status">
        <BaseSpinner label="Carregando livros do país" />
      </div>
      <div v-else-if="error" class="panel__state" role="alert">
        <p>{{ getErrorMessage(error) }}</p>
        <BaseButton variant="secondary" size="sm" @click="emit('retry')">
          Tentar novamente
        </BaseButton>
      </div>
      <template v-else-if="detail">
        <p v-if="!detail.books.length" class="muted">Nenhum livro encontrado para este país.</p>
        <ul v-else role="list" class="books">
          <li v-for="book in detail.books" :key="book.libraryItemId">
            <RouterLink
              :to="{ name: 'library-item', params: { id: book.libraryItemId } }"
              class="book"
            >
              <BookCover :title="book.title" :cover-url="book.coverUrl" size="sm" />
              <span class="book__text">
                <span class="book__title">{{ book.title }}</span>
                <span class="muted">{{ book.authors.join(', ') || 'Autoria desconhecida' }}</span>
                <span class="book__meta">
                  <span v-if="book.finishedDate">Lido em {{ formatDate(book.finishedDate) }}</span>
                  <StarRating
                    v-if="book.rating"
                    :model-value="book.rating"
                    readonly
                    size="sm"
                    label="Nota"
                  />
                </span>
              </span>
            </RouterLink>
          </li>
        </ul>
      </template>
    </div>
  </aside>
</template>

<style scoped>
.panel {
  display: flex;
  flex-direction: column;
  background: rgb(10 18 32 / 0.97);
  border: 1px solid var(--color-border);
  box-shadow: var(--shadow-lg);
  backdrop-filter: blur(10px);
  z-index: var(--z-overlay);
}
.panel__grip {
  display: none;
}
.panel__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-3);
  padding: var(--space-5) var(--space-5) var(--space-3);
  border-bottom: 1px solid var(--color-hairline);
}
.panel__header h2 {
  font-size: var(--text-xl);
  outline: none;
  margin-left: -6px;
  padding: 0 6px;
}
.panel__header h2:focus-visible {
  outline: 2px solid var(--color-sky);
  outline-offset: 3px;
  border-radius: var(--radius-sm);
}
.panel__counts {
  margin-top: var(--space-1);
  font-size: var(--text-sm);
}
.panel__close {
  display: grid;
  place-items: center;
  width: 36px;
  height: 36px;
  flex: none;
  border-radius: var(--radius-md);
  color: var(--color-text-muted);
}
.panel__close:hover {
  background: var(--color-surface-2);
  color: var(--color-text);
}
.panel__body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: var(--space-3) var(--space-4) var(--space-5);
}
.panel__state {
  display: grid;
  justify-items: center;
  gap: var(--space-3);
  padding: var(--space-6) var(--space-3);
  text-align: center;
}
.books {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.book {
  display: flex;
  gap: var(--space-3);
  align-items: center;
  padding: var(--space-2);
  border-radius: var(--radius-md);
  color: inherit;
  text-decoration: none;
}
.book:hover {
  background: var(--color-surface-2);
  color: inherit;
}
.book__text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  font-size: var(--text-sm);
}
.book__title {
  font-family: var(--font-display);
  font-size: var(--text-base);
}
.book__meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-xs);
  color: var(--color-text-faint);
}

/* Desktop: drawer em coluna ao lado do mapa (a altura vem de --stage-height da página). */
@media (min-width: 900px) {
  .panel {
    height: var(--stage-height, 600px);
    border-radius: var(--radius-xl);
  }
}

/* Mobile/tablet: bottom sheet acima da navegação inferior. */
@media (max-width: 899px) {
  .panel {
    position: fixed;
    left: 0;
    right: 0;
    bottom: calc(var(--bottom-nav-height) + env(safe-area-inset-bottom));
    max-height: min(70dvh, 560px);
    border-width: 1px 0 0;
    border-radius: var(--radius-xl) var(--radius-xl) 0 0;
  }
  .panel__grip {
    display: block;
    width: 40px;
    height: 4px;
    margin: var(--space-2) auto 0;
    border-radius: var(--radius-pill);
    background: var(--color-border);
  }
  .panel__header {
    padding-top: var(--space-3);
  }
}
</style>
