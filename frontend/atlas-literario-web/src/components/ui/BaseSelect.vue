<script setup lang="ts" generic="T extends string | number">
import { useId } from 'vue'

defineOptions({ inheritAttrs: false })

withDefaults(
  defineProps<{
    label: string
    options: ReadonlyArray<{ value: T; label: string }>
    error?: string | null
    hint?: string
  }>(),
  { error: null },
)

const model = defineModel<T>()
const id = useId()
</script>

<template>
  <div class="field" :class="{ 'field--invalid': !!error }">
    <label :for="id" class="field__label">{{ label }}</label>
    <div class="select">
      <select
        :id="id"
        v-model="model"
        v-bind="$attrs"
        class="field__control select__control"
        :aria-invalid="!!error"
      >
        <option v-for="option in options" :key="String(option.value)" :value="option.value">
          {{ option.label }}
        </option>
      </select>
      <svg class="select__chevron" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
        <path d="m6 9 6 6 6-6" fill="none" stroke="currentColor" stroke-width="1.8" />
      </svg>
    </div>
    <p v-if="error" class="field__error" role="alert">{{ error }}</p>
    <p v-else-if="hint" class="field__hint">{{ hint }}</p>
  </div>
</template>

<style scoped src="./fieldStyles.css"></style>
<style scoped>
.select {
  position: relative;
}
.select__control {
  appearance: none;
  padding-right: 2.25rem;
  cursor: pointer;
}
.select__chevron {
  position: absolute;
  right: 0.8rem;
  top: 50%;
  transform: translateY(-50%);
  color: var(--color-text-faint);
  pointer-events: none;
}
</style>
