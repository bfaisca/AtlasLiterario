<script setup lang="ts">
import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth'
import type { ProfileSummaryDto } from '@/types/api'
import { pluralize } from '@/utils/labels'
import BaseBadge from '@/components/ui/BaseBadge.vue'
import FollowButton from '@/components/people/FollowButton.vue'

const props = defineProps<{ person: ProfileSummaryDto }>()

const auth = useAuthStore()
const isSelf = computed(() => !!auth.user?.handle && auth.user.handle === props.person.handle)
const initials = computed(() =>
  props.person.name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part.charAt(0).toLocaleUpperCase('pt-BR'))
    .join(''),
)
</script>

<template>
  <article class="person" data-testid="person-card">
    <RouterLink
      :to="{ name: 'user-profile', params: { handle: person.handle } }"
      class="person__link"
    >
      <span class="person__avatar" aria-hidden="true">{{ initials }}</span>
      <span class="person__text">
        <span class="person__name">{{ person.name }}</span>
        <span class="person__handle">@{{ person.handle }}</span>
        <span class="person__meta">
          {{ pluralize(person.followersCount, 'seguidor', 'seguidores') }} ·
          {{ pluralize(person.readBooksCount, 'livro lido', 'livros lidos') }}
        </span>
      </span>
    </RouterLink>
    <div class="person__actions">
      <BaseBadge v-if="person.followsYou && !isSelf" tone="sky">Segue você</BaseBadge>
      <FollowButton
        v-if="!isSelf"
        :handle="person.handle"
        :is-following="person.isFollowing"
        :follows-you="person.followsYou"
      />
    </div>
  </article>
</template>

<style scoped>
.person {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  padding: var(--space-3) var(--space-4);
  background: var(--color-surface);
  border: 1px solid var(--color-hairline);
  border-radius: var(--radius-lg);
}
.person__link {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-width: 0;
  flex: 1;
  color: inherit;
  text-decoration: none;
  border-radius: var(--radius-md);
}
.person__link:hover .person__name {
  color: var(--color-brass-strong);
}
.person__avatar {
  display: grid;
  place-items: center;
  flex: none;
  width: 44px;
  height: 44px;
  border-radius: var(--radius-pill);
  background: var(--color-brass-soft);
  color: var(--color-brass);
  font-weight: 600;
  letter-spacing: 0.04em;
}
.person__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.person__name {
  font-family: var(--font-display);
  font-size: var(--text-lg);
  overflow-wrap: anywhere;
}
.person__handle {
  font-size: var(--text-sm);
  color: var(--color-sky);
}
.person__meta {
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.person__actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: var(--space-2);
}
@media (max-width: 520px) {
  .person {
    flex-direction: column;
    align-items: stretch;
  }
  .person__actions {
    justify-content: space-between;
  }
}
</style>
