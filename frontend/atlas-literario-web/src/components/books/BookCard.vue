<script setup lang="ts">
import { computed } from 'vue'
import type { LibraryItemDto } from '@/types/api'
import { formatDate } from '@/utils/format'
import { lacksAuthorCountry, lacksOriginCountry } from '@/utils/libraryGeo'
import StarRating from '@/components/ui/StarRating.vue'
import BookCover from './BookCover.vue'
import ReadingStatusBadge from './ReadingStatusBadge.vue'

const props = defineProps<{ item: LibraryItemDto }>()

const authorNames = computed(() => props.item.book.authors.map((a) => a.name).join(', '))

/** Países da obra para exibição; vazio = origem desconhecida (nunca "nenhuma"). */
const originLabel = computed(() => {
  const origins = props.item.book.originCountries
  return origins.length ? origins.map((o) => o.countryName).join(' · ') : null
})

/** Lacunas geográficas: dado ainda não informado (desconhecido), nunca "inexistente". */
const missingOrigin = computed(() => lacksOriginCountry(props.item))
const missingAuthorCountry = computed(() => lacksAuthorCountry(props.item))
</script>

<template>
  <RouterLink :to="{ name: 'library-item', params: { id: item.id } }" class="book-card">
    <BookCover
      :title="item.book.title"
      :author="item.book.authors[0]?.name"
      :cover-url="item.book.coverUrl"
    />
    <div class="book-card__body">
      <ReadingStatusBadge :status="item.status" />
      <h3 class="book-card__title">{{ item.book.title }}</h3>
      <p class="book-card__authors">{{ authorNames || 'Autoria desconhecida' }}</p>
      <p v-if="originLabel" class="book-card__meta">
        <span class="book-card__origin">{{ originLabel }}</span>
      </p>
      <ul
        v-if="missingOrigin || missingAuthorCountry"
        role="list"
        class="book-card__gaps"
        aria-label="Dados geográficos ainda não informados"
      >
        <li
          v-if="missingOrigin"
          class="book-card__gap"
          title="País de origem da obra ainda não informado"
        >
          Obra sem país
        </li>
        <li
          v-if="missingAuthorCountry"
          class="book-card__gap"
          title="Nacionalidade do autor ainda não informada"
        >
          Autor sem país
        </li>
      </ul>
      <div v-if="item.status === 'Read'" class="book-card__footer">
        <StarRating v-if="item.rating" :model-value="item.rating" readonly size="sm" />
        <span v-if="item.finishedDate" class="book-card__date">{{
          formatDate(item.finishedDate)
        }}</span>
      </div>
    </div>
  </RouterLink>
</template>

<style scoped>
.book-card {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-3);
  border-radius: var(--radius-lg);
  color: inherit;
  text-decoration: none;
  transition: background-color var(--duration-base) var(--ease-out);
}
.book-card:hover {
  color: inherit;
  background: var(--color-surface);
}
.book-card:hover :deep(.cover) {
  transform: translateY(-3px) rotate(-0.4deg);
}
.book-card :deep(.cover) {
  transition: transform var(--duration-slow) var(--ease-out);
}
.book-card__body {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--space-1);
}
.book-card__title {
  margin-top: var(--space-1);
  font-size: var(--text-lg);
  line-height: 1.25;
}
.book-card__authors {
  font-size: var(--text-sm);
  color: var(--color-text-muted);
}
.book-card__meta {
  font-size: var(--text-xs);
  letter-spacing: 0.04em;
  color: var(--color-text-faint);
}
.book-card__gaps {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1);
}
.book-card__gap {
  padding: 0 0.5rem;
  border-radius: var(--radius-pill);
  border: 1px dashed var(--color-border);
  font-size: var(--text-xs);
  line-height: 1.5;
  color: var(--color-text-faint);
}
.book-card__footer {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  margin-top: var(--space-1);
}
.book-card__date {
  font-size: var(--text-xs);
  color: var(--color-text-faint);
}
</style>
