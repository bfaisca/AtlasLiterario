<script setup lang="ts">
import { useId } from 'vue'

defineOptions({ inheritAttrs: false })

withDefaults(
  defineProps<{
    label: string
    error?: string | null
    hint?: string
    rows?: number
    maxlength?: number
  }>(),
  { error: null, rows: 4 },
)

const model = defineModel<string>({ default: '' })
const id = useId()
</script>

<template>
  <div class="field" :class="{ 'field--invalid': !!error }">
    <label :for="id" class="field__label">{{ label }}</label>
    <textarea
      :id="id"
      v-model="model"
      v-bind="$attrs"
      class="field__control textarea"
      :rows="rows"
      :maxlength="maxlength"
      :aria-invalid="!!error"
      :aria-describedby="error ? `${id}-error` : hint ? `${id}-hint` : undefined"
    />
    <p v-if="error" :id="`${id}-error`" class="field__error" role="alert">{{ error }}</p>
    <p v-else-if="hint" :id="`${id}-hint`" class="field__hint">{{ hint }}</p>
  </div>
</template>

<style scoped src="./fieldStyles.css"></style>
<style scoped>
.textarea {
  resize: vertical;
  min-height: 6rem;
  line-height: var(--leading-normal);
}
</style>
