<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'

/**
 * EMOJI TANLAGICH — guruh chati va shaxsiy yozishmalar uchun BITTA komponent.
 *
 * Talab (2026-08-13): *"telegram chat kabi… emoji…"*. Fayl/rasm yuborish shu
 * ishga KIRMAYDI — u yangi entity va migratsiya talab qiladi (R16b, 4-to'lqin).
 *
 * ★ KUTUBXONA QO'SHILMADI. Tayyor emoji-picker paketlari 300 KB dan ortiq
 * ma'lumot (unicode jadvallari, tarjimalar, teri rangi variantlari) olib
 * keladi, ilova esa telefon internetida ochiladi. Bu yerda ATAYLAB QISQA,
 * qo'lda tanlangan ro'yxat bor: ustoz-o'quvchi yozishmasida haqiqatda
 * ishlatiladigan belgilar. Ro'yxatga qo'shish — bitta qator.
 *
 * ★ SERVER TOMONI TAYYOR: `MessageText.Normalize` matnni kesganda surrogat
 * juftlikni BUZMAYDI, chiqarish esa `v-text` + `whitespace-pre-wrap` — ya'ni
 * bu ish sof frontend.
 *
 * ★ KURSOR JOYIGA QO'YILADI, oxiriga EMAS: foydalanuvchi jumla o'rtasiga
 * qaytib emoji qo'yishi normal holat; oxiriga yopishtirish yozilgan matnni
 * buzib ko'rsatardi.
 */
const props = defineProps<{
  /**
   * Yozish maydoni. Kursor joyi (`selectionStart`) SHU elementdan o'qiladi,
   * shuning uchun komponent uni prop sifatida oladi — o'z ichida saqlamaydi.
   */
  target: HTMLTextAreaElement | null
  modelValue: string
  /** Server chegarasi (`GROUP_CHAT_BODY_MAX` / `DM_BODY_MAX`). */
  maxLength: number
}>()

const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

/**
 * Bo'limlar — Telegram'dagidek, lekin QISQA. Har bir belgining nomi bor:
 * u `aria-label` ga tushadi (skrinrider "emoji" deb o'qib qo'ymasin) va
 * sichqoncha ostida `title` bo'lib ko'rinadi.
 */
const EMOJI_GROUPS: readonly {
  readonly title: string
  readonly items: readonly { readonly char: string; readonly label: string }[]
}[] = [
  {
    title: 'Yuzlar',
    items: [
      { char: '🙂', label: 'tabassum' },
      { char: '😀', label: 'kulgi' },
      { char: '😅', label: 'yengil kulgi' },
      { char: '😊', label: 'mamnun' },
      { char: '😍', label: 'zavq' },
      { char: '😉', label: 'ko‘z qisish' },
      { char: '🤗', label: 'quchoq' },
      { char: '🤔', label: 'o‘ylanish' },
      { char: '😐', label: 'befarq' },
      { char: '😴', label: 'uyqu' },
      { char: '😢', label: 'xafa' },
      { char: '😭', label: 'yig‘i' },
      { char: '😳', label: 'hayrat' },
      { char: '😡', label: 'jahl' },
      { char: '🤒', label: 'kasal' },
      { char: '🙃', label: 'hazil' },
    ],
  },
  {
    title: 'Imo-ishora',
    items: [
      { char: '👍', label: 'zo‘r' },
      { char: '👎', label: 'yoqmadi' },
      { char: '👌', label: 'bo‘ldi' },
      { char: '👏', label: 'qarsak' },
      { char: '🙏', label: 'rahmat' },
      { char: '🤝', label: 'kelishuv' },
      { char: '💪', label: 'kuch' },
      { char: '👋', label: 'salom' },
      { char: '☝️', label: 'diqqat' },
      { char: '✍️', label: 'yozish' },
      { char: '🤲', label: 'duo' },
      { char: '🫡', label: 'bosh ustiga' },
    ],
  },
  {
    title: 'Belgilar',
    items: [
      { char: '❤️', label: 'yurak' },
      { char: '🔥', label: 'olov' },
      { char: '⭐', label: 'yulduz' },
      { char: '✅', label: 'bajarildi' },
      { char: '❌', label: 'xato' },
      { char: '❗', label: 'muhim' },
      { char: '❓', label: 'savol' },
      { char: '⏰', label: 'vaqt' },
      { char: '🎉', label: 'tabrik' },
      { char: '🏆', label: 'kubok' },
      { char: '💯', label: 'yuz ball' },
      { char: '🎯', label: 'nishon' },
    ],
  },
  {
    title: 'O‘quv',
    items: [
      { char: '📚', label: 'kitoblar' },
      { char: '📖', label: 'kitob' },
      { char: '📝', label: 'vazifa' },
      { char: '✏️', label: 'qalam' },
      { char: '📌', label: 'muhim eslatma' },
      { char: '📅', label: 'kalendar' },
      { char: '🎓', label: 'bitiruv' },
      { char: '💡', label: 'g‘oya' },
      { char: '🔔', label: 'eslatma' },
      { char: '📢', label: 'e’lon' },
      { char: '💻', label: 'kompyuter' },
      { char: '☕', label: 'tanaffus' },
    ],
  },
]

const open = ref(false)
const root = ref<HTMLElement | null>(null)
const toggleButton = ref<HTMLButtonElement | null>(null)

function insert(char: string): void {
  const value = props.modelValue

  /*
    Chegara maydonning `maxlength` i bilan BIR XIL qoida: emoji bilan
    chegaradan oshib ketsa hech narsa qo'shilmaydi. Aks holda oxirgi belgi
    serverda kesilib, foydalanuvchi buni yuborgandan keyin bilardi.
    ★ Uzunlik UTF-16 birligida sanaladi (emoji ko'pincha 2 birlik) — server
    ham aynan shunday sanaydi.
  */
  if (value.length + char.length > props.maxLength) return

  const element = props.target
  const start = element?.selectionStart ?? value.length
  const end = element?.selectionEnd ?? value.length
  const caret = start + char.length

  emit('update:modelValue', `${value.slice(0, start)}${char}${value.slice(end)}`)

  if (element === null) return
  // Kursorni qo'shilgan belgidan KEYIN qo'yamiz — DOM yangilangach.
  // ★ Fokus ATAYLAB o'g'irlanmaydi: tugmalarda `mousedown.prevent` bor,
  // ya'ni sichqoncha bilan tanlaganda fokus maydonda QOLADI va foydalanuvchi
  // yozishda davom etaveradi. Klaviatura foydalanuvchisida esa fokus
  // panelda qoladi — u ketma-ket bir nechta emoji tanlashi mumkin.
  void nextTick(() => {
    element.setSelectionRange(caret, caret)
  })
}

function handleDocumentPointer(event: MouseEvent): void {
  const node = event.target
  if (node instanceof Node && root.value?.contains(node) === true) return
  open.value = false
}

function handleDocumentKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape') return
  open.value = false
  // Fokus tugmaga qaytadi — klaviatura foydalanuvchisi "joyini" yo'qotmasin.
  toggleButton.value?.focus()
}

function stopListening(): void {
  document.removeEventListener('mousedown', handleDocumentPointer)
  document.removeEventListener('keydown', handleDocumentKeydown)
}

watch(open, (value) => {
  if (value) {
    document.addEventListener('mousedown', handleDocumentPointer)
    document.addEventListener('keydown', handleDocumentKeydown)
  } else {
    stopListening()
  }
})

onBeforeUnmount(stopListening)
</script>

<template>
  <div
    ref="root"
    class="relative shrink-0"
  >
    <!--
      Tugma ikonkasi EMAS, emojining O'ZI: `AppIcon` to'plamida tabassum
      shakli yo'q, ro'yxatni esa shu ish uchun kengaytirish kerak emas edi.
      `mousedown.prevent` — panel ochilganda yozish maydoni fokusni
      YO'QOTMASIN (kursor joyi shu fokusga bog'liq).
    -->
    <button
      ref="toggleButton"
      type="button"
      class="tap-target flex size-11 items-center justify-center rounded-full border border-line-strong bg-ink-900 text-lg leading-none transition-colors hover:bg-ink-800"
      :class="{ 'bg-ink-800': open }"
      aria-haspopup="true"
      :aria-expanded="open"
      aria-label="Emoji qo‘shish"
      @mousedown.prevent
      @click="open = !open"
    >
      🙂
    </button>

    <!--
      Panel YUQORIGA ochiladi (`bottom-full`): yozish paneli ekranning eng
      pastida turadi, pastga ochilsa panel ekrandan chiqib ketardi.
      Kenglik 320px li ekranda ham sig'adi (`max-w`), kataklar esa qisilganda
      ham 44px BALANDLIGINI saqlaydi (WCAG 2.5.5).
    -->
    <div
      v-if="open"
      class="absolute bottom-full left-0 z-30 mb-2 w-[19rem] max-w-[calc(100vw-2rem)] rounded-2xl border border-line bg-ink-900 p-2 shadow-lg"
      role="group"
      aria-label="Emoji"
    >
      <div class="scrollbar-slim max-h-60 overflow-y-auto">
        <template
          v-for="group in EMOJI_GROUPS"
          :key="group.title"
        >
          <p
            class="px-1 pb-1 pt-1.5 text-[11px] font-bold uppercase tracking-[1px] text-dim"
            v-text="group.title"
          />
          <div class="grid grid-cols-6">
            <button
              v-for="item in group.items"
              :key="item.char"
              type="button"
              class="flex min-h-11 items-center justify-center rounded-lg text-xl leading-none transition-colors hover:bg-ink-800"
              :aria-label="item.label"
              :title="item.label"
              @mousedown.prevent
              @click="insert(item.char)"
              v-text="item.char"
            />
          </div>
        </template>
      </div>
    </div>
  </div>
</template>
