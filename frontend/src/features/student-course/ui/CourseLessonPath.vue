<script setup lang="ts">
import { computed } from 'vue'

import { showToast } from '@/features/student-toast/model/useToast'
import type { CourseLessonDto } from '@/shared/types'
import { AppIcon } from '@/shared/ui'

import { lockMessage } from '../model/useStudentCourse'

/**
 * Modul darslari — "ilon izi" yo'lakcha (eski `coursePath()` / `.c-path`).
 *
 * Nega aynan shu ko'rinish: bugungi o'quvchi kursni AYNAN shunday ko'radi —
 * dumaloq tugmalar zigzag bo'lib pastga tushadi va orasida punktir ulagich
 * bo'ladi. Ro'yxat ko'rinishiga o'tkazish "boshqa ilova" taassurotini berardi.
 */
const props = defineProps<{
  lessons: CourseLessonDto[]
  /** Hozirgi qadam — oltin, pulsatsiyalanadigan tugma. */
  currentLessonId: number | null
}>()

const emit = defineEmits<{ open: [lesson: CourseLessonDto] }>()

/** Eski `PATH_OFF` — tugmalarning gorizontal siljishi (piksel). */
const PATH_OFFSETS = [0, 26, 38, 26, 0, -26, -38, -26]

type NodeState = 'now' | 'open' | 'lock'

const nodes = computed(() =>
  props.lessons.map((lesson, index) => {
    const state: NodeState = !lesson.unlocked
      ? 'lock'
      : lesson.id === props.currentLessonId
        ? 'now'
        : 'open'
    return {
      lesson,
      state,
      offset: PATH_OFFSETS[index % PATH_OFFSETS.length] ?? 0,
      isLast: index === props.lessons.length - 1,

      /*
        ULAGICH holati SHU tugunning darsiga qarab belgilanadi: chiziq
        i-darsdan (i+1)-darsga boradi, ya'ni u "shu dars tugatildimi?"
        savoliga javob beradi — keyingisiga emas. Eski ilovada ham shunday
        edi (`.c-seg` tugundan KEYIN chiziladi va `done` klassini o'sha
        tugunning holatidan oladi).
      */
      segmentDone: lesson.completed,
    }
  }),
)

/*
  Tugma uslublari eski CSS'dan: 66px doira, ostida 5px "qalinlik" soyasi
  (bosilganda 2px ga tushadi — o'yin tugmasi effekti).

  ★ RANGLAR TOKENGA O'TKAZILDI. Ilgari `now` holati oltin gradient +
  `#3a2600` matn + `#a9760a` soya bilan QOTIB QOLGAN edi (eski navy+oltin
  temadan). Yorug' temada u butun ilovadan ajralib, "boshqa ekran"
  taassurotini berardi; brend esa endi indigo. Uchala holat ham
  brend/neytral tokenlarda: aksent almashsa yo'lakcha o'z-o'zidan
  moslashadi.

  `lock` soyasi `rgb(0 0 0 / .28)` EMAS: qora soya oq fonda "teshik"
  bo'lib ko'rinardi — `line-strong` yumshoq qalinlik beradi.
*/
const NODE_STYLE: Record<NodeState, Record<string, string>> = {
  now: {
    background: 'linear-gradient(180deg, var(--color-brand-500), var(--color-brand-600))',
    color: 'var(--color-on-brand)',
    boxShadow: '0 5px 0 var(--color-brand-700)',
  },
  open: {
    background: 'var(--color-ink-800)',
    color: 'var(--color-brand-500)',
    border: '2px solid var(--color-brand-500)',
    boxShadow: '0 5px 0 color-mix(in oklab, var(--color-brand-500) 28%, transparent)',
  },
  lock: {
    background: 'var(--color-ink-800)',
    color: 'var(--color-slate-500)',
    boxShadow: '0 5px 0 var(--color-line-strong)',
  },
}

function handleClick(lesson: CourseLessonDto): void {
  // Qulflangan darsda SABAB aytiladi — "bosdim, hech nima bo'lmadi" holati
  // eng ko'p savol tug'diradigan joy edi.
  if (!lesson.unlocked) {
    showToast(lockMessage(lesson.lockReason))
    return
  }
  emit('open', lesson)
}
</script>

<template>
  <!--
    ★ GORIZONTAL HIMOYA (2026-08-13).

    "Ilon izi" tugunni `translateX` bilan ±38px ga suradi (`PATH_OFFSETS`),
    tugun esa `w-24` (96px) va uning ostidagi yozuv 104px gacha cho'ziladi —
    ya'ni markazdan eng chekka nuqta 52 + 38 = 90px. Tor ekranda (320px da
    yo'lakchaga 288px qoladi, yarmi 144px) bu hali sig'adi, LEKIN qirqilishi
    ikki yo'l bilan yuz berardi: uzun, bo'linmaydigan dars nomi 104px lik
    quticha ichiga sig'may chetga chiqib ketardi va uni tashqi `<section>`
    (`overflow-hidden`) kesardi.

    Shuning uchun ikki qo'shimcha: yozuv endi so'z ichidan ham ko'chiriladi
    (`break-words`, pastda) va yo'lakchaning O'ZI `overflow-x: clip` bilan
    yopiladi — sahifa hech qachon gorizontal skroll olmaydi. `clip`
    ATAYLAB `auto` EMAS: `overflow-x: auto` ikkinchi o'qni ham `auto` ga
    majburlaydi va butun yo'lakcha ichki skroll oynasiga aylanib qolardi.
    Vertikal yo'nalish esa ochiq qoladi — tugmalarning 5px "qalinlik"
    soyasi va `node-pulse` kattalashuvi kesilmasin.

    ★ ILON IZI KENG USTUNDA KENGAYADI (2026-08-13, desktop 2-iteratsiya).

    `PATH_OFFSETS` ±38px — 358px lik telefon ustuni uchun tanlangan o'lcham.
    Desktopda modullar ustuni ~1180px gacha kengayadi va o'sha ±38px lik
    tebranish 96px lik tugunlar bilan birga kartochka o'rtasida yo'qolib
    ketardi: "ilon" emas, deyarli TO'G'RI CHIZIQ. Shuning uchun siljish
    KO'PAYTUVCHI bilan beriladi (`--path-scale`) — SHAKL O'ZGARMAYDI,
    faqat amplitudasi ustunga moslashadi.

    ★ O'LCHOV OYNA EMAS, KONTEYNER: `lg:` bu yerda YARAMAYDI. 1024px da
    chap ustun atigi ~378px, 2560px da esa ~1180px — bitta `lg:`
    ko'paytuvchisi birinchi holda tugunlarni `overflow-x-clip` ostiga
    kesib yuborardi. `@container` esa yo'lakchaning HAQIQIY kengligini
    o'lchaydi, ya'ni qiymat hech qachon sig'imdan oshmaydi: eng kattasida
    38 × 2.8 + 52 = 158px yarim kenglik, konteyner esa ≥1152px.

    ★ NEGA KO'PAYTUVCHI 2.8 DA TO'XTAYDI, kartochkani to'ldirmaydi.
    Ikki qo'shni tugun orasidagi vertikal masofa QOTIB QOLGAN (66 + 14 + 26
    ≈ 106px), gorizontal qadam esa ko'paytuvchi bilan o'sadi: telefonda eng
    katta qadam 26px (~14°), 2.8 da 73px (~35°). Undan yuqorisi "yumshoq
    to'lqin" ni O'TKIR ZIGZAGGA aylantirardi va punktir ulagich (u DOIM
    markazda) tugunlardan uzilib qolardi — ya'ni bo'sh joyni to'ldirish
    uchun naqshning O'ZI buzilardi. Yon bo'shliq — yo'lakcha ko'rinishining
    bir qismi, xato emas.

    ★ TELEFON YO'LI TEGILMAYDI: chegaralar ATAYLAB 42rem (672px) dan
    boshlanadi, karkas ustuni esa `lg` gacha 520px bilan qulflangan
    (`StudentShell`), ya'ni yo'lakcha u yerda 488px dan keng bo'la olmaydi
    — birorta ham so'rov yonmaydi.
  -->
  <div
    class="@container relative overflow-x-clip px-0 pb-[18px] pt-3.5 @2xl:[--path-scale:1.8] @4xl:[--path-scale:2.3] @6xl:[--path-scale:2.8]"
  >
    <template
      v-for="node in nodes"
      :key="node.lesson.id"
    >
      <!--
        Siljish `calc()` bilan: `--path-scale` yuqoridagi konteyner
        so'rovlaridan keladi va sukut bo'yicha 1 (telefon).
      -->
      <div
        class="relative z-[1] mx-auto flex w-24 flex-col items-center py-[7px]"
        :style="{
          '--node-offset': `${node.offset}px`,
          transform: 'translateX(calc(var(--node-offset) * var(--path-scale, 1)))',
        }"
      >
        <!--
          ★ HOVER `filter` ORQALI, fon/soya orqali EMAS: uchala holatning
          foni, matni va 5px lik "qalinlik" soyasi inline `style` da
          (`NODE_STYLE`), inline'ni esa `hover:bg-*` klassi yenga olmaydi.
          `brightness` mustaqil xossa — u har uch holatda ham ishlaydi.

          Qulflangan tugun ham javob beradi: u BOSILADI (sababni toast
          bilan aytadi), ya'ni "jonsiz" ko'rinishi noto'g'ri bo'lardi.

          ★ YANGI ANIMATSIYA QO'SHILMAYDI: hozirgi dars `animate-node-pulse`
          bilan allaqachon harakatda, ikkinchi harakat u bilan raqobat
          qilardi. `filter` esa `transform` ga TEGMAYDI — pulsatsiya
          buzilmaydi.

          ★ O'TISH RO'YXATI QO'LDA YOZILGAN, `transition-transform` EMAS:
          Tailwind v4 da `translate-*` alohida `translate` XOSSASIGA
          yoziladi (`transform` ga emas), ya'ni `transition-transform`
          aslida `transform, translate, scale, rotate` ni qamraydi. Faqat
          `transform, filter` deb yozilsa, telefondagi `active:translate-y`
          bosish effekti o'tishsiz, sakrab ishlab qolardi — shuning uchun
          eski to'rtlik AYNAN saqlanib, ustiga `filter` qo'shildi.
        -->
        <button
          type="button"
          class="flex size-[66px] items-center justify-center rounded-full transition-[transform,translate,scale,rotate,filter] hover:brightness-95 active:translate-y-[3px]"
          :class="[
            node.state === 'now' ? 'animate-node-pulse' : '',
            node.state === 'lock' ? 'cursor-not-allowed' : '',
          ]"
          :style="NODE_STYLE[node.state]"
          :aria-label="node.lesson.name ?? 'Dars'"
          @click="handleClick(node.lesson)"
        >
          <AppIcon
            :name="node.state === 'lock' ? 'lock' : 'play'"
            :size="28"
          />
        </button>

        <span
          class="mt-[7px] line-clamp-2 max-w-[104px] break-words text-center text-[11.5px] font-bold leading-tight"
          :class="node.state === 'now' ? 'text-brand-500' : 'text-slate-400'"
          v-text="node.lesson.name"
        />
        <span
          v-if="node.state === 'now'"
          class="mt-[5px] text-[10px] font-extrabold uppercase tracking-[0.4px] text-brand-500"
        >
          Boshlash
        </span>
      </div>

      <!--
        Ulagich (eski `.c-seg`): dars TUGATILGAN bo'lsa yashil UZLUKSIZ
        chiziq (`.c-seg.done`), aks holda punktir.

        ★ "TUGATILGAN" HOLATI QAYTARILDI (2026-08-13, R9). Bu yerda ilgari
          "v2 da server darsning tugatilganini bermaydi" deb yozilgan edi va
          shu sababli uzluksiz holat UMUMAN chizilmasdi. IZOH NOTO'G'RI EDI:
          `CourseLessonDto.completed` WAVE 2 dan beri keladi, faqat frontend
          TIPIDA maydon yo'q edi. Ogohlantirish esa o'z kuchida qoladi va
          endi ham bajarilyapti: "uzluksiz" holat AYNAN `completed` ga
          bog'langan, `unlocked` ga EMAS — aks holda o'quvchi ochib qo'ygan,
          lekin tugatmagan darsi bajarilgandek ko'rinardi.

        ★ RANG — YASHIL, BREND EMAS: yashil bu yerda "muvaffaqiyat"
          semantikasi (eski ilovadagi rang), brend indigosi esa `now`
          tugunining rangi. Ikkalasini birlashtirsak "hozirgi qadam" va
          "o'tilgan yo'l" bir xil ko'rinardi. Qiymat TOKENDAN
          (`--color-green-500`), qotib qolgan `#22c55e` emas — yorug'/qorong'i
          temada o'zi moslashadi.

        ★ PUNKTIR NEGA `repeating-linear-gradient`, `border-dashed` emas:
          chiziq 4px KENG va 26px baland — nuqta oralig'i (6px/12px) aniq
          boshqarilishi kerak, `dashed` esa uni brauzerga qoldiradi.
      -->
      <div
        v-if="!node.isLast"
        class="flex h-[26px] items-center justify-center"
        aria-hidden="true"
      >
        <span
          class="h-full w-1 rounded-sm"
          :style="
            node.segmentDone
              ? { background: 'var(--color-green-500)' }
              : {
                background:
                  'repeating-linear-gradient(180deg, var(--color-line) 0 6px, transparent 6px 12px)',
              }
          "
        />
      </div>
    </template>
  </div>
</template>
