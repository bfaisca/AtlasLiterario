<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { MapVisibility } from '@/types/api'
import { formatDate } from '@/utils/format'
import { validateName } from '@/utils/validation'
import BaseBadge from '@/components/ui/BaseBadge.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseCard from '@/components/ui/BaseCard.vue'
import BaseInput from '@/components/ui/BaseInput.vue'
import BaseSelect from '@/components/ui/BaseSelect.vue'

/** Esqueleto da Etapa 2 — troca de senha e links compartilhados nas próximas etapas. */
const auth = useAuthStore()
const ui = useUiStore()
const router = useRouter()

const form = reactive<{ name: string; mapVisibility: MapVisibility }>({
  name: '',
  mapVisibility: 'Private',
})
const nameError = ref<string | null>(null)
const saving = ref(false)

const VISIBILITY_OPTIONS: Array<{ value: MapVisibility; label: string }> = [
  { value: 'Private', label: 'Privado — só eu vejo meus mapas' },
  { value: 'LinkOnly', label: 'Por link — quem tiver o link vê os agregados' },
]

watch(
  () => auth.user,
  (user) => {
    if (user) Object.assign(form, { name: user.name, mapVisibility: user.mapVisibility })
  },
  { immediate: true },
)

async function save() {
  nameError.value = validateName(form.name)
  if (nameError.value) return
  saving.value = true
  try {
    await auth.updateProfile({ name: form.name.trim(), mapVisibility: form.mapVisibility })
    ui.success('Perfil atualizado.')
  } catch (error) {
    ui.notifyError(error)
  } finally {
    saving.value = false
  }
}

async function logout() {
  await auth.logout()
  await router.replace({ name: 'login' })
}
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <p class="eyebrow">Perfil</p>
        <h1>
          {{ auth.user?.name }}
          <BaseBadge v-if="auth.isModerator" tone="brass" class="profile__role"
            >Moderador</BaseBadge
          >
        </h1>
        <p class="muted">{{ auth.user?.email }} · desde {{ formatDate(auth.user?.createdAt) }}</p>
      </div>
      <BaseButton variant="ghost" @click="logout">Sair</BaseButton>
    </header>

    <BaseCard class="profile">
      <form class="stack" @submit.prevent="save">
        <BaseInput
          v-model="form.name"
          label="Nome de exibição"
          maxlength="100"
          :error="nameError"
        />
        <BaseSelect
          v-model="form.mapVisibility"
          label="Visibilidade dos mapas"
          :options="VISIBILITY_OPTIONS"
          hint="Links públicos mostram apenas países e contagens — nunca títulos, notas ou resenhas."
        />
        <div><BaseButton type="submit" :loading="saving">Salvar</BaseButton></div>
      </form>
    </BaseCard>
  </div>
</template>

<style scoped>
.profile__role {
  vertical-align: middle;
  margin-left: var(--space-2);
  font-family: var(--font-body);
}
.profile {
  max-width: 560px;
}
</style>
