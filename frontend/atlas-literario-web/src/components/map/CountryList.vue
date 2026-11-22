<script setup lang="ts">
/** Lista alternativa ao mapa (teclado/leitor de tela): países explorados, mais livros primeiro. */
import { computed } from 'vue'
import type { CountryMapDatum } from '@/types/api'
import { CONTINENT_LABELS, pluralize } from '@/utils/labels'
import { sortCountriesForList } from '@/utils/mapSelection'

const props = defineProps<{
  countries: readonly CountryMapDatum[]
  selectedIso: string | null
}>()
const emit = defineEmits<{ select: [iso: string] }>()

const sorted = computed(() => sortCountriesForList(props.countries))
</script>

<template>
  <section class="list" aria-labelledby="paises-explorados" data-keep-panel>
    <h2 id="paises-explorados" class="list__title">Países explorados</h2>
    <p v-if="!sorted.length" class="muted">Nenhum país explorado neste mapa ainda.</p>
    <ul v-else role="list" class="list__items">
      <li v-for="country in sorted" :key="country.iso">
        <button
          type="button"
          class="item"
          :class="{ 'is-selected': country.iso === selectedIso }"
          :aria-pressed="country.iso === selectedIso"
          @click="emit('select', country.iso)"
        >
          <span class="item__name">{{ country.name }}</span>
          <span class="item__meta">
            {{ pluralize(country.bookCount, 'livro', 'livros') }} ·
            {{ pluralize(country.authorCount, 'autor', 'autores') }}
          </span>
          <span class="item__continent">{{ CONTINENT_LABELS[country.continent] }}</span>
        </button>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.list__title {
  font-size: var(--text-lg);
  margin-bottom: var(--space-3);
}
.list__items {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(230px, 1fr));
  gap: var(--space-2);
  max-height: 340px;
  overflow-y: auto;
  padding: 2px;
}
.item {
  display: grid;
  gap: 2px;
  width: 100%;
  padding: var(--space-2) var(--space-3);
  text-align: left;
  border-radius: var(--radius-md);
  background: var(--color-surface);
  border: 1px solid var(--color-hairline);
  transition:
    background-color var(--duration-base) var(--ease-out),
    border-color var(--duration-base) var(--ease-out);
}
.item:hover {
  background: var(--color-surface-2);
}
.item.is-selected {
  border-color: var(--color-sky);
  background: var(--color-surface-2);
}
.item__name {
  font-family: var(--font-display);
  font-size: var(--text-base);
}
.item__meta,
.item__continent {
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.item__continent {
  color: var(--color-text-faint);
}
</style>
