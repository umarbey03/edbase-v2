<script setup lang="ts">
import { computed, useId } from 'vue'

import type { MascotMood } from '../model/types'

/**
 * ════════════════════════════════════════════════════════════════════════
 * «NURI» — OLTIN TOMCHI (2026-10-29, loyiha egasi tanlagan «A» yo'nalish)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Nuri — nur tomchisi: "nur" yorug'lik degani, ZIN-NUR nomi bilan bir
 * ma'noda. 🔴 Platformada diniy atama va ramz UMUMAN ishlatilmaydi
 * (loyiha egasi qarori) — tomchi va nur betaraf obraz.
 *
 * ★ BITTA TANA, OLTI KAYFIYAT. Kayfiyat to'rt tutqich bilan beriladi:
 *   1) tana shakli (uchi tik / oldinga / orqaga / egilgan),
 *   2) yuz (ko'z, qosh, og'iz),
 *   3) qo'llar va kichik aksessuar (uchqun, o'y nuqtalari, "z"),
 *   4) NUR KUCHI — orqadagi halqa va ichki yorug'lik (100% → 20%).
 *   O'quvchi qancha faol bo'lsa, Nuri shuncha yorqin yonadi.
 *
 * ★ LOYIHA EGASINING NAMUNASIDAN 4 TA FARQ (ataylab):
 *   1) «O'ylayotgan» da ko'z QISILMAYDI — qisilgan ko'z "hazil" deb
 *      o'qilardi. Ikkala ko'z ochiq, yuqoriga qaragan, bitta qosh baland.
 *   2) «Sog'ingan» da tana YASSI BO'LAKKA aylanmaydi — tomchi shakli
 *      saqlanadi (aks holda Nurini tanib bo'lmasdi): uchi pastga egilgan,
 *      tana biroz cho'kkan, nur xira.
 *   3) Qo'llar tana chetiga TEGIB turadi — havoda suzgan oval kichik
 *      o'lchamda "quloq" bo'lib ko'rinardi.
 *   4) IXCHAM rejim (`size` < 40 yoki `compact`): qo'l, aksessuar va
 *      yonoq olib tashlanadi, ko'zlar kattalashadi, kayfiyatni esa NUR
 *      HALQASINING RANGI aytadi — 24 px da yuz ifodasi o'qilmaydi.
 *
 * 🔴 RANGLAR TOKEN EMAS — QAT'IY QIYMAT: maskot yorug' panelda ham, to'q
 *    yashil sahnada ham BIR XIL rangda turishi kerak (logotip kabi).
 *    Palitra pastda, `<style>` da, bitta joyda.
 *
 * ★ ANIMATSIYA: kayfiyatga xos tana harakati (sakrash / suzish / nafas),
 *   uchining tebranishi, nur pulsatsiyasi, ko'z pirpiratish va kayfiyat
 *   aksessuari. `prefers-reduced-motion` da HAMMASI o'chadi va har element
 *   o'zining bazaviy (to'g'ri chizilgan) holatida qoladi.
 *
 * O'lcham: `size` (px) berilsa shu; berilmasa o'ramni to'liq egallaydi
 * (`class="size-40"` kabi chaqiruvchi beradi). viewBox — 160×180.
 */
const props = withDefaults(
  defineProps<{
    mood?: MascotMood
    /** Kenglik, px. Balandlik viewBox nisbatidan (160×180) olinadi. */
    size?: number
    /** Ixcham rejim majburan. Berilmasa `size` < 40 da o'zi yoqiladi. */
    compact?: boolean
    /**
     * Skrinrider uchun matn. Berilmasa maskot BEZAK (`aria-hidden`) —
     * odatda uning yonida gapning o'zi matn sifatida turadi.
     */
    label?: string
  }>(),
  { mood: 'happy', size: undefined, compact: undefined, label: undefined },
)

type Eyes = 'open' | 'up' | 'joy' | 'closed'
type Motion = 'hop' | 'bob' | 'breathe'
type Sway = 'sway' | 'sway-sm' | 'none'

interface Pose {
  /** Tana yo'li — uchi kayfiyatni aytadi. Pastki qismi hamma joyda bir xil. */
  body: string
  /** Yuz markazi (ichki nur va krem doira). */
  face: { x: number, y: number, r: number }
  eyes: Eyes
  /** Qosh yo'llari (bo'sh — qoshsiz). */
  brows: string[]
  mouth: { d: string, filled: boolean }
  /** Nur kuchi 0..1: halqa shaffofligi va ichki yorug'lik. */
  nur: number
  /** Ixcham rejimdagi halqa rangi — kayfiyatni rang aytadi. */
  ring: string
  motion: Motion
  sway: Sway
  /** Tana harakati davomiyligi (s). */
  tempo: number
}

const BASE_TAIL = 'C130 142 108 164 80 164 C52 164 30 142 30 114'

const POSES: Record<MascotMood, Pose> = {
  celebrate: {
    body: `M80 4 C92 34 130 76 130 114 ${BASE_TAIL} C30 76 68 34 80 4 Z`,
    face: { x: 78, y: 119, r: 29 },
    eyes: 'joy',
    brows: [],
    mouth: { d: 'M66 124 Q79 145 92 124 Z', filled: true },
    nur: 1,
    ring: '#ffd25e',
    motion: 'hop',
    sway: 'sway',
    tempo: 1.6,
  },
  happy: {
    body: `M80 14 C92 40 130 78 130 114 ${BASE_TAIL} C30 78 68 40 80 14 Z`,
    face: { x: 78, y: 119, r: 26 },
    eyes: 'open',
    brows: [],
    mouth: { d: 'M70 127 Q79 138 88 127 Z', filled: true },
    nur: 0.8,
    ring: '#ffe08a',
    motion: 'bob',
    sway: 'sway',
    tempo: 3.8,
  },
  encouraging: {
    body: `M96 14 C100 44 130 80 130 114 ${BASE_TAIL} C30 76 74 46 96 14 Z`,
    face: { x: 82, y: 119, r: 26 },
    eyes: 'open',
    brows: ['M62 102 L76 106', 'M104 102 L90 106'],
    mouth: { d: 'M73 128 Q85 137 95 126', filled: false },
    nur: 0.75,
    ring: '#9fd9bf',
    motion: 'bob',
    sway: 'sway-sm',
    tempo: 3.2,
  },
  thinking: {
    body: `M62 16 C88 32 130 76 130 114 ${BASE_TAIL} C30 82 58 52 62 16 Z`,
    face: { x: 78, y: 119, r: 24 },
    eyes: 'up',
    // Chap qosh baland (savol), o'ng qosh tekis — ko'z qisilmaydi.
    brows: ['M56 100 Q64 92 73 98', 'M86 104 L98 103'],
    mouth: { d: 'M72 131 Q79 127 88 132', filled: false },
    nur: 0.6,
    ring: '#c9b8f2',
    motion: 'bob',
    sway: 'sway-sm',
    tempo: 3.8,
  },
  concerned: {
    body: 'M58 44 C90 56 128 86 128 120 C128 146 107 164 80 164 C53 164 32 146 32 120 C32 92 54 74 58 44 Z',
    face: { x: 78, y: 127, r: 21 },
    eyes: 'open',
    brows: ['M58 111 L71 106', 'M100 111 L87 106'],
    mouth: { d: 'M72 138 Q79 132 86 138', filled: false },
    nur: 0.4,
    ring: '#f2b48a',
    motion: 'breathe',
    sway: 'none',
    tempo: 4.4,
  },
  sleepy: {
    // Tomchi shakli SAQLANADI: uchi chapga-pastga egilgan, tana cho'kkan.
    body: 'M40 52 C70 58 130 88 130 122 C130 147 108 164 80 164 C52 164 30 147 30 122 C30 96 48 80 40 52 Z',
    face: { x: 79, y: 129, r: 22 },
    eyes: 'closed',
    brows: [],
    mouth: { d: 'M79 141 m-3.5 0 a3.5 4 0 1 0 7 0 a3.5 4 0 1 0 -7 0', filled: true },
    nur: 0.2,
    ring: '#c4ccc8',
    motion: 'breathe',
    sway: 'none',
    tempo: 5.6,
  },
}

const pose = computed(() => POSES[props.mood])
/** Gradient id — sahifada bir nechta Nuri bo'lsa ham to'qnashmasin. */
const haloId = `nuri-halo-${useId()}`
const isCompact = computed(() => props.compact ?? (props.size !== undefined && props.size < 40))

const sizeStyle = computed(() =>
  props.size === undefined ? undefined : { width: `${props.size}px`, height: `${(props.size * 180) / 160}px` },
)

/**
 * Ichki (och) tana — tashqi konturdan 10% kichik va 4px chapga: o'ng va
 * pastda to'q kontur qalinroq qoladi, chapda esa ingichka (yorug'lik chapdan).
 */
const innerTransform = computed(() => {
  const y = props.mood === 'sleepy' ? 122 : props.mood === 'concerned' ? 104 : 100
  return `translate(80 ${y}) scale(0.9) translate(-84 ${-y})`
})

/** Ko'zlar: markazlar yuz markazidan hisoblanadi. */
const eyePos = computed(() => {
  const { x, y, r } = pose.value.face
  const gap = isCompact.value ? 15 : 13
  return { lx: x - gap, rx: x + gap, y: y - r * 0.24 }
})

/** Ixcham rejimda ko'z kattaroq — 24 px da ham ko'rinsin. */
const eyeR = computed(() => (isCompact.value ? { rx: 7, ry: 9 } : { rx: 5, ry: 7 }))

/** To'rt uchli uchqun (yulduzcha) — markaz va radius bo'yicha. */
function sparkle(cx: number, cy: number, r: number): string {
  return `M${cx} ${cy - r} Q${cx} ${cy} ${cx + r} ${cy} Q${cx} ${cy} ${cx} ${cy + r} `
    + `Q${cx} ${cy} ${cx - r} ${cy} Q${cx} ${cy} ${cx} ${cy - r} Z`
}
</script>

<template>
  <svg
    class="mascot"
    :class="[`mascot--${mood}`, { 'size-full': size === undefined, 'mascot--compact': isCompact }]"
    :style="[sizeStyle, { '--m-tempo': `${pose.tempo}s` }]"
    viewBox="0 0 160 180"
    fill="none"
    :role="label === undefined ? undefined : 'img'"
    :aria-label="label"
    :aria-hidden="label === undefined ? 'true' : undefined"
  >
    <!-- Yer soyasi — tana bilan birga sakramaydi, faqat torayadi. -->
    <ellipse
      v-if="!isCompact"
      class="m-shadow"
      cx="80"
      cy="172"
      :rx="mood === 'sleepy' ? 42 : 36"
      ry="4"
      fill="#000"
      opacity="0.1"
    />

    <g :class="`m-motion m-${pose.motion}`">
      <!-- ═══ NUR HALQASI — kayfiyatning «kuchi» ═══ -->
      <g class="m-halo">
        <template v-if="isCompact">
          <circle
            cx="80"
            cy="104"
            r="78"
            :fill="pose.ring"
          />
        </template>
        <!--
          ★ Yumshoq nur (radial gradient), qattiq disk EMAS: to'q yashil va
            qorong'i fonda disk loyqa kulrang dog' bo'lib qolardi, gradient
            esa har qanday fonda "yorug'lik" bo'lib o'qiladi.
        -->
        <template v-else>
          <defs>
            <radialGradient :id="haloId">
              <stop
                offset="0.45"
                stop-color="var(--m-halo)"
                :stop-opacity="0.25 + pose.nur * 0.6"
              />
              <stop
                offset="1"
                stop-color="var(--m-halo)"
                stop-opacity="0"
              />
            </radialGradient>
          </defs>
          <circle
            cx="80"
            cy="110"
            :r="58 + pose.nur * 22"
            :fill="`url(#${haloId})`"
          />
        </template>
      </g>

      <!-- ═══ AKSESSUARLAR (ixcham rejimda yo'q) ═══ -->
      <template v-if="!isCompact">
        <template v-if="mood === 'celebrate'">
          <path
            class="m-twinkle"
            :d="sparkle(22, 52, 8)"
            fill="var(--m-gold)"
          />
          <path
            class="m-twinkle m-delay-1"
            :d="sparkle(141, 36, 7)"
            fill="var(--m-gold)"
          />
          <path
            class="m-twinkle m-delay-2"
            :d="sparkle(149, 128, 5)"
            fill="var(--m-gold)"
          />
          <circle
            cx="44"
            cy="24"
            r="2.6"
            fill="var(--m-tongue)"
          />
          <circle
            cx="118"
            cy="12"
            r="2.4"
            fill="#4cc597"
          />
        </template>
        <path
          v-else-if="mood === 'encouraging'"
          class="m-lines"
          d="M148 70 l6 -5 M152 84 h8 M144 58 l3 -7"
          stroke="var(--m-gold-shade)"
          stroke-width="3"
          stroke-linecap="round"
        />
        <g v-else-if="mood === 'thinking'">
          <circle
            class="m-dot"
            cx="122"
            cy="50"
            r="2.8"
            fill="#9db4a8"
          />
          <circle
            class="m-dot m-delay-1"
            cx="133"
            cy="38"
            r="3.8"
            fill="#9db4a8"
          />
          <circle
            class="m-dot m-delay-2"
            cx="146"
            cy="23"
            r="5.2"
            fill="#9db4a8"
          />
        </g>
        <g
          v-else-if="mood === 'sleepy'"
          stroke="#7c9a8c"
          stroke-width="2.6"
          stroke-linecap="round"
          stroke-linejoin="round"
        >
          <path
            class="m-z"
            d="M110 62 h9 l-9 10 h9"
          />
          <path
            class="m-z m-delay-2"
            d="M126 38 h12 l-12 13 h12"
          />
        </g>

        <!-- Orqadagi qo'llar — tana chetiga tegib turadi (tana ostida). -->
        <template v-if="mood === 'celebrate'">
          <ellipse
            cx="31"
            cy="88"
            rx="6.5"
            ry="11"
            fill="var(--m-gold-shade)"
            transform="rotate(-28 31 88)"
          />
          <ellipse
            cx="129"
            cy="88"
            rx="6.5"
            ry="11"
            fill="var(--m-gold-shade)"
            transform="rotate(28 129 88)"
          />
        </template>
        <ellipse
          v-else-if="mood === 'happy' || mood === 'encouraging'"
          cx="33"
          cy="130"
          rx="6"
          ry="9"
          fill="var(--m-gold-shade)"
        />
        <g
          v-if="mood === 'happy'"
          class="m-wave"
        >
          <ellipse
            cx="133"
            cy="100"
            rx="6"
            ry="10"
            fill="var(--m-gold-shade)"
            transform="rotate(-35 133 100)"
          />
        </g>
      </template>

      <!-- ═══ TANA ═══ -->
      <g :class="pose.sway === 'none' ? undefined : `m-${pose.sway}`">
        <g :opacity="mood === 'sleepy' ? 0.9 : 1">
          <path
            :d="pose.body"
            fill="var(--m-gold-shade)"
          />
          <path
            :d="pose.body"
            :fill="mood === 'sleepy' ? 'var(--m-gold-dim)' : 'var(--m-gold)'"
            :transform="innerTransform"
          />
        </g>

        <!-- Ichki nur va yuz doirasi -->
        <g class="m-glow">
          <ellipse
            :cx="pose.face.x"
            :cy="pose.face.y - 1"
            :rx="pose.face.r + 9"
            :ry="pose.face.r + 7"
            fill="var(--m-glass-glow)"
            :opacity="0.45 + pose.nur * 0.55"
          />
          <ellipse
            :cx="pose.face.x"
            :cy="pose.face.y + 1"
            :rx="pose.face.r"
            :ry="pose.face.r - 2"
            fill="var(--m-glass)"
          />
        </g>

        <!-- Yaltiroq chiziq -->
        <ellipse
          v-if="!isCompact"
          :cx="pose.face.x - 24"
          :cy="pose.face.y - 28"
          rx="4"
          ry="11"
          fill="#fff"
          opacity="0.7"
          :transform="`rotate(22 ${pose.face.x - 24} ${pose.face.y - 28})`"
        />

        <!-- ═══ KO'ZLAR ═══ -->
        <g
          v-if="pose.eyes === 'open' || pose.eyes === 'up'"
          class="m-eyes"
        >
          <template
            v-for="ex in [eyePos.lx, eyePos.rx]"
            :key="ex"
          >
            <ellipse
              :cx="ex"
              :cy="eyePos.y"
              :rx="eyeR.rx"
              :ry="eyeR.ry"
              fill="var(--m-ink)"
            />
            <!-- `up` — yaltiroq nuqta yuqori-o'ngda: nigoh tepaga, o'yga. -->
            <circle
              :cx="ex + (pose.eyes === 'up' ? 2.4 : 1.6)"
              :cy="eyePos.y - (pose.eyes === 'up' ? 4 : 2.6)"
              :r="isCompact ? 2.4 : 1.8"
              fill="#fff"
            />
          </template>
        </g>
        <g
          v-else
          stroke="var(--m-ink)"
          :stroke-width="isCompact ? 4.5 : 3.2"
          stroke-linecap="round"
          fill="none"
        >
          <template
            v-for="ex in [eyePos.lx, eyePos.rx]"
            :key="ex"
          >
            <!-- `joy` — ^ ^ (quvonchdan yumilgan), `closed` — pastga yoy. -->
            <path
              v-if="pose.eyes === 'joy'"
              :d="`M${ex - 6} ${eyePos.y + 2} Q${ex} ${eyePos.y - 7} ${ex + 6} ${eyePos.y + 2}`"
            />
            <path
              v-else
              :d="`M${ex - 6} ${eyePos.y} Q${ex} ${eyePos.y + 5} ${ex + 6} ${eyePos.y}`"
            />
          </template>
        </g>

        <!-- Qoshlar -->
        <path
          v-for="d in pose.brows"
          :key="d"
          :d="d"
          stroke="var(--m-ink)"
          stroke-width="3"
          stroke-linecap="round"
        />

        <!-- Yonoqlar -->
        <template v-if="!isCompact && (mood === 'celebrate' || mood === 'happy')">
          <ellipse
            cx="55"
            :cy="pose.face.y + 6"
            rx="6"
            ry="3.5"
            fill="var(--m-blush)"
            opacity="0.6"
          />
          <ellipse
            cx="101"
            :cy="pose.face.y + 6"
            rx="6"
            ry="3.5"
            fill="var(--m-blush)"
            opacity="0.6"
          />
        </template>

        <!-- Og'iz -->
        <path
          v-if="pose.mouth.filled"
          :d="pose.mouth.d"
          fill="var(--m-mouth)"
        />
        <path
          v-else
          :d="pose.mouth.d"
          stroke="var(--m-mouth)"
          :stroke-width="isCompact ? 4 : 3.2"
          stroke-linecap="round"
        />
        <ellipse
          v-if="mood === 'celebrate'"
          cx="79"
          :cy="pose.face.y + 14"
          rx="6"
          ry="3.2"
          fill="var(--m-tongue)"
        />

        <!-- Old qo'llar (tana ustida) -->
        <template v-if="!isCompact">
          <ellipse
            v-if="mood === 'thinking'"
            cx="66"
            cy="146"
            rx="8"
            ry="6.5"
            fill="var(--m-gold-shade)"
          />
          <template v-else-if="mood === 'concerned'">
            <ellipse
              cx="71"
              cy="153"
              rx="7"
              ry="5.5"
              fill="var(--m-gold-shade)"
            />
            <ellipse
              cx="87"
              cy="153"
              rx="7"
              ry="5.5"
              fill="var(--m-gold-shade)"
            />
          </template>
        </template>
      </g>

      <!-- Musht — tana chetida, «yur, boshladik» -->
      <g
        v-if="!isCompact && mood === 'encouraging'"
        class="m-pump"
      >
        <circle
          cx="133"
          cy="96"
          r="10"
          fill="var(--m-gold-shade)"
        />
        <circle
          cx="131"
          cy="94"
          r="7"
          fill="var(--m-gold)"
        />
      </g>
    </g>
  </svg>
</template>

<style scoped>
/*
  ════════════════════════════════════════════════════════════════════
  PALITRA — personajning BUTUN rangi shu yerda (izoh: yuqorida, 🔴 qism)
  ════════════════════════════════════════════════════════════════════
*/
.mascot {
  --m-gold: #efbd4e;        /* tana */
  --m-gold-shade: #cf972b;  /* kontur, qo'llar — oq fonda siluetni ushlaydi */
  --m-gold-dim: #e3bb62;    /* «sog'ingan» — xira oltin */
  --m-glass: #fff6df;       /* yuz doirasi */
  --m-glass-glow: #ffdf8a;  /* ichki nur */
  --m-halo: #ffeeb8;
  --m-ink: #1d2a24;         /* ko'z, qosh — sof qora emas, yumshoq */
  --m-mouth: #5a2320;
  --m-tongue: #ff8f7e;
  --m-blush: #ff9d8c;

  display: block;
  overflow: visible;
}

/* Tana harakati — yer (80, 164) atrofida, soya bilan bir maromda. */
.m-motion,
.m-sway,
.m-sway-sm {
  transform-box: view-box;
  transform-origin: 80px 164px;
}
.m-hop { animation: m-hop var(--m-tempo) ease-in-out infinite; }
.m-bob { animation: m-bob var(--m-tempo) ease-in-out infinite; }
.m-breathe { animation: m-breathe var(--m-tempo) ease-in-out infinite; }
.m-sway { animation: m-sway 2.6s ease-in-out infinite; }
.m-sway-sm { animation: m-sway-sm 2.6s ease-in-out infinite; }

.m-shadow,
.m-halo,
.m-glow,
.m-eyes,
.m-twinkle,
.m-dot,
.m-z {
  transform-box: fill-box;
  transform-origin: center;
}
.m-shadow { animation: m-shadow var(--m-tempo) ease-in-out infinite; }
.m-halo { animation: m-halo 3s ease-in-out infinite; }
.m-glow { animation: m-glow 3s ease-in-out infinite; }
.mascot--celebrate .m-halo,
.mascot--celebrate .m-glow { animation-duration: 2.2s; }
.mascot--sleepy .m-halo,
.mascot--sleepy .m-glow { animation-duration: 5.6s; }
.m-eyes { animation: m-blink 5.4s ease-in-out 1.2s infinite; }
.m-twinkle { animation: m-twinkle 1.8s ease-in-out infinite; }
.m-dot { animation: m-dots 2.4s ease-in-out infinite; }
.m-z { animation: m-z 3.2s ease-in-out infinite; }
.m-delay-1 { animation-delay: 0.6s; }
.m-delay-2 { animation-delay: 1.1s; }

.m-wave {
  transform-box: view-box;
  transform-origin: 126px 110px;
  animation: m-wave 1.3s ease-in-out infinite;
}
.m-pump { animation: m-pump 2.6s ease-in-out infinite; }

/* Ixcham rejimda faqat nur pulsatsiyasi — kichik belgida tebranish bezovta qiladi. */
.mascot--compact .m-motion,
.mascot--compact .m-sway,
.mascot--compact .m-sway-sm,
.mascot--compact .m-glow { animation: none; }

@keyframes m-bob { 0%, 100% { transform: translateY(0) scale(1, 1); } 50% { transform: translateY(-5px) scale(0.985, 1.015); } }
@keyframes m-hop {
  0%, 80%, 100% { transform: translateY(0) scale(1, 1); }
  12% { transform: translateY(0) scale(1.06, 0.92); }
  38% { transform: translateY(-14px) scale(0.95, 1.06); }
  62% { transform: translateY(0) scale(1.05, 0.94); }
}
@keyframes m-breathe { 0%, 100% { transform: scale(1, 1); } 50% { transform: scale(1.03, 0.96); } }
@keyframes m-sway { 0%, 100% { transform: skewX(0); } 30% { transform: skewX(-3.5deg); } 70% { transform: skewX(3.5deg); } }
@keyframes m-sway-sm { 0%, 100% { transform: skewX(0); } 50% { transform: skewX(-1.5deg); } }
@keyframes m-glow { 0%, 100% { transform: scale(1); opacity: 1; } 50% { transform: scale(0.94); opacity: 0.82; } }
@keyframes m-halo { 0%, 100% { transform: scale(1); opacity: 1; } 50% { transform: scale(1.07); opacity: 0.65; } }
@keyframes m-shadow { 0%, 100% { transform: scaleX(1); opacity: 1; } 50% { transform: scaleX(0.88); opacity: 0.7; } }
@keyframes m-blink { 0%, 93%, 100% { transform: scaleY(1); } 96% { transform: scaleY(0.1); } }
@keyframes m-wave { 0%, 100% { transform: rotate(0); } 50% { transform: rotate(-22deg); } }
@keyframes m-pump { 0%, 70%, 100% { transform: translateY(0); } 80% { transform: translateY(-6px); } }
@keyframes m-twinkle { 0%, 100% { transform: scale(1); opacity: 1; } 50% { transform: scale(0.5); opacity: 0.5; } }
@keyframes m-z { 0%, 100% { transform: translate(0, 0); opacity: 1; } 50% { transform: translate(3px, -5px); opacity: 0.4; } }
@keyframes m-dots { 0%, 100% { opacity: 1; } 50% { opacity: 0.35; } }

/*
  Global qoida (`style.css`) animatsiyani 0.01ms ga qisqartiradi; bu yerda
  esa butunlay o'chiriladi — har element bazaviy holatida qoladi.
*/
@media (prefers-reduced-motion: reduce) {
  .mascot * {
    animation: none !important;
  }
}
</style>
