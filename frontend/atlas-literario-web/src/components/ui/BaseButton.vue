<script setup lang="ts">
import { computed } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import BaseSpinner from './BaseSpinner.vue'

const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
    size?: 'sm' | 'md' | 'lg'
    type?: 'button' | 'submit' | 'reset'
    loading?: boolean
    disabled?: boolean
    block?: boolean
    /** Renderiza como RouterLink */
    to?: RouteLocationRaw
  }>(),
  { variant: 'primary', size: 'md', type: 'button', loading: false, disabled: false, block: false },
)

const isDisabled = computed(() => props.disabled || props.loading)
const classes = computed(() => [
  'btn',
  `btn--${props.variant}`,
  `btn--${props.size}`,
  { 'btn--block': props.block, 'btn--loading': props.loading },
])
</script>

<template>
  <RouterLink v-if="to && !isDisabled" :to="to" :class="classes">
    <span class="btn__label"><slot /></span>
  </RouterLink>
  <button v-else :type="type" :class="classes" :disabled="isDisabled" :aria-busy="loading">
    <BaseSpinner v-if="loading" size="sm" class="btn__spinner" />
    <span class="btn__label"><slot /></span>
  </button>
</template>

<style scoped>
.btn {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  border-radius: var(--radius-pill);
  border: 1px solid transparent;
  font-weight: 600;
  letter-spacing: 0.01em;
  text-decoration: none;
  white-space: nowrap;
  transition:
    background-color var(--duration-fast) var(--ease-out),
    border-color var(--duration-fast) var(--ease-out),
    color var(--duration-fast) var(--ease-out),
    box-shadow var(--duration-base) var(--ease-out),
    transform var(--duration-fast) var(--ease-out);
}

.btn:active:not(:disabled) {
  transform: translateY(1px);
}

.btn:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.btn--sm {
  padding: 0.375rem 0.875rem;
  font-size: var(--text-sm);
}
.btn--md {
  padding: 0.625rem 1.25rem;
  font-size: var(--text-sm);
}
.btn--lg {
  padding: 0.8rem 1.6rem;
  font-size: var(--text-base);
}
.btn--block {
  display: flex;
  width: 100%;
}

.btn--primary {
  background: var(--color-brass);
  color: var(--color-text-inverse);
}
.btn--primary:hover:not(:disabled) {
  background: var(--color-brass-strong);
  color: var(--color-text-inverse);
  box-shadow: var(--glow-brass);
}

.btn--secondary {
  background: var(--color-surface-2);
  border-color: var(--color-border);
  color: var(--color-text);
}
.btn--secondary:hover:not(:disabled) {
  border-color: var(--color-border-strong);
  background: var(--color-surface-3);
  color: var(--color-text);
}

.btn--ghost {
  color: var(--color-text-muted);
}
.btn--ghost:hover:not(:disabled) {
  color: var(--color-text);
  background: var(--color-surface-2);
}

.btn--danger {
  background: var(--color-danger-soft);
  border-color: rgb(242 155 143 / 0.35);
  color: var(--color-danger);
}
.btn--danger:hover:not(:disabled) {
  background: rgb(242 155 143 / 0.22);
}

.btn__label {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
}
.btn--loading .btn__label {
  opacity: 0.6;
}
</style>
