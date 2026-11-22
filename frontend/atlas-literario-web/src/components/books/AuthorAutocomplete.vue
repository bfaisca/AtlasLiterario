<script setup lang="ts">
/**
 * Campo de autor com autocomplete (combobox ARIA 1.2, lista com `aria-activedescendant`).
 * - `modelValue`: texto do campo; `authorId`: id do autor escolhido (null = texto livre).
 *   Toda mudança sai num único evento `change` ({ name, authorId }) para que pai com listas
 *   paralelas (nomes/ids) atualize os dois de forma atômica.
 * - Digitar sempre desfaz a escolha; escolher uma sugestão fixa o autor por id.
 * - Sugestões vêm de `GET /authors?q=` (backend); falhas viram mensagem amigável, sem bloquear
 *   o preenchimento manual.
 */
import { computed, ref, useId, watch } from 'vue'
import AppIcon from '@/components/ui/AppIcon.vue'
import { AUTHOR_SEARCH_MIN_LENGTH, useAuthorSearch } from '@/composables/useAuthorSearch'
import type { AuthorSuggestionDto } from '@/types/api'

const props = withDefaults(
  defineProps<{
    label: string
    modelValue: string
    authorId?: string | null
    error?: string | null
  }>(),
  { authorId: null, error: null },
)
const emit = defineEmits<{ change: [value: { name: string; authorId: string | null }] }>()

const text = computed(() => props.modelValue)
const authorId = computed(() => props.authorId)

const uid = useId()
const listboxId = `${uid}-listbox`
const statusId = `${uid}-status`
const optionId = (index: number) => `${uid}-option-${index}`

const { suggestions, loading, error: searchError, hasNoResults, search, clear } = useAuthorSearch()

const open = ref(false)
const activeIndex = ref(-1)
/** Nacionalidades da sugestão escolhida (só para exibir; o id é a verdade). */
const pinned = ref<AuthorSuggestionDto | null>(null)

const hasQuery = computed(() => text.value.trim().length >= AUTHOR_SEARCH_MIN_LENGTH)
const expanded = computed(
  () =>
    open.value &&
    !authorId.value &&
    hasQuery.value &&
    (suggestions.value.length > 0 || hasNoResults.value || !!searchError.value || loading.value),
)
const showOptions = computed(() => expanded.value && suggestions.value.length > 0)
const activeDescendant = computed(() =>
  showOptions.value && activeIndex.value >= 0 ? optionId(activeIndex.value) : undefined,
)

const statusMessage = computed(() => {
  if (!expanded.value) return ''
  if (searchError.value) return searchError.value
  if (loading.value) return 'Buscando autores…'
  if (hasNoResults.value)
    return 'Nenhum autor cadastrado com esse nome. O nome digitado será usado.'
  const n = suggestions.value.length
  return `${n} ${n === 1 ? 'sugestão disponível' : 'sugestões disponíveis'}.`
})

watch(suggestions, () => {
  activeIndex.value = -1
})
// Se o pai trocar o id (ex.: remover outro autor reaproveita a linha), o rótulo antigo não vale.
const pinnedLabel = computed(() =>
  pinned.value && pinned.value.id === authorId.value ? nationalitiesLabel(pinned.value) : null,
)

function nationalitiesLabel(suggestion: Pick<AuthorSuggestionDto, 'nationalityNames'>): string {
  return suggestion.nationalityNames.length
    ? suggestion.nationalityNames.join(', ')
    : 'país desconhecido'
}

function onInput(event: Event) {
  const value = (event.target as HTMLInputElement).value
  emit('change', { name: value, authorId: null })
  pinned.value = null
  open.value = true
  search(value)
}

function choose(suggestion: AuthorSuggestionDto) {
  emit('change', { name: suggestion.name, authorId: suggestion.id })
  pinned.value = suggestion
  open.value = false
  activeIndex.value = -1
  clear()
}

function unpin() {
  emit('change', { name: text.value, authorId: null })
  pinned.value = null
}

function close() {
  open.value = false
  activeIndex.value = -1
}

function move(delta: 1 | -1) {
  const count = suggestions.value.length
  if (!count) return
  open.value = true
  const current = activeIndex.value
  activeIndex.value = delta === 1 ? (current + 1) % count : current <= 0 ? count - 1 : current - 1
}

function onKeydown(event: KeyboardEvent) {
  switch (event.key) {
    case 'ArrowDown':
      if (showOptions.value || suggestions.value.length) {
        event.preventDefault()
        move(1)
      }
      break
    case 'ArrowUp':
      if (showOptions.value || suggestions.value.length) {
        event.preventDefault()
        move(-1)
      }
      break
    case 'Enter': {
      // Com uma opção ativa, Enter escolhe (e não envia o formulário); sem ela, segue o fluxo normal.
      const active = showOptions.value ? suggestions.value[activeIndex.value] : undefined
      if (active) {
        event.preventDefault()
        choose(active)
      }
      break
    }
    case 'Escape':
      // Só consome o Esc se havia algo para fechar (senão o modal pai fecha normalmente).
      if (expanded.value) {
        event.stopPropagation()
        close()
      }
      break
    case 'Tab':
      close()
      break
  }
}

function onFocus() {
  if (!authorId.value && hasQuery.value) open.value = true
}
</script>

<template>
  <div class="ac field" :class="{ 'field--invalid': !!props.error }">
    <label :for="`${uid}-input`" class="field__label">{{ props.label }}</label>
    <div class="ac__wrapper">
      <input
        :id="`${uid}-input`"
        class="field__control"
        type="text"
        role="combobox"
        autocomplete="off"
        aria-autocomplete="list"
        :aria-expanded="showOptions"
        :aria-controls="listboxId"
        :aria-activedescendant="activeDescendant"
        :aria-invalid="!!props.error"
        :aria-describedby="[statusId, props.error ? `${uid}-error` : ''].filter(Boolean).join(' ')"
        :value="text"
        @input="onInput"
        @keydown="onKeydown"
        @focus="onFocus"
        @blur="close"
      />
      <!-- mousedown.prevent mantém o foco no input ao clicar numa opção (o blur não fecha antes do clique). -->
      <ul
        v-show="showOptions"
        :id="listboxId"
        class="ac__list"
        role="listbox"
        :aria-label="`Sugestões de ${props.label.toLowerCase()}`"
        @mousedown.prevent
      >
        <li
          v-for="(suggestion, index) in suggestions"
          :id="optionId(index)"
          :key="suggestion.id"
          class="ac__option"
          :class="{ 'ac__option--active': index === activeIndex }"
          role="option"
          :aria-selected="index === activeIndex"
          @click="choose(suggestion)"
          @mousemove="activeIndex = index"
        >
          <span class="ac__name">{{ suggestion.name }}</span>
          <span class="ac__meta">{{ nationalitiesLabel(suggestion) }}</span>
        </li>
      </ul>
    </div>

    <!-- Região viva: anuncia busca, quantidade, vazio e erro sem roubar o foco. -->
    <p
      :id="statusId"
      class="ac__status"
      :class="{ 'ac__status--error': !!searchError && expanded }"
      role="status"
      aria-live="polite"
    >
      {{ statusMessage }}
    </p>

    <p v-if="authorId" class="ac__pinned" data-testid="author-pinned">
      <AppIcon name="check" :size="14" />
      <span>
        Autor já cadastrado
        <template v-if="pinnedLabel"> · {{ pinnedLabel }}</template>
      </span>
      <button type="button" class="ac__unpin" @click="unpin">Usar outro nome</button>
    </p>

    <p v-if="props.error" :id="`${uid}-error`" class="field__error" role="alert">
      {{ props.error }}
    </p>
  </div>
</template>

<style scoped src="../ui/fieldStyles.css"></style>
<style scoped>
.ac__wrapper {
  position: relative;
}
.ac__list {
  position: absolute;
  z-index: var(--z-overlay);
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  max-height: 16rem;
  overflow-y: auto;
  padding: var(--space-1);
  margin: 0;
  list-style: none;
  background: var(--color-surface-2);
  border: 1px solid var(--color-border-strong);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg);
}
.ac__option {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  cursor: pointer;
}
.ac__option--active {
  background: var(--color-surface-3);
}
.ac__name {
  font-size: var(--text-sm);
  color: var(--color-text);
}
.ac__meta {
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.ac__status {
  min-height: 0;
  font-size: var(--text-xs);
  color: var(--color-text-faint);
}
.ac__status:empty {
  display: none;
}
.ac__status--error {
  color: var(--color-danger);
}
.ac__pinned {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-xs);
  color: var(--color-success);
}
.ac__unpin {
  color: var(--color-sky);
  font-size: var(--text-xs);
  text-decoration: underline;
}
@media (prefers-reduced-motion: no-preference) {
  .ac__option {
    transition: background-color var(--duration-fast) var(--ease-out);
  }
}
</style>
