<script setup lang="ts">
/**
 * Item de livro do painel do país. Compartilhado entre o mapa de origens (lista de livros) e o de
 * autores (livros de cada autor). Dois formatos:
 * - `CountryBookDto` (biblioteca própria): link para o item da biblioteca;
 * - `ProfileBookDto` (perfil público, sem `libraryItemId`): sem link (não é a biblioteca de quem
 *   vê) e com a resenha, que abre sob demanda ("Ler resenha").
 * `showAuthors` omite a linha de autoria quando o contexto já a informa (card do autor).
 */
import { computed, ref, useId } from 'vue'
import { RouterLink } from 'vue-router'
import BookCover from '@/components/books/BookCover.vue'
import StarRating from '@/components/ui/StarRating.vue'
import type { CountryBookDto, ProfileBookDto } from '@/types/api'
import { formatDate } from '@/utils/format'

const props = withDefaults(
  defineProps<{ book: CountryBookDto | ProfileBookDto; showAuthors?: boolean }>(),
  { showAuthors: true },
)

/** Só o livro da própria biblioteca tem `libraryItemId`; de terceiros nunca há link. */
const libraryItemId = computed(() =>
  'libraryItemId' in props.book ? props.book.libraryItemId : null,
)
const review = computed(() =>
  'review' in props.book && props.book.review?.trim() ? props.book.review : null,
)
const reviewOpen = ref(false)
const reviewId = `review-${useId()}`
</script>

<template>
  <div class="book-wrap">
    <component
      :is="libraryItemId ? RouterLink : 'div'"
      v-bind="libraryItemId ? { to: { name: 'library-item', params: { id: libraryItemId } } } : {}"
      class="book"
      :class="{ 'book--static': !libraryItemId }"
    >
      <BookCover :title="book.title" :cover-url="book.coverUrl" size="sm" />
      <span class="book__text">
        <span class="book__title">{{ book.title }}</span>
        <span v-if="showAuthors" class="muted">
          {{ book.authors.join(', ') || 'Autoria desconhecida' }}
        </span>
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
    </component>
    <template v-if="review">
      <button
        type="button"
        class="review-toggle"
        :aria-expanded="reviewOpen"
        :aria-controls="reviewId"
        data-testid="review-toggle"
        @click="reviewOpen = !reviewOpen"
      >
        {{ reviewOpen ? 'Ocultar resenha' : 'Ler resenha' }}
      </button>
      <blockquote
        v-show="reviewOpen"
        :id="reviewId"
        class="review"
        data-testid="review-text"
        :aria-label="`Resenha de ${book.title}`"
      >
        {{ review }}
      </blockquote>
    </template>
  </div>
</template>

<style scoped>
.book {
  display: flex;
  gap: var(--space-3);
  align-items: center;
  padding: var(--space-2);
  border-radius: var(--radius-md);
  color: inherit;
  text-decoration: none;
}
a.book:hover {
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
.review-toggle {
  margin: 0 var(--space-2) var(--space-1) calc(var(--space-2) + 2px);
  padding: 2px var(--space-1);
  font-size: var(--text-xs);
  color: var(--color-sky);
  text-decoration: underline;
  text-underline-offset: 3px;
  border-radius: var(--radius-sm);
}
.review-toggle:focus-visible {
  outline: 2px solid var(--color-sky);
  outline-offset: 2px;
}
.review {
  margin: var(--space-1) var(--space-2) var(--space-2);
  padding: var(--space-2) var(--space-3);
  border-left: 2px solid var(--color-brass);
  background: var(--color-surface);
  border-radius: 0 var(--radius-md) var(--radius-md) 0;
  font-size: var(--text-sm);
  color: var(--color-text-muted);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
</style>
