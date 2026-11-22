<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { useStatisticsStore } from '@/stores/statistics'
import { getErrorMessage } from '@/api/errors'
import { formatDate, formatNumber } from '@/utils/format'
import { MAP_TYPE_LABELS, pluralize } from '@/utils/labels'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseCard from '@/components/ui/BaseCard.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import BookCover from '@/components/books/BookCover.vue'
import StatTile from '@/components/stats/StatTile.vue'

/** Dashboard (api-contracts §8): só livros lidos nos números; estados carregando/erro/vazio. */
const auth = useAuthStore()
const statistics = useStatisticsStore()
const { dashboard, dashboardLoading: loading, dashboardError: error } = storeToRefs(statistics)

const firstName = computed(() => auth.user?.name.split(' ')[0] ?? '')

/** "Até 5, por mapa": um ranking para cada mapa, na ordem do contrato. */
const topByMap = computed(() =>
  (['Authors', 'Origins'] as const).map((type) => ({
    type,
    countries: dashboard.value?.topCountries.filter((c) => c.mapType === type) ?? [],
  })),
)

onMounted(() => statistics.fetchDashboard())
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <p class="eyebrow">Seu atlas</p>
        <h1>Olá, {{ firstName }}.</h1>
      </div>
      <div class="cluster">
        <BaseButton :to="{ name: 'search' }" variant="secondary">Adicionar livro</BaseButton>
        <BaseButton :to="{ name: 'map' }">Abrir o mapa</BaseButton>
      </div>
    </header>

    <div v-if="loading && !dashboard" class="center"><BaseSpinner size="lg" /></div>
    <div v-else-if="error" role="alert">
      <EmptyState
        icon="alert"
        title="Não foi possível carregar o painel"
        :description="getErrorMessage(error)"
      >
        <BaseButton variant="secondary" @click="statistics.fetchDashboard()"
          >Tentar novamente</BaseButton
        >
      </EmptyState>
    </div>

    <template v-else-if="dashboard">
      <BaseCard padding="none" class="stats" role="group" aria-label="Resumo da sua leitura">
        <StatTile
          label="Livros lidos"
          :value="formatNumber(dashboard.totalReadBooks)"
          :caption="`${pluralize(dashboard.totalAuthors, 'autor', 'autores')}`"
          accent
        />
        <StatTile
          label="Países · autores"
          :value="dashboard.countriesByAuthors"
          caption="nacionalidades"
        />
        <StatTile label="Países · obras" :value="dashboard.countriesByOrigins" caption="origens" />
        <StatTile
          label="Países distintos"
          :value="dashboard.distinctCountries"
          :caption="`${dashboard.passportProgress.percentage.toLocaleString('pt-BR')}% do mundo`"
        />
        <StatTile label="Continentes" :value="dashboard.continentsExplored" caption="de 6" />
        <StatTile
          label="Lendo agora"
          :value="dashboard.readingNow"
          :caption="`${dashboard.wantToRead} na lista de desejos`"
        />
      </BaseCard>

      <div class="grid">
        <BaseCard aria-labelledby="dash-recentes">
          <h2 id="dash-recentes" class="eyebrow">Concluídos recentemente</h2>
          <ul v-if="dashboard.recentlyFinished.length" role="list" class="recent">
            <li v-for="book in dashboard.recentlyFinished" :key="book.libraryItemId">
              <RouterLink
                :to="{ name: 'library-item', params: { id: book.libraryItemId } }"
                class="recent__item"
              >
                <BookCover :title="book.title" :cover-url="book.coverUrl" size="sm" />
                <span class="recent__text">
                  <span class="recent__title">{{ book.title }}</span>
                  <span class="muted">{{ book.authors.join(', ') || 'Autoria desconhecida' }}</span>
                  <span class="recent__date">{{ formatDate(book.finishedDate) }}</span>
                </span>
              </RouterLink>
            </li>
          </ul>
          <p v-else class="muted">Nenhuma leitura concluída ainda.</p>
        </BaseCard>

        <BaseCard aria-labelledby="dash-top">
          <h2 id="dash-top" class="eyebrow">Países com mais livros</h2>
          <template v-if="dashboard.topCountries.length">
            <section v-for="group in topByMap" :key="group.type" class="top-group">
              <h3 class="top-group__title">{{ MAP_TYPE_LABELS[group.type] }}</h3>
              <ol v-if="group.countries.length" role="list" class="top">
                <li v-for="country in group.countries" :key="country.iso">
                  <span>{{ country.name }}</span>
                  <span class="numeric">{{ country.bookCount }}</span>
                </li>
              </ol>
              <p v-else class="muted">Nenhum país identificado.</p>
            </section>
          </template>
          <p v-else class="muted">Conclua leituras para ver seus países.</p>
        </BaseCard>
      </div>
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
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  padding: var(--space-3) 0;
  overflow: hidden;
}
.grid {
  display: grid;
  grid-template-columns: 3fr 2fr;
  gap: var(--space-5);
}
@media (max-width: 900px) {
  .grid {
    grid-template-columns: 1fr;
  }
}
.recent {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: var(--space-3);
  margin-top: var(--space-4);
}
.recent__item {
  display: flex;
  gap: var(--space-3);
  align-items: center;
  padding: var(--space-2);
  border-radius: var(--radius-md);
  color: inherit;
  text-decoration: none;
}
.recent__item:hover {
  background: var(--color-surface-2);
  color: inherit;
}
.recent__text {
  display: flex;
  flex-direction: column;
  font-size: var(--text-sm);
  min-width: 0;
}
.recent__title {
  font-family: var(--font-display);
  font-size: var(--text-base);
}
.recent__date {
  font-size: var(--text-xs);
  color: var(--color-text-faint);
}
.top {
  margin-top: var(--space-4);
  display: flex;
  flex-direction: column;
}
.top-group {
  margin-top: var(--space-4);
}
.top-group__title {
  font-family: var(--font-body);
  font-size: var(--text-sm);
  font-weight: 500;
  color: var(--color-text-muted);
}
.top-group .top {
  margin-top: var(--space-2);
}
.top li {
  display: grid;
  grid-template-columns: 1fr 2.5rem;
  gap: var(--space-3);
  align-items: baseline;
  padding: var(--space-2) 0;
  border-bottom: 1px solid var(--color-hairline);
}
.top li > :last-child {
  text-align: right;
  font-family: var(--font-display);
  font-size: var(--text-lg);
}
</style>
