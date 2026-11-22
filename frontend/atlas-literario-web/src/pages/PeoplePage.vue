<script setup lang="ts">
import { computed } from 'vue'
import { getErrorMessage } from '@/api/errors'
import { PEOPLE_SEARCH_MIN_LENGTH, usePeopleSearch } from '@/composables/usePeopleSearch'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseInput from '@/components/ui/BaseInput.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import PersonCard from '@/components/people/PersonCard.vue'

/** Descoberta de pessoas por apelido ou nome (só perfis públicos). Seguir é assimétrico. */
const { text, results, loading, error, searchedQuery, onInput } = usePeopleSearch()

const trimmed = computed(() => text.value.trim())
const tooShort = computed(() => trimmed.value.length < PEOPLE_SEARCH_MIN_LENGTH)
const noResults = computed(
  () => !loading.value && !error.value && searchedQuery.value !== null && !results.value.length,
)
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <p class="eyebrow">Comunidade</p>
        <h1>Pessoas</h1>
        <p class="muted">
          Encontre leitores pelo apelido ou pelo nome e veja os mapas que eles escolheram tornar
          públicos. Você não precisa seguir ninguém para ver um perfil público.
        </p>
      </div>
      <div class="cluster">
        <BaseButton variant="ghost" :to="{ name: 'me-following' }">Quem eu sigo</BaseButton>
        <BaseButton variant="ghost" :to="{ name: 'me-followers' }">Meus seguidores</BaseButton>
      </div>
    </header>

    <div class="search" role="search">
      <BaseInput
        :model-value="text"
        label="Buscar pessoas"
        type="search"
        placeholder="Apelido ou nome"
        autocomplete="off"
        spellcheck="false"
        hint="Digite ao menos 2 caracteres."
        data-testid="people-search"
        @update:model-value="onInput"
      />
    </div>

    <div class="results" aria-live="polite" :aria-busy="loading">
      <div v-if="loading && !results.length" class="center">
        <BaseSpinner label="Buscando pessoas" />
      </div>
      <div v-else-if="error" role="alert">
        <EmptyState icon="alert" title="A busca falhou" :description="getErrorMessage(error)" />
      </div>
      <EmptyState
        v-else-if="tooShort"
        icon="search"
        title="Comece a digitar"
        description="Os resultados aparecem enquanto você digita."
      />
      <EmptyState
        v-else-if="noResults"
        icon="user"
        title="Ninguém encontrado"
        :description="`Nenhum perfil público corresponde a “${searchedQuery}”. Perfis privados não aparecem na busca.`"
      />
      <ul v-else-if="results.length" role="list" class="list" aria-label="Resultados da busca">
        <li v-for="person in results" :key="person.handle">
          <PersonCard :person="person" />
        </li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
.search {
  max-width: 560px;
}
.center {
  display: grid;
  place-items: center;
  padding: var(--space-7);
}
.list {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
</style>
