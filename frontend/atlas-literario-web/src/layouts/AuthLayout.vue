<script setup lang="ts">
import { USE_MOCKS } from '@/api'
</script>

<template>
  <div class="auth">
    <!-- Fundo: gratícula de globo e estrelas sutis (decorativo) -->
    <svg
      class="auth__sky"
      viewBox="0 0 800 800"
      aria-hidden="true"
      preserveAspectRatio="xMidYMid slice"
    >
      <defs>
        <radialGradient id="globe-glow" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="#1f4e8c" stop-opacity="0.35" />
          <stop offset="100%" stop-color="#1f4e8c" stop-opacity="0" />
        </radialGradient>
      </defs>
      <circle cx="400" cy="400" r="380" fill="url(#globe-glow)" />
      <g fill="none" stroke="#9ccaf5" stroke-opacity="0.09" stroke-width="1">
        <circle cx="400" cy="400" r="300" />
        <ellipse cx="400" cy="400" rx="300" ry="100" />
        <ellipse cx="400" cy="400" rx="300" ry="200" />
        <ellipse cx="400" cy="400" rx="100" ry="300" />
        <ellipse cx="400" cy="400" rx="200" ry="300" />
        <line x1="100" y1="400" x2="700" y2="400" />
        <line x1="400" y1="100" x2="400" y2="700" />
      </g>
      <g fill="#e8eef8">
        <circle cx="90" cy="120" r="1.2" opacity="0.5" />
        <circle cx="690" cy="80" r="1" opacity="0.4" />
        <circle cx="730" cy="300" r="1.4" opacity="0.35" />
        <circle cx="60" cy="560" r="1" opacity="0.45" />
        <circle cx="610" cy="700" r="1.2" opacity="0.3" />
        <circle cx="200" cy="740" r="0.9" opacity="0.4" />
        <circle cx="520" cy="40" r="0.8" opacity="0.5" />
      </g>
      <circle cx="540" cy="250" r="5" fill="#e3bf7c" opacity="0.9" />
      <circle cx="540" cy="250" r="14" fill="none" stroke="#e3bf7c" stroke-opacity="0.35" />
    </svg>

    <main class="auth__main">
      <RouterLink :to="{ name: 'login' }" class="auth__brand">
        <span class="eyebrow">Atlas</span>
        <span class="auth__wordmark">Literário</span>
      </RouterLink>
      <div class="auth__card">
        <RouterView v-slot="{ Component }">
          <Transition name="page-fade" mode="out-in">
            <component :is="Component" />
          </Transition>
        </RouterView>
      </div>
      <p class="auth__tagline">O mundo, contado pelos livros que você leu.</p>
      <p v-if="USE_MOCKS" class="auth__demo">
        Modo demonstração — entre com <strong>leitora@atlas.dev</strong> / <strong>atlas1234</strong
        ><br />
        Moderadora: <strong>moderadora@atlas.dev</strong> / <strong>atlas1234</strong>
      </p>
    </main>
  </div>
</template>

<style scoped>
.auth {
  position: relative;
  min-height: 100dvh;
  display: grid;
  place-items: center;
  padding: var(--space-6) var(--space-4);
  overflow: hidden;
  background: var(--color-ocean);
}
.auth__sky {
  position: absolute;
  inset: -10%;
  width: 120%;
  height: 120%;
  pointer-events: none;
  animation: drift 120s linear infinite;
}
@keyframes drift {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .auth__sky {
    animation: none;
  }
}
.auth__main {
  position: relative;
  width: min(420px, 100%);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-5);
}
.auth__brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-decoration: none;
  color: var(--color-text);
}
.auth__brand .eyebrow {
  color: var(--color-brass);
  letter-spacing: 0.4em;
}
.auth__wordmark {
  font-family: var(--font-display);
  font-size: var(--text-display);
  font-weight: 400;
  font-style: italic;
  line-height: 1;
}
.auth__card {
  width: 100%;
  padding: var(--space-6);
  border-radius: var(--radius-xl);
  background: rgb(12 22 38 / 0.82);
  border: 1px solid var(--color-border);
  box-shadow: var(--shadow-lg);
  backdrop-filter: blur(10px);
}
@media (max-width: 480px) {
  .auth__card {
    padding: var(--space-5);
  }
}
.auth__tagline {
  font-family: var(--font-display);
  font-style: italic;
  color: var(--color-text-muted);
  text-align: center;
}
.auth__demo {
  font-size: var(--text-xs);
  color: var(--color-text-faint);
  text-align: center;
}
</style>
