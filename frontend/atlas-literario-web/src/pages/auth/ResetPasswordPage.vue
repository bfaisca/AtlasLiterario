<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { toApiError } from '@/api/errors'
import { useFormErrors } from '@/composables/useFormErrors'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import { validatePassword, validatePasswordConfirmation } from '@/utils/validation'
import AppIcon from '@/components/ui/AppIcon.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseInput from '@/components/ui/BaseInput.vue'

const auth = useAuthStore()
const ui = useUiStore()
const route = useRoute()
const router = useRouter()

const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : ''))
const form = reactive({ newPassword: '', confirmPassword: '' })
const submitting = ref(false)
const tokenRejected = ref(false)
const { fieldErrors, formError, validate, applyApiError } = useFormErrors([
  'newPassword',
  'confirmPassword',
] as const)

async function submit() {
  const valid = validate({
    newPassword: validatePassword(form.newPassword),
    confirmPassword: validatePasswordConfirmation(form.newPassword, form.confirmPassword),
  })
  if (!valid) return

  submitting.value = true
  try {
    await auth.resetPassword({ token: token.value, newPassword: form.newPassword })
    ui.success('Senha redefinida. Entre com a nova senha.')
    if (auth.isAuthenticated) await auth.logout()
    await router.replace({ name: 'login' })
  } catch (error) {
    applyApiError(error)
    tokenRejected.value = toApiError(error).code === 'auth.invalid_reset_token'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div v-if="!token" class="auth-form">
    <header class="auth-form__header">
      <h1 class="auth-form__title">Link inválido</h1>
      <p class="auth-form__subtitle">
        Este endereço não contém um token de redefinição. Use o link recebido por e-mail ou solicite
        um novo.
      </p>
    </header>
    <BaseButton :to="{ name: 'forgot-password' }" block>Solicitar novo link</BaseButton>
  </div>

  <form v-else class="auth-form" novalidate @submit.prevent="submit">
    <header class="auth-form__header">
      <h1 class="auth-form__title">Nova senha</h1>
      <p class="auth-form__subtitle">
        Escolha uma senha com pelo menos 8 caracteres, letras e números.
      </p>
    </header>

    <div v-if="formError" class="auth-form__error" role="alert">
      <AppIcon name="alert" :size="18" />
      <span>
        {{ formError }}
        <RouterLink v-if="tokenRejected" :to="{ name: 'forgot-password' }">
          Solicitar novo link
        </RouterLink>
      </span>
    </div>

    <BaseInput
      v-model="form.newPassword"
      label="Nova senha"
      type="password"
      autocomplete="new-password"
      :error="fieldErrors.newPassword"
      required
    />
    <BaseInput
      v-model="form.confirmPassword"
      label="Confirmar nova senha"
      type="password"
      autocomplete="new-password"
      :error="fieldErrors.confirmPassword"
      required
    />
    <BaseButton type="submit" size="lg" block :loading="submitting">Redefinir senha</BaseButton>
  </form>
</template>

<style scoped src="./authForm.css"></style>
