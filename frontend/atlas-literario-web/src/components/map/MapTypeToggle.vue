<script setup lang="ts">
import type { MapType } from '@/types/api'
import { MAP_TYPE_SHORT_LABELS } from '@/utils/labels'

const model = defineModel<MapType>({ required: true })
const options: MapType[] = ['Authors', 'Origins']
const HINTS: Record<MapType, string> = {
  Authors: 'Países das nacionalidades dos autores',
  Origins: 'Países de origem literária das obras',
}
</script>

<template>
  <div class="toggle" role="radiogroup" aria-label="Tipo de mapa">
    <button
      v-for="option in options"
      :key="option"
      type="button"
      role="radio"
      class="toggle__option"
      :class="{ 'is-active': model === option }"
      :aria-checked="model === option"
      :title="HINTS[option]"
      @click="model = option"
    >
      {{ MAP_TYPE_SHORT_LABELS[option] }}
    </button>
  </div>
</template>

<style scoped>
.toggle {
  display: inline-flex;
  padding: 3px;
  gap: 2px;
  border-radius: var(--radius-pill);
  background: var(--color-surface);
  border: 1px solid var(--color-border);
}
.toggle__option {
  padding: 0.4rem 1rem;
  border-radius: var(--radius-pill);
  font-size: var(--text-sm);
  font-weight: 600;
  color: var(--color-text-muted);
  transition:
    background-color var(--duration-base) var(--ease-out),
    color var(--duration-base) var(--ease-out);
}
.toggle__option:hover {
  color: var(--color-text);
}
.toggle__option.is-active {
  background: var(--color-surface-3);
  color: var(--color-brass-strong);
  box-shadow: inset 0 0 0 1px rgb(227 191 124 / 0.25);
}
</style>
