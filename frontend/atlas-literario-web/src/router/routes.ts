import type { RouteRecordRaw } from 'vue-router'

export const routes: RouteRecordRaw[] = [
  {
    path: '/',
    component: () => import('@/layouts/AppLayout.vue'),
    meta: { requiresAuth: true },
    children: [
      {
        path: '',
        name: 'dashboard',
        component: () => import('@/pages/DashboardPage.vue'),
        meta: { title: 'Início' },
      },
      {
        path: 'library',
        name: 'library',
        component: () => import('@/pages/LibraryPage.vue'),
        meta: { title: 'Biblioteca' },
      },
      {
        path: 'library/:id',
        name: 'library-item',
        component: () => import('@/pages/BookDetailsPage.vue'),
        props: true,
        meta: { title: 'Livro' },
      },
      {
        path: 'search',
        name: 'search',
        component: () => import('@/pages/SearchPage.vue'),
        meta: { title: 'Adicionar livros' },
      },
      {
        path: 'books/new',
        name: 'book-new',
        component: () => import('@/pages/ManualBookPage.vue'),
        meta: { title: 'Cadastrar livro' },
      },
      {
        path: 'map/:type(authors|origins)?',
        name: 'map',
        component: () => import('@/pages/MapPage.vue'),
        meta: { title: 'Mapa' },
      },
      {
        path: 'passports',
        name: 'passports',
        component: () => import('@/pages/PassportsPage.vue'),
        meta: { title: 'Passaportes' },
      },
      {
        path: 'profile',
        name: 'profile',
        component: () => import('@/pages/ProfilePage.vue'),
        meta: { title: 'Perfil' },
      },
      {
        path: 'pessoas',
        name: 'people',
        component: () => import('@/pages/PeoplePage.vue'),
        meta: { title: 'Pessoas' },
      },
      {
        path: 'u/:handle',
        name: 'user-profile',
        component: () => import('@/pages/UserProfilePage.vue'),
        props: true,
        meta: { title: 'Perfil de leitor' },
      },
      {
        path: 'me/followers',
        name: 'me-followers',
        component: () => import('@/pages/MyFollowsPage.vue'),
        props: { kind: 'followers' },
        meta: { title: 'Seguidores' },
      },
      {
        path: 'me/following',
        name: 'me-following',
        component: () => import('@/pages/MyFollowsPage.vue'),
        props: { kind: 'following' },
        meta: { title: 'Seguindo' },
      },
    ],
  },
  {
    path: '/',
    component: () => import('@/layouts/AuthLayout.vue'),
    children: [
      {
        path: 'login',
        name: 'login',
        component: () => import('@/pages/auth/LoginPage.vue'),
        meta: { guestOnly: true, title: 'Entrar' },
      },
      {
        path: 'register',
        name: 'register',
        component: () => import('@/pages/auth/RegisterPage.vue'),
        meta: { guestOnly: true, title: 'Criar conta' },
      },
      {
        path: 'forgot-password',
        name: 'forgot-password',
        component: () => import('@/pages/auth/ForgotPasswordPage.vue'),
        meta: { guestOnly: true, title: 'Recuperar senha' },
      },
      {
        // Não é guestOnly: o link do e-mail deve funcionar mesmo com sessão aberta.
        path: 'reset-password',
        name: 'reset-password',
        component: () => import('@/pages/auth/ResetPasswordPage.vue'),
        meta: { title: 'Redefinir senha' },
      },
    ],
  },
  {
    path: '/share/:token',
    name: 'shared-map',
    component: () => import('@/pages/SharedMapPage.vue'),
    props: true,
    meta: { title: 'Mapa compartilhado' },
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/pages/NotFoundPage.vue'),
    meta: { title: 'Página não encontrada' },
  },
]
