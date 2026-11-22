<script setup lang="ts">
withDefaults(defineProps<{ size?: 'sm' | 'md' | 'lg'; label?: string }>(), {
  size: 'md',
  label: 'Carregando',
})
</script>

<template>
  <span class="spinner" :class="`spinner--${size}`" role="status">
    <span class="sr-only">{{ label }}</span>
  </span>
</template>

<style scoped>
.spinner {
  display: inline-block;
  border-radius: 50%;
  border: 2px solid var(--color-border);
  border-top-color: var(--color-brass);
  animation: spin 0.8s linear infinite;
  flex-shrink: 0;
}
.spinner--sm {
  width: 14px;
  height: 14px;
}
.spinner--md {
  width: 22px;
  height: 22px;
}
.spinner--lg {
  width: 36px;
  height: 36px;
  border-width: 3px;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .spinner {
    animation-duration: 2.4s;
  }
}
</style>
