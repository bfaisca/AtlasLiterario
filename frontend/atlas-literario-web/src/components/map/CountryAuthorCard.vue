<script setup lang="ts">
/**
 * Card de autor do painel do mapa de autores. O cabeçalho é um botão (aria-expanded/aria-controls)
 * que expande/recolhe os livros lidos deste autor. O estado de abertura é local ao card.
 */
import { computed, ref, useId } from 'vue'
import CountryBookItem from '@/components/map/CountryBookItem.vue'
import AppIcon from '@/components/ui/AppIcon.vue'
import type { CountryAuthorDto, ProfileCountryAuthorDto } from '@/types/api'
import { pluralize } from '@/utils/labels'

const props = defineProps<{ author: CountryAuthorDto | ProfileCountryAuthorDto }>()

const open = ref(false)
const regionId = `author-books-${useId()}`

const initials = computed(() => {
  const words = props.author.name.split(/\s+/).filter(Boolean)
  const first = words[0]?.[0] ?? '?'
  const last = words.length > 1 ? (words[words.length - 1]?.[0] ?? '') : ''
  return (first + last).toLocaleUpperCase('pt-BR')
})
const bookCountLabel = computed(() => pluralize(props.author.books.length, 'livro', 'livros'))
</script>

<template>
  <div class="author-card" :class="{ 'author-card--open': open }" data-testid="author-card">
    <button
      type="button"
      class="author-card__header"
      :aria-expanded="open"
      :aria-controls="regionId"
      data-testid="author-card-toggle"
      @click="open = !open"
    >
      <span class="author-card__avatar" aria-hidden="true">{{ initials }}</span>
      <span class="author-card__text">
        <span class="author-card__name">{{ author.name }}</span>
        <span class="author-card__country">{{ author.countryName }} · {{ bookCountLabel }}</span>
      </span>
      <AppIcon name="chevronDown" :size="18" class="author-card__chevron" />
    </button>
    <div
      v-show="open"
      :id="regionId"
      class="author-card__books"
      role="region"
      :aria-label="`Livros de ${author.name}`"
    >
      <ul role="list" class="author-card__list">
        <li v-for="book in author.books" :key="book.bookId">
          <CountryBookItem :book="book" :show-authors="false" />
        </li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
.author-card {
  background: var(--color-surface);
  border: 1px solid var(--color-hairline);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease;
}
.author-card--open {
  border-color: var(--color-border);
}
.author-card__header {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  width: 100%;
  padding: var(--space-2) var(--space-3);
  text-align: left;
  color: inherit;
  border-radius: var(--radius-md);
  cursor: pointer;
}
.author-card__header:hover {
  background: var(--color-surface-2);
}
.author-card__header:focus-visible {
  outline: 2px solid var(--color-sky);
  outline-offset: -2px;
}
.author-card__avatar {
  display: grid;
  place-items: center;
  flex: none;
  width: 36px;
  height: 36px;
  border-radius: var(--radius-pill);
  background: var(--color-brass-soft);
  color: var(--color-brass);
  font-size: var(--text-xs);
  font-weight: 600;
  letter-spacing: 0.04em;
}
.author-card__text {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
}
.author-card__name {
  font-family: var(--font-display);
  font-size: var(--text-base);
  color: var(--color-text);
  overflow-wrap: anywhere;
}
.author-card__country {
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.author-card__chevron {
  flex: none;
  color: var(--color-text-faint);
  transition: transform 0.2s ease;
}
.author-card--open .author-card__chevron {
  transform: rotate(180deg);
}
.author-card__books {
  padding: 0 var(--space-2) var(--space-2);
  border-top: 1px solid var(--color-hairline);
}
.author-card__list {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  padding-top: var(--space-2);
}
@media (prefers-reduced-motion: reduce) {
  .author-card,
  .author-card__chevron {
    transition: none;
  }
}
</style>
