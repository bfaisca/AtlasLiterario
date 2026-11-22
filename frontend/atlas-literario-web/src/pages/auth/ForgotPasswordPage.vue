<script setup lang="ts">
import { ref } from 'vue'
import { useFormErrors } from '@/composables/useFormErrors'
import { useAuthStore } from '@/stores/auth'
import { validateEmail } from '@/utils/validation'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseInput from '@/components/ui/BaseInput.vue'

const auth = useAuthStore()
const email = ref('')
const submitting = ref(false)
const sent = ref(false)
const { fieldErrors, formError, validate, applyApiError } = useFormErrors(['email'] as const)

async function submit() {
  if (!validate({ email: validateEmail(email.value) })) return
  submitting.value = true
  try {
    await auth.requestPasswordReset({ email: email.value.trim() })
    // A API sempre responde 202: não revelamos se o e-mail existe.
    sent.value = true
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
      <h1 class="auth-form__title">Recuperar senha</h1>
      <p class="auth-form__subtitle">
        Informe seu e-mail e enviaremos um link para criar uma nova senha.
      </p>
    </header>

    <template v-if="sent">
      <p class="auth-form__success" role="status">
        Se houver uma conta com <strong>{{ email }}</strong>, você receberá um e-mail com as
        instruções em instantes. O link expira em 1 hora.
      </p>
      <BaseButton :to="{ name: 'login' }" variant="secondary" block>Voltar para o login</BaseButton>
    </template>

    <template v-else>
      <div v-if="formError" class="auth-form__error" role="alert">
        <AppIcon name="alert" :size="18" />
        <span>{{ formError }}</span>
      </div>
      <BaseInput
        v-model="email"
        label="E-mail"
        type="email"
        autocomplete="email"
        inputmode="email"
        :error="fieldErrors.email"
        required
      />
      <BaseButton type="submit" size="lg" block :loading="submitting">Enviar link</BaseButton>
      <p class="auth-form__links">
        <RouterLink :to="{ name: 'login' }">Lembrei a senha</RouterLink>
      </p>
    </template>
  </form>
</template>

<style scoped src="./authForm.css"></style>
