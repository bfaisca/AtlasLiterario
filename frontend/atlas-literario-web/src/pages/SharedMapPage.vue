<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api } from '@/api'
import type { PublicMapDto } from '@/types/api'
import { MAP_TYPE_LABELS, pluralize } from '@/utils/labels'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import MapLegend from '@/components/map/MapLegend.vue'
import WorldMap from '@/components/map/WorldMap.vue'

/** Página pública (sem autenticação): somente agregados do mapa compartilhado. */
const props = defineProps<{ token: string }>()
const map = ref<PublicMapDto | null>(null)
const loading = ref(true)
const notFound = ref(false)

onMounted(async () => {
  try {
    map.value = await api.maps.getPublic(props.token)
  } catch {
    notFound.value = true
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="shared">
    <header class="shared__header">
      <p class="eyebrow shared__brand">Atlas Literário</p>
      <template v-if="map">
        <h1>O atlas de {{ map.displayName }}</h1>
        <p class="muted">
          {{ MAP_TYPE_LABELS[map.mapType] }} ·
          {{ pluralize(map.exploredCountries, 'país', 'países') }} ·
          {{ pluralize(map.totalReadBooks, 'livro lido', 'livros lidos') }}
        </p>
      </template>
    </header>

    <div v-if="loading" class="shared__state"><BaseSpinner size="lg" /></div>
    <div v-else-if="notFound || !map" class="shared__state">
      <EmptyState
        icon="globe"
        title="Mapa indisponível"
        description="Este link não existe, foi revogado ou o perfil é privado."
      >
        <BaseButton :to="{ name: 'login' }" variant="secondary"
          >Conhecer o Atlas Literário</BaseButton
        >
      </EmptyState>
    </div>
    <div v-else class="shared__stage">
      <WorldMap
        :data="map.countries"
        :max-book-count="map.maxBookCount"
        :label="`${MAP_TYPE_LABELS[map.mapType]} de ${map.displayName}`"
      />
      <div class="shared__legend"><MapLegend :max-book-count="map.maxBookCount" /></div>
    </div>
  </div>
</template>

<style scoped>
.shared {
  min-height: 100dvh;
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
  padding: var(--space-6) var(--space-5);
  background: var(--color-ocean);
}
.shared__header {
  text-align: center;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.shared__brand {
  color: var(--color-brass);
  letter-spacing: 0.3em;
}
.shared__state {
  flex: 1;
  display: grid;
  place-items: center;
}
.shared__stage {
  position: relative;
  flex: 1;
  min-height: 360px;
  border-radius: var(--radius-xl);
  border: 1px solid var(--color-hairline);
  overflow: hidden;
}
.shared__legend {
  position: absolute;
  left: var(--space-4);
  bottom: var(--space-4);
}
</style>
