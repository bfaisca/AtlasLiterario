<script setup lang="ts">
import { computed } from 'vue'
import { READING_STATUSES, type ReadingStatus } from '@/types/api'
import { READING_STATUS_LABELS } from '@/utils/labels'
import { REVIEW_MAX_LENGTH, type ReadingDraft, type ReadingErrors } from '@/utils/readingForm'
import BaseInput from '@/components/ui/BaseInput.vue'
import BaseTextArea from '@/components/ui/BaseTextArea.vue'
import StarRating from '@/components/ui/StarRating.vue'

/** Campos da experiência de leitura (status, nota, datas, resenha). Validação: utils/readingForm. */
const props = withDefaults(defineProps<{ errors?: ReadingErrors }>(), { errors: () => ({}) })
const draft = defineModel<ReadingDraft>({ required: true })

const showFinished = computed(() => draft.value.status === 'Read')

function setStatus(status: ReadingStatus) {
  draft.value = { ...draft.value, status }
}

function update<K extends keyof ReadingDraft>(key: K, value: ReadingDraft[K]) {
  draft.value = { ...draft.value, [key]: value }
}
</script>

<template>
  <div class="reading">
    <fieldset class="reading__status">
      <legend class="reading__legend">Status de leitura</legend>
      <div class="reading__options" role="radiogroup" aria-label="Status de leitura">
        <label
          v-for="status in READING_STATUSES"
          :key="status"
          class="reading__option"
          :class="{ 'is-active': draft.status === status }"
        >
          <input
            type="radio"
            name="reading-status"
            :value="status"
            :checked="draft.status === status"
            class="sr-only"
            @change="setStatus(status)"
          />
          {{ READING_STATUS_LABELS[status] }}
        </label>
      </div>
    </fieldset>

    <div class="reading__rating">
      <span class="reading__legend">Sua avaliação</span>
      <StarRating
        :model-value="draft.rating"
        size="lg"
        label="Sua avaliação"
        @update:model-value="update('rating', $event)"
      />
      <p v-if="props.errors.rating" class="reading__error" role="alert">
        {{ props.errors.rating }}
      </p>
    </div>

    <div class="reading__dates">
      <BaseInput
        :model-value="draft.startDate"
        label="Início da leitura"
        type="date"
        :error="props.errors.startDate"
        @update:model-value="update('startDate', $event)"
      />
      <BaseInput
        v-if="showFinished"
        :model-value="draft.finishedDate"
        label="Conclusão"
        type="date"
        :error="props.errors.finishedDate"
        :hint="draft.finishedDate ? undefined : 'Em branco: usamos a data de hoje.'"
        @update:model-value="update('finishedDate', $event)"
      />
    </div>

    <BaseTextArea
      :model-value="draft.review"
      label="Resenha (opcional)"
      :rows="4"
      :maxlength="REVIEW_MAX_LENGTH"
      :error="props.errors.review"
      :hint="`${draft.review.length.toLocaleString('pt-BR')} / 10.000`"
      @update:model-value="update('review', $event)"
    />
  </div>
</template>

<style scoped>
.reading {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}
.reading__status {
  border: 0;
  padding: 0;
  margin: 0;
}
.reading__legend {
  display: block;
  margin-bottom: var(--space-2);
  font-size: var(--text-sm);
  font-weight: 500;
  color: var(--color-text-muted);
}
.reading__options {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: var(--space-2);
}
@media (max-width: 480px) {
  .reading__options {
    grid-template-columns: repeat(2, 1fr);
  }
}
.reading__option {
  display: grid;
  place-items: center;
  padding: 0.6rem var(--space-2);
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border);
  font-size: var(--text-sm);
  font-weight: 500;
  color: var(--color-text-muted);
  cursor: pointer;
  text-align: center;
  transition: all var(--duration-fast) var(--ease-out);
}
.reading__option:hover {
  color: var(--color-text);
  border-color: var(--color-border-strong);
}
.reading__option:has(input:focus-visible) {
  box-shadow: var(--focus-ring);
}
.reading__option.is-active {
  color: var(--color-brass-strong);
  border-color: rgb(227 191 124 / 0.5);
  background: var(--color-brass-soft);
}
.reading__dates {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-3);
}
@media (max-width: 480px) {
  .reading__dates {
    grid-template-columns: 1fr;
  }
}
.reading__error {
  margin-top: var(--space-1);
  font-size: var(--text-xs);
  color: var(--color-danger);
}
</style>
