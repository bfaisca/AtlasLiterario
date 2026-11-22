<script setup lang="ts">
import { computed, ref } from 'vue'

/**
 * Avaliação 1–5. `v-model` numérico ou null (sem avaliação).
 * Clicar novamente na nota atual limpa a avaliação. Teclado: setas ajustam a nota.
 */
const props = withDefaults(
  defineProps<{ readonly?: boolean; size?: 'sm' | 'md' | 'lg'; label?: string }>(),
  { readonly: false, size: 'md', label: 'Avaliação' },
)

const model = defineModel<number | null>({ default: null })
const hovered = ref<number | null>(null)
const display = computed(() => hovered.value ?? model.value ?? 0)
const stars = [1, 2, 3, 4, 5] as const

function select(value: number) {
  if (props.readonly) return
  model.value = model.value === value ? null : value
}

function onKeydown(event: KeyboardEvent) {
  if (props.readonly) return
  const current = model.value ?? 0
  if (event.key === 'ArrowRight' || event.key === 'ArrowUp') {
    model.value = Math.min(5, current + 1)
    event.preventDefault()
  } else if (event.key === 'ArrowLeft' || event.key === 'ArrowDown') {
    model.value = current <= 1 ? null : current - 1
    event.preventDefault()
  }
}

const ariaText = computed(() => (model.value ? `${model.value} de 5 estrelas` : 'Sem avaliação'))
</script>

<template>
  <span
    v-if="readonly"
    class="rating rating--readonly"
    :class="`rating--${size}`"
    role="img"
    :aria-label="`${label}: ${ariaText}`"
  >
    <span
      v-for="star in stars"
      :key="star"
      class="rating__star"
      :class="{ 'is-on': star <= display }"
      >★</span
    >
  </span>
  <span
    v-else
    class="rating"
    :class="`rating--${size}`"
    role="radiogroup"
    :aria-label="label"
    @keydown="onKeydown"
    @mouseleave="hovered = null"
  >
    <button
      v-for="star in stars"
      :key="star"
      type="button"
      class="rating__star"
      :class="{ 'is-on': star <= display }"
      role="radio"
      :aria-checked="model === star"
      :aria-label="`${star} ${star === 1 ? 'estrela' : 'estrelas'}`"
      :tabindex="(model ?? 1) === star ? 0 : -1"
      @click="select(star)"
      @mouseenter="hovered = star"
    >
      ★
    </button>
  </span>
</template>

<style scoped>
.rating {
  display: inline-flex;
  gap: 2px;
  line-height: 1;
}
.rating--sm {
  font-size: 0.875rem;
}
.rating--md {
  font-size: 1.25rem;
}
.rating--lg {
  font-size: 1.75rem;
}
.rating__star {
  color: var(--color-surface-3);
  transition:
    color var(--duration-fast) var(--ease-out),
    transform var(--duration-fast) var(--ease-out);
}
.rating__star.is-on {
  color: var(--color-brass);
}
button.rating__star {
  padding: 2px;
  border-radius: var(--radius-sm);
}
button.rating__star:hover {
  transform: scale(1.12);
}
</style>
