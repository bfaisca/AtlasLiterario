<script setup lang="ts">
import { computed } from 'vue'
import { legendTicks, UNEXPLORED_COLOR } from '@/composables/useColorScale'
import { pluralize } from '@/utils/labels'

/** Legenda com os pontos reais da escala (1 … máximo do mapa), em cores idênticas às do mapa. */
const props = defineProps<{ maxBookCount: number }>()

const ticks = computed(() => legendTicks(props.maxBookCount, 5))
const summary = computed(() =>
  props.maxBookCount > 0
    ? `Escala logarítmica de 1 a ${pluralize(props.maxBookCount, 'livro', 'livros')}`
    : 'Nenhum país explorado',
)
</script>

<template>
  <div class="legend" role="group" :aria-label="`Legenda do mapa. ${summary}.`">
    <span class="legend__item legend__item--unexplored">
      <span class="legend__swatch" :style="{ background: UNEXPLORED_COLOR }" />
      <span class="legend__label">0</span>
    </span>
    <span v-for="tick in ticks" :key="tick.count" class="legend__item" data-testid="legend-tick">
      <span class="legend__swatch" :style="{ background: tick.color }" />
      <span class="legend__label numeric">{{ tick.count }}</span>
    </span>
    <span v-if="maxBookCount > 0" class="legend__max">máx. {{ maxBookCount }}</span>
  </div>
</template>

<style scoped>
.legend {
  display: inline-flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-pill);
  background: rgb(6 11 20 / 0.8);
  border: 1px solid var(--color-hairline);
  backdrop-filter: blur(6px);
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.legend__item {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}
.legend__swatch {
  width: 14px;
  height: 14px;
  border-radius: 3px;
  border: 1px solid var(--color-border);
}
.legend__max {
  padding-left: var(--space-2);
  border-left: 1px solid var(--color-hairline);
  color: var(--color-text);
  font-weight: 600;
}
@media (max-width: 480px) {
  .legend {
    gap: var(--space-2);
    padding: var(--space-1) var(--space-2);
  }
  .legend__swatch {
    width: 10px;
    height: 10px;
  }
  .legend__max {
    display: none;
  }
}
</style>
