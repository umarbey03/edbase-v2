import { createRouter, createWebHistory } from 'vue-router'
import type { RouteRecordRaw } from 'vue-router'

import { homeRouteFor } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { isTelegramMiniApp } from '@/shared/lib/telegram-web-app'
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

  /*
    ════════════════════════════════════════════════════════════════════
    SALOMLASHUV — «Nuri» o'quvchini kutib oladi (2026-08-30)
    ════════════════════════════════════════════════════════════════════

    Kirishdan keyin o'quvchi darhol bosh sahifaga tushmaydi: maskot uni
    ismi bilan kutib oladi va gapi uning holatiga qarab o'zgaradi
    (sabab va qoidalar — `StudentGreetingPage.vue` va
    `features/nuri-greeting/model/greeting-message.ts` izohlarida).

    ★ `AppShell` DAN HAM, `StudentShell` DAN HAM TASHQARIDA — `live-room`
      bilan AYNI mulohaza: ekran to'liq sahnani egallaydi. Pastdagi tab
      paneli yoki yuqoridagi appbar bu yerda faqat xalaqit berardi
      (foydalanuvchi salomlashuvni O'QIMASDAN tabga bosib ketardi va
      ekran o'z vazifasini bajarmasdi).

    ★ `roles: STUDENT` — HIMOYANING O'ZI, bezak emas: xodim manzilni
      qo'lda yozsa (yoki eski xatcho'pdan kirsa) qo'riqchi uni o'z bosh
      sahifasiga qaytaradi. Ekran mazmuni butunlay o'quvchi
      ma'lumotidan yasaladi va xodimda u yo'q.
  */
  {
    path: '/salom',
    name: 'student-greeting',
    component: () => import('@/pages/student/StudentGreetingPage.vue'),
    meta: { requiresAuth: true, title: 'Salom', roles: STUDENT },
  },

  {
    /*
      ════════════════════════════════════════════════════════════════════
      ILDIZ MANZIL — MEHMONGA LANDING, KIRGANGA O'Z BOSH SAHIFASI
      ════════════════════════════════════════════════════════════════════

      ⚠️ 2026-08-28 DA O'ZGARDI. Ilgari bu yerda `requiresAuth: true` va
      rolga qarab `redirect` turardi, ya'ni kirmagan odam darhol `/login`
      ga tashlanardi va markazning ochiq yuzi umuman yo'q edi (sabab va
      qaror — `LandingPage.vue` izohida).

      ★ NEGA `beforeEach` EMAS, `beforeEnter`: qoida FAQAT shu marshrutga
        tegishli. Global qo'riqchiga qo'shilsa, u har navigatsiyada
        tekshirilib, "ildiz manzil" degan maxsus holat butun ilova
        bo'ylab tarqalardi.

      ★ NEGA `requiresAuth` YO'Q: bu sahifa ANONIM. Global qo'riqchi uni
        `/login` ga tashlamasligi kerak — aks holda landing hech qachon
        ko'rinmasdi.
    */
    path: '/',
    name: 'landing',
    component: () => import('@/pages/landing/LandingPage.vue'),
    meta: { title: 'Arab tili kursi' },
    beforeEnter: () => {
      /*
        🔴 TELEGRAM MINI APP — LANDING KO'RSATILMAYDI.

        Telegram ilovani AYNAN `/` da ochadi. Bu yerda landing chizilsa,
        o'quvchi Mini App ichida reklama sahifasini ko'rardi va avtomatik
        kirish ekraniga umuman tushmasdi (u `/login` da yashaydi).

        Tekshiruv `useAuthStore` dan OLDIN: Mini App'da sessiya hali
        yo'q va u aynan shu yo'naltirishdan keyin ochiladi.

        ★ FRAGMENT (`#tgWebAppData=...`) BU YERDA UZATILMAYDI — VA
          KERAK EMAS. Telegram imzoni manzil fragmentiga qo'yadi, lekin
          `shared/lib/telegram-web-app.ts` uni MODUL YUKLANGAN ZAHOTI,
          router umuman ishga tushishidan OLDIN suratga oladi (o'sha
          fayldagi 2-blok izohi). Ya'ni bu yo'naltirish fragmentni
          "yo'qotsa" ham, imzo allaqachon xotirada.
      */
      if (isTelegramMiniApp()) return { path: '/login' }

      const auth = useAuthStore()

      // Kirgan foydalanuvchi landing'ni ko'rmaydi — har ochilishda
      // reklama sahifasidan o'tib yurishi kerak bo'lardi.
      //
      // ★ `beforeEach` bu paytda `bootstrap()` ni ALLAQACHON kutgan,
      //   ya'ni `isAuthenticated` haqiqiy qiymat (yangi ochilgan
      //   sahifada ham).
      if (auth.isAuthenticated) return { name: homeRouteFor(auth.role) }

      return true
    },
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
        /*
          BOSHQARUV PANELI (2026-08-18) — o'quv bo'limi va adminning BOSH
          sahifasi (loyiha egasi: *"default holatida biror bir dashboard
          qil"*). Ilgari ular "Guruhlar" ro'yxatiga tushardi — u ish
          ro'yxati, "bugun nima diqqat talab qiladi?" degan savolga
          javob bermasdi.
        */
        path: 'boshqaruv/panel',
        name: 'manage-dashboard',
        component: () => import('@/pages/manage/ManageDashboardPage.vue'),
        meta: { title: 'Boshqaruv paneli', roles: MANAGERS },
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
        /*
          USTOZLAR HOLATI (2026-08-17) — kunlik "darsga o'ta olasizmi?"
          tasdiqlash + o'rinbosar tizimining o'quv bo'limi paneli. Suhbat
          Telegram bot orqali ketadi; bu sahifa faqat BUGUNGI holatni
          ko'rsatadi (polling).
        */
        path: 'boshqaruv/ustozlar-holati',
        name: 'manage-teacher-availability',
        component: () => import('@/pages/manage/ManageTeacherAvailabilityPage.vue'),
        meta: { title: 'Ustozlar holati', roles: MANAGERS },
      },
      {
        /*
          DARSGA KIRMAGANLAR (2026-08-18) — o'quv bo'limi so'rovi: *"bir
          kun avval darsga kirmagan o'quvchilarni bittada ko'ra olishimiz
          uchun"*. Mavjud davomat ekrani BITTA DARS kesimida ishlaydi;
          bu esa bir kunning barcha guruhlarini bitta ro'yxatga yig'adi.

          ★ `STAFF_AND_MANAGERS`: qo'ng'iroqlarni amalda guruh kuratori
          qiladi, ya'ni ro'yxat unga ham ochiq bo'lishi kerak (server
          ham o'quvchidan boshqa hammaga ruxsat beradi).
        */
        path: 'boshqaruv/kelmaganlar',
        name: 'manage-absentees',
        component: () => import('@/pages/manage/ManageAbsenteesPage.vue'),
        meta: { title: 'Darsga kirmaganlar', roles: STAFF_AND_MANAGERS },
      },
      {
        /*
          TO'KILISHLAR (2026-08-17) — o'quvchi qachon, qaysi guruhdan, qaysi
          ustozdan va nima sababdan ketgani/muzlatilgani/ko'chirilgani.
          Manba: o'chmaydigan `GroupMembershipEvent` jurnali.
        */
        path: 'boshqaruv/tokilishlar',
        name: 'manage-attrition',
        component: () => import('@/pages/manage/ManageAttritionPage.vue'),
        meta: { title: 'To‘kilishlar', roles: MANAGERS },
      },
      {
        /*
          ARIZALAR (2026-08-28) — landing sahifadagi «Kursga yozilish»
          formasidan kelgan so'rovlar.

          🔴 ARIZA HISOB EMAS: bu sahifadagi hech qanday amal
             foydalanuvchi yaratmaydi (sabab `ManageApplicationsPage.vue`
             va backenddagi `EnrollmentApplication` izohida).
        */
        path: 'boshqaruv/arizalar',
        name: 'manage-applications',
        component: () => import('@/pages/manage/ManageApplicationsPage.vue'),
        meta: { title: 'Arizalar', roles: MANAGERS },
      },
      {
        /*
          JARIMALAR (2026-08-18) — ustoz/kurator uchun. O'quv bo'limi
          ko'radi va qo'lda kirita oladi.

          ★ TASDIQLASH/BEKOR QILISH — JARIMA TURIGA bog'liq (izoh
          `PenaltyService.EnsureCanReviewAsync` da): TIZIM yozganini
          (kechikish, o'tilmagan dars) o'quv bo'limi ham ko'rib chiqadi,
          QO'LDA yozilganini esa faqat administrator — aks holda bitta
          odam ham jarima yozib, ham uni pulga aylantirardi. Server 403
          bilan qo'riqlaydi, UI esa tugmani `canReview` bilan yashiradi.

          Shu sababli marshrut `MANAGERS` — admin-only EMAS.
        */
        path: 'boshqaruv/jarimalar',
        name: 'manage-penalties',
        component: () => import('@/pages/manage/ManagePenaltiesPage.vue'),
        meta: { title: 'Jarimalar', roles: MANAGERS },
      },
      {
        /*
          FAQAT ADMIN (loyiha egasi, 2026-08-15): *"to'lovlar va moliya
          qismi o'quv bo'limi uchun kerak emas, u qismi admin panelda
          bo'lsa yetadi"*. `PaymentsController.ManageRoles` o'zi
          o'zgarmadi ("Academic,Admin" qoladi) \u2014 student profilidagi
          moliya bo'limi (`ProfileFinanceSection`) kabi BOSHQA ekranlar
          O'SHA endpointlarni O'QISH uchun ishlatadi va ular bu talabga
          aloqasi yo'q. Faqat bu IKKI SAHIFANING o'zi Admin'ga qulflandi.
        */
        path: 'boshqaruv/tolovlar',
        name: 'manage-payments',
        component: () => import('@/pages/manage/ManagePaymentsPage.vue'),
        meta: { title: 'To\u2018lovlar', roles: ADMIN_ONLY },
      },
      {
        path: 'boshqaruv/moliya',
        name: 'manage-finance',
        component: () => import('@/pages/manage/ManageFinancePage.vue'),
        meta: { title: 'Moliya', roles: ADMIN_ONLY },
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
          "Xabarlar" — guruhlarga Telegram/platforma chati orqali xabar
          yuborish (2026-08-16). Rollar `GroupBroadcastsController.ManageRoles`
          bilan AYNI ("Academic,Admin") — ustoz/kurator bu ekranni ko'rmaydi,
          o'z guruh chatiga guruh sahifasidagi "Chat" tabidan yozadi.
        */
        path: 'boshqaruv/xabarlar',
        name: 'manage-broadcasts',
        component: () => import('@/pages/manage/ManageBroadcastsPage.vue'),
        meta: { title: 'Xabarlar', roles: MANAGERS },
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
      {
        /*
          SOZLAMALAR (o'quv jarayoni) — Admin'ning "Tizim sozlamalari"
          (`manage-settings`, INFRATUZILMA) dan MUSTAQIL: bu yerdagilar
          o'quv jarayoni sozlamalari (dars tahlili mezonlari, guruh
          yo'nalishlari). Ko'rinadigan nom shunchaki "Sozlamalar" (loyiha
          egasi, 2026-08-15) — marshrut nomi va ruxsat ro'yxati o'zgarmadi,
          faqat yorliq qisqardi. Rollar `SessionReviewsController.WriteRoles`
          bilan AYNI.
        */
        path: 'boshqaruv/oquv-sozlamalari',
        name: 'manage-academic-settings',
        component: () => import('@/pages/manage/ManageAcademicSettingsPage.vue'),
        meta: { title: 'Sozlamalar', roles: MANAGERS },
      },
      {
        /*
          OYLIK HISOBLASH (2026-08-16) — ustoz/kurator haqi. `ADMIN_ONLY`:
          `PayrollController` ham FAQAT Admin (Academic emas) — izoh
          `PayrollService` sinfida ("kim TO'LOV OLADI — ustoz VA kurator,
          lekin kim KO'RADI/BOSHQARADI — faqat Admin").
        */
        path: 'boshqaruv/oylik-hisoblash',
        name: 'manage-payroll',
        component: () => import('@/pages/manage/ManagePayrollPage.vue'),
        meta: { title: 'Oylik hisoblash', roles: ADMIN_ONLY },
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
  scrollBehavior(to) {
    /*
      ★ LANGAR (`#ariza`) — 2026-08-28 da qo'shildi. Kirish sahifasidagi
        «ariza qoldiring» havolasi landing'ning AYNAN o'sha bo'limiga
        olib borishi kerak; ilgari u har doim sahifa boshiga tushardi.

      🔴 SELEKTOR TEKSHIRILADI, XOM `to.hash` ISHLATILMAYDI.

      Telegram Mini App ilovani `#tgWebAppData=...` fragmenti bilan
      ochadi. U CSS selektor sifatida YAROQSIZ (`=` va boshqa belgilar)
      va `querySelector` istisno tashlardi — ya'ni Mini App'ning butun
      navigatsiyasi bitta imzo fragmenti tufayli buzilardi.

      Shuning uchun faqat SODDA langar qabul qilinadi: harf, raqam va
      chiziqcha.
    */
    if (/^#[a-z][a-z0-9-]*$/i.test(to.hash)) {
      // `top: 80` — yopishqoq yuqori panel sarlavhani yopib qo'ymasin.
      return { el: to.hash, top: 80, behavior: 'smooth' }
    }

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

/*
  Brauzer tabidagi sarlavha — DOIMIY brend nomi (loyiha egasining talabi,
  2026-08-28). Ilgari u `${sahifa} — <brend>` shaklida edi va tab tor
  bo'lganda faqat sahifa nomi ko'rinardi ("Tizim sozla…"), ya'ni brend
  eng ko'rinadigan joyda umuman o'qilmasdi.

  ★ NOM YOZILISHI — `ZIN-NUR ONLINE`, landing sahifa bilan AYNI
    (2026-08-29 da tanlandi). Ikki xil yozilish saytda darrov ko'zga
    tashlanardi: tabda bir xil, sahifada boshqa.

  ⚠️ `to.meta.title` OLIB TASHLANMADI: u `AppShell.vue` da ilova ichidagi
  sarlavha uchun ishlatiladi. Bu yerda faqat brauzer sarlavhasi o'zgardi.
*/
const DOCUMENT_TITLE = 'ZIN-NUR ONLINE'

router.afterEach(() => {
  document.title = DOCUMENT_TITLE
})
