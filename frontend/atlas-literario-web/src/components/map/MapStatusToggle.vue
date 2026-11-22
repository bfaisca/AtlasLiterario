<script setup lang="ts">
import type { MapStatusFilter } from '@/types/api'
import { MAP_STATUS_OPTIONS } from '@/utils/mapStatus'

/** Filtro de status de leitura do mapa (visualização apenas; Passaporte segue só `Read`). */
const model = defineModel<MapStatusFilter>({ required: true })
</script>

<template>
  <div class="status-toggle" role="radiogroup" aria-label="Status de leitura exibido no mapa">
    <button
      v-for="option in MAP_STATUS_OPTIONS"
      :key="option.value"
      type="button"
      role="radio"
      class="status-toggle__option"
      :class="{ 'is-active': model === option.value }"
      :aria-checked="model === option.value"
      @click="model = option.value"
    >
      {{ option.label }}
    </button>
  </div>
</template>

<style scoped>
.status-toggle {
  display: inline-flex;
  flex-wrap: wrap;
  padding: 3px;
  gap: 2px;
  border-radius: var(--radius-lg);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
}
.status-toggle__option {
  padding: 0.3rem 0.75rem;
  border-radius: var(--radius-pill);
  font-size: var(--text-xs);
  font-weight: 600;
  color: var(--color-text-muted);
  transition:
    background-color var(--duration-base) var(--ease-out),
    color var(--duration-base) var(--ease-out);
}
.status-toggle__option:hover {
  color: var(--color-text);
}
.status-toggle__option.is-active {
  background: var(--color-surface-3);
  color: var(--color-brass-strong);
  box-shadow: inset 0 0 0 1px rgb(227 191 124 / 0.25);
}
</style>
