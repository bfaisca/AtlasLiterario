<script setup lang="ts">
withDefaults(
  defineProps<{ as?: string; padding?: 'none' | 'sm' | 'md' | 'lg'; interactive?: boolean }>(),
  { as: 'section', padding: 'md', interactive: false },
)
</script>

<template>
  <component
    :is="as"
    class="card"
    :class="[`card--${padding}`, { 'card--interactive': interactive }]"
  >
    <slot />
  </component>
</template>

<style scoped>
.card {
  position: relative;
  background:
    linear-gradient(180deg, rgb(255 255 255 / 0.018), transparent 40%), var(--color-surface);
  border: 1px solid var(--color-hairline);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-sm);
}
.card--none {
  padding: 0;
}
.card--sm {
  padding: var(--space-3);
}
.card--md {
  padding: var(--space-5);
}
.card--lg {
  padding: var(--space-6);
}
.card--interactive {
  transition:
    border-color var(--duration-base) var(--ease-out),
    transform var(--duration-base) var(--ease-out),
    box-shadow var(--duration-base) var(--ease-out);
}
.card--interactive:hover {
  border-color: var(--color-border);
  transform: translateY(-2px);
  box-shadow: var(--shadow-md);
}
</style>
