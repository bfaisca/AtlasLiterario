<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useUiStore } from '@/stores/ui'
import AppIcon from './AppIcon.vue'
import type { IconName } from './icons'

const ui = useUiStore()
const { toasts } = storeToRefs(ui)

const ICON_BY_KIND: Record<string, IconName> = {
  success: 'check',
  error: 'alert',
  info: 'info',
  warning: 'alert',
}
</script>

<template>
  <div class="toasts" aria-live="polite" aria-atomic="false">
    <TransitionGroup name="toast">
      <div
        v-for="toast in toasts"
        :key="toast.id"
        class="toast"
        :class="`toast--${toast.kind}`"
        :role="toast.kind === 'error' ? 'alert' : 'status'"
      >
        <AppIcon :name="ICON_BY_KIND[toast.kind] ?? 'info'" :size="18" class="toast__icon" />
        <p class="toast__message">{{ toast.message }}</p>
        <button
          class="toast__close"
          type="button"
          aria-label="Fechar aviso"
          @click="ui.dismiss(toast.id)"
        >
          <AppIcon name="close" :size="16" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toasts {
  position: fixed;
  z-index: var(--z-toast);
  right: var(--space-4);
  bottom: var(--space-4);
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  width: min(380px, calc(100vw - 2 * var(--space-4)));
  pointer-events: none;
}
@media (max-width: 899px) {
  .toasts {
    bottom: calc(var(--bottom-nav-height) + var(--space-3) + env(safe-area-inset-bottom));
  }
}
.toast {
  --toast-color: var(--color-sky);
  pointer-events: auto;
  display: flex;
  align-items: flex-start;
  gap: var(--space-3);
  padding: var(--space-3) var(--space-3) var(--space-3) var(--space-4);
  background: var(--color-surface-2);
  border: 1px solid var(--color-border);
  border-left: 3px solid var(--toast-color);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg);
}
.toast--success {
  --toast-color: var(--color-success);
}
.toast--warning {
  --toast-color: var(--color-warning);
}
.toast--error {
  --toast-color: var(--color-danger);
}
.toast__icon {
  color: var(--toast-color);
  margin-top: 2px;
}
.toast__message {
  flex: 1;
  font-size: var(--text-sm);
}
.toast__close {
  color: var(--color-text-faint);
  padding: 2px;
  border-radius: var(--radius-sm);
}
.toast__close:hover {
  color: var(--color-text);
}
.toast-enter-active,
.toast-leave-active {
  transition:
    opacity var(--duration-base) var(--ease-out),
    transform var(--duration-base) var(--ease-out);
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
