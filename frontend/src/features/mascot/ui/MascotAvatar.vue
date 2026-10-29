<script setup lang="ts">
import { computed } from 'vue'

import type { MascotMood } from '../model/types'

/**
 * ════════════════════════════════════════════════════════════════════════
 * «NURI» — FONUS-MASKOT (2026-10-29, eski boyqushning o'rniga)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasining bahosi: eski boyqush "soxta, juda oddiy, chalkash va
 * noprofessional" edi. Yangi personaj — sharqona FONUS (chiroq), ismi
 * hamon «Nuri», endi esa ism va shakl bir-biriga mos: ZIN-NUR = nur.
 *
 * ★ NEGA FONUS, HAYVON EMAS:
 *   1) MA'NO: markaz nomi "nur" — chiroq uni so'zsiz aytadi. Boyqush esa
 *      "bilim" ning G'arbcha ramzi va brend bilan bog'lanmasdi.
 *   2) ODOB: islomiy ta'lim markazida jonzot tasvirini bezak sifatida
 *      ishlatmaslikni afzal ko'radiganlar bor. Jonsiz buyum-personaj bu
 *      savolni umuman ochmaydi.
 *   3) KAYFIYAT TILI TAYYOR: olovning kattaligi va yorqinligi — o'quvchi
 *      holatining tabiiy metaforasi. Yutuqda olov baland, faollik
 *      yo'qligida xira. Bitta qarashda tushuniladi, matnsiz ham.
 *
 * ★ KOMPOZITSIYA (tepadan pastga): olov → piyoz-gumbaz (oltin) →
 *   zumrad korpus → ichida ISLOMIY RAVOQ shaklidagi yorug' oyna (yuz shu
 *   oynada) → oltin taglik. Uslub — tekis geometrik: har material 2-3
 *   ohangda (asosiy / soya / yaltiroq), gradient va kontur yo'q.
 *
 * ★ BITTA TANA, OLTI KAYFIYAT. O'zgaradiganlar faqat: ko'z, qosh, og'iz,
 *   qo'l holati, olov kattaligi, nur halqasi va kichik aksessuar
 *   (uchqun / "z" / o'y nuqtalari). Tana chizig'i HECH QACHON
 *   o'zgarmaydi — personaj har ekranda o'sha "Nuri" bo'lib tanilsin.
 *
 * 🔴 RANGLAR TOKEN EMAS — QAT'IY QIYMAT (eski boyqushdagi qarorning
 *    teskarisi, va bu ataylab): maskot ikki xil sirtda turadi — yorug'
 *    "Zumrad" panel va to'q yashil salomlashuv sahnasi (`.surface-brand`).
 *    U sirtda `--color-brand-*` SHAMPANGA aylanadi, `--color-green-*` esa
 *    yorug' panelda TO'Q bo'lib qoladi — ya'ni tokenga bog'langan personaj
 *    har ekranda boshqa rangda chiqardi. Personaj — logotip kabi: rangi
 *    sirtdan qat'i nazar BIR XIL. Palitra pastda, `<style>` dagi
 *    o'zgaruvchilarda, bitta joyda.
 *
 * ★ ANIMATSIYA: suzish, ko'z pirpiratish, olov lipillashi va kayfiyatga
 *   xos bitta harakat (qo'l silkitish, uchqun, "z" ko'tarilishi).
 *   `prefers-reduced-motion` da HAMMASI o'chadi va har element o'zining
 *   BAZAVIY (to'g'ri chizilgan) holatida qoladi — hech bir animatsiyada
 *   `fill-mode` yo'q.
 *
 * O'lcham: `size` (px) berilsa shu; berilmasa o'ramni to'liq egallaydi
 * (`class="size-40"` kabi chaqiruvchi beradi).
 */
const props = withDefaults(
  defineProps<{
    mood?: MascotMood
    /** Kenglik, px. Balandlik viewBox nisbatidan (160×180) olinadi. */
    size?: number
    /**
     * Skrinrider uchun matn. Berilmasa maskot BEZAK (`aria-hidden`) —
     * odatda uning yonida gapning o'zi matn sifatida turadi.
     */
    label?: string
  }>(),
  { mood: 'happy', size: undefined, label: undefined },
)

type Eyes = 'open' | 'joy' | 'closed' | 'up' | 'soft'
type Brows = 'neutral' | 'raised' | 'worried' | 'quizzical' | 'flat'
type Mouth = 'grin' | 'beam' | 'smile' | 'wobble' | 'o' | 'side'
type Arms = 'wave' | 'cheer' | 'pump' | 'clasp' | 'chin' | 'droop'

interface Pose {
  eyes: Eyes
  brows: Brows
  mouth: Mouth
  arms: Arms
  /** Olov masshtabi (1 — oddiy). */
  flame: number
  /** Orqadagi nur halqasining shaffofligi. */
  glow: number
}

/*
  ★ JADVAL — kayfiyat va uning ko'rinishi BITTA joyda. Yangi kayfiyat
  qo'shish = shu yerga bitta qator (+ kerak bo'lsa aksessuar).
*/
const POSES: Record<MascotMood, Pose> = {
  happy: { eyes: 'open', brows: 'neutral', mouth: 'grin', arms: 'wave', flame: 1, glow: 0.22 },
  celebrate: { eyes: 'joy', brows: 'raised', mouth: 'beam', arms: 'cheer', flame: 1.22, glow: 0.34 },
  encouraging: { eyes: 'open', brows: 'raised', mouth: 'smile', arms: 'pump', flame: 1.05, glow: 0.2 },
  concerned: { eyes: 'soft', brows: 'worried', mouth: 'wobble', arms: 'clasp', flame: 0.82, glow: 0.12 },
  sleepy: { eyes: 'closed', brows: 'flat', mouth: 'o', arms: 'droop', flame: 0.6, glow: 0.07 },
  thinking: { eyes: 'up', brows: 'quizzical', mouth: 'side', arms: 'chin', flame: 1, glow: 0.16 },
}

const pose = computed(() => POSES[props.mood])

const sizeStyle = computed(() =>
  props.size === undefined ? undefined : { width: `${props.size}px`, height: `${(props.size * 180) / 160}px` },
)

/** Olov o'z TAGI (80, 40) atrofida kattalashadi — gumbazdan uzilmasin. */
const flameTransform = computed(() => {
  const s = pose.value.flame
  return `translate(80 40) scale(${s}) translate(-80 -40)`
})

/** To'rt uchli uchqun (yulduzcha) — markaz va radius bo'yicha. */
function sparkle(cx: number, cy: number, r: number): string {
  return `M${cx} ${cy - r} Q${cx} ${cy} ${cx + r} ${cy} Q${cx} ${cy} ${cx} ${cy + r} `
    + `Q${cx} ${cy} ${cx - r} ${cy} Q${cx} ${cy} ${cx} ${cy - r} Z`
}
</script>

<template>
  <svg
    class="mascot"
    :class="[`mascot--${mood}`, { 'size-full': size === undefined }]"
    :style="sizeStyle"
    viewBox="0 0 160 180"
    fill="none"
    :role="label === undefined ? undefined : 'img'"
    :aria-label="label"
    :aria-hidden="label === undefined ? 'true' : undefined"
  >
    <!-- Yer soyasi — suzish bilan birga qimirlamaydi (tashqarida). -->
    <ellipse
      class="m-shadow"
      cx="80"
      cy="173"
      rx="42"
      ry="4.5"
    />

    <g class="m-bob">
      <!-- Nur halqasi — kayfiyatga qarab yorqinlashadi yoki so'nadi. -->
      <circle
        class="m-glow"
        cx="80"
        cy="104"
        r="72"
        :opacity="pose.glow"
      />
      <circle
        class="m-glow"
        cx="80"
        cy="104"
        r="54"
        :opacity="pose.glow"
      />

      <!-- ═════════════ AKSESSUARLAR (orqa reja) ═════════════ -->
      <g
        v-if="mood === 'celebrate'"
        class="m-sparkles"
      >
        <path
          class="m-gold m-twinkle"
          :d="sparkle(24, 56, 8)"
        />
        <path
          class="m-gold m-twinkle m-twinkle--2"
          :d="sparkle(138, 38, 6)"
        />
        <path
          class="m-gold m-twinkle m-twinkle--3"
          :d="sparkle(148, 124, 5)"
        />
        <path
          class="m-gold m-twinkle m-twinkle--2"
          :d="sparkle(12, 118, 4.5)"
        />
        <circle
          class="m-coral"
          cx="40"
          cy="30"
          r="2.6"
        />
        <circle
          class="m-mint"
          cx="122"
          cy="18"
          r="2.4"
        />
        <rect
          class="m-mint"
          x="132"
          y="80"
          width="5"
          height="5"
          rx="1.2"
          transform="rotate(24 134.5 82.5)"
        />
      </g>

      <g
        v-else-if="mood === 'sleepy'"
        class="m-zzz"
      >
        <path
          class="m-z m-z--1"
          d="M114 50 h9 l-9 10 h9"
        />
        <path
          class="m-z m-z--2"
          d="M130 28 h12 l-12 13 h12"
        />
      </g>

      <g
        v-else-if="mood === 'thinking'"
        class="m-dots"
      >
        <circle
          class="m-thought"
          cx="122"
          cy="54"
          r="2.6"
        />
        <circle
          class="m-thought"
          cx="132"
          cy="42"
          r="3.6"
        />
        <circle
          class="m-thought"
          cx="145"
          cy="27"
          r="5"
        />
      </g>

      <g
        v-else-if="mood === 'encouraging'"
        class="m-burst"
      >
        <path
          class="m-stroke-gold"
          d="M150 70 l6 -4 M152 82 h7 M146 60 l3 -6"
        />
      </g>

      <!-- ═════════════ OLOV ═════════════ -->
      <g :transform="flameTransform">
        <g
          class="m-flame"
          :class="{ 'm-flame--dim': mood === 'sleepy' }"
        >
          <path
            class="m-flame-outer"
            d="M80 2 C87 12 93 18 93 25 C93 32 87 37 80 37 C73 37 67 32 67 25 C67 18 73 12 80 2 Z"
          />
          <path
            class="m-flame-inner"
            d="M80 13 C84 19 87 22 87 27 C87 31 84 34 80 34 C76 34 73 31 73 27 C73 22 76 19 80 13 Z"
          />
          <ellipse
            class="m-flame-core"
            cx="80"
            cy="29"
            rx="3"
            ry="4"
          />
        </g>
      </g>

      <!-- ═════════════ GUMBAZ ═════════════ -->
      <circle
        class="m-gold"
        cx="80"
        cy="40"
        r="4"
      />
      <path
        class="m-gold"
        d="M50 64 C48 54 60 48 70 44 C75 42 78 40 80 38 C82 40 85 42 90 44 C100 48 112 54 110 64 Z"
      />
      <path
        class="m-gold-shade"
        d="M80 38 C82 40 85 42 90 44 C100 48 112 54 110 64 H96 C97 54 90 46 80 38 Z"
      />
      <path
        class="m-gold-light"
        d="M58 58 C58 52 64 48 70 46"
        stroke-width="3"
        stroke-linecap="round"
      />

      <!-- ═════════════ KORPUS ═════════════ -->
      <path
        class="m-body"
        d="M44 66 H116 C126 66 132 74 132 86 V142 C132 153 125 160 113 160 H47 C35 160 28 153 28 142 V86 C28 74 34 66 44 66 Z"
      />
      <path
        class="m-body-shade"
        d="M112 66 H116 C126 66 132 74 132 86 V142 C132 153 125 160 113 160 H106 C117 155 122 146 122 132 V90 C122 78 119 70 112 66 Z"
      />
      <rect
        class="m-body-light"
        x="34"
        y="82"
        width="5"
        height="44"
        rx="2.5"
      />

      <!-- Tepa belbog' — gumbaz va korpus orasidagi oltin halqa. -->
      <rect
        class="m-gold"
        x="38"
        y="60"
        width="84"
        height="10"
        rx="5"
      />
      <path
        class="m-gold-shade"
        d="M104 60 H117 C119.8 60 122 62.2 122 65 C122 67.8 119.8 70 117 70 H104 Z"
      />
      <circle
        class="m-cream"
        cx="62"
        cy="65"
        r="1.6"
      />
      <circle
        class="m-cream"
        cx="80"
        cy="65"
        r="1.6"
      />
      <circle
        class="m-cream"
        cx="98"
        cy="65"
        r="1.6"
      />

      <!-- ═════════════ RAVOQ-OYNA (yuz) ═════════════ -->
      <path
        class="m-glass"
        d="M46 146 V100 C46 87 61 79 80 72 C99 79 114 87 114 100 V146 C114 149 112 151 109 151 H51 C48 151 46 149 46 146 Z"
      />
      <ellipse
        class="m-glass-glow"
        cx="80"
        cy="118"
        rx="27"
        ry="25"
        :opacity="0.35 + pose.glow"
      />
      <path
        class="m-frame"
        d="M46 146 V100 C46 87 61 79 80 72 C99 79 114 87 114 100 V146 C114 149 112 151 109 151 H51 C48 151 46 149 46 146 Z"
        stroke-width="3"
      />
      <path
        class="m-glint"
        d="M53 104 C53 96 58 90 65 86"
        stroke-width="3"
        stroke-linecap="round"
      />

      <!-- Yonoqlar -->
      <ellipse
        class="m-blush"
        cx="58"
        cy="125"
        rx="6"
        ry="3.4"
      />
      <ellipse
        class="m-blush"
        cx="102"
        cy="125"
        rx="6"
        ry="3.4"
      />

      <!-- ── Qoshlar ── -->
      <g
        class="m-ink-stroke"
        stroke-width="3"
        stroke-linecap="round"
      >
        <template v-if="pose.brows === 'neutral'">
          <path d="M59 99 Q65.5 95 72 98" />
          <path d="M88 98 Q94.5 95 101 99" />
        </template>
        <template v-else-if="pose.brows === 'raised'">
          <path d="M59 95 Q65.5 90 72 93" />
          <path d="M88 93 Q94.5 90 101 95" />
        </template>
        <template v-else-if="pose.brows === 'worried'">
          <path d="M59 100 Q66 99 72 94" />
          <path d="M88 94 Q94 99 101 100" />
        </template>
        <template v-else-if="pose.brows === 'quizzical'">
          <path d="M59 99 Q65.5 97 72 99" />
          <path d="M88 94 Q94.5 89 101 93" />
        </template>
        <template v-else>
          <path d="M60 102 H71" />
          <path d="M89 102 H100" />
        </template>
      </g>

      <!-- ── Ko'zlar ── -->
      <g
        v-if="pose.eyes === 'open' || pose.eyes === 'soft' || pose.eyes === 'up'"
        class="m-eyes"
      >
        <ellipse
          class="m-ink"
          cx="66"
          cy="111"
          rx="6"
          :ry="pose.eyes === 'soft' ? 6.4 : 7.5"
        />
        <ellipse
          class="m-ink"
          cx="94"
          cy="111"
          rx="6"
          :ry="pose.eyes === 'soft' ? 6.4 : 7.5"
        />
        <!-- Yaltiroq nuqta: 'up' da yuqori-o'ngga siljiydi (o'ylab qaraydi). -->
        <circle
          class="m-white"
          :cx="pose.eyes === 'up' ? 69 : 68"
          :cy="pose.eyes === 'up' ? 106.5 : 108"
          r="2.2"
        />
        <circle
          class="m-white"
          :cx="pose.eyes === 'up' ? 97 : 96"
          :cy="pose.eyes === 'up' ? 106.5 : 108"
          r="2.2"
        />
        <circle
          v-if="pose.eyes !== 'up'"
          class="m-white"
          cx="64"
          cy="114"
          r="1"
        />
        <circle
          v-if="pose.eyes !== 'up'"
          class="m-white"
          cx="92"
          cy="114"
          r="1"
        />
      </g>
      <g
        v-else
        class="m-ink-stroke"
        stroke-width="3.2"
        stroke-linecap="round"
      >
        <!-- 'joy' — tepaga qayrilgan yoy (^ ^); 'closed' — pastga (mudroq). -->
        <template v-if="pose.eyes === 'joy'">
          <path d="M59 113 Q66 104 73 113" />
          <path d="M87 113 Q94 104 101 113" />
        </template>
        <template v-else>
          <path d="M59 110 Q66 116 73 110" />
          <path d="M87 110 Q94 116 101 110" />
        </template>
      </g>

      <!-- ── Og'iz ── -->
      <template v-if="pose.mouth === 'grin' || pose.mouth === 'beam'">
        <path
          class="m-mouth"
          :d="pose.mouth === 'beam'
            ? 'M68 123 C69 137 91 137 92 123 C85 125 75 125 68 123 Z'
            : 'M71 124 C72 133 88 133 89 124 C84 125.5 76 125.5 71 124 Z'"
        />
        <ellipse
          class="m-tongue"
          cx="80"
          :cy="pose.mouth === 'beam' ? 131.5 : 129.8"
          :rx="pose.mouth === 'beam' ? 6 : 4.2"
          :ry="pose.mouth === 'beam' ? 2.6 : 1.8"
        />
      </template>
      <path
        v-else-if="pose.mouth === 'smile'"
        class="m-ink-stroke"
        d="M72 125 Q80 132 88 125"
        stroke-width="3"
        stroke-linecap="round"
      />
      <path
        v-else-if="pose.mouth === 'wobble'"
        class="m-ink-stroke"
        d="M73 128 Q76.5 125 80 127 Q83.5 129 87 126"
        stroke-width="2.8"
        stroke-linecap="round"
      />
      <ellipse
        v-else-if="pose.mouth === 'o'"
        class="m-mouth"
        cx="80"
        cy="127"
        rx="3"
        ry="3.6"
      />
      <path
        v-else
        class="m-ink-stroke"
        d="M75 127 Q82 129 88 124"
        stroke-width="3"
        stroke-linecap="round"
      />

      <!-- ═════════════ QO'LLAR ═════════════ -->
      <!--
        Qo'l — yumaloq uchli qalin chiziq + oltin "kaft". Yelka nuqtalari
        o'zgarmaydi (30,112) / (130,112); faqat yo'nalishi kayfiyatga bog'liq.
      -->
      <g
        class="m-arm-stroke"
        stroke-width="10"
        stroke-linecap="round"
      >
        <template v-if="pose.arms === 'wave'">
          <path d="M31 114 Q20 126 23 140" />
          <g class="m-wave">
            <path d="M129 112 Q145 102 144 84" />
          </g>
        </template>
        <template v-else-if="pose.arms === 'cheer'">
          <path d="M31 110 Q16 98 18 78" />
          <path d="M129 110 Q144 98 142 78" />
        </template>
        <template v-else-if="pose.arms === 'pump'">
          <path d="M31 114 Q20 126 23 140" />
          <g class="m-pump">
            <path d="M129 114 Q148 110 146 90" />
          </g>
        </template>
        <template v-else-if="pose.arms === 'clasp'">
          <path d="M31 116 C31 136 50 148 70 149" />
          <path d="M129 116 C129 136 110 148 90 149" />
        </template>
        <template v-else-if="pose.arms === 'chin'">
          <path d="M31 114 Q20 126 23 140" />
          <path d="M129 116 C142 134 124 142 106 137" />
        </template>
        <template v-else>
          <path d="M31 118 Q25 132 29 146" />
          <path d="M129 118 Q135 132 131 146" />
        </template>
      </g>

      <!-- Kaftlar — chiziq uchiga qo'yiladigan oltin doiralar. -->
      <g class="m-hand">
        <template v-if="pose.arms === 'wave'">
          <circle
            cx="23"
            cy="140"
            r="6"
          />
          <g class="m-wave">
            <circle
              cx="144"
              cy="84"
              r="6.5"
            />
          </g>
        </template>
        <template v-else-if="pose.arms === 'cheer'">
          <circle
            cx="18"
            cy="78"
            r="6.5"
          />
          <circle
            cx="142"
            cy="78"
            r="6.5"
          />
        </template>
        <template v-else-if="pose.arms === 'pump'">
          <circle
            cx="23"
            cy="140"
            r="6"
          />
          <g class="m-pump">
            <circle
              cx="146"
              cy="89"
              r="7.5"
            />
          </g>
        </template>
        <template v-else-if="pose.arms === 'clasp'">
          <circle
            cx="73"
            cy="149"
            r="6"
          />
          <circle
            cx="87"
            cy="149"
            r="6"
          />
        </template>
        <template v-else-if="pose.arms === 'chin'">
          <circle
            cx="23"
            cy="140"
            r="6"
          />
          <circle
            cx="104"
            cy="136"
            r="6"
          />
        </template>
        <template v-else>
          <circle
            cx="29"
            cy="146"
            r="6"
          />
          <circle
            cx="131"
            cy="146"
            r="6"
          />
        </template>
      </g>

      <!-- ═════════════ TAGLIK ═════════════ -->
      <rect
        class="m-gold"
        x="38"
        y="157"
        width="84"
        height="9"
        rx="4.5"
      />
      <path
        class="m-gold-shade"
        d="M104 157 H117.5 C120 157 122 159 122 161.5 C122 164 120 166 117.5 166 H104 Z"
      />
      <rect
        class="m-gold-shade"
        x="48"
        y="165"
        width="14"
        height="5"
        rx="2"
      />
      <rect
        class="m-gold-shade"
        x="98"
        y="165"
        width="14"
        height="5"
        rx="2"
      />
    </g>
  </svg>
</template>

<style scoped>
/*
  ════════════════════════════════════════════════════════════════════
  PALITRA — personajning BUTUN rangi shu yerda (izoh: yuqorida, 🔴 qism)
  ════════════════════════════════════════════════════════════════════
  Har material uch ohangda: asosiy / soya / yaltiroq.
*/
.mascot {
  --m-body: #12946a;        /* zumrad — markaz yashili */
  --m-body-shade: #0c7350;
  --m-body-light: #4cc597;
  --m-arm: #0e8159;
  --m-gold: #efbd4e;        /* mis-oltin — gumbaz, belbog', kaft */
  --m-gold-shade: #cf972b;
  --m-gold-light: #fbe4a6;
  --m-glass: #fff6df;       /* ichidan yonib turgan oyna */
  --m-glass-glow: #ffdf8a;
  --m-ink: #1d2a24;         /* ko'z, qosh — sof qora emas, yumshoq */
  --m-mouth: #5a2320;
  --m-tongue: #ff8f7e;
  --m-blush: #ff9d8c;
  --m-flame-outer: #f59a2c;
  --m-flame-inner: #ffd45a;
  --m-flame-core: #fff8e2;
  --m-halo: #ffeeb8;

  display: block;
  overflow: visible;
}

.m-shadow { fill: #000; opacity: 0.1; }
.m-glow { fill: var(--m-halo); }
.m-body { fill: var(--m-body); }
.m-body-shade { fill: var(--m-body-shade); }
.m-body-light { fill: var(--m-body-light); opacity: 0.75; }
.m-gold { fill: var(--m-gold); }
.m-gold-shade { fill: var(--m-gold-shade); }
.m-gold-light { stroke: var(--m-gold-light); }
.m-cream { fill: var(--m-glass); }
.m-glass { fill: var(--m-glass); }
.m-glass-glow { fill: var(--m-glass-glow); }
.m-frame { stroke: var(--m-gold); }
.m-glint { stroke: #fff; opacity: 0.85; }
.m-blush { fill: var(--m-blush); opacity: 0.55; }
.m-ink { fill: var(--m-ink); }
.m-ink-stroke { stroke: var(--m-ink); }
.m-white { fill: #fff; }
.m-mouth { fill: var(--m-mouth); }
.m-tongue { fill: var(--m-tongue); }
.m-arm-stroke { stroke: var(--m-arm); }
.m-hand { fill: var(--m-gold); }
.m-flame-outer { fill: var(--m-flame-outer); }
.m-flame-inner { fill: var(--m-flame-inner); }
.m-flame-core { fill: var(--m-flame-core); }
.m-coral { fill: #ff8f7e; }
.m-mint { fill: #4cc597; }
.m-z { stroke: #7c9a8c; stroke-width: 2.6; stroke-linecap: round; stroke-linejoin: round; }
.m-thought { fill: #9db4a8; }
.m-stroke-gold { stroke: var(--m-gold-shade); stroke-width: 3; stroke-linecap: round; }

/* Mudroq olov — xira. */
.m-flame--dim { opacity: 0.72; }

/*
  ════════════════════════════════════════════════════════════════════
  HARAKAT
  ════════════════════════════════════════════════════════════════════
  🔴 `transform-box` HAR JOYDA ochiq yozilgan: SVG'da `transform-origin`
     sukut bo'yicha viewBox'ga nisbatan. Qo'l YELKA atrofida aylanishi
     uchun `view-box` + aniq koordinata, ko'z va uchqun esa O'Z markazi
     atrofida (`fill-box`).

  ★ DAVRLAR BIR-BIRIGA KARRALI EMAS (3.8 / 5.4 / 2.6 / 1.9 s) — harakat
    "mexanik" takrorlanmaydi.
*/
@keyframes m-bob {
  0%, 100% { transform: translateY(0); }
  50%      { transform: translateY(-4px); }
}
.m-bob { animation: m-bob 3.8s ease-in-out infinite; }
/* Mudroq — sekin "nafas olish". */
.mascot--sleepy .m-bob { animation-duration: 5.6s; }

@keyframes m-blink {
  0%, 93%, 100% { transform: scaleY(1); }
  96%           { transform: scaleY(0.1); }
}
.m-eyes {
  transform-box: fill-box;
  transform-origin: center;
  animation: m-blink 5.4s ease-in-out 1.2s infinite;
}

@keyframes m-flicker {
  0%, 100% { transform: scale(1, 1) skewX(0deg); }
  30%      { transform: scale(0.95, 1.05) skewX(-2deg); }
  60%      { transform: scale(1.04, 0.96) skewX(2deg); }
}
.m-flame {
  transform-box: fill-box;
  transform-origin: 50% 100%;
  animation: m-flicker 1.9s ease-in-out infinite;
}

@keyframes m-wave {
  0%, 100% { transform: rotate(0deg); }
  50%      { transform: rotate(-14deg); }
}
.m-wave {
  transform-box: view-box;
  transform-origin: 129px 112px;
  animation: m-wave 1.3s ease-in-out infinite;
}

@keyframes m-pump {
  0%, 70%, 100% { transform: translateY(0); }
  80%           { transform: translateY(-5px); }
}
.m-pump { animation: m-pump 2.6s ease-in-out infinite; }

@keyframes m-twinkle {
  0%, 100% { transform: scale(1); opacity: 1; }
  50%      { transform: scale(0.55); opacity: 0.55; }
}
.m-twinkle {
  transform-box: fill-box;
  transform-origin: center;
  animation: m-twinkle 1.8s ease-in-out infinite;
}
.m-twinkle--2 { animation-delay: 0.6s; }
.m-twinkle--3 { animation-delay: 1.1s; }

@keyframes m-float-z {
  0%, 100% { transform: translate(0, 0); opacity: 1; }
  50%      { transform: translate(3px, -5px); opacity: 0.45; }
}
.m-z { animation: m-float-z 3.2s ease-in-out infinite; }
.m-z--2 { animation-delay: 1.1s; }

@keyframes m-dots {
  0%, 100% { opacity: 1; }
  50%      { opacity: 0.4; }
}
.m-thought { animation: m-dots 2.4s ease-in-out infinite; }
.m-thought:nth-child(2) { animation-delay: 0.3s; }
.m-thought:nth-child(3) { animation-delay: 0.6s; }

/*
  Global qoida (`style.css`) animatsiyani 0.01ms ga qisqartiradi; bu yerda
  esa butunlay o'chiriladi — cheksiz suzish bir martalik "sakrash" bo'lib
  ham qolmasin. Bazaviy holatlar to'g'ri chizilgan (yuqoridagi izoh).
*/
@media (prefers-reduced-motion: reduce) {
  .m-bob,
  .m-eyes,
  .m-flame,
  .m-wave,
  .m-pump,
  .m-twinkle,
  .m-z,
  .m-thought {
    animation: none;
  }
}
</style>
