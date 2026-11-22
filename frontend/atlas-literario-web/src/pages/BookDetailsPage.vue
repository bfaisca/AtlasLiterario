<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '@/api'
import { getErrorMessage, toApiError } from '@/api/errors'
import type { AuthorSummaryDto, GeoEnrichmentStatus, LibraryItemDto } from '@/types/api'
import { useAuthStore } from '@/stores/auth'
import { useLibraryStore } from '@/stores/library'
import { useUiStore } from '@/stores/ui'
import {
  draftFromBook,
  isUploadedCoverUrl,
  toCreateBookRequest,
  validateBook,
  type BookDraft,
  type BookErrors,
} from '@/utils/bookForm'
import { formatDate } from '@/utils/format'
import {
  draftFromItem,
  toReadingRequest,
  validateReading,
  type ReadingDraft,
  type ReadingErrors,
} from '@/utils/readingForm'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseBadge from '@/components/ui/BaseBadge.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseCard from '@/components/ui/BaseCard.vue'
import BaseModal from '@/components/ui/BaseModal.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import ConfirmDialog from '@/components/ui/ConfirmDialog.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import StarRating from '@/components/ui/StarRating.vue'
import BookCover from '@/components/books/BookCover.vue'
import BookFieldsForm from '@/components/books/BookFieldsForm.vue'
import CoverPicker from '@/components/books/CoverPicker.vue'
import ReadingFields from '@/components/books/ReadingFields.vue'
import ReadingStatusBadge from '@/components/books/ReadingStatusBadge.vue'
import CountryPicker from '@/components/geo/CountryPicker.vue'
import GeoRelationBadge from '@/components/geo/GeoRelationBadge.vue'

const props = defineProps<{ id: string }>()
const router = useRouter()
const library = useLibraryStore()
const auth = useAuthStore()
const ui = useUiStore()

const item = ref<LibraryItemDto | null>(null)
const loading = ref(true)
const loadError = ref<unknown>(null)

// Leitura
const reading = ref<ReadingDraft | null>(null)
const readingErrors = ref<ReadingErrors>({})
const savingReading = ref(false)
const readingDirty = computed(
  () =>
    !!item.value &&
    !!reading.value &&
    JSON.stringify(toReadingRequest(reading.value)) !==
      JSON.stringify(toReadingRequest(draftFromItem(item.value))),
)

// Remoção
const confirmRemove = ref(false)
const removing = ref(false)

// Capa
const coverOpen = ref(false)
const coverFile = ref<File | null>(null)
const uploadingCover = ref(false)
const coverError = ref<string | null>(null)
const confirmRemoveCover = ref(false)
const removingCover = ref(false)
const hasUploadedCover = computed(() => isUploadedCoverUrl(item.value?.book.coverUrl))

// Geografia
const enriching = ref(false)
const geoModal = ref<{ kind: 'origins' } | { kind: 'author'; author: AuthorSummaryDto } | null>(
  null,
)
const geoOpen = ref(false)
const geoSelection = ref<string[]>([])
const savingGeo = ref(false)
const geoError = ref<string | null>(null)

// Dados bibliográficos
const bookOpen = ref(false)
const bookDraft = ref<BookDraft | null>(null)
const bookErrors = ref<BookErrors>({})
const savingBook = ref(false)
const bookError = ref<string | null>(null)

const ENRICHMENT_LABELS: Record<GeoEnrichmentStatus, string> = {
  Completed: 'Dados geográficos completos',
  Partial: 'Dados geográficos parciais',
  Pending: 'Dados geográficos ainda não consultados',
  Failed: 'Falha ao consultar as fontes geográficas',
}

const canEnrich = computed(() => {
  const status = item.value?.book.geoEnrichmentStatus
  return status === 'Pending' || status === 'Partial' || status === 'Failed'
})

function setItem(next: LibraryItemDto) {
  item.value = next
  reading.value = draftFromItem(next)
  readingErrors.value = {}
}

async function load() {
  loading.value = true
  loadError.value = null
  try {
    setItem(await library.getItem(props.id))
  } catch (e) {
    loadError.value = e
  } finally {
    loading.value = false
  }
}

async function reloadQuietly() {
  try {
    const next = await library.getItem(props.id)
    item.value = next
    if (!readingDirty.value) reading.value = draftFromItem(next)
  } catch (e) {
    ui.notifyError(e)
  }
}

watch(() => props.id, load, { immediate: true })

async function saveReading() {
  if (!item.value || !reading.value) return
  readingErrors.value = validateReading(reading.value)
  if (Object.keys(readingErrors.value).length) return
  savingReading.value = true
  try {
    setItem(await library.update(item.value.id, toReadingRequest(reading.value)))
    ui.success('Leitura atualizada.')
  } catch (e) {
    const apiError = toApiError(e)
    readingErrors.value = {
      startDate: apiError.fieldError('startDate'),
      finishedDate: apiError.fieldError('finishedDate'),
      rating: apiError.fieldError('rating'),
      review: apiError.fieldError('review'),
    }
    ui.error(getErrorMessage(apiError))
  } finally {
    savingReading.value = false
  }
}

function openCover() {
  coverFile.value = null
  coverError.value = null
  coverOpen.value = true
}

/** Envia assim que a imagem é escolhida e reduzida; em caso de erro, permite tentar de novo. */
async function uploadCover() {
  if (!item.value || !coverFile.value || uploadingCover.value) return
  uploadingCover.value = true
  coverError.value = null
  try {
    await api.books.uploadCover(item.value.book.id, coverFile.value)
    library.invalidate()
    coverOpen.value = false
    await reloadQuietly()
    ui.success('Capa atualizada.')
  } catch (e) {
    coverError.value = getErrorMessage(e)
  } finally {
    uploadingCover.value = false
  }
}
watch(coverFile, (file) => {
  if (file) void uploadCover()
})

async function removeCover() {
  if (!item.value) return
  removingCover.value = true
  try {
    await api.books.removeCover(item.value.book.id)
    library.invalidate()
    confirmRemoveCover.value = false
    await reloadQuietly()
    ui.success('Capa removida.')
  } catch (e) {
    ui.notifyError(e)
  } finally {
    removingCover.value = false
  }
}

async function remove() {
  if (!item.value) return
  removing.value = true
  try {
    await library.remove(item.value.id)
    ui.success(`“${item.value.book.title}” foi removido da sua biblioteca.`)
    confirmRemove.value = false
    await router.replace({ name: 'library' })
  } catch (e) {
    ui.notifyError(e)
  } finally {
    removing.value = false
  }
}

async function enrich() {
  if (!item.value) return
  enriching.value = true
  try {
    await api.books.enrich(item.value.book.id)
    library.invalidate()
    await reloadQuietly()
    ui.success('Dados geográficos atualizados.')
  } catch (e) {
    ui.notifyError(e)
    // Sem resposta (timeout/rede): o servidor pode ter concluído; atualiza a tela em silêncio.
    if (toApiError(e).status === 0) await reloadQuietly()
  } finally {
    enriching.value = false
  }
}

/** Conjunto vazio: qualquer um informa (1ª informação). Preenchido: só moderador edita. */
type GeoAction = 'inform' | 'edit' | 'locked'
function geoAction(filled: boolean): GeoAction {
  if (!filled) return 'inform'
  return auth.isModerator ? 'edit' : 'locked'
}
const originsAction = computed(() => geoAction(!!item.value?.book.originCountries.length))
const hasLockedGeo = computed(
  () =>
    !!item.value &&
    (originsAction.value === 'locked' ||
      item.value.book.authors.some((a) => geoAction(a.nationalities.length > 0) === 'locked')),
)

function openOrigins() {
  if (!item.value) return
  geoModal.value = { kind: 'origins' }
  geoSelection.value = item.value.book.originCountries.map((o) => o.countryIso)
  geoError.value = null
  geoOpen.value = true
}

function openAuthor(author: AuthorSummaryDto) {
  geoModal.value = { kind: 'author', author }
  geoSelection.value = author.nationalities.map((n) => n.countryIso)
  geoError.value = null
  geoOpen.value = true
}

const geoTitle = computed(() =>
  geoModal.value?.kind === 'author'
    ? `Nacionalidades de ${geoModal.value.author.name}`
    : 'Países de origem da obra',
)

async function saveGeo() {
  if (!item.value || !geoModal.value) return
  savingGeo.value = true
  geoError.value = null
  try {
    if (geoModal.value.kind === 'origins') {
      await api.books.updateOriginCountries(item.value.book.id, { countryIsos: geoSelection.value })
    } else {
      await api.authors.updateCountries(geoModal.value.author.id, {
        citizenshipIsos: geoSelection.value,
      })
    }
    library.invalidate()
    geoOpen.value = false
    await reloadQuietly()
    ui.success('Correção salva.')
  } catch (e) {
    const apiError = toApiError(e)
    if (apiError.code === 'geo.moderator_required') {
      // Outra pessoa pode ter preenchido o conjunto antes: refaz a leitura e fecha o seletor.
      geoOpen.value = false
      ui.error(getErrorMessage(apiError))
      library.invalidate()
      await reloadQuietly()
    } else {
      geoError.value = getErrorMessage(apiError)
    }
  } finally {
    savingGeo.value = false
  }
}

function openBook() {
  if (!item.value) return
  bookDraft.value = draftFromBook(item.value.book)
  bookErrors.value = {}
  bookError.value = null
  bookOpen.value = true
}

async function saveBook() {
  if (!item.value || !bookDraft.value) return
  bookErrors.value = validateBook(bookDraft.value)
  if (Object.keys(bookErrors.value).length) return
  savingBook.value = true
  bookError.value = null
  try {
    await api.books.update(item.value.book.id, toCreateBookRequest(bookDraft.value, false))
    library.invalidate()
    bookOpen.value = false
    await reloadQuietly()
    ui.success('Dados do livro atualizados.')
  } catch (e) {
    const apiError = toApiError(e)
    bookErrors.value = {
      title: apiError.fieldError('title'),
      authorNames: apiError.fieldError('authorNames'),
      isbn13: apiError.fieldError('isbn13'),
      isbn10: apiError.fieldError('isbn10'),
    }
    bookError.value = getErrorMessage(apiError)
  } finally {
    savingBook.value = false
  }
}
</script>

<template>
  <div class="page">
    <RouterLink :to="{ name: 'library' }" class="back">
      <AppIcon name="arrowLeft" :size="16" /> Biblioteca
    </RouterLink>

    <div v-if="loading" class="center"><BaseSpinner size="lg" /></div>
    <EmptyState
      v-else-if="loadError || !item"
      icon="alert"
      title="Livro não encontrado"
      :description="getErrorMessage(loadError)"
    >
      <BaseButton :to="{ name: 'library' }" variant="secondary">Voltar à biblioteca</BaseButton>
    </EmptyState>

    <template v-else>
      <article class="details">
        <BookCover
          :title="item.book.title"
          :author="item.book.authors[0]?.name"
          :cover-url="item.book.coverUrl"
          size="lg"
        />
        <div class="details__body">
          <ReadingStatusBadge :status="item.status" />
          <h1 class="details__title">{{ item.book.title }}</h1>
          <p v-if="item.book.subtitle" class="details__subtitle">{{ item.book.subtitle }}</p>
          <p class="details__authors">
            {{ item.book.authors.map((a) => a.name).join(', ') || 'Autoria desconhecida' }}
            <span v-if="item.book.publicationYear" class="muted"
              >· {{ item.book.publicationYear }}</span
            >
          </p>
          <div v-if="item.rating || item.finishedDate" class="cluster">
            <StarRating v-if="item.rating" :model-value="item.rating" readonly />
            <span v-if="item.finishedDate" class="muted"
              >Concluído em {{ formatDate(item.finishedDate) }}</span
            >
          </div>
          <p v-if="item.book.description" class="details__description">
            {{ item.book.description }}
          </p>
          <blockquote v-if="item.review" class="details__review">{{ item.review }}</blockquote>

          <dl class="meta">
            <div v-if="item.book.publisher">
              <dt>Editora</dt>
              <dd>{{ item.book.publisher }}</dd>
            </div>
            <div v-if="item.book.isbn13">
              <dt>ISBN-13</dt>
              <dd class="numeric">{{ item.book.isbn13 }}</dd>
            </div>
            <div v-if="item.book.isbn10">
              <dt>ISBN-10</dt>
              <dd class="numeric">{{ item.book.isbn10 }}</dd>
            </div>
            <div v-if="item.book.originalLanguage">
              <dt>Idioma original</dt>
              <dd>{{ item.book.originalLanguage }}</dd>
            </div>
            <div v-if="item.book.genres.length">
              <dt>Gêneros</dt>
              <dd>{{ item.book.genres.join(', ') }}</dd>
            </div>
          </dl>
          <BaseButton variant="ghost" size="sm" @click="openBook">
            <AppIcon name="edit" :size="16" /> Corrigir dados do livro
          </BaseButton>
        </div>
      </article>

      <div class="panels">
        <BaseCard as="section" aria-labelledby="cover-title">
          <div class="panel-head">
            <h2 id="cover-title" class="panel-title">Capa</h2>
          </div>
          <p class="muted">
            {{
              hasUploadedCover
                ? 'Capa enviada. Ela é compartilhada por todos os leitores deste livro.'
                : item.book.coverUrl
                  ? 'Capa obtida de uma fonte externa.'
                  : 'Este livro ainda não tem capa.'
            }}
          </p>
          <div class="cover-actions">
            <BaseButton variant="secondary" size="sm" @click="openCover">Trocar capa</BaseButton>
            <BaseButton
              v-if="hasUploadedCover"
              variant="ghost"
              size="sm"
              @click="confirmRemoveCover = true"
            >
              Remover capa
            </BaseButton>
          </div>
        </BaseCard>

        <BaseCard as="section" aria-labelledby="geo-title">
          <div class="panel-head">
            <h2 id="geo-title" class="panel-title">Geografia literária</h2>
            <BaseBadge
              :tone="
                item.book.geoEnrichmentStatus === 'Completed'
                  ? 'success'
                  : item.book.geoEnrichmentStatus === 'Failed'
                    ? 'danger'
                    : 'warning'
              "
            >
              {{ ENRICHMENT_LABELS[item.book.geoEnrichmentStatus] }}
            </BaseBadge>
          </div>
          <div v-if="canEnrich" class="enrich">
            <p class="muted">
              {{
                item.book.geoEnrichmentStatus === 'Failed'
                  ? 'As fontes (Wikidata) não responderam quando este livro foi cadastrado.'
                  : 'Alguns dados geográficos ainda não foram encontrados. O que não foi encontrado aparece como “Desconhecida”, não como ausência.'
              }}
            </p>
            <BaseButton size="sm" variant="secondary" :loading="enriching" @click="enrich">
              <AppIcon name="refresh" :size="16" /> Buscar dados novamente
            </BaseButton>
            <p class="muted enrich__progress" role="status" aria-live="polite">
              <template v-if="enriching">
                Buscando nacionalidade e origem da obra… isso pode levar alguns segundos
              </template>
            </p>
          </div>

          <h3 class="eyebrow geo-heading">Nacionalidade dos autores</h3>
          <ul role="list" class="geo-list">
            <li v-for="author in item.book.authors" :key="author.id" class="geo-row">
              <div class="geo-row__main">
                <strong>{{ author.name }}</strong>
                <div class="cluster geo-row__badges">
                  <GeoRelationBadge
                    v-for="n in author.nationalities"
                    :key="n.countryIso"
                    :relation="n"
                  />
                  <BaseBadge v-if="!author.nationalities.length">Desconhecida</BaseBadge>
                </div>
              </div>
              <BaseButton
                v-if="geoAction(author.nationalities.length > 0) !== 'locked'"
                size="sm"
                variant="ghost"
                :aria-label="`${geoAction(author.nationalities.length > 0) === 'inform' ? 'Informar país de' : 'Editar países de'} ${author.name}`"
                @click="openAuthor(author)"
              >
                {{
                  geoAction(author.nationalities.length > 0) === 'inform'
                    ? 'Informar país'
                    : 'Editar países'
                }}
              </BaseButton>
            </li>
            <li v-if="!item.book.authors.length" class="geo-row">
              <BaseBadge>Autoria desconhecida</BaseBadge>
            </li>
          </ul>

          <h3 class="eyebrow geo-heading">Origem da obra</h3>
          <div class="geo-row">
            <div class="cluster geo-row__badges">
              <GeoRelationBadge
                v-for="o in item.book.originCountries"
                :key="o.countryIso"
                :relation="o"
              />
              <BaseBadge v-if="!item.book.originCountries.length">Desconhecida</BaseBadge>
            </div>
            <BaseButton
              v-if="originsAction !== 'locked'"
              size="sm"
              variant="ghost"
              :aria-label="
                originsAction === 'inform'
                  ? 'Informar país de origem da obra'
                  : 'Editar países de origem da obra'
              "
              @click="openOrigins"
            >
              {{ originsAction === 'inform' ? 'Informar país' : 'Editar países' }}
            </BaseButton>
          </div>
          <p v-if="hasLockedGeo" class="geo-note geo-note--locked" data-testid="moderation-note">
            Somente moderadores podem alterar países já informados.
          </p>
          <p class="geo-note">
            Somente leituras concluídas acendem países no mapa. Dados desconhecidos não contam como
            “nenhum país”.
          </p>
        </BaseCard>

        <BaseCard as="section" aria-labelledby="reading-title">
          <h2 id="reading-title" class="panel-title">Sua leitura</h2>
          <form v-if="reading" class="reading-form" novalidate @submit.prevent="saveReading">
            <ReadingFields v-model="reading" :errors="readingErrors" />
            <div class="reading-form__actions">
              <BaseButton variant="danger" size="sm" @click="confirmRemove = true">
                <AppIcon name="trash" :size="16" /> Remover da biblioteca
              </BaseButton>
              <BaseButton type="submit" :loading="savingReading" :disabled="!readingDirty">
                Salvar leitura
              </BaseButton>
            </div>
          </form>
        </BaseCard>
      </div>
    </template>

    <ConfirmDialog
      v-model:open="confirmRemoveCover"
      title="Remover capa?"
      message="A capa enviada será removida deste livro para todos os leitores. Você pode enviar outra depois."
      confirm-label="Remover capa"
      danger
      :loading="removingCover"
      @confirm="removeCover"
    />

    <BaseModal
      v-model:open="coverOpen"
      variant="drawer"
      title="Trocar capa"
      :persistent="uploadingCover"
    >
      <div v-if="item" class="cover-form">
        <p class="geo-warning" role="note">
          <AppIcon name="info" :size="18" />
          <span>A capa é compartilhada por todos os leitores deste livro.</span>
        </p>
        <CoverPicker
          v-model:file="coverFile"
          :title="item.book.title"
          :author="item.book.authors[0]?.name"
          :current-url="item.book.coverUrl"
          :disabled="uploadingCover"
        />
        <p class="cover-form__status" role="status" aria-live="polite">
          <template v-if="uploadingCover"><BaseSpinner size="sm" /> Enviando capa…</template>
        </p>
        <p v-if="coverError" class="form-error" role="alert">{{ coverError }}</p>
        <BaseButton v-if="coverError && coverFile" size="sm" @click="uploadCover">
          Tentar enviar de novo
        </BaseButton>
      </div>
    </BaseModal>

    <ConfirmDialog
      v-model:open="confirmRemove"
      title="Remover da biblioteca?"
      :message="`“${item?.book.title ?? ''}” sairá da sua biblioteca, com status, nota, datas e resenha. O livro continua no catálogo do Atlas.`"
      confirm-label="Remover"
      danger
      :loading="removing"
      @confirm="remove"
    />

    <BaseModal v-model:open="geoOpen" :title="geoTitle">
      <div class="geo-form">
        <p class="geo-warning" role="note">
          <AppIcon name="info" :size="18" />
          <span>
            A correção é salva como <strong>manual e verificada</strong> e vale para o catálogo de
            <strong>todos os leitores</strong> do Atlas.
            <template v-if="geoModal?.kind === 'author'">
              Informe cidadanias — não o país de nascimento ou de residência.
            </template>
            <template v-else>
              Informe a origem literária da obra — não o local onde a história se passa nem o país
              de publicação.
            </template>
          </span>
        </p>
        <CountryPicker
          v-model="geoSelection"
          :label="geoModal?.kind === 'author' ? 'Países de cidadania' : 'Países de origem'"
        />
        <p v-if="geoError" class="form-error" role="alert">{{ geoError }}</p>
      </div>
      <template #footer>
        <BaseButton variant="ghost" :disabled="savingGeo" @click="geoOpen = false"
          >Cancelar</BaseButton
        >
        <BaseButton :loading="savingGeo" @click="saveGeo">Salvar correção</BaseButton>
      </template>
    </BaseModal>

    <BaseModal v-model:open="bookOpen" variant="drawer" title="Corrigir dados do livro">
      <form v-if="bookDraft" id="book-form" novalidate @submit.prevent="saveBook">
        <p class="geo-warning" role="note">
          <AppIcon name="info" :size="18" />
          <span>Os dados bibliográficos são compartilhados por todos os leitores deste livro.</span>
        </p>
        <p v-if="bookError" class="form-error" role="alert">{{ bookError }}</p>
        <BookFieldsForm v-model="bookDraft" :errors="bookErrors" />
      </form>
      <template #footer>
        <BaseButton variant="ghost" :disabled="savingBook" @click="bookOpen = false"
          >Cancelar</BaseButton
        >
        <BaseButton type="submit" form="book-form" :loading="savingBook">Salvar</BaseButton>
      </template>
    </BaseModal>
  </div>
</template>

<style scoped>
.center {
  display: grid;
  place-items: center;
  padding: var(--space-8);
}
.back {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  font-size: var(--text-sm);
  color: var(--color-text-muted);
  text-decoration: none;
}
.details {
  display: grid;
  grid-template-columns: 220px 1fr;
  gap: var(--space-7);
  align-items: start;
}
@media (max-width: 720px) {
  .details {
    grid-template-columns: 1fr;
    gap: var(--space-5);
  }
  .details :deep(.cover) {
    width: 150px;
  }
}
.details__body {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--space-3);
}
.details__subtitle {
  color: var(--color-text-muted);
  font-size: var(--text-lg);
}
.details__authors {
  font-size: var(--text-lg);
}
.details__description {
  max-width: 62ch;
  color: var(--color-text-muted);
}
.details__review {
  max-width: 62ch;
  padding-left: var(--space-4);
  border-left: 2px solid var(--color-brass);
  font-family: var(--font-display);
  font-style: italic;
  font-size: var(--text-lg);
}
.meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-3) var(--space-6);
  margin: var(--space-2) 0 0;
  font-size: var(--text-sm);
}
.meta dt {
  font-size: var(--text-xs);
  letter-spacing: var(--tracking-caps);
  text-transform: uppercase;
  color: var(--color-text-faint);
}
.meta dd {
  margin: 0;
}
.panels {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-5);
  align-items: start;
}
@media (max-width: 1000px) {
  .panels {
    grid-template-columns: 1fr;
  }
}
.panel-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
}
.panel-title {
  font-size: var(--text-xl);
}
.cover-actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
  margin-top: var(--space-3);
}
.cover-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.cover-form__status {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-sm);
  color: var(--color-text-muted);
}
.cover-form__status:empty {
  display: none;
}
.enrich__progress {
  flex-basis: 100%;
}
.enrich__progress:empty {
  display: none;
}
.enrich {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  margin-top: var(--space-4);
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-md);
  background: var(--color-warning-soft);
  font-size: var(--text-sm);
}
.geo-heading {
  margin-top: var(--space-5);
  margin-bottom: var(--space-2);
}
.geo-list {
  display: flex;
  flex-direction: column;
}
.geo-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  padding: var(--space-2) 0;
  border-bottom: 1px solid var(--color-hairline);
}
.geo-row__main {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  font-size: var(--text-sm);
}
.geo-row__badges {
  gap: var(--space-2);
}
.geo-note {
  margin-top: var(--space-4);
  font-size: var(--text-xs);
  color: var(--color-text-faint);
}
.reading-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
  margin-top: var(--space-4);
}
.reading-form__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: var(--space-3);
}
.geo-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.geo-warning {
  display: flex;
  gap: var(--space-2);
  align-items: flex-start;
  padding: var(--space-3);
  border-radius: var(--radius-md);
  background: var(--color-sky-soft);
  color: var(--color-text-muted);
  font-size: var(--text-sm);
  margin-bottom: var(--space-4);
}
.geo-form .geo-warning {
  margin-bottom: 0;
}
.form-error {
  padding: var(--space-3);
  border-radius: var(--radius-md);
  background: var(--color-danger-soft);
  color: var(--color-danger);
  font-size: var(--text-sm);
  margin-bottom: var(--space-3);
}
</style>
