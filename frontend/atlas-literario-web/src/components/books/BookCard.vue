<script setup lang="ts">
import { computed } from 'vue'
import type { LibraryItemDto } from '@/types/api'
import { formatDate } from '@/utils/format'
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
      <p class="book-card__meta">
        <span v-if="originLabel" class="book-card__origin">{{ originLabel }}</span>
        <span v-else class="book-card__origin book-card__origin--unknown">Origem desconhecida</span>
      </p>
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
.book-card__origin--unknown {
  font-style: italic;
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
