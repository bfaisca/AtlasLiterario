<script setup lang="ts">
import { computed } from 'vue'
import { isUploadedCoverUrl, type BookDraft, type BookErrors } from '@/utils/bookForm'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseInput from '@/components/ui/BaseInput.vue'
import BaseTextArea from '@/components/ui/BaseTextArea.vue'
import CountryPicker from '@/components/geo/CountryPicker.vue'

/** Campos bibliográficos (cadastro manual e correção). Validação: utils/bookForm. */
const props = withDefaults(
  defineProps<{ errors?: BookErrors; showOrigins?: boolean; coverUrlLabel?: string }>(),
  { errors: () => ({}), showOrigins: false, coverUrlLabel: 'URL da capa' },
)
const draft = defineModel<BookDraft>({ required: true })

const uploadedCover = computed(() => isUploadedCoverUrl(draft.value.coverUrl))

function set<K extends keyof BookDraft>(key: K, value: BookDraft[K]) {
  draft.value = { ...draft.value, [key]: value }
}

function setAuthor(index: number, name: string) {
  set(
    'authorNames',
    draft.value.authorNames.map((n, i) => (i === index ? name : n)),
  )
}

function addAuthor() {
  set('authorNames', [...draft.value.authorNames, ''])
}

function removeAuthor(index: number) {
  const next = draft.value.authorNames.filter((_, i) => i !== index)
  set('authorNames', next.length ? next : [''])
}
</script>

<template>
  <div class="book-form">
    <BaseInput
      :model-value="draft.title"
      label="Título"
      required
      :error="props.errors.title"
      @update:model-value="set('title', $event)"
    />
    <BaseInput
      :model-value="draft.subtitle"
      label="Subtítulo"
      @update:model-value="set('subtitle', $event)"
    />

    <fieldset class="book-form__authors">
      <legend class="book-form__legend">Autores</legend>
      <div v-for="(name, index) in draft.authorNames" :key="index" class="book-form__author">
        <BaseInput
          :model-value="name"
          :label="`Autor ${index + 1}`"
          :error="index === 0 ? props.errors.authorNames : null"
          @update:model-value="setAuthor(index, $event)"
        />
        <button
          v-if="draft.authorNames.length > 1"
          type="button"
          class="book-form__remove"
          :aria-label="`Remover autor ${index + 1}`"
          @click="removeAuthor(index)"
        >
          <AppIcon name="trash" :size="16" />
        </button>
      </div>
      <BaseButton variant="ghost" size="sm" @click="addAuthor">+ Adicionar autor</BaseButton>
    </fieldset>

    <div class="book-form__grid">
      <BaseInput
        :model-value="draft.publisher"
        label="Editora"
        @update:model-value="set('publisher', $event)"
      />
      <BaseInput
        :model-value="draft.publicationYear"
        label="Ano de publicação"
        inputmode="numeric"
        :error="props.errors.publicationYear"
        @update:model-value="set('publicationYear', $event)"
      />
      <BaseInput
        :model-value="draft.isbn13"
        label="ISBN-13"
        inputmode="numeric"
        :error="props.errors.isbn13"
        @update:model-value="set('isbn13', $event)"
      />
      <BaseInput
        :model-value="draft.isbn10"
        label="ISBN-10"
        :error="props.errors.isbn10"
        @update:model-value="set('isbn10', $event)"
      />
      <BaseInput
        :model-value="draft.originalLanguage"
        label="Idioma original"
        hint="Código como pt, es, en."
        @update:model-value="set('originalLanguage', $event)"
      />
      <BaseInput
        :model-value="draft.genres"
        label="Gêneros"
        hint="Separe por vírgulas."
        @update:model-value="set('genres', $event)"
      />
    </div>

    <slot name="cover" />
    <BaseInput
      :model-value="draft.coverUrl"
      :label="props.coverUrlLabel"
      :type="uploadedCover ? 'text' : 'url'"
      :hint="
        uploadedCover
          ? 'Este livro tem uma capa enviada. Para trocar o arquivo, use a seção Capa; informar um endereço aqui a substitui.'
          : undefined
      "
      :error="props.errors.coverUrl"
      @update:model-value="set('coverUrl', $event)"
    />
    <BaseTextArea
      :model-value="draft.description"
      label="Sinopse"
      :rows="3"
      @update:model-value="set('description', $event)"
    />

    <CountryPicker
      v-if="props.showOrigins"
      :model-value="draft.originCountryIsos"
      label="Países de origem da obra"
      hint="Origem literária da obra — não confundir com a nacionalidade do autor nem com o local da história."
      @update:model-value="set('originCountryIsos', $event)"
    />
    <p v-if="props.showOrigins" class="book-form__moderation" role="note">
      Depois de salvo, só moderadores poderão alterar estes países.
    </p>
  </div>
</template>

<style scoped>
.book-form__moderation {
  margin-top: calc(-1 * var(--space-2));
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.book-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.book-form__authors {
  border: 0;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--space-2);
}
.book-form__legend {
  margin-bottom: var(--space-2);
  font-size: var(--text-sm);
  font-weight: 500;
  color: var(--color-text-muted);
}
.book-form__author {
  width: 100%;
  display: flex;
  align-items: flex-end;
  gap: var(--space-2);
}
.book-form__author > :first-child {
  flex: 1;
}
.book-form__remove {
  display: grid;
  place-items: center;
  width: 2.6rem;
  height: 2.6rem;
  border-radius: var(--radius-md);
  color: var(--color-text-faint);
}
.book-form__remove:hover {
  color: var(--color-danger);
  background: var(--color-danger-soft);
}
.book-form__grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-4) var(--space-3);
}
@media (max-width: 560px) {
  .book-form__grid {
    grid-template-columns: 1fr;
  }
}
</style>
