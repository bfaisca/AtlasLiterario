<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '@/api'
import { getErrorMessage, toApiError } from '@/api/errors'
import { useLibraryStore } from '@/stores/library'
import { useUiStore } from '@/stores/ui'
import {
  emptyBookDraft,
  toCreateBookRequest,
  validateBook,
  type BookDraft,
  type BookErrors,
} from '@/utils/bookForm'
import {
  emptyReadingDraft,
  toReadingRequest,
  validateReading,
  type ReadingDraft,
  type ReadingErrors,
} from '@/utils/readingForm'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseCard from '@/components/ui/BaseCard.vue'
import BookFieldsForm from '@/components/books/BookFieldsForm.vue'
import CoverPicker from '@/components/books/CoverPicker.vue'
import ReadingFields from '@/components/books/ReadingFields.vue'

/**
 * Cadastro manual: POST /books → POST /library (bookId criado) → POST /books/{id}/cover.
 * A capa vai por último porque o contrato (v1.6) exige o livro na biblioteca do usuário; a falha
 * do envio da capa não desfaz nada: o livro já está cadastrado e a capa pode ser reenviada no detalhe.
 */
const router = useRouter()
const library = useLibraryStore()
const ui = useUiStore()

const book = ref<BookDraft>(emptyBookDraft())
const reading = ref<ReadingDraft>(emptyReadingDraft())
const bookErrors = ref<BookErrors>({})
const readingErrors = ref<ReadingErrors>({})
const formError = ref<string | null>(null)
const saving = ref(false)
const phase = ref<'book' | 'library' | 'cover'>('book')
const PHASE_TEXT = {
  book: 'Cadastrando o livro…',
  library: 'Adicionando à sua biblioteca… isso pode levar alguns segundos',
  cover: 'Enviando capa…',
} as const
/** Capa escolhida (já reduzida). Mutuamente exclusiva com o campo de URL. */
const coverFile = ref<File | null>(null)
watch(coverFile, (file) => {
  if (file) book.value = { ...book.value, coverUrl: '' }
})
watch(
  () => book.value.coverUrl,
  (url) => {
    if (url.trim() && coverFile.value) coverFile.value = null
  },
)
const coverTitle = computed(() => book.value.title.trim() || 'Novo livro')
/** Livro já criado numa tentativa anterior (evita duplicar no catálogo ao repetir). */
const createdBookId = ref<string | null>(null)

async function submit() {
  if (saving.value) return // sem duplo envio (Enter durante o envio)
  bookErrors.value = createdBookId.value ? {} : validateBook(book.value)
  readingErrors.value = validateReading(reading.value)
  if (Object.keys(bookErrors.value).length || Object.keys(readingErrors.value).length) {
    formError.value = 'Verifique os campos destacados.'
    return
  }

  saving.value = true
  formError.value = null
  try {
    if (!createdBookId.value) {
      phase.value = 'book'
      const created = await api.books.create(toCreateBookRequest(book.value, true))
      createdBookId.value = created.id
    }
    phase.value = 'library'
    const item = await library.add({
      bookId: createdBookId.value,
      ...toReadingRequest(reading.value),
    })
    if (coverFile.value) {
      phase.value = 'cover'
      try {
        await api.books.uploadCover(item.book.id, coverFile.value)
        library.invalidate()
        ui.success(`“${item.book.title}” foi cadastrado na sua biblioteca, com a capa enviada.`)
      } catch {
        // O livro já existe e está na biblioteca: segue para o detalhe, onde dá para reenviar.
        ui.warning(
          'Livro cadastrado, mas a capa não foi enviada. Você pode tentar de novo na página do livro.',
        )
      }
    } else {
      ui.success(`“${item.book.title}” foi cadastrado na sua biblioteca.`)
    }
    await router.push({ name: 'library-item', params: { id: item.id } })
  } catch (error) {
    const apiError = toApiError(error)
    bookErrors.value = {
      title: apiError.fieldError('title'),
      authorNames: apiError.fieldError('authorNames'),
      isbn13: apiError.fieldError('isbn13'),
      isbn10: apiError.fieldError('isbn10'),
    }
    formError.value = getErrorMessage(apiError)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="page">
    <RouterLink :to="{ name: 'search' }" class="back"
      ><AppIcon name="arrowLeft" :size="16" /> Pesquisa</RouterLink
    >
    <header class="page-header">
      <div>
        <p class="eyebrow">Cadastro manual</p>
        <h1>Novo livro</h1>
        <p class="muted intro">
          Para livros que não aparecem na pesquisa. Informe o que souber — o que ficar em branco
          permanece desconhecido.
        </p>
      </div>
    </header>

    <form class="manual" novalidate @submit.prevent="submit">
      <p v-if="formError" class="manual__error" role="alert">{{ formError }}</p>
      <p v-if="createdBookId" class="manual__note" role="status">
        O livro já foi criado no catálogo; falta apenas adicioná-lo à sua biblioteca.
      </p>

      <BaseCard :class="{ 'is-locked': !!createdBookId }">
        <h2 class="section-title">Dados do livro</h2>
        <fieldset :disabled="!!createdBookId || saving" class="manual__fieldset">
          <BookFieldsForm
            v-model="book"
            :errors="bookErrors"
            show-origins
            cover-url-label="Ou informe o endereço da imagem"
          >
            <template #cover>
              <div class="manual__cover">
                <h3 class="eyebrow">Capa</h3>
                <CoverPicker v-model:file="coverFile" :title="coverTitle" :disabled="saving" />
              </div>
            </template>
          </BookFieldsForm>
        </fieldset>
      </BaseCard>

      <BaseCard>
        <h2 class="section-title">Sua leitura</h2>
        <ReadingFields v-model="reading" :errors="readingErrors" />
      </BaseCard>

      <div class="manual__actions">
        <BaseButton variant="ghost" :to="{ name: 'search' }">Cancelar</BaseButton>
        <BaseButton type="submit" size="lg" :loading="saving">Cadastrar e adicionar</BaseButton>
      </div>
      <p class="manual__progress" role="status" aria-live="polite">
        <template v-if="saving">{{ PHASE_TEXT[phase] }}</template>
      </p>
    </form>
  </div>
</template>

<style scoped>
.manual__cover {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.manual__progress {
  font-size: var(--text-sm);
  color: var(--color-text-muted);
}
.manual__progress:empty {
  display: none;
}
.back {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  font-size: var(--text-sm);
  color: var(--color-text-muted);
  text-decoration: none;
}
.intro {
  max-width: 60ch;
  margin-top: var(--space-2);
}
.manual {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
  max-width: 760px;
}
.manual__fieldset {
  border: 0;
  padding: 0;
  margin: 0;
}
.section-title {
  font-size: var(--text-xl);
  margin-bottom: var(--space-4);
}
.manual__error {
  padding: var(--space-3);
  border-radius: var(--radius-md);
  background: var(--color-danger-soft);
  color: var(--color-danger);
  font-size: var(--text-sm);
}
.manual__note {
  padding: var(--space-3);
  border-radius: var(--radius-md);
  background: var(--color-warning-soft);
  color: var(--color-warning);
  font-size: var(--text-sm);
}
.is-locked {
  opacity: 0.6;
}
.manual__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-3);
}
</style>
