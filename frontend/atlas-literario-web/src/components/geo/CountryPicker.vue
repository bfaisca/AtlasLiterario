<script setup lang="ts">
import { computed, onMounted, ref, useId } from 'vue'
import { useCountriesStore } from '@/stores/countries'
import AppIcon from '@/components/ui/AppIcon.vue'

/**
 * Seletor múltiplo de países (ISO alpha-3). Busca por nome em português (ou inglês/ISO),
 * teclado: ↑/↓ navegam, Enter alterna, Backspace com busca vazia remove o último.
 */
const props = withDefaults(
  defineProps<{ label: string; hint?: string; placeholder?: string; error?: string | null }>(),
  { placeholder: 'Digite o nome de um país…', error: null },
)

const model = defineModel<string[]>({ default: () => [] })
const countries = useCountriesStore()
const id = useId()
const term = ref('')
const active = ref(0)

onMounted(() => void countries.load())

const results = computed(() => countries.search(term.value, 8))
const selected = computed(() => model.value.map((iso) => ({ iso, name: countries.nameOf(iso) })))

function toggle(iso: string) {
  model.value = model.value.includes(iso)
    ? model.value.filter((i) => i !== iso)
    : [...model.value, iso]
}

function choose(iso: string) {
  toggle(iso)
  term.value = ''
  active.value = 0
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'ArrowDown') {
    active.value = Math.min(active.value + 1, results.value.length - 1)
    event.preventDefault()
  } else if (event.key === 'ArrowUp') {
    active.value = Math.max(active.value - 1, 0)
    event.preventDefault()
  } else if (event.key === 'Enter') {
    const country = results.value[active.value]
    if (country) choose(country.iso)
    event.preventDefault() // nunca submete o formulário a partir da busca
  } else if (event.key === 'Backspace' && !term.value && model.value.length) {
    model.value = model.value.slice(0, -1)
  } else if (event.key === 'Escape') {
    term.value = ''
  }
}
</script>

<template>
  <div class="picker" :class="{ 'picker--invalid': !!props.error }">
    <label :for="id" class="picker__label">{{ label }}</label>

    <ul
      v-if="selected.length"
      role="list"
      class="picker__chips"
      :aria-label="`${label}: selecionados`"
    >
      <li v-for="country in selected" :key="country.iso" class="picker__chip">
        <span>{{ country.name }}</span>
        <button type="button" :aria-label="`Remover ${country.name}`" @click="toggle(country.iso)">
          <AppIcon name="close" :size="14" />
        </button>
      </li>
    </ul>
    <p v-else class="picker__empty">Nenhum país selecionado — fica como desconhecido.</p>

    <input
      :id="id"
      v-model="term"
      type="search"
      class="picker__input"
      role="combobox"
      autocomplete="off"
      :placeholder="placeholder"
      :aria-expanded="results.length > 0"
      :aria-controls="`${id}-list`"
      :aria-activedescendant="results.length ? `${id}-opt-${active}` : undefined"
      @input="active = 0"
      @keydown="onKeydown"
    />
    <ul
      v-if="results.length"
      :id="`${id}-list`"
      role="listbox"
      aria-multiselectable="true"
      class="picker__list"
    >
      <li
        v-for="(country, index) in results"
        :id="`${id}-opt-${index}`"
        :key="country.iso"
        role="option"
        class="picker__option"
        :class="{ 'is-active': index === active }"
        :aria-selected="model.includes(country.iso)"
        @mousedown.prevent="choose(country.iso)"
        @mouseenter="active = index"
      >
        <span class="picker__check" aria-hidden="true">{{
          model.includes(country.iso) ? '✓' : ''
        }}</span>
        <span>{{ country.namePt }}</span>
        <span class="picker__iso">{{ country.iso }}</span>
      </li>
    </ul>
    <p v-else-if="term && !countries.loading" class="picker__hint">Nenhum país encontrado.</p>

    <p v-if="props.error" class="picker__error" role="alert">{{ props.error }}</p>
    <p v-else-if="hint" class="picker__hint">{{ hint }}</p>
  </div>
</template>

<style scoped>
.picker {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.picker__label {
  font-size: var(--text-sm);
  font-weight: 500;
  color: var(--color-text-muted);
}
.picker__chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.picker__chip {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  padding: 0.2rem 0.3rem 0.2rem 0.7rem;
  border-radius: var(--radius-pill);
  background: var(--color-sky-soft);
  border: 1px solid rgb(156 202 245 / 0.3);
  color: var(--color-sky);
  font-size: var(--text-sm);
}
.picker__chip button {
  display: grid;
  place-items: center;
  width: 1.4rem;
  height: 1.4rem;
  border-radius: 50%;
  color: inherit;
}
.picker__chip button:hover {
  background: rgb(156 202 245 / 0.2);
}
.picker__empty,
.picker__hint {
  font-size: var(--text-xs);
  color: var(--color-text-faint);
}
.picker__input {
  width: 100%;
  padding: 0.7rem 0.9rem;
  background: var(--color-ocean);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
}
.picker__input:focus {
  outline: none;
  border-color: var(--color-sky);
  box-shadow: 0 0 0 3px var(--color-sky-soft);
}
.picker--invalid .picker__input {
  border-color: var(--color-danger);
}
.picker__list {
  list-style: none;
  padding: var(--space-1);
  margin: 0;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  background: var(--color-surface-2);
  max-height: 16rem;
  overflow-y: auto;
}
.picker__option {
  display: grid;
  grid-template-columns: 1.2rem 1fr auto;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  cursor: pointer;
  font-size: var(--text-sm);
}
.picker__option.is-active {
  background: var(--color-surface-3);
}
.picker__check {
  color: var(--color-brass);
}
.picker__iso {
  font-family: var(--font-mono);
  font-size: var(--text-xs);
  color: var(--color-text-faint);
}
.picker__error {
  font-size: var(--text-xs);
  color: var(--color-danger);
}
</style>
