<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { getErrorMessage, toApiError } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { MapVisibility } from '@/types/api'
import { formatDate } from '@/utils/format'
import { HANDLE_MAX_LENGTH, normalizeHandle, validateHandle } from '@/utils/handle'
import { pluralize } from '@/utils/labels'
import { validateName } from '@/utils/validation'
import BaseBadge from '@/components/ui/BaseBadge.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseCard from '@/components/ui/BaseCard.vue'
import BaseInput from '@/components/ui/BaseInput.vue'
import BaseSelect from '@/components/ui/BaseSelect.vue'

/** Perfil próprio: nome, apelido, visibilidade (com aviso do que `Public` expõe) e seguidores. */
const auth = useAuthStore()
const ui = useUiStore()
const router = useRouter()

const form = reactive<{ name: string; handle: string; mapVisibility: MapVisibility }>({
  name: '',
  handle: '',
  mapVisibility: 'Private',
})
const nameError = ref<string | null>(null)
const handleError = ref<string | null>(null)
const saving = ref(false)

const VISIBILITY_OPTIONS: Array<{ value: MapVisibility; label: string }> = [
  { value: 'Private', label: 'Privado — só eu vejo meus mapas' },
  { value: 'LinkOnly', label: 'Por link — quem tiver o link vê os agregados' },
  { value: 'Public', label: 'Público — qualquer usuário vê meus mapas completos' },
]

const isPublic = computed(() => form.mapVisibility === 'Public')
const visibilityHint = computed(() =>
  form.mapVisibility === 'LinkOnly'
    ? 'Links compartilhados mostram apenas países e contagens — nunca títulos, notas ou resenhas.'
    : form.mapVisibility === 'Private'
      ? 'Ninguém além de você vê seus mapas ou sua biblioteca.'
      : undefined,
)
/** Erros da API que pertencem ao campo de apelido (mensagem por código). */
const HANDLE_ERROR_CODES = [
  'profile.handle_invalid',
  'profile.handle_taken',
  'profile.handle_required',
]

/**
 * Sincroniza o formulário só quando os campos EDITÁVEIS do usuário mudam (login, salvar). Uma
 * releitura de `/auth/me` (só contagens novas) não pode apagar o que a pessoa está digitando.
 */
watch(
  () =>
    auth.user
      ? JSON.stringify([auth.user.id, auth.user.name, auth.user.handle, auth.user.mapVisibility])
      : null,
  () => {
    const user = auth.user
    if (user) {
      Object.assign(form, {
        name: user.name,
        handle: user.handle ?? '',
        mapVisibility: user.mapVisibility,
      })
    }
  },
  { immediate: true },
)
// O apelido é sempre minúsculo (o servidor também normaliza).
watch(
  () => form.handle,
  (value) => {
    const lowered = value.toLowerCase()
    if (lowered !== value) form.handle = lowered
  },
)

onMounted(() => {
  // Contagens de seguidores/seguindo mudam em outras telas; falha aqui não atrapalha a página.
  void auth.refreshUser().catch(() => undefined)
})

function validateForm(): boolean {
  nameError.value = validateName(form.name)
  handleError.value = validateHandle(form.handle)
  if (!handleError.value && isPublic.value && !normalizeHandle(form.handle)) {
    handleError.value = 'Para deixar o perfil público, escolha um apelido.'
  }
  return !nameError.value && !handleError.value
}

async function save() {
  if (!validateForm()) return
  saving.value = true
  try {
    // `handle: null` remove o apelido (só aceito fora do modo público); vazio nunca é enviado como "".
    await auth.updateProfile({
      name: form.name.trim(),
      mapVisibility: form.mapVisibility,
      handle: normalizeHandle(form.handle) || null,
    })
    ui.success('Perfil atualizado.')
  } catch (error) {
    const apiError = toApiError(error)
    if (apiError.code && HANDLE_ERROR_CODES.includes(apiError.code)) {
      handleError.value = getErrorMessage(apiError)
    } else {
      ui.notifyError(apiError)
    }
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
        <p v-if="auth.user?.handle" class="profile__handle">@{{ auth.user.handle }}</p>
        <p class="muted">{{ auth.user?.email }} · desde {{ formatDate(auth.user?.createdAt) }}</p>
      </div>
      <BaseButton variant="ghost" @click="logout">Sair</BaseButton>
    </header>

    <BaseCard class="profile">
      <form class="stack" novalidate @submit.prevent="save">
        <BaseInput
          v-model="form.name"
          label="Nome de exibição"
          maxlength="100"
          :error="nameError"
        />
        <BaseInput
          v-model="form.handle"
          label="Apelido"
          :maxlength="HANDLE_MAX_LENGTH"
          autocomplete="off"
          autocapitalize="none"
          spellcheck="false"
          placeholder="ex.: marina_lima"
          :error="handleError"
          :required="isPublic"
          hint="De 3 a 30 caracteres: letras minúsculas, números e sublinhado (_). É como outras pessoas encontram você. Obrigatório para perfil público."
          data-testid="handle-input"
        />
        <BaseSelect
          v-model="form.mapVisibility"
          label="Visibilidade dos mapas"
          :options="VISIBILITY_OPTIONS"
          :hint="visibilityHint"
          data-testid="visibility-select"
        />

        <div v-if="isPublic" class="public-warning" role="note" data-testid="public-warning">
          <strong>Atenção: perfil público.</strong>
          <p>
            Qualquer usuário autenticado do Atlas Literário poderá encontrar você pelo apelido e ver
            seus mapas completos, sem precisar seguir você: os
            <strong
              >países, os autores, os títulos dos livros que você leu e as suas resenhas</strong
            >. Só livros marcados como “Lido” aparecem; o restante da biblioteca continua privado.
          </p>
          <p>Você pode voltar para “Privado” a qualquer momento.</p>
        </div>

        <div><BaseButton type="submit" :loading="saving">Salvar</BaseButton></div>
      </form>
    </BaseCard>

    <BaseCard class="profile" aria-labelledby="perfil-comunidade">
      <h2 id="perfil-comunidade" class="eyebrow">Comunidade</h2>
      <ul class="follows" role="list">
        <li>
          <RouterLink :to="{ name: 'me-followers' }" data-testid="link-followers">
            {{ pluralize(auth.user?.followersCount ?? 0, 'seguidor', 'seguidores') }}
          </RouterLink>
        </li>
        <li>
          <RouterLink :to="{ name: 'me-following' }" data-testid="link-following">
            {{ auth.user?.followingCount ?? 0 }} seguindo
          </RouterLink>
        </li>
      </ul>
      <div class="cluster">
        <BaseButton variant="secondary" size="sm" :to="{ name: 'people' }">
          Encontrar pessoas
        </BaseButton>
        <BaseButton
          v-if="auth.user?.handle && auth.user.mapVisibility === 'Public'"
          variant="ghost"
          size="sm"
          :to="{ name: 'user-profile', params: { handle: auth.user.handle } }"
        >
          Ver como os outros me veem
        </BaseButton>
      </div>
    </BaseCard>
  </div>
</template>

<style scoped>
.profile__role {
  vertical-align: middle;
  margin-left: var(--space-2);
  font-family: var(--font-body);
}
.profile__handle {
  color: var(--color-sky);
}
.profile {
  max-width: 560px;
}
.public-warning {
  display: grid;
  gap: var(--space-2);
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-md);
  background: var(--color-brass-soft);
  border: 1px solid rgb(227 191 124 / 0.4);
  color: var(--color-brass-strong);
  font-size: var(--text-sm);
}
.follows {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-5);
  margin: var(--space-3) 0;
  font-family: var(--font-display);
  font-size: var(--text-lg);
}
</style>
