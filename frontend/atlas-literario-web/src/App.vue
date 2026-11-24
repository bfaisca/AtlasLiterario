<script setup lang="ts">
import { apiWaking } from '@/api/warmup'
import ToastHost from '@/components/ui/ToastHost.vue'
</script>

<template>
  <div v-if="apiWaking" class="api-waking" role="status" aria-live="polite">
    Acordando o servidor… no plano gratuito isso pode levar cerca de 1 minuto.
  </div>
  <RouterView />
  <ToastHost />
</template>

<style scoped>
.api-waking {
  position: fixed;
  z-index: var(--z-toast);
  top: var(--space-3);
  left: 50%;
  transform: translateX(-50%);
  max-width: calc(100vw - 2 * var(--space-4));
  padding: var(--space-2) var(--space-4);
  border-radius: 999px;
  background: rgb(20 30 50 / 0.95);
  border: 1px solid rgb(255 255 255 / 0.15);
  color: #fff;
  font-size: 0.875rem;
  text-align: center;
}
</style>
