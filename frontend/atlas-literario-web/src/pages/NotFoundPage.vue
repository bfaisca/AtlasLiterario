<script setup lang="ts">
import BaseButton from '@/components/ui/BaseButton.vue'
</script>

<template>
  <div class="not-found">
    <p class="eyebrow">Erro 404</p>
    <h1>Terra incógnita</h1>
    <p class="muted">Esta página não consta em nenhum mapa conhecido.</p>
    <BaseButton :to="{ name: 'dashboard' }">Voltar ao início</BaseButton>
  </div>
</template>

<style scoped>
.not-found {
  min-height: 100dvh;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--space-4);
  padding: var(--space-6);
  text-align: center;
}
.not-found h1 {
  font-size: var(--text-display);
  font-style: italic;
}
</style>
