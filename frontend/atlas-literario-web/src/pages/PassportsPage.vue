<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useStatisticsStore } from '@/stores/statistics'
import { getErrorMessage } from '@/api/errors'
import type { CountryMapDatum } from '@/types/api'
import { CONTINENT_LABELS, MAP_TYPE_LABELS, pluralize } from '@/utils/labels'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseCard from '@/components/ui/BaseCard.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import StatTile from '@/components/stats/StatTile.vue'
import TimelineChart from '@/components/stats/TimelineChart.vue'

/** Passaportes Literários (api-contracts §8): só livros lidos. Estados: carregando, erro, vazio. */
const statistics = useStatisticsStore()
const { passports, passportsLoading: loading, passportsError: error } = storeToRefs(statistics)

const TOP_COUNT = 5

const isEmpty = computed(
  () =>
    !!passports.value &&
    passports.value.distinctCountries === 0 &&
    passports.value.timeline.length === 0 &&
    passports.value.authors.unknownBookCount === 0 &&
    passports.value.origins.unknownBookCount === 0,
)

const rankings = computed(() => {
  const data = passports.value
  if (!data) return []
  return [
    { type: 'Authors' as const, stats: data.authors },
    { type: 'Origins' as const, stats: data.origins },
  ].map(({ type, stats }) => {
    const top: CountryMapDatum[] = stats.booksByCountry.slice(0, TOP_COUNT)
    const max = Math.max(1, ...top.map((c) => c.bookCount))
    return { type, stats, top: top.map((c) => ({ ...c, ratio: c.bookCount / max })) }
  })
})

function percentage(explored: number, total: number): number {
  return total ? Math.round((explored / total) * 100) : 0
}

onMounted(() => statistics.fetchPassports())
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <p class="eyebrow">Passaportes literários</p>
        <h1>Carimbos da sua viagem</h1>
        <p class="muted">Contam apenas os livros que você marcou como lidos.</p>
      </div>
    </header>

    <div v-if="loading && !passports" class="center"><BaseSpinner size="lg" /></div>
    <div v-else-if="error" role="alert">
      <EmptyState
        icon="alert"
        title="Não foi possível carregar os passaportes"
        :description="getErrorMessage(error)"
      >
        <BaseButton variant="secondary" @click="statistics.fetchPassports()">
          Tentar novamente
        </BaseButton>
      </EmptyState>
    </div>
    <EmptyState
      v-else-if="isEmpty"
      icon="passport"
      title="Seu passaporte ainda está em branco"
      description="Marque livros como lidos para ganhar os primeiros carimbos."
    >
      <BaseButton :to="{ name: 'library' }">Ir para a biblioteca</BaseButton>
    </EmptyState>

    <template v-else-if="passports">
      <BaseCard padding="none" class="stats" role="group" aria-label="Resumo do passaporte">
        <StatTile label="Países distintos" :value="passports.distinctCountries" accent />
        <StatTile
          :label="MAP_TYPE_LABELS.Authors"
          :value="passports.authors.exploredCountries"
          :caption="`${pluralize(passports.authors.unknownBookCount, 'livro', 'livros')} sem nacionalidade conhecida`"
        />
        <StatTile
          :label="MAP_TYPE_LABELS.Origins"
          :value="passports.origins.exploredCountries"
          :caption="`${pluralize(passports.origins.unknownBookCount, 'livro', 'livros')} sem origem conhecida`"
        />
        <StatTile
          label="Do mundo"
          :value="`${passports.progress.percentage.toLocaleString('pt-BR')}%`"
          :caption="`${passports.progress.exploredCountries} de ${passports.progress.totalCountries} países`"
        />
      </BaseCard>

      <BaseCard aria-labelledby="passaporte-continentes">
        <h2 id="passaporte-continentes" class="eyebrow">Continentes</h2>
        <ul role="list" class="continents">
          <li v-for="c in passports.continents" :key="c.continent">
            <span :id="`continente-${c.continent}`">{{ CONTINENT_LABELS[c.continent] }}</span>
            <span
              class="bar"
              role="progressbar"
              :aria-labelledby="`continente-${c.continent}`"
              aria-valuemin="0"
              :aria-valuemax="c.totalCountries"
              :aria-valuenow="c.exploredCountries"
              :aria-valuetext="`${c.exploredCountries} de ${c.totalCountries} países`"
            >
              <span
                class="bar__fill"
                :style="{ width: `${percentage(c.exploredCountries, c.totalCountries)}%` }"
              />
            </span>
            <span class="numeric muted">{{ c.exploredCountries }}/{{ c.totalCountries }}</span>
          </li>
        </ul>
      </BaseCard>

      <div class="rankings">
        <BaseCard
          v-for="ranking in rankings"
          :key="ranking.type"
          :aria-labelledby="`ranking-${ranking.type}`"
        >
          <h2 :id="`ranking-${ranking.type}`" class="eyebrow">
            Países com mais livros · {{ MAP_TYPE_LABELS[ranking.type] }}
          </h2>
          <ol v-if="ranking.top.length" role="list" class="top">
            <li v-for="country in ranking.top" :key="country.iso">
              <span>{{ country.name }}</span>
              <span class="bar bar--thin" aria-hidden="true">
                <span class="bar__fill" :style="{ width: `${country.ratio * 100}%` }" />
              </span>
              <span class="numeric">{{ country.bookCount }}</span>
            </li>
          </ol>
          <p v-else class="muted">Nenhum país identificado neste mapa ainda.</p>
        </BaseCard>
      </div>

      <BaseCard aria-labelledby="passaporte-evolucao">
        <h2 id="passaporte-evolucao" class="eyebrow">Evolução das leituras</h2>
        <TimelineChart v-if="passports.timeline.length" :points="passports.timeline" />
        <p v-else class="muted">A evolução aparece quando você concluir a primeira leitura.</p>
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
.continents,
.top {
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
@media (max-width: 480px) {
  .continents li {
    grid-template-columns: 7rem 1fr 3rem;
  }
}
.continents li > :last-child {
  text-align: right;
}
.rankings {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: var(--space-5);
}
.top li {
  display: grid;
  grid-template-columns: minmax(6rem, 1fr) 1fr 2rem;
  align-items: center;
  gap: var(--space-3);
  font-size: var(--text-sm);
}
.top li > :last-child {
  text-align: right;
  font-family: var(--font-display);
  font-size: var(--text-lg);
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
@media (prefers-reduced-motion: reduce) {
  .bar__fill {
    transition: none;
  }
}
</style>
