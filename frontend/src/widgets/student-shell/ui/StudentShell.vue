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
import StudentSidebar from './StudentSidebar.vue'
import StudentTabBar from './StudentTabBar.vue'
import StudentToast from './StudentToast.vue'

/**
 * O'QUVCHI KARKASI — eski `student.html` ning Telegram Mini App tuzilishi:
 * yopishqoq appbar, 520px kenglikdagi markazlashgan ustun, pastda 5 tabli
 * panel.
 *
 * ★ 2026-08-13: USTIGA DESKTOP QATLAMI QO'SHILDI (≥1024px) — yon menyu,
 * kengroq ustun, tab panelisiz. TELEFON YO'LI TEGILMAGAN va bu shunchaki
 * ehtiyotkorlik emas: o'quvchi paneli AYNI PAYTDA Telegram Mini App'ning
 * o'zi (alohida bundle yo'q, `telegram-web-app.ts` ish vaqtida
 * tarmoqlanadi), Telegram esa Mini App'ni HAR DOIM telefon kengligidagi
 * oynada ochadi — Telegram Desktop'da ham. Ya'ni 1024px dan past hamma
 * narsa Mini App tajribasi bo'lib qoladi.
 *
 * Shuning uchun DESKTOP QOIDALARI FAQAT `lg:` PREFIKSI OSTIDA yoziladi,
 * JS chegara tekshiruvi bilan EMAS: `useBreakpoint()` bor, lekin uni bu
 * yerda ishlatsak chegarani IKKI hakam belgilardi (CSS `64rem`, JS
 * `1024px`) va foydalanuvchi brauzerning asosiy shrift o'lchamini
 * kattalashtirsa ular ajralib ketardi — yon menyu chiqib, kontent hali
 * telefon rejimida qolgan oraliq holat. Bitta hakam = bunday holat
 * mumkin emas.
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
  belgilaydi, ya'ni forma unga BOSHI BERK ko'cha — u yerda raqam so'raladi
  va kod AYNI shu Telegram hisobiga qaytib kelardi. Shu bilan birga tugmani butunlay olib tashlash ham to'g'ri emas:
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

    `lg:flex` — desktop qatlamining YAGONA tuzilish o'zgarishi: yon menyu va
    kontent yonma-yon. 1024px dan pastda bu klass umuman qo'llanmaydi.
  -->
  <div class="min-h-dvh bg-ink-950 font-sans text-[15px] leading-normal text-slate-100 lg:flex">
    <!--
      ===================== Desktop: doimiy yon ustun =====================
      `hidden lg:block` — xodim karkasidagi (`AppShell`) bilan AYNAN bir xil
      naqsh va bir xil kenglik (230px). Telefonda `display:none`, ya'ni
      ko'rinish daraxtidan ham, a11y daraxtidan ham butunlay chiqadi:
      pastki tab paneli bilan hech qachon birga bo'lmaydi.
    -->
    <aside class="sticky top-0 hidden h-dvh w-[230px] shrink-0 border-r border-line lg:block">
      <StudentSidebar
        :display-name="auth.displayName"
        @open-profile="profileOpen = true"
      />
    </aside>

    <!--
      Eski `body { max-width: 520px; margin: 0 auto }`. 560px dan keng
      ekranlarda chap/o'ng chegara chiziladi — ustun "osilib qolmasin".
      Pastki bo'shliq tab paneli + safe-area balandligiga teng.

      ★ DESKTOPDA TO'RT QO'SHIMCHA, hammasi `lg:` ostida:
        • `lg:max-w-[1600px]` — 520px qulfi yechiladi.

          🔴 ILGARI 960px EDI — LOYIHA EGASI RAD ETDI (2026-08-13):
          *"desktop variantida menudan tashqari contentlar to'liq ekran va
          kenglik bo'yicha moslangan holda joylashmayapti… shunchaki
          centerga tartiblab qo'ymang"*. 2560px monitorda 960px lik ustun
          ikki yonida ~700px dan bo'sh joy qoldirardi — ilova "buzilgan"
          ko'rinardi.

          ★ LEKIN CHEKSIZ HAM EMAS, va bu ZIDDIYAT EMAS: 15px shriftdagi
          BITTA ustun 2330px ga cho'zilsa qator 300+ belgi bo'lib o'qilmay
          qoladi. Yechim kenglikni cheklashda emas — uni ISHLATISHDA:
          sahifalar desktopda KO'P USTUNGA bo'linadi (`docs/
          MOSLASHUVCHANLIK.md` 6-bo'lim), ya'ni bo'sh joyni chiziq
          uzunligi emas, KONTENT to'ldiradi. 1600px — ikki-uch ustun
          bemalol sig'adigan, lekin ultra-keng monitorda ham o'lchovi
          buzilmaydigan kenglik.
        • `lg:border-x-0` — chap tomonda yon menyu chegarasi bor, ustunning
          o'z ramkasi endi ortiqcha "ikkinchi quti" bo'lib ko'rinardi.

      ★ PLANSHET BOSQICHI — `md:max-w-[840px]` (2026-08-13, loyiha egasi:
        *"ipad qismlarida ham to'liq ekran holatida ishlamayapti"*).

        Desktop qatlami `lg:` (1024px) dan boshlanadi, lekin iPad TIK holati
        768px, iPad Air esa 820px — ya'ni ikkalasi ham `lg:` dan PAST. Ular
        520px lik ustunda qolib, yon tomonlarida ~124px dan bo'sh joy va
        ko'rinib turgan `xs:border-x` chegaralari bilan "tugallanmagan"
        ko'rinardi.

        ★ NEGA YON MENYU EMAS, KENGROQ USTUN: 768px da yon menyu (230px)
        kontentga atigi 538px qoldirardi — bu 520px dan deyarli farq
        qilmaydi, ya'ni menyu qo'shib hech narsa yutilmasdi. Shuning uchun
        planshetda pastki tab paneli QOLADI (iPad ilovalarida odatiy naqsh),
        faqat ustun kengayadi. Yon menyu `lg:` da paydo bo'ladi — o'shanda
        kontentga 1370px qoladi.

        ★ Ikki ustunli sahifa setkalari ATAYLAB `lg:` da qoladi: 840px da
        600px lik kalendar + darslar ro'yxati yonma-yon sig'masdi.
        • `lg:pb-0!` — tab paneli desktopda yo'q, uning o'rniga qoldirilgan
          80px ham kerak emas. ★ `!` SHART: bu bo'shliq inline `style` da
          (ichida `env(safe-area-inset-bottom)` bor, uni utility bilan
          berib bo'lmaydi), inline'ni esa faqat `!important` yenga oladi.
          Inline `style` ning O'ZIGA ATAYLAB tegilmadi — telefon yo'li bir
          bayt ham o'zgarmasligi kerak edi.
    -->
    <div
      class="mx-auto w-full max-w-[520px] xs:border-x xs:border-line md:max-w-[840px] lg:min-w-0 lg:max-w-[1600px] lg:flex-1 lg:border-x-0 lg:pb-0!"
      style="padding-bottom: calc(80px + env(safe-area-inset-bottom, 0px))"
    >
      <StudentAppBar
        :display-name="auth.displayName"
        :next-session="schedule.nextAny.value"
        :now="now"
        @open-profile="profileOpen = true"
      />

      <!-- Eski `.wrap { padding: 4px 16px 24px }` -->
      <main class="px-4 pb-6 pt-1 md:px-6 lg:px-8 lg:pb-12">
        <RouterView v-slot="{ Component }">
          <component :is="Component" />
        </RouterView>
      </main>
    </div>

    <!--
      Pastki 5 tab — `lg:hidden` komponentning O'ZIDA. Desktopda uning
      o'rnini yuqoridagi yon ustun egallaydi; ikkalasi ham BITTA ro'yxatdan
      oziqlanadi (`useStudentNav` -> `STUDENT_NAV`).
    -->
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
