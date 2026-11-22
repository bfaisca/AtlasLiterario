<script setup lang="ts">
/** Lista paginada de pessoas com estados de carregamento, erro e vazio. */
import { getErrorMessage } from '@/api/errors'
import type { PeopleList } from '@/stores/profiles'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import PersonCard from '@/components/people/PersonCard.vue'

defineProps<{ list: PeopleList; label: string; emptyTitle: string; emptyDescription?: string }>()
const emit = defineEmits<{ retry: []; 'load-more': [] }>()
</script>

<template>
  <div class="people" :aria-busy="list.loading">
    <div v-if="list.loading && !list.items.length" class="people__state">
      <BaseSpinner :label="`Carregando ${label}`" />
    </div>
    <div v-else-if="list.error && !list.items.length" role="alert">
      <EmptyState
        icon="alert"
        :title="`Não foi possível carregar: ${label}`"
        :description="getErrorMessage(list.error)"
      >
        <BaseButton variant="secondary" @click="emit('retry')">Tentar novamente</BaseButton>
      </EmptyState>
    </div>
    <EmptyState
      v-else-if="list.loaded && !list.items.length"
      icon="user"
      :title="emptyTitle"
      :description="emptyDescription"
    />
    <template v-else>
      <ul role="list" class="people__list" :aria-label="label">
        <li v-for="person in list.items" :key="person.handle">
          <PersonCard :person="person" />
        </li>
      </ul>
      <p v-if="list.error" class="people__error" role="alert">
        {{ getErrorMessage(list.error) }}
      </p>
      <div v-if="list.page < list.totalPages" class="people__more">
        <BaseButton variant="secondary" :loading="list.loadingMore" @click="emit('load-more')">
          Carregar mais
        </BaseButton>
      </div>
    </template>
  </div>
</template>

<style scoped>
.people__state {
  display: grid;
  place-items: center;
  padding: var(--space-7);
}
.people__list {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.people__more {
  display: flex;
  justify-content: center;
  margin-top: var(--space-4);
}
.people__error {
  margin-top: var(--space-3);
  color: var(--color-danger, #f08a8a);
  font-size: var(--text-sm);
}
</style>
