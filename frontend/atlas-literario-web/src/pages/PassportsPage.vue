<script setup lang="ts">
import { onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useStatisticsStore } from '@/stores/statistics'
import { getErrorMessage } from '@/api/errors'
import { CONTINENT_LABELS } from '@/utils/labels'
import BaseCard from '@/components/ui/BaseCard.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import StatTile from '@/components/stats/StatTile.vue'

/** Esqueleto da Etapa 2 — Passaportes completos na Etapa 5. */
const statistics = useStatisticsStore()
const { passports, loading, error } = storeToRefs(statistics)

onMounted(() => statistics.fetchPassports())
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <p class="eyebrow">Passaportes literários</p>
        <h1>Carimbos da sua viagem</h1>
      </div>
    </header>

    <div v-if="loading && !passports" class="center"><BaseSpinner size="lg" /></div>
    <EmptyState
      v-else-if="error"
      icon="alert"
      title="Não foi possível carregar os passaportes"
      :description="getErrorMessage(error)"
    />
    <template v-else-if="passports">
      <BaseCard padding="none" class="stats">
        <StatTile label="Países distintos" :value="passports.distinctCountries" accent />
        <StatTile
          label="Via autores"
          :value="passports.authors.exploredCountries"
          :caption="`${passports.authors.unknownBookCount} sem nacionalidade conhecida`"
        />
        <StatTile
          label="Via obras"
          :value="passports.origins.exploredCountries"
          :caption="`${passports.origins.unknownBookCount} sem origem conhecida`"
        />
        <StatTile
          label="Do mundo"
          :value="`${passports.progress.percentage.toLocaleString('pt-BR')}%`"
          :caption="`${passports.progress.exploredCountries} de ${passports.progress.totalCountries}`"
        />
      </BaseCard>

      <BaseCard>
        <p class="eyebrow">Continentes</p>
        <ul role="list" class="continents">
          <li v-for="c in passports.continents" :key="c.continent">
            <span>{{ CONTINENT_LABELS[c.continent] }}</span>
            <span class="bar"
              ><span
                class="bar__fill"
                :style="{
                  width: `${c.totalCountries ? (c.exploredCountries / c.totalCountries) * 100 : 0}%`,
                }"
            /></span>
            <span class="numeric muted">{{ c.exploredCountries }}/{{ c.totalCountries }}</span>
          </li>
        </ul>
      </BaseCard>
    </template>
  </div>
</template>

<style scoped>
.center {
  display: grid;
  place-items: center;
  padding: var(--space-8);
}
.stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  padding: var(--space-3) 0;
}
.continents {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  margin-top: var(--space-4);
}
.continents li {
  display: grid;
  grid-template-columns: 10rem 1fr 3rem;
  align-items: center;
  gap: var(--space-3);
  font-size: var(--text-sm);
}
.continents li > :last-child {
  text-align: right;
}
.bar {
  height: 6px;
  border-radius: var(--radius-pill);
  background: var(--map-unexplored);
  overflow: hidden;
}
.bar__fill {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, var(--map-stop-35), var(--map-stop-100));
  transition: width var(--duration-slow) var(--ease-out);
}
</style>
