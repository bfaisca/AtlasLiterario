<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useFormErrors } from '@/composables/useFormErrors'
import { safeRedirect } from '@/router/guards'
import { useAuthStore } from '@/stores/auth'
import { validateEmail } from '@/utils/validation'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseInput from '@/components/ui/BaseInput.vue'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const form = reactive({ email: '', password: '' })
const submitting = ref(false)
const { fieldErrors, formError, validate, applyApiError } = useFormErrors([
  'email',
  'password',
] as const)

async function submit() {
  const valid = validate({
    email: validateEmail(form.email),
    password: form.password ? null : 'Informe sua senha.',
  })
  if (!valid) return

  submitting.value = true
  try {
    await auth.login({ email: form.email.trim(), password: form.password })
    await router.replace(safeRedirect(route.query.redirect) ?? { name: 'dashboard' })
  } catch (error) {
    applyApiError(error)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <form class="auth-form" novalidate @submit.prevent="submit">
    <header class="auth-form__header">
      <h1 class="auth-form__title">Bem-vinda de volta</h1>
      <p class="auth-form__subtitle">Entre para continuar sua viagem pelos livros.</p>
    </header>

    <div v-if="formError" class="auth-form__error" role="alert">
      <AppIcon name="alert" :size="18" />
      <span>{{ formError }}</span>
    </div>

    <BaseInput
      v-model="form.email"
      label="E-mail"
      type="email"
      autocomplete="email"
      inputmode="email"
      :error="fieldErrors.email"
      required
    />
    <BaseInput
      v-model="form.password"
      label="Senha"
      type="password"
      autocomplete="current-password"
      :error="fieldErrors.password"
      required
    />
    <div class="auth-form__aside">
      <RouterLink :to="{ name: 'forgot-password' }">Esqueci minha senha</RouterLink>
    </div>

    <BaseButton type="submit" size="lg" block :loading="submitting">Entrar</BaseButton>

    <p class="auth-form__links">
      <span>
        Ainda não tem conta?
        <RouterLink :to="{ name: 'register', query: route.query }">Criar conta</RouterLink>
      </span>
    </p>
  </form>
</template>

<style scoped src="./authForm.css"></style>
