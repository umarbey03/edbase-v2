import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'

import { useAuthStore } from '@/features/auth/model/auth.store'

declare module 'vue-router' {
  interface RouteMeta {
    /** Kirish talab qilinadimi. */
    requiresAuth?: boolean
    /** Brauzer sarlavhasi. */
    title?: string
  }
}

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    redirect: { name: 'sessions' },
  },
  {
    path: '/login',
    name: 'login',
    component: () => import('@/pages/auth/LoginPage.vue'),
    meta: { title: 'Kirish' },
  },
  {
    path: '/darslar',
    name: 'sessions',
    component: () => import('@/pages/student/StudentHomePage.vue'),
    meta: { requiresAuth: true, title: 'Darslarim' },
  },
  {
    path: '/live/:sessionId(\\d+)',
    name: 'live-room',
    component: () => import('@/pages/live/LiveRoomPage.vue'),
    meta: { requiresAuth: true, title: 'Jonli dars' },
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('@/pages/NotFoundPage.vue'),
    meta: { title: 'Sahifa topilmadi' },
  },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior() {
    return { top: 0 }
  },
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()

  // Sahifa birinchi marta yuklanganda refresh token orqali sessiyani tiklaymiz.
  await auth.bootstrap()

  if (to.meta.requiresAuth === true && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.name === 'login' && auth.isAuthenticated) {
    return { name: 'sessions' }
  }

  return true
})

router.afterEach((to) => {
  const title = to.meta.title
  document.title = title !== undefined ? `${title} — Zin-Nur` : 'Zin-Nur'
})
