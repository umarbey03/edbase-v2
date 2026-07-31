<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

import type { User } from '@/entities/user'
import { lookup } from '@/shared/lib/lookup'
import { closeMiniApp } from '@/shared/lib/telegram-web-app'
import { AppIcon, BaseButton, BaseSpinner } from '@/shared/ui'
import type { IconName } from '@/shared/ui'

import { goToBot, hasBotLink } from '../model/bot-link'
import { INIT_DATA_YOQ, useTelegramAuth } from '../model/useTelegramAuth'

/**
 * TELEGRAM MINI APP KIRISH EKRANI.
 *
 * Ilova Telegram ichida ochilganda `LoginPage` email formasi o'rniga SHU
 * ekranni ko'rsatadi. Oddiy brauzerda bu komponent umuman mount bo'lmaydi —
 * ya'ni xodimlarning email+parol oqimi tegilmagan holda qoladi.
 *
 * 🔴 BU YERDA TELEFON RAQAM (yoki boshqa shaxsiy ma'lumot) SO'RALADIGAN
 * MAYDON YO'Q va bo'lmasligi kerak. Eski tizimda aynan shunday oyna bor edi
 * (audit X-1b) va odam u yerga BOSHQA odamning raqamini yozib, uning
 * akkauntiga kirib olardi. Bog'lash faqat botda, `contact_shared` orqali
 * bo'ladi — o'quvchi bu yerdan faqat BOTGA yo'naltiriladi.
 */
const emit = defineEmits<{
  /** Kirish muvaffaqiyatli — sahifa foydalanuvchini yo'naltiradi. */
  success: [user: User]
  /** Foydalanuvchi email+parol formasini so'radi (xodim yoki 503 holati). */
  emailLogin: []
}>()

const flow = useTelegramAuth((user) => {
  emit('success', user)
})

/**
 * Tizim tugmasi ishlamagan holat uchun qo'shimcha ko'rsatma.
 *
 * `close()` va `openTelegramLink()` — SDK metodlari; SDK yuklanmagan yoki
 * mijoz eski bo'lsa ular hech narsa qilmaydi. Bunda tugma "bosilgandek"
 * ko'rinib, aslida hech narsa bo'lmasligi eng yomon variant bo'lardi.
 */
const manualHint = ref(false)

/*
  ★ O'QUVCHI TEMASI SHU YERDA HAM QO'YILADI.

  `StudentShell` `data-theme="student"` ni `<html>` ga mount'da qo'yadi, lekin
  kirish ekrani u KARKASDAN TASHQARIDA (hali kirilmagan). Temasiz bu ekran
  xodimlarning yashil kirish sahifasi ranglarida chiqib, bir soniyadan keyin
  o'quvchi paneli oltin-navy bo'lib almashardi — Telegram sarlavhasi esa
  allaqachon `#051e2d` ga bo'yalgan bo'lardi (`applyMiniAppChrome`).

  Atribut olib tashlanadi, chunki 403/503 holatida foydalanuvchi email
  formasiga o'tadi — u ataylab temasiz (eski ilovadagidek yashil).
*/
onMounted(() => {
  document.documentElement.dataset['theme'] = 'student'
  void flow.begin()
})

onBeforeUnmount(() => {
  delete document.documentElement.dataset['theme']
})

/** Xato ekranining ko'rinishi — HTTP kodiga qarab. */
interface ErrorView {
  icon: IconName
  title: string
  /** NIMA QILISH kerakligi. Xato SABABINI server aytadi (`flow.message`). */
  hint: string
  primary: 'bot' | 'close' | 'retry' | 'email'
  secondary: 'retry' | 'email' | 'none'
}

/*
  Backend shartnomasidagi jadvalning AYNAN nusxasi. Har kod uchun ALOHIDA
  ekran, chunki foydalanuvchining harakati har birida boshqacha: 409 da botga
  borish, 403 da email formasi, 401 da ilovani qayta ochish. Umumiy "xatolik
  yuz berdi" ekrani o'quvchini nima qilishini bilmay qoldirardi.
*/
const ERROR_VIEWS: Record<string, ErrorView> = {
  [INIT_DATA_YOQ]: {
    icon: 'alert',
    title: 'Kirish ma’lumoti topilmadi',
    hint: 'Ilovani bot chatidagi tugma orqali qayta oching.',
    primary: 'close',
    secondary: 'email',
  },
  // Tarmoq xatosi — `ApiError` uni `status: 0` bilan beradi.
  0: {
    icon: 'wifi-off',
    title: 'Aloqa yo‘q',
    hint: 'Internet aloqasini tekshirib, qayta urinib ko‘ring.',
    primary: 'retry',
    secondary: 'none',
  },
  401: {
    icon: 'lock',
    title: 'Kirish ma’lumoti yaroqsiz',
    hint: 'Ilovani yopib, qaytadan oching.',
    primary: 'close',
    secondary: 'retry',
  },
  403: {
    icon: 'user',
    title: 'Bu ilova o‘quvchilar uchun',
    hint: 'Xodimlar email va parol bilan kiradi.',
    primary: 'email',
    secondary: 'none',
  },
  409: {
    icon: 'phone',
    title: 'Telegram akkaunt bog‘lanmagan',
    /*
      Bot tugmasi «📱 Raqamni ulashish» deb nomlanadi, lekin bu yerda EMOJI
      YOZILMAYDI: ilova shrifti (`Plus Jakarta Sans`) emoji bermaydi va u
      tizim shriftiga tushadi — emoji shrifti yo'q qurilmada bo'sh kvadrat
      chiqardi (headless brauzerda aynan shunday ko'rindi). Matnning o'zi
      tugmani topish uchun yetarli.
    */
    hint: 'Botga qayting va «Raqamni ulashish» tugmasini bosing — shundan keyin ilova ochiladi.',
    primary: 'bot',
    secondary: 'retry',
  },
  429: {
    icon: 'clock',
    title: 'Juda ko‘p urinish',
    hint: 'Biroz kutib, qayta urinib ko‘ring.',
    primary: 'retry',
    secondary: 'none',
  },
  503: {
    icon: 'alert',
    title: 'Telegram vaqtincha ishlamayapti',
    // Server 503 da "email va parol bilan kiring" deb ALLAQACHON aytadi
    // (jonli javobda tekshirildi) — takrorlamaymiz, faqat vaqtinchaligini
    // qo'shamiz, aks holda o'quvchi Telegram butunlay o'chgan deb o'ylardi.
    hint: 'Bu vaqtinchalik holat — keyinroq Telegram orqali kirish yana ishlaydi.',
    primary: 'email',
    secondary: 'retry',
  },
}

const FALLBACK_VIEW: ErrorView = {
  icon: 'alert',
  title: 'Kirib bo‘lmadi',
  hint: 'Birozdan so‘ng qayta urinib ko‘ring.',
  primary: 'retry',
  secondary: 'email',
}

const view = computed(() => lookup(ERROR_VIEWS, String(flow.status.value), FALLBACK_VIEW))

const retryLabel = computed(() =>
  flow.cooldown.value > 0 ? `Qayta urinish (${flow.cooldown.value})` : 'Qayta urinish',
)

const botLabel = computed(() => (hasBotLink() ? 'Botni ochish' : 'Botga qaytish'))

function handleBot(): void {
  manualHint.value = !goToBot()
}

function handleClose(): void {
  manualHint.value = !closeMiniApp()
}
</script>

<template>
  <div class="flex min-h-dvh items-center justify-center bg-ink-950 px-5 py-10">
    <div class="w-full max-w-sm text-center">
      <div
        class="mx-auto flex size-12 items-center justify-center rounded-xl bg-brand-500 text-lg font-bold text-on-brand"
      >
        Z
      </div>
      <h1 class="mt-4 text-2xl font-bold tracking-tight text-slate-50">
        Zin<span class="text-brand-500">-Nur</span>
      </h1>

      <!-- YUKLANISH: oq ekran emas, nima bo'layotgani yozilgan. -->
      <div
        v-if="flow.stage.value === 'kirilmoqda' || flow.stage.value === 'kirildi'"
        class="mt-10 flex flex-col items-center gap-3 text-slate-400"
        role="status"
      >
        <BaseSpinner
          size="lg"
          label="Kirilmoqda"
        />
        <p class="text-sm">
          Telegram orqali kirilmoqda…
        </p>
      </div>

      <!-- CHIQISHDAN KEYIN: avtomatik kirish ataylab to'xtatilgan. -->
      <div
        v-else-if="flow.stage.value === 'kutilmoqda'"
        class="mt-10"
      >
        <p class="text-sm text-slate-300">
          Siz tizimdan chiqdingiz.
        </p>
        <BaseButton
          class="mt-6"
          size="lg"
          block
          @click="flow.retry()"
        >
          Qayta kirish
        </BaseButton>
      </div>

      <!-- XATO: har kod uchun o'z sarlavhasi, sababi va harakati. -->
      <div
        v-else
        class="mt-8"
      >
        <div
          class="mx-auto flex size-14 items-center justify-center rounded-full bg-ink-900 text-brand-500 ring-1 ring-inset ring-line"
        >
          <AppIcon
            :name="view.icon"
            :size="24"
          />
        </div>

        <h2
          class="mt-4 text-lg font-bold text-slate-100"
          v-text="view.title"
        />

        <!-- Sabab — SERVERDAN (`ProblemDetails.detail`), o'zimiz yozmaymiz. -->
        <p
          class="mt-2 text-sm text-slate-300"
          role="alert"
          v-text="flow.message.value"
        />

        <p
          class="mt-3 text-[13px] leading-relaxed text-slate-400"
          v-text="view.hint"
        />

        <div class="mt-7 flex flex-col gap-2.5">
          <BaseButton
            v-if="view.primary === 'bot'"
            size="lg"
            block
            @click="handleBot"
          >
            {{ botLabel }}
          </BaseButton>
          <BaseButton
            v-else-if="view.primary === 'close'"
            size="lg"
            block
            @click="handleClose"
          >
            Ilovani yopish
          </BaseButton>
          <BaseButton
            v-else-if="view.primary === 'email'"
            size="lg"
            block
            @click="emit('emailLogin')"
          >
            Email va parol bilan kirish
          </BaseButton>
          <BaseButton
            v-else
            size="lg"
            block
            :disabled="!flow.canRetry.value"
            @click="flow.retry()"
          >
            {{ retryLabel }}
          </BaseButton>

          <BaseButton
            v-if="view.secondary === 'retry'"
            variant="ghost"
            size="md"
            block
            :disabled="!flow.canRetry.value"
            @click="flow.retry()"
          >
            {{ retryLabel }}
          </BaseButton>
          <BaseButton
            v-else-if="view.secondary === 'email'"
            variant="ghost"
            size="md"
            block
            @click="emit('emailLogin')"
          >
            Email va parol bilan kirish
          </BaseButton>
        </div>

        <p
          v-if="manualHint"
          class="mt-5 rounded-xl bg-ink-900 px-3 py-2 text-[12px] text-slate-400 ring-1 ring-inset ring-line"
        >
          Telegram bu amalni bajara olmadi. Ilovani qo‘lda yopib, bot chatiga
          qayting.
        </p>
      </div>
    </div>
  </div>
</template>
