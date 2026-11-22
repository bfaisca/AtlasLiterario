<script setup lang="ts">
/**
 * Seguir / Seguindo / Seguir de volta (relação assimétrica, sem amizade). A mudança é otimista
 * (store de perfis) e volta atrás em caso de erro, que é exibido em um aviso.
 */
import { computed } from 'vue'
import { useProfilesStore } from '@/stores/profiles'
import { useUiStore } from '@/stores/ui'
import BaseButton from '@/components/ui/BaseButton.vue'

const props = defineProps<{ handle: string; isFollowing: boolean; followsYou: boolean }>()

const profiles = useProfilesStore()
const ui = useUiStore()

const pending = computed(() => !!profiles.followPending[props.handle])
const label = computed(() =>
  props.isFollowing ? 'Seguindo' : props.followsYou ? 'Seguir de volta' : 'Seguir',
)
const accessibleLabel = computed(() =>
  props.isFollowing
    ? `Seguindo @${props.handle}. Deixar de seguir`
    : props.followsYou
      ? `Seguir @${props.handle} de volta`
      : `Seguir @${props.handle}`,
)

async function toggle() {
  try {
    await profiles.toggleFollow(props.handle)
  } catch (error) {
    ui.notifyError(error)
  }
}
</script>

<template>
  <BaseButton
    :variant="isFollowing ? 'secondary' : 'primary'"
    size="sm"
    :disabled="pending"
    :aria-pressed="isFollowing"
    :aria-label="accessibleLabel"
    data-testid="follow-button"
    @click="toggle"
  >
    {{ label }}
  </BaseButton>
</template>
