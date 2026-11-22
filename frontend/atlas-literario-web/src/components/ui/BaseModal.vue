<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, useId, watch } from 'vue'
import AppIcon from './AppIcon.vue'

/** Diálogo modal (`variant="dialog"`) ou painel lateral (`variant="drawer"`, base em mobile). */
const props = withDefaults(
  defineProps<{
    title: string
    variant?: 'dialog' | 'drawer'
    closeOnBackdrop?: boolean
    /** Bloqueia Esc, backdrop e botão de fechar (ex.: durante um envio em andamento). */
    persistent?: boolean
  }>(),
  { variant: 'dialog', closeOnBackdrop: true, persistent: false },
)

const open = defineModel<boolean>('open', { required: true })
const panel = ref<HTMLElement | null>(null)
const titleId = useId()
let previouslyFocused: HTMLElement | null = null

function close() {
  if (props.persistent) return
  open.value = false
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') close()
}

watch(
  open,
  async (isOpen) => {
    if (typeof document === 'undefined') return
    if (isOpen) {
      previouslyFocused = document.activeElement as HTMLElement | null
      document.addEventListener('keydown', onKeydown)
      document.body.style.overflow = 'hidden'
      await nextTick()
      panel.value?.focus()
    } else {
      document.removeEventListener('keydown', onKeydown)
      document.body.style.overflow = ''
      previouslyFocused?.focus?.()
    }
  },
  { immediate: true },
)

onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown)
  document.body.style.overflow = ''
})

function onBackdrop() {
  if (props.closeOnBackdrop) close()
}
</script>

<template>
  <Teleport to="body">
    <Transition :name="variant === 'drawer' ? 'drawer' : 'dialog'">
      <div v-if="open" class="overlay" :class="`overlay--${variant}`" @click.self="onBackdrop">
        <div
          ref="panel"
          class="panel"
          :class="`panel--${variant}`"
          role="dialog"
          aria-modal="true"
          :aria-labelledby="titleId"
          tabindex="-1"
        >
          <header class="panel__header">
            <h2 :id="titleId" class="panel__title">{{ title }}</h2>
            <button
              type="button"
              class="panel__close"
              aria-label="Fechar"
              :disabled="persistent"
              @click="close"
            >
              <AppIcon name="close" />
            </button>
          </header>
          <div class="panel__body"><slot /></div>
          <footer v-if="$slots.footer" class="panel__footer"><slot name="footer" /></footer>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: var(--z-modal);
  background: var(--color-overlay);
  backdrop-filter: blur(4px);
  display: flex;
}
.overlay--dialog {
  align-items: center;
  justify-content: center;
  padding: var(--space-4);
}
.overlay--drawer {
  justify-content: flex-end;
}
.panel {
  display: flex;
  flex-direction: column;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  box-shadow: var(--shadow-lg);
  outline: none;
  max-height: 100%;
}
.panel--dialog {
  width: min(520px, 100%);
  max-height: calc(100dvh - 2 * var(--space-4));
  border-radius: var(--radius-xl);
}
.panel--drawer {
  width: min(440px, 100%);
  height: 100%;
  border-radius: var(--radius-xl) 0 0 var(--radius-xl);
}
@media (max-width: 640px) {
  .overlay--drawer {
    align-items: flex-end;
  }
  .panel--drawer {
    width: 100%;
    height: auto;
    max-height: 85dvh;
    border-radius: var(--radius-xl) var(--radius-xl) 0 0;
  }
}
.panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  padding: var(--space-5) var(--space-5) var(--space-3);
}
.panel__title {
  font-size: var(--text-xl);
}
.panel__close {
  color: var(--color-text-muted);
  padding: var(--space-1);
  border-radius: var(--radius-sm);
}
.panel__close:hover {
  color: var(--color-text);
}
.panel__body {
  padding: 0 var(--space-5) var(--space-5);
  overflow-y: auto;
}
.panel__footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-3);
  padding: var(--space-4) var(--space-5);
  border-top: 1px solid var(--color-hairline);
}
.dialog-enter-active,
.dialog-leave-active,
.drawer-enter-active,
.drawer-leave-active {
  transition: opacity var(--duration-base) var(--ease-out);
}
.dialog-enter-active .panel,
.dialog-leave-active .panel,
.drawer-enter-active .panel,
.drawer-leave-active .panel {
  transition: transform var(--duration-slow) var(--ease-out);
}
.dialog-enter-from,
.dialog-leave-to,
.drawer-enter-from,
.drawer-leave-to {
  opacity: 0;
}
.dialog-enter-from .panel,
.dialog-leave-to .panel {
  transform: translateY(12px) scale(0.98);
}
.drawer-enter-from .panel,
.drawer-leave-to .panel {
  transform: translateX(24px);
}
@media (max-width: 640px) {
  .drawer-enter-from .panel,
  .drawer-leave-to .panel {
    transform: translateY(24px);
  }
}
</style>
