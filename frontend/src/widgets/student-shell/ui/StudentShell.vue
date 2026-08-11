<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { RouterView, useRouter } from 'vue-router'

import { useStudentSchedule } from '@/features/student-schedule/model/useStudentSchedule'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { markMiniAppLogout } from '@/features/telegram-auth'
import { closeMiniApp, isTelegramMiniApp } from '@/shared/lib/telegram-web-app'
import { useNow } from '@/shared/lib/use-now'

import StudentAppBar from './StudentAppBar.vue'
import StudentProfileSheet from './StudentProfileSheet.vue'
import StudentTabBar from './StudentTabBar.vue'
import StudentToast from './StudentToast.vue'

/**
 * O'QUVCHI KARKASI — eski `student.html` ning Telegram Mini App tuzilishi:
 * yopishqoq appbar, 520px kenglikdagi markazlashgan ustun, pastda 5 tabli
 * panel.
 *
 * NEGA XODIM KARKASI (`AppShell`) DAN ALOHIDA: ikkalasi bir-biriga o'xshamaydi
 * (yon menyu vs pastki tab) va ularni bitta komponentda `v-if` bilan
 * birlashtirsak, xodim tomonidagi har o'zgarish o'quvchi tomonini ham
 * buzish xavfini tug'dirardi. Marshrut daraxtida ham ular ALOHIDA shox:
 * `router/index.ts` da o'quvchi yo'llari `StudentShell` ostida, xodim
 * yo'llari esa `AppShell` ostida — bitta rol ikkinchisining karkasiga
 * tushib qolishi mumkin emas.
 */
const auth = useAuthStore()
const router = useRouter()

const now = useNow()
const schedule = useStudentSchedule(now)

const profileOpen = ref(false)

/*
  ★ TEMA `<html>` GA QO'YILADI, karkas `<div>` iga EMAS.

  Boshida `data-theme="student"` shu komponentning ildiz `<div>` ida edi va u
  ishlamadi: `BaseModal` va toast `<Teleport to="body">` bilan chiziladi, ya'ni
  DOM'da karkasdan TASHQARIDA turadi va tema doirasidan chiqib ketadi —
  profil varag'i xodim temasining to'q yashil ranglarida ko'rinardi
  (brauzerda ekran surati bilan tasdiqlangan).

  `<html>` ga qo'yilganda: teleport qilingan tugunlar ham, `<body>` fonining
  o'zi ham (`html { background-color: var(--color-ink-950) }`) avtomatik
  o'quvchi ranglarini oladi — iPhone'dagi overscroll ham to'g'ri rangda.

  Bir vaqtda faqat BITTA karkas mount bo'ladi (marshrut daraxti shunday
  qurilgan), shuning uchun atributni mount'da qo'yib, unmount'da olib
  tashlash yetarli — xodim sahifalari hech qachon o'quvchi temasini ko'rmaydi.

  ★ 2026-08-10: ilova yagona YORUG' temaga o'tdi va `style.css` dagi
  `[data-theme='student']` bloki deyarli BO'SHATILDI — unda faqat
  `--radius-xl: 1rem` qoldi (eski Mini App'ning 16px kartochka radiusi).
  Mexanizm ATAYLAB saqlanadi: kelajakda rolni ajratish kerak bo'lsa shu
  blokka bitta `--color-brand-500` yozuvi qo'shiladi, bu yerdagi kodga
  tegilmaydi.
*/
const STUDENT_THEME_COLOR = '#f4f6fb'
let previousThemeColor: string | null = null

function themeColorMeta(): HTMLMetaElement | null {
  return document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')
}

onMounted(() => {
  document.documentElement.dataset['theme'] = 'student'
  // Mobil brauzer manzil panelining rangi ham sahifa foniga moslashadi.
  const meta = themeColorMeta()
  if (meta !== null) {
    previousThemeColor = meta.content
    meta.content = STUDENT_THEME_COLOR
  }
})

onBeforeUnmount(() => {
  delete document.documentElement.dataset['theme']
  const meta = themeColorMeta()
  if (meta !== null && previousThemeColor !== null) meta.content = previousThemeColor
})

/*
  ★ MINI APP'DA "CHIQISH" NIMA DEGANI.

  Brauzerda chiqish = sessiyani tozalab, kirish formasiga qaytish. Telegram
  ichida bu MA'NOSIZ bo'lardi: o'quvchi kim ekanini Telegram akkaunti
  belgilaydi, u email va parolni umuman bilmaydi — forma unga BOSHI BERK
  ko'cha. Shu bilan birga tugmani butunlay olib tashlash ham to'g'ri emas:
  telefon boshqa odam qo'lida qolishi mumkin va sessiyani tozalash kerak.

  Shuning uchun Mini App'da chiqish = SESSIYANI TOZALAB, ILOVANI YOPISH.
  Foydalanuvchi bot chatiga qaytadi; ilovani qayta ochsa, o'sha Telegram
  akkaunti bilan yana kiradi.

  ★ `close()` DAN KEYIN HAM KIRISH EKRANIGA O'TAMIZ. Telegram ilovani
  yopganda webview butunlay yo'q qilinadi, ya'ni bu navigatsiya odatda
  ko'rinmaydi. Lekin `close()` "sukut bilan" ishlamasligi ham mumkin (eski
  mijoz metodni tanimaydi) — brauzerda aynan shu holat sinaldi va o'quvchi
  o'chirilgan sessiya bilan bosh sahifada QOLIB KETGAN edi. Navigatsiya
  har doim bajarilsa, bunday oraliq holat bo'lishi mumkin emas.

  `markMiniAppLogout()` esa kirish ekraniga "bu safar avtomatik kirma" deb
  aytadi: aks holda ekran darhol qayta kirib, chiqish hech qanday ta'sir
  ko'rsatmagandek tuyulardi.
*/
async function handleLogout(): Promise<void> {
  profileOpen.value = false
  await auth.logout()

  if (isTelegramMiniApp()) {
    markMiniAppLogout()
    closeMiniApp()
  }

  await router.replace({ name: 'login' })
}
</script>

<template>
  <!--
    Tema `<html>` da (yuqoridagi izohga qarang), bu yerda faqat JOYLASHUV va
    tipografiya: eski ilovaning `body { font-size: 15px; line-height: 1.5 }`.
  -->
  <div class="min-h-dvh bg-ink-950 font-sans text-[15px] leading-normal text-slate-100">
    <!--
      Eski `body { max-width: 520px; margin: 0 auto }`. 560px dan keng
      ekranlarda chap/o'ng chegara chiziladi — ustun "osilib qolmasin".
      Pastki bo'shliq tab paneli + safe-area balandligiga teng.
    -->
    <div
      class="mx-auto w-full max-w-[520px] min-[560px]:border-x min-[560px]:border-line"
      style="padding-bottom: calc(80px + env(safe-area-inset-bottom, 0px))"
    >
      <StudentAppBar
        :display-name="auth.displayName"
        :next-session="schedule.nextAny.value"
        :now="now"
        @open-profile="profileOpen = true"
      />

      <!-- Eski `.wrap { padding: 4px 16px 24px }` -->
      <main class="px-4 pb-6 pt-1">
        <RouterView v-slot="{ Component }">
          <component :is="Component" />
        </RouterView>
      </main>
    </div>

    <StudentTabBar />
    <StudentToast />

    <StudentProfileSheet
      :open="profileOpen"
      :user="auth.user"
      @close="profileOpen = false"
      @logout="handleLogout"
    />
  </div>
</template>
