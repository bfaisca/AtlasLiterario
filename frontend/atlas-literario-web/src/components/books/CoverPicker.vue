<script setup lang="ts">
/**
 * Seletor de capa: escolher arquivo (ou arrastar e soltar), reduzir no cliente, pré-visualizar.
 * `v-model:file` recebe o arquivo JÁ preparado (≤ 2 MB), ou null. Não envia nada: quem usa decide
 * quando. Remover aqui só descarta a seleção (volta à capa atual, se houver).
 */
import { computed, onBeforeUnmount, ref, useId, watch } from 'vue'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import { ACCEPT_ATTRIBUTE, CoverError, prepareCover } from '@/utils/imageResize'
import BookCover from './BookCover.vue'

const props = withDefaults(
  defineProps<{
    title: string
    author?: string
    /** Capa já existente, exibida enquanto nada foi selecionado. */
    currentUrl?: string | null
    disabled?: boolean
    /** Substituível em testes. */
    prepare?: (file: File) => Promise<File>
  }>(),
  { currentUrl: null, disabled: false, prepare: prepareCover },
)

const file = defineModel<File | null>('file', { default: null })
const emit = defineEmits<{ error: [message: string] }>()

const input = ref<HTMLInputElement | null>(null)
const processing = ref(false)
const message = ref<string | null>(null)
const dragging = ref(false)
const previewUrl = ref<string | null>(null)
const messageId = useId()
let dragDepth = 0

const busy = computed(() => processing.value || props.disabled)
const shownUrl = computed(() => previewUrl.value ?? props.currentUrl)

function revoke() {
  if (previewUrl.value && typeof URL.revokeObjectURL === 'function') {
    URL.revokeObjectURL(previewUrl.value)
  }
  previewUrl.value = null
}

watch(
  file,
  (next) => {
    revoke()
    if (next && typeof URL.createObjectURL === 'function') {
      previewUrl.value = URL.createObjectURL(next)
    }
  },
  { immediate: true },
)
onBeforeUnmount(revoke)

function describeSize(bytes: number): string {
  return bytes >= 1024 * 1024
    ? `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`
    : `${Math.max(1, Math.round(bytes / 1024))} kB`
}

async function handleFile(candidate: File | null | undefined) {
  if (!candidate || busy.value) return
  processing.value = true
  message.value = null
  try {
    file.value = await props.prepare(candidate)
  } catch (error) {
    const text =
      error instanceof CoverError
        ? error.message
        : 'Não foi possível processar esta imagem. Tente outro arquivo.'
    message.value = text
    emit('error', text)
  } finally {
    processing.value = false
  }
}

function onInputChange(event: Event) {
  const target = event.target as HTMLInputElement
  const picked = target.files?.[0]
  target.value = '' // permite escolher o mesmo arquivo de novo
  void handleFile(picked)
}

function onDrop(event: DragEvent) {
  dragging.value = false
  dragDepth = 0
  void handleFile(event.dataTransfer?.files?.[0])
}

function onDragEnter() {
  dragDepth++
  dragging.value = true
}
function onDragLeave() {
  dragDepth = Math.max(0, dragDepth - 1)
  if (dragDepth === 0) dragging.value = false
}

function clear() {
  file.value = null
  message.value = null
}
</script>

<template>
  <div
    class="picker"
    :class="{ 'is-dragging': dragging, 'is-busy': busy }"
    data-testid="cover-picker"
    @dragenter.prevent="onDragEnter"
    @dragover.prevent
    @dragleave.prevent="onDragLeave"
    @drop.prevent="onDrop"
  >
    <div class="picker__cover">
      <BookCover :title="title" :author="author" :cover-url="shownUrl" size="md" />
    </div>
    <div class="picker__side">
      <p class="picker__hint">
        Arraste uma imagem para cá ou escolha um arquivo. JPEG, PNG ou WebP; a imagem é reduzida
        automaticamente.
      </p>
      <div class="picker__actions">
        <BaseButton
          variant="secondary"
          class="picker__button"
          :disabled="busy"
          :aria-describedby="messageId"
          @click="input?.click()"
        >
          <AppIcon name="plus" :size="16" />
          {{ file ? 'Escolher outra imagem' : 'Escolher imagem' }}
        </BaseButton>
        <BaseButton
          v-if="file"
          variant="ghost"
          class="picker__button"
          :disabled="busy"
          @click="clear"
        >
          <AppIcon name="trash" :size="16" /> Remover
        </BaseButton>
      </div>
      <input
        ref="input"
        type="file"
        class="sr-only"
        :accept="ACCEPT_ATTRIBUTE"
        tabindex="-1"
        aria-hidden="true"
        :disabled="busy"
        data-testid="cover-input"
        @change="onInputChange"
      />
      <p :id="messageId" class="picker__status" role="status" aria-live="polite">
        <template v-if="processing">
          <BaseSpinner size="sm" /> <span>Preparando imagem…</span>
        </template>
        <template v-else-if="message">
          <span class="picker__error" role="alert">{{ message }}</span>
        </template>
        <template v-else-if="file">
          <span>{{ file.name }} · {{ describeSize(file.size) }}</span>
        </template>
      </p>
    </div>
  </div>
</template>

<style scoped>
.picker {
  display: flex;
  gap: var(--space-4);
  align-items: flex-start;
  padding: var(--space-3);
  border-radius: var(--radius-lg);
  border: 1px dashed var(--color-border);
  transition:
    border-color var(--duration-base) var(--ease-out),
    background-color var(--duration-base) var(--ease-out);
}
.picker.is-dragging {
  border-color: var(--color-sky);
  background: var(--color-surface-2);
}
.picker__cover {
  flex: none;
  width: 112px;
}
.picker__side {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  min-width: 0;
}
.picker__hint {
  font-size: var(--text-sm);
  color: var(--color-text-muted);
}
.picker__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.picker__button {
  min-height: 44px;
}
.picker__status {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-sm);
  color: var(--color-text-muted);
  overflow-wrap: anywhere;
}
.picker__status:empty {
  display: none;
}
.picker__error {
  color: var(--color-danger);
}
@media (max-width: 480px) {
  .picker {
    flex-direction: column;
    align-items: stretch;
  }
  .picker__cover {
    align-self: center;
  }
}
</style>
