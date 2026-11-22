<script setup lang="ts">
/**
 * Evolução das leituras (Passaporte): barras = livros concluídos no mês; linha = países distintos
 * acumulados. Gráfico decorativo para quem enxerga (`role="img"` com resumo); a tabela oculta
 * traz os mesmos números para leitores de tela.
 */
import { computed } from 'vue'
import type { TimelinePointDto } from '@/types/api'
import { formatMonth } from '@/utils/format'
import { pluralize } from '@/utils/labels'

const props = defineProps<{ points: readonly TimelinePointDto[] }>()

const W = 640
const H = 190
const PAD = { left: 28, right: 28, top: 12, bottom: 24 }
const innerW = W - PAD.left - PAD.right
const innerH = H - PAD.top - PAD.bottom

const maxBooks = computed(() => Math.max(1, ...props.points.map((p) => p.booksFinished)))
const maxCountries = computed(() =>
  Math.max(1, ...props.points.map((p) => p.cumulativeDistinctCountries)),
)
const step = computed(() => innerW / Math.max(props.points.length, 1))

const bars = computed(() =>
  props.points.map((p, i) => {
    const height = (p.booksFinished / maxBooks.value) * innerH
    return {
      key: p.month,
      x: PAD.left + i * step.value + step.value * 0.18,
      y: PAD.top + innerH - height,
      width: step.value * 0.64,
      height,
    }
  }),
)
const line = computed(() =>
  props.points
    .map((p, i) => {
      const x = PAD.left + i * step.value + step.value / 2
      const y = PAD.top + innerH - (p.cumulativeDistinctCountries / maxCountries.value) * innerH
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' '),
)

const first = computed(() => props.points[0])
const last = computed(() => props.points[props.points.length - 1])
const summary = computed(() =>
  first.value && last.value
    ? `Evolução mensal de ${formatMonth(first.value.month)} a ${formatMonth(last.value.month)}: ` +
      `${pluralize(last.value.cumulativeBooks, 'livro lido', 'livros lidos')} e ` +
      `${pluralize(last.value.cumulativeDistinctCountries, 'país explorado', 'países explorados')} no total.`
    : '',
)
</script>

<template>
  <figure class="timeline">
    <svg :viewBox="`0 0 ${W} ${H}`" role="img" :aria-label="summary" class="timeline__svg">
      <line
        :x1="PAD.left"
        :x2="W - PAD.right"
        :y1="PAD.top + innerH"
        :y2="PAD.top + innerH"
        class="timeline__axis"
      />
      <rect
        v-for="bar in bars"
        :key="bar.key"
        :x="bar.x"
        :y="bar.y"
        :width="bar.width"
        :height="bar.height"
        rx="2"
        class="timeline__bar"
      />
      <polyline :points="line" class="timeline__line" fill="none" />
      <text :x="PAD.left" :y="H - 6" class="timeline__label">
        {{ first ? formatMonth(first.month) : '' }}
      </text>
      <text :x="W - PAD.right" :y="H - 6" text-anchor="end" class="timeline__label">
        {{ last ? formatMonth(last.month) : '' }}
      </text>
      <text :x="PAD.left - 6" :y="PAD.top + 8" text-anchor="end" class="timeline__label">
        {{ maxBooks }}
      </text>
      <text :x="W - PAD.right + 6" :y="PAD.top + 8" class="timeline__label timeline__label--line">
        {{ maxCountries }}
      </text>
    </svg>
    <figcaption class="timeline__legend">
      <span><i class="swatch swatch--bar" aria-hidden="true" /> Livros concluídos no mês</span>
      <span><i class="swatch swatch--line" aria-hidden="true" /> Países distintos (acumulado)</span>
    </figcaption>
    <table class="sr-only">
      <caption>
        Leituras por mês
      </caption>
      <thead>
        <tr>
          <th scope="col">Mês</th>
          <th scope="col">Livros concluídos</th>
          <th scope="col">Livros acumulados</th>
          <th scope="col">Países distintos acumulados</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="p in points" :key="p.month">
          <th scope="row">{{ formatMonth(p.month) }}</th>
          <td>{{ p.booksFinished }}</td>
          <td>{{ p.cumulativeBooks }}</td>
          <td>{{ p.cumulativeDistinctCountries }}</td>
        </tr>
      </tbody>
    </table>
  </figure>
</template>

<style scoped>
.timeline__svg {
  width: 100%;
  height: auto;
  display: block;
}
.timeline__axis {
  stroke: var(--color-border);
  stroke-width: 1;
}
.timeline__bar {
  fill: var(--map-stop-35);
}
.timeline__line {
  stroke: var(--color-brass);
  stroke-width: 2;
  stroke-linejoin: round;
  stroke-linecap: round;
}
.timeline__label {
  font-size: 10px;
  fill: var(--color-text-faint);
}
.timeline__label--line {
  fill: var(--color-brass);
}
.timeline__legend {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-4);
  margin-top: var(--space-2);
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.swatch {
  display: inline-block;
  vertical-align: middle;
  margin-right: var(--space-1);
}
.swatch--bar {
  width: 10px;
  height: 10px;
  border-radius: 2px;
  background: var(--map-stop-35);
}
.swatch--line {
  width: 14px;
  height: 2px;
  background: var(--color-brass);
}
</style>
