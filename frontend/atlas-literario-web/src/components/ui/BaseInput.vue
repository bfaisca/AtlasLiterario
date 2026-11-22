<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import AppIcon from './AppIcon.vue'

defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    label: string
    type?: 'text' | 'email' | 'password' | 'search' | 'number' | 'date' | 'url'
    error?: string | null
    hint?: string
    autocomplete?: string
    required?: boolean
    /** Em campos de senha, exibe o botão mostrar/ocultar (padrão: true). */
    revealable?: boolean
  }>(),
  { type: 'text', error: null, revealable: true },
)

const model = defineModel<string>({ default: '' })
const id = useId()

const isPassword = computed(() => props.type === 'password')
const canReveal = computed(() => isPassword.value && props.revealable)
const revealed = ref(false)
const inputType = computed(() => (isPassword.value && revealed.value ? 'text' : props.type))
</script>

<template>
  <div class="field" :class="{ 'field--invalid': !!error }">
    <label :for="id" class="field__label">{{ label }}</label>
    <div class="field__wrapper">
      <input
        :id="id"
        v-model="model"
        v-bind="$attrs"
        class="field__control"
        :class="{ 'field__control--with-action': canReveal }"
        :type="inputType"
        :autocomplete="autocomplete"
        :required="required"
        :aria-invalid="!!error"
        :aria-describedby="error ? `${id}-error` : hint ? `${id}-hint` : undefined"
      />
      <button
        v-if="canReveal"
        type="button"
        class="field__action"
        :aria-label="revealed ? 'Ocultar senha' : 'Mostrar senha'"
        :aria-pressed="revealed"
        :aria-controls="id"
        @click="revealed = !revealed"
      >
        <AppIcon :name="revealed ? 'eyeOff' : 'eye'" :size="18" />
      </button>
    </div>
    <p v-if="error" :id="`${id}-error`" class="field__error" role="alert">{{ error }}</p>
    <p v-else-if="hint" :id="`${id}-hint`" class="field__hint">{{ hint }}</p>
  </div>
</template>

<style scoped src="./fieldStyles.css"></style>
<style scoped>
.field__wrapper {
  position: relative;
}
.field__control--with-action {
  padding-right: 2.75rem;
}
.field__action {
  position: absolute;
  top: 50%;
  right: 0.4rem;
  transform: translateY(-50%);
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  border-radius: var(--radius-sm);
  color: var(--color-text-faint);
  transition: color var(--duration-fast) var(--ease-out);
}
.field__action:hover,
.field__action[aria-pressed='true'] {
  color: var(--color-text);
}
</style>
