<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { toApiError } from '@/api/errors'
import { useFormErrors } from '@/composables/useFormErrors'
import { safeRedirect } from '@/router/guards'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import {
  validateEmail,
  validateName,
  validatePassword,
  validatePasswordConfirmation,
} from '@/utils/validation'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseInput from '@/components/ui/BaseInput.vue'

const auth = useAuthStore()
const ui = useUiStore()
const router = useRouter()
const route = useRoute()

const form = reactive({ name: '', email: '', password: '', confirmPassword: '' })
const submitting = ref(false)
const { fieldErrors, formError, validate, applyApiError } = useFormErrors([
  'name',
  'email',
  'password',
  'confirmPassword',
] as const)

async function submit() {
  const valid = validate({
    name: validateName(form.name),
    email: validateEmail(form.email),
    password: validatePassword(form.password),
    confirmPassword: validatePasswordConfirmation(form.password, form.confirmPassword),
  })
  if (!valid) return

  submitting.value = true
  try {
    await auth.register({
      name: form.name.trim(),
      email: form.email.trim(),
      password: form.password,
    })
    ui.success('Conta criada. Boas leituras!')
    await router.replace(safeRedirect(route.query.redirect) ?? { name: 'dashboard' })
  } catch (error) {
    applyApiError(error)
    if (toApiError(error).code === 'auth.email_taken') {
      fieldErrors.email = formError.value
      formError.value = null
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <form class="auth-form" novalidate @submit.prevent="submit">
    <header class="auth-form__header">
      <h1 class="auth-form__title">Abra seu atlas</h1>
      <p class="auth-form__subtitle">Cada livro lido acende um país no seu mapa.</p>
    </header>

    <div v-if="formError" class="auth-form__error" role="alert">
      <AppIcon name="alert" :size="18" />
      <span>{{ formError }}</span>
    </div>

    <BaseInput
      v-model="form.name"
      label="Nome ou apelido"
      autocomplete="name"
      maxlength="100"
      :error="fieldErrors.name"
      required
    />
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
      autocomplete="new-password"
      hint="Mínimo de 8 caracteres, com letras e números."
      :error="fieldErrors.password"
      required
    />
    <BaseInput
      v-model="form.confirmPassword"
      label="Confirmar senha"
      type="password"
      autocomplete="new-password"
      :error="fieldErrors.confirmPassword"
      required
    />

    <BaseButton type="submit" size="lg" block :loading="submitting">Criar conta</BaseButton>

    <p class="auth-form__links">
      <span>
        Já tem conta?
        <RouterLink :to="{ name: 'login', query: route.query }">Entrar</RouterLink>
      </span>
    </p>
  </form>
</template>

<style scoped src="./authForm.css"></style>
