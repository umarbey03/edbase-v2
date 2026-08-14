import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'

import { homeRouteFor } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import type { UserRoleName } from '@/shared/types'

declare module 'vue-router' {
  interface RouteMeta {
    /** Kirish talab qilinadimi. */
    requiresAuth?: boolean
    /** Brauzer sarlavhasi va mobil yuqori paneldagi nom. */
    title?: string
    /**
     * Sahifani KIM ochishi mumkin. Bo'sh bo'lsa — kirgan har kim.
     * Ruxsatsiz rol o'z bosh sahifasiga qaytariladi (login'ga EMAS: u
     * allaqachon tizimda, uni chiqarib yuborish xato bo'lardi).
     */
    roles?: UserRoleName[]
  }
}

const STUDENT: UserRoleName[] = ['Student']
const STAFF: UserRoleName[] = ['Teacher', 'Assistant']
const MANAGERS: UserRoleName[] = ['Academic', 'Admin']
const STAFF_AND_MANAGERS: UserRoleName[] = ['Teacher', 'Assistant', 'Academic', 'Admin']
/**
 * FAQAT admin. `MANAGERS` dan ATAYLAB alohida: barcha
 * `/api/v1/settings/*` yo'llari serverda `[Authorize(Roles = "Admin")]`
 * bilan yopilgan va o'quv bo'limi u yerdan 403 oladi.
 */
const ADMIN_ONLY: UserRoleName[] = ['Admin']

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/pages/auth/LoginPage.vue'),
    meta: { title: 'Kirish' },
  },

  /*
    Jonli dars ATAYLAB `AppShell` dan tashqarida: u to'liq ekranni egallaydi
    (video + chat) va yon menyu u yerda faqat xalaqit berardi.
  */
  {
    path: '/live/:sessionId(\\d+)',
    name: 'live-room',
    component: () => import('@/pages/live/LiveRoomPage.vue'),
    meta: { requiresAuth: true, title: 'Jonli dars' },
  },

  {
    /*
      Ildiz manzil rolga qarab yo'naltiriladi. Sahifa YANGI ochilganda sessiya
      hali tiklanmagan bo'ladi (rol `null`) — bunda o'quvchi sahifasiga
      boriladi, so'ng `beforeEach` bootstrap'dan keyin rolga mos sahifaga
      qayta yo'naltiradi.

      Yozuv ALOHIDA turadi (karkas komponentisiz), chunki o'quvchi va xodim
      endi IKKI XIL karkasda yashaydi — bu redirect ikkalasiga ham tegishli.
    */
    path: '/',
    meta: { requiresAuth: true },
    redirect: () => ({ name: homeRouteFor(useAuthStore().role) }),
  },

  /*
    ============================ O'QUVCHI KARKASI ============================
    Telegram Mini App ko'rinishi: 520px ustun, oltin tema, pastda 5 tab.

    NEGA ALOHIDA SHOX: o'quvchi va xodim interfeyslari bir-biriga o'xshamaydi
    (pastki tab vs yon menyu). Ularni bitta karkasda `v-if` bilan
    birlashtirsak, xodim tomonidagi har o'zgarish o'quvchi tomonini buzish
    xavfini tug'dirardi — bu yerda esa xodim marshrutlari TEGILMAGAN.

    Yo'l nomlari eski ilovaning tab tartibini takrorlaydi; vazifa va testlar
    `oquv/` ostida, chunki eski ilovada ular "O'quv" ichida edi.
  */
  {
    path: '/',
    component: () => import('@/widgets/student-shell/ui/StudentShell.vue'),
    meta: { requiresAuth: true },
    children: [
      {
        path: 'bosh',
        name: 'student-home',
        component: () => import('@/pages/student/StudentHomePage.vue'),
        meta: { title: 'Bosh sahifa', roles: STUDENT },
      },
      {
        path: 'kalendar',
        name: 'student-calendar',
        component: () => import('@/pages/student/StudentCalendarPage.vue'),
        meta: { title: 'Kalendar', roles: STUDENT },
      },
      {
        path: 'oquv',
        name: 'student-learn',
        component: () => import('@/pages/student/StudentLearnPage.vue'),
        meta: { title: 'O‘quv', roles: STUDENT },
      },
      {
        path: 'reyting',
        name: 'student-rating',
        component: () => import('@/pages/student/StudentRatingPage.vue'),
        meta: { title: 'Reyting', roles: STUDENT },
      },
      {
        path: 'suhbat',
        name: 'student-chat',
        component: () => import('@/pages/student/StudentChatPage.vue'),
        meta: { title: 'Chat', roles: STUDENT },
      },
      {
        path: 'oquv/vazifalarim',
        name: 'student-assignments',
        component: () => import('@/pages/student/StudentAssignmentsPage.vue'),
        meta: { title: 'Vazifalarim', roles: STUDENT },
      },
      {
        path: 'oquv/testlarim',
        name: 'student-tests',
        component: () => import('@/pages/student/StudentTestsPage.vue'),
        meta: { title: 'Testlarim', roles: STUDENT },
      },
      {
        /*
          "Dars yozuvlari" — eski `student.html` da yozuvlar hisoblagichi
          AYNAN "O'quv" ekranida turgan (`learn-rec-meta`), shuning uchun bu
          yerda ham `oquv/` ostidagi ichki sahifa. Pastki 5 tabga TEGILMAGAN.
        */
        path: 'oquv/yozuvlar',
        name: 'student-recordings',
        component: () => import('@/pages/student/StudentRecordingsPage.vue'),
        meta: { title: 'Dars yozuvlari', roles: STUDENT },
      },
      {
        /*
          Test YECHISH — alohida marshrut, modal emas: test 20+ savoldan
          iborat bo'ladi va telefonda bir necha ekran egallaydi; modal ichida
          tasodifiy "tashqariga bosish" belgilangan javoblarni yo'q qilardi.

          `roles: STUDENT` serverdagi qoidaning nusxasi: `start`/`take`/`submit`
          uchalasi ham `[Authorize(Roles = "Student")]` va `TestService`
          `LoadStudentAsync` da rolni BAZADAN qayta tekshiradi.
        */
        path: 'oquv/testlarim/:testId(\\d+)',
        name: 'student-test-take',
        component: () => import('@/pages/student/StudentTestTakePage.vue'),
        meta: { title: 'Test', roles: STUDENT },
      },
    ],
  },

  /*
    ============================= XODIM KARKASI ==============================
    Yon menyuli panel (ustoz, kurator, o'quv bo'limi, admin) — O'ZGARISHSIZ.
  */
  {
    path: '/',
    component: () => import('@/widgets/app-shell/ui/AppShell.vue'),
    meta: { requiresAuth: true },
    children: [
      /* --------------------------- Ustoz / kurator -------------------------- */
      {
        path: 'ustoz',
        name: 'teacher-groups',
        component: () => import('@/pages/teacher/TeacherGroupsPage.vue'),
        meta: { title: 'Guruhlarim', roles: STAFF },
      },
      {
        path: 'ustoz/darslar',
        name: 'teacher-sessions',
        component: () => import('@/pages/teacher/TeacherSessionsPage.vue'),
        meta: { title: 'Darslarim', roles: STAFF },
      },
      {
        // Eski ustoz panelining "Bosh sahifa" bo'limi: bugungi va kelgusi darslar.
        path: 'ustoz/bosh',
        name: 'teacher-home',
        component: () => import('@/pages/teacher/TeacherHomePage.vue'),
        meta: { title: 'Bosh sahifa', roles: STAFF },
      },
      {
        // "Kuratorlik" — kurator guruhlari va ularga bog'langan ustoz guruhlari.
        path: 'ustoz/kuratorlik',
        name: 'teacher-curator',
        component: () => import('@/pages/teacher/TeacherCuratorPage.vue'),
        meta: { title: 'Kuratorlik', roles: STAFF },
      },
      {
        /*
          "Chatlar" — eski `teacher.html` dagi `#chats-hub`: barcha GURUH
          chatlari bitta ro'yxatda.

          ★ "Savollar" (`teacher-chat`) dan BOSHQA NARSA va ular ATAYLAB
          alohida marshrut: bu yerda GURUHNING umumiy suhbati (guruhdagi
          hamma ko'radi), u yerda esa KURATOR ↔ O'QUVCHI shaxsiy yozishmasi.
          Ikkisini bitta sahifaga qo'shsak, xodim o'quvchining shaxsiy
          savoliga butun guruh oldida javob yozib qo'yishi mumkin edi.
        */
        path: 'ustoz/chatlar',
        name: 'teacher-group-chats',
        component: () => import('@/pages/teacher/TeacherGroupChatsPage.vue'),
        meta: { title: 'Chatlar', roles: STAFF },
      },
      {
        // Eski paneldagi "Savollar" (o'qilmagan belgisi bilan) — DM yozishmalari.
        path: 'ustoz/savollar',
        name: 'teacher-chat',
        component: () => import('@/pages/teacher/TeacherChatPage.vue'),
        meta: { title: 'Savollar', roles: STAFF },
      },
      {
        path: 'ustoz/baholash',
        name: 'teacher-grading',
        component: () => import('@/pages/teacher/TeacherGradingPage.vue'),
        meta: { title: 'Vazifalar', roles: STAFF },
      },
      {
        // Guruh tafsilotini o'quv bo'limi ham ochadi (guruhlar ro'yxatidan).
        path: 'ustoz/guruh/:groupId(\\d+)',
        name: 'teacher-group',
        component: () => import('@/pages/teacher/TeacherGroupPage.vue'),
        meta: { title: 'Guruh', roles: STAFF_AND_MANAGERS },
      },

      /* -------------------------- O'quv bo'limi / admin --------------------- */
      {
        path: 'boshqaruv',
        name: 'manage-users',
        component: () => import('@/pages/manage/ManageUsersPage.vue'),
        meta: { title: 'Foydalanuvchilar', roles: MANAGERS },
      },
      {
        path: 'boshqaruv/guruhlar',
        name: 'manage-groups',
        component: () => import('@/pages/manage/ManageGroupsPage.vue'),
        meta: { title: 'Guruhlar', roles: MANAGERS },
      },
      {
        path: 'boshqaruv/darslar',
        name: 'manage-sessions',
        component: () => import('@/pages/manage/ManageSessionsPage.vue'),
        meta: { title: 'Jonli darslar', roles: MANAGERS },
      },
      {
        /*
          "Dars yozuvlari" — eski `academic.html` dagi `#recordings` bo'limi.

          ROLLAR `MANAGERS`: `GET /api/v1/recordings` ustoz va o'quvchiga ham
          200 qaytaradi (jonli tekshirilgan) — server ro'yxatni o'zi cheklaydi.
          Lekin BU sahifa butun markaz kesimidagi ko'rinish va eski ilovada ham
          faqat o'quv bo'limi menyusida bo'lgan; ustoz o'z yozuvlarini guruh
          ichidagi "Yozuvlar" tabida, o'quvchi esa "O'quv" ostida ko'radi.
        */
        path: 'boshqaruv/yozuvlar',
        name: 'manage-recordings',
        component: () => import('@/pages/manage/ManageRecordingsPage.vue'),
        meta: { title: 'Dars yozuvlari', roles: MANAGERS },
      },
      {
        path: 'boshqaruv/tolovlar',
        name: 'manage-payments',
        component: () => import('@/pages/manage/ManagePaymentsPage.vue'),
        meta: { title: 'To\u2018lovlar', roles: MANAGERS },
      },
      {
        path: 'boshqaruv/moliya',
        name: 'manage-finance',
        component: () => import('@/pages/manage/ManageFinancePage.vue'),
        meta: { title: 'Moliya', roles: MANAGERS },
      },
      {
        path: 'boshqaruv/kurslar',
        name: 'manage-courses',
        component: () => import('@/pages/manage/ManageCoursesPage.vue'),
        meta: { title: 'Kurs kontenti', roles: MANAGERS },
      },
      {
        /*
          Uy vazifalari o'quv bo'limida ALOHIDA sahifa: KURS darsiga vazifa
          biriktirishni FAQAT shu rol bajaradi
          (`AssignmentService.EnsureCanCreateAsync`), ustozning "Baholash"
          sahifasi esa `roles: STAFF` bilan yopiq — ya'ni bu marshrutsiz
          endpoint'ning yarmiga UI'dan yo'l bo'lmasdi.
        */
        path: 'boshqaruv/vazifalar',
        name: 'manage-assignments',
        component: () => import('@/pages/manage/ManageAssignmentsPage.vue'),
        meta: { title: 'Uy vazifalari', roles: MANAGERS },
      },
      {
        /*
          Testlar FAQAT o'quv bo'limi va adminda: `TestsController` da tuzish
          amallari `[Authorize(Roles = "Academic,Admin")]` bilan yopiq va
          `TestService.LoadAuthorAsync` ni takroran tekshiradi — ustoz o'z
          guruhiga test tuza olmaydi, chunki test kurs darsiga yoki butun
          platformaga taalluqli.
        */
        path: 'boshqaruv/testlar',
        name: 'manage-tests',
        component: () => import('@/pages/manage/ManageTestsPage.vue'),
        meta: { title: 'Testlar', roles: MANAGERS },
      },
      {
        path: 'boshqaruv/testlar/:testId(\\d+)',
        name: 'manage-test',
        component: () => import('@/pages/manage/ManageTestPage.vue'),
        meta: { title: 'Test', roles: MANAGERS },
      },
      {
        /*
          Kurs kontentini FAQAT o'quv bo'limi va admin o'zgartiradi — ustoz va
          kurator ATAYLAB chetda: kontent barcha guruhlarga umumiy
          (`CourseService.EnsureCanManage`). Marshrutdagi ro'yxat serverdagi
          qoidani takrorlaydi, uni almashtirmaydi.
        */
        path: 'boshqaruv/kurslar/:courseId(\\d+)',
        name: 'manage-course',
        component: () => import('@/pages/manage/ManageCoursePage.vue'),
        meta: { title: 'Kurs kontenti', roles: MANAGERS },
      },
      {
        /*
          TIZIM SOZLAMALARI — YAGONA `roles: ADMIN_ONLY` marshrut.

          Server allaqachon 403 qaytaradi, lekin guard'siz o'quv bo'limi
          sahifani OCHIB, faqat xato ekranini ko'rardi: menyu bandi yo'q
          bo'lsa ham manzilni qo'lda yozish yoki eski xatcho'p orqali kirish
          mumkin. Guard uni o'z bosh sahifasiga qaytaradi.
        */
        path: 'boshqaruv/sozlamalar',
        name: 'manage-settings',
        component: () => import('@/pages/manage/ManageSettingsPage.vue'),
        meta: { title: 'Tizim sozlamalari', roles: ADMIN_ONLY },
      },
    ],
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
    return { name: homeRouteFor(auth.role) }
  }

  // Rol tekshiruvi: ruxsat yo'q bo'lsa O'Z bosh sahifasiga qaytaramiz.
  const allowed = to.meta.roles
  if (allowed !== undefined && auth.isAuthenticated) {
    const role = auth.role
    if (role === null || !allowed.includes(role)) {
      const home = homeRouteFor(role)
      // Bosh sahifaning o'zi taqiqlangan bo'lsa (mos kelmaydigan rol) — tsikl
      // bo'lmasligi uchun `true` qaytaramiz; bunday holat faqat backend yangi
      // rol qo'shsa yuzaga keladi.
      if (to.name === home) return true
      return { name: home }
    }
  }

  return true
})

router.afterEach((to) => {
  const title = to.meta.title
  document.title = title !== undefined ? `${title} — Zin-Nur` : 'Zin-Nur'
})
