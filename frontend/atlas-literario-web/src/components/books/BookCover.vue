<script setup lang="ts">
import { computed, ref, watch } from 'vue'

/**
 * Capa do livro com fallback tipográfico elegante (lombada + título serifado) quando
 * não há URL ou a imagem falha. A cor de fundo é derivada do título (estável).
 */
const props = withDefaults(
  defineProps<{
    title: string
    author?: string
    coverUrl?: string | null
    size?: 'sm' | 'md' | 'lg'
  }>(),
  { coverUrl: null, size: 'md' },
)

const failed = ref(false)
watch(
  () => props.coverUrl,
  () => (failed.value = false),
)

const showImage = computed(() => !!props.coverUrl && !failed.value)

const PALETTES = [
  ['#1f3a5f', '#e3bf7c'],
  ['#3b2a4f', '#d9c3f2'],
  ['#1d4a45', '#bfe7d6'],
  ['#4a2a2a', '#f2c2a6'],
  ['#2b3550', '#bfe0ff'],
  ['#3d3a24', '#ecdca2'],
] as const

const palette = computed(() => {
  let hash = 0
  for (const char of props.title) hash = (hash * 31 + char.charCodeAt(0)) >>> 0
  return PALETTES[hash % PALETTES.length]!
})
</script>

<template>
  <div class="cover" :class="`cover--${size}`">
    <img
      v-if="showImage"
      :src="coverUrl!"
      :alt="`Capa de ${title}`"
      class="cover__image"
      loading="lazy"
      referrerpolicy="no-referrer"
      @error="failed = true"
    />
    <div
      v-else
      class="cover__fallback"
      :style="{ '--cover-bg': palette[0], '--cover-ink': palette[1] }"
      role="img"
      :aria-label="`Capa indisponível: ${title}`"
    >
      <span class="cover__rule" />
      <span class="cover__title">{{ title }}</span>
      <span v-if="author" class="cover__author">{{ author }}</span>
      <span class="cover__rule cover__rule--bottom" />
    </div>
  </div>
</template>

<style scoped>
.cover {
  position: relative;
  aspect-ratio: 2 / 3;
  border-radius: 4px var(--radius-sm) var(--radius-sm) 4px;
  overflow: hidden;
  background: var(--color-surface-2);
  box-shadow:
    inset 3px 0 0 rgb(0 0 0 / 0.25),
    var(--shadow-md);
  flex-shrink: 0;
}
.cover--sm {
  width: 56px;
}
.cover--md {
  width: 100%;
}
.cover--lg {
  width: min(220px, 100%);
}
.cover__image {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.cover__fallback {
  height: 100%;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 0.6em;
  padding: 12% 10% 12% 14%;
  background:
    linear-gradient(90deg, rgb(0 0 0 / 0.28) 0 6%, rgb(255 255 255 / 0.05) 6% 7%, transparent 7%),
    radial-gradient(120% 80% at 80% 0%, rgb(255 255 255 / 0.08), transparent 60%), var(--cover-bg);
  color: var(--cover-ink);
  text-align: left;
}
.cover__title {
  font-family: var(--font-display);
  font-weight: 500;
  line-height: 1.1;
  font-size: clamp(0.7rem, 0.55rem + 0.9vw, 1.05rem);
  display: -webkit-box;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.cover--sm .cover__title {
  font-size: 0.5rem;
}
.cover--lg .cover__title {
  font-size: 1.5rem;
}
.cover__author {
  font-size: 0.65em;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  opacity: 0.75;
}
.cover--sm .cover__author {
  display: none;
}
.cover__rule {
  width: 30%;
  height: 1px;
  background: currentColor;
  opacity: 0.5;
}
</style>
