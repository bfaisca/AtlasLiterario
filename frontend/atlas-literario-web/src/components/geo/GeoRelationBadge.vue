<script setup lang="ts">
import { computed } from 'vue'
import type { GeoRelationDto } from '@/types/api'
import { GEO_SOURCE_LABELS } from '@/utils/labels'
import BaseBadge from '@/components/ui/BaseBadge.vue'

/** País com procedência visível: fonte, confiança e verificação. */
const props = defineProps<{ relation: GeoRelationDto }>()

const tone = computed(() =>
  props.relation.isVerified ? 'success' : props.relation.confidence < 0.8 ? 'warning' : 'sky',
)
const provenance = computed(() => {
  const r = props.relation
  const source = GEO_SOURCE_LABELS[r.source]
  return r.isVerified ? `${source} · verificado` : `${source} · ${Math.round(r.confidence * 100)}%`
})
</script>

<template>
  <BaseBadge :tone="tone" :title="`Fonte: ${provenance}`">
    {{ relation.countryName }}
    <span class="provenance">{{ relation.isVerified ? '✓' : provenance }}</span>
  </BaseBadge>
</template>

<style scoped>
.provenance {
  font-weight: 500;
  opacity: 0.75;
}
</style>
