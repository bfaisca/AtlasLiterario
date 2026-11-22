<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '@/api'
import { getErrorMessage, toApiError } from '@/api/errors'
import type { BookSearchResultDto, LibraryItemDto } from '@/types/api'
import { useLibraryStore } from '@/stores/library'
import { findLibraryItemId, type AddOutcome } from '@/utils/addRecovery'
import {
  emptyReadingDraft,
  toReadingRequest,
  validateReading,
  type ReadingDraft,
  type ReadingErrors,
} from '@/utils/readingForm'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import BaseModal from '@/components/ui/BaseModal.vue'
import BookCover from './BookCover.vue'
import ReadingFields from './ReadingFields.vue'

/** "Adicionar à biblioteca" a partir de um resultado de pesquisa. */
const props = defineProps<{ result: BookSearchResultDto | null }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ added: [item: LibraryItemDto, outcome: AddOutcome] }>()

const library = useLibraryStore()
const draft = ref<ReadingDraft>(emptyReadingDraft())
const errors = ref<ReadingErrors>({})
const formError = ref<string | null>(null)
const saving = ref(false)
const phase = ref<'saving' | 'checking'>('saving')

const PROGRESS_TEXT = {
  saving: 'Salvando e buscando nacionalidade e origem da obra… isso pode levar alguns segundos',
  checking: 'A resposta demorou. Conferindo se o livro já foi adicionado…',
} as const

watch(open, (isOpen) => {
  if (isOpen) {
    draft.value = emptyReadingDraft()
    errors.value = {}
    formError.value = null
  }
})

const subtitle = computed(() => {
  const r = props.result
  if (!r) return ''
  return [r.authors.join(', ') || 'Autoria desconhecida', r.publicationYear]
    .filter(Boolean)
    .join(' · ')
})

/** Item já existente/criado: carrega o item completo e conclui como sucesso. */
async function finishWithExisting(itemId: string, outcome: AddOutcome) {
  const item = await library.getItem(itemId)
  library.invalidate()
  open.value = false
  emit('added', item, outcome)
}

async function submit() {
  const result = props.result
  if (!result || saving.value) return // sem duplo envio
  errors.value = validateReading(draft.value)
  if (Object.keys(errors.value).length) return

  saving.value = true
  phase.value = 'saving'
  formError.value = null
  try {
    // Resultado local já está no catálogo: usa bookId; senão importa pela fonte externa.
    const target = result.bookId ? { bookId: result.bookId } : { source: result.source }
    const item = await library.add({ ...target, ...toReadingRequest(draft.value) })
    open.value = false
    emit('added', item, 'created')
  } catch (error) {
    const apiError = toApiError(error)
    try {
      if (apiError.status === 0) {
        // Sem resposta: o servidor pode ter concluído. Confere antes de acusar erro.
        phase.value = 'checking'
        const itemId = await findLibraryItemId(result, api.books.search)
        if (itemId) return await finishWithExisting(itemId, 'recovered')
        formError.value = `${getErrorMessage(apiError)} O livro não foi adicionado; seus dados foram mantidos, é só tentar novamente.`
        return
      }
      if (apiError.status === 409 && apiError.code === 'library.duplicate') {
        const itemId = result.libraryItemId ?? (await findLibraryItemId(result, api.books.search))
        if (itemId) return await finishWithExisting(itemId, 'duplicate')
      }
    } catch (followUp) {
      formError.value = getErrorMessage(toApiError(followUp))
      return
    }
    errors.value = {
      startDate: apiError.fieldError('startDate'),
      finishedDate: apiError.fieldError('finishedDate'),
      rating: apiError.fieldError('rating'),
      review: apiError.fieldError('review'),
    }
    formError.value = getErrorMessage(apiError)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <BaseModal
    v-model:open="open"
    variant="drawer"
    title="Adicionar à biblioteca"
    :persistent="saving"
  >
    <form v-if="result" id="add-to-library" class="add" novalidate @submit.prevent="submit">
      <div class="add__book">
        <BookCover :title="result.title" :cover-url="result.coverUrl" size="sm" />
        <div>
          <p class="add__title">{{ result.title }}</p>
          <p class="muted add__meta">{{ subtitle }}</p>
        </div>
      </div>
      <p v-if="formError" class="add__error" role="alert">{{ formError }}</p>
      <p class="add__progress" role="status" aria-live="polite">
        <template v-if="saving">
          <BaseSpinner size="sm" />
          <span>{{ PROGRESS_TEXT[phase] }}</span>
        </template>
      </p>
      <fieldset class="add__fields" :disabled="saving">
        <ReadingFields v-model="draft" :errors="errors" />
      </fieldset>
    </form>
    <template #footer>
      <BaseButton variant="ghost" :disabled="saving" @click="open = false">Cancelar</BaseButton>
      <BaseButton type="submit" form="add-to-library" :loading="saving">Adicionar</BaseButton>
    </template>
  </BaseModal>
</template>

<style scoped>
.add {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}
.add__book {
  display: flex;
  gap: var(--space-3);
  align-items: center;
}
.add__title {
  font-family: var(--font-display);
  font-size: var(--text-lg);
}
.add__meta {
  font-size: var(--text-sm);
}
.add__progress {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  color: var(--color-text-muted);
  font-size: var(--text-sm);
}
.add__progress:empty {
  display: none;
}
.add__fields {
  border: 0;
  padding: 0;
  margin: 0;
  min-width: 0;
}
.add__error {
  padding: var(--space-3);
  border-radius: var(--radius-md);
  background: var(--color-danger-soft);
  color: var(--color-danger);
  font-size: var(--text-sm);
}
</style>
