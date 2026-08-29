<script setup lang="ts">
/**
 * ════════════════════════════════════════════════════════════════════════
 * «NURI» — MASKOT (boyqush). 2026-08-30
 * ════════════════════════════════════════════════════════════════════════
 *
 * 🔴 RASM EMAS, INLINE SVG — VA BU MAJBURIY TANLOV EMAS, ATAYLAB QILINGAN:
 *
 *   1) TASHQI MANBA YO'Q. Ilovaning CSP siyosati begona hostdan rasm
 *      yuklashga ruxsat bermaydi, `public/` ga qo'yilgan PNG esa qo'shimcha
 *      so'rov demakdir — maskot esa kirishdan KEYINGI BIRINCHI ekranda
 *      turadi, ya'ni u yuklanguncha ekranda teshik bo'lardi.
 *
 *   2) RANG TOKENGA BOG'LANADI. Bitmap rasm brend rangi o'zgarganda
 *      eskirib qolardi; bu yerda esa har bo'yoq `style.css` dagi
 *      o'zgaruvchidan keladi (`fill-brand-vivid`, `fill-green-*`).
 *
 *   3) ANIMATSIYA QILSA BO'LADI — ko'z pirillashi va yengil suzish
 *      shaklning O'Z qismlariga qo'llanadi.
 *
 * ★ RANG TANLOVI — TO'Q YASHIL SIRT (`.surface-brand`) UCHUN:
 *   sirtda `--color-brand-500` SHAMPANGA aylanadi (o'sha blokdagi izoh:
 *   "to'q yashil sirtda yashil tugma ko'rinmaydi"), ya'ni tanani
 *   `brand-500` bilan bo'yasak boyqush SARIQ chiqardi. Shuning uchun tana
 *   `--color-brand-vivid` (#00a36c) — u `@theme` da BIR MARTA e'lon
 *   qilingan va hech qaysi sirt uni qayta belgilamaydi, ya'ni har temada
 *   AYNI yashil bo'ladi. To'q yashil fonda kontrasti ≈3.7:1 — matn emas,
 *   yirik grafika uchun yetarli.
 *
 *   Tumshuq va oyoqlar esa ATAYLAB `brand-500` (shampan): shu tufayli
 *   maskot pastdagi «Boshladik!» tugmasi bilan bitta rang oilasida
 *   bo'ladi va ekran bir butun ko'rinadi.
 */
</script>

<template>
  <!--
    Tashqi o'ram — KIRISH animatsiyasi (`animate-pop`, `style.css` dagi
    mavjud token). Ichkaridagi `<svg>` esa DOIMIY suzish animatsiyasini
    oladi: ikkalasi bitta elementda bo'lsa biri ikkinchisini bekor
    qilardi (CSS'da `animation` xossasi yagona).

    O'lchamni CHAQIRUVCHI beradi (`class="size-36"`), shuning uchun bu yerda
    o'lcham yo'q — `svg` esa `size-full` bilan o'ramga to'liq moslashadi.
  -->
  <div class="animate-pop">
    <!--
      🔴 `aria-hidden` — MASKOT BEZAK. Uning yonida «NURI» yorlig'i va
      gapning O'ZI matn sifatida turadi; skrinrider uchun boyqushning
      shaklini takrorlash faqat ortiqcha shovqin bo'lardi.
    -->
    <svg
      class="nuri-float size-full"
      viewBox="0 0 120 132"
      fill="none"
      aria-hidden="true"
    >
      <!--
        Shox patlar TANADAN OLDIN chiziladi: shu tartibda ularning asosi
        tana ostida qoladi va uchburchak "yopishtirilgan" bo'lib
        ko'rinmaydi.
      -->
      <path
        class="nuri-tuft nuri-tuft--l fill-brand-vivid"
        d="M30 32 L35 6 L54 24 Z"
      />
      <path
        class="nuri-tuft nuri-tuft--r fill-brand-vivid"
        d="M90 32 L85 6 L66 24 Z"
      />

      <!-- Tana — bitta yumaloq shakl (bosh va gavda ajratilmagan). -->
      <ellipse
        class="fill-brand-vivid"
        cx="60"
        cy="68"
        rx="42"
        ry="48"
      />

      <!-- Qanotlar — tananing yon qirralarida, to'qroq yashil. -->
      <path
        class="nuri-wing nuri-wing--l fill-green-200"
        d="M23 60 C14 78 16 97 31 107 C26 88 26 74 31 58 Z"
      />
      <path
        class="nuri-wing nuri-wing--r fill-green-200"
        d="M97 60 C106 78 104 97 89 107 C94 88 94 74 89 58 Z"
      />

      <!-- Qorin — ochiq yaltiroq yashil, tana bilan kontrast. -->
      <ellipse
        class="fill-green-800"
        cx="60"
        cy="88"
        rx="25"
        ry="25"
      />

      <!-- Qoshlar — boyqushni "boyqush" qiladigan detal. -->
      <path
        class="stroke-green-200"
        d="M31 41 Q44 32 57 38"
        stroke-width="3.5"
        stroke-linecap="round"
      />
      <path
        class="stroke-green-200"
        d="M89 41 Q76 32 63 38"
        stroke-width="3.5"
        stroke-linecap="round"
      />

      <!-- Ko'z doiralari. -->
      <circle
        class="fill-slate-50"
        cx="45"
        cy="57"
        r="17"
      />
      <circle
        class="fill-slate-50"
        cx="75"
        cy="57"
        r="17"
      />

      <!-- Qorachiqlar — bir-biriga qaragan (do'stona ifoda). -->
      <circle
        class="fill-green-50"
        cx="47"
        cy="58"
        r="8"
      />
      <circle
        class="fill-green-50"
        cx="73"
        cy="58"
        r="8"
      />
      <circle
        class="nuri-pupil fill-slate-50"
        cx="44"
        cy="55"
        r="2.8"
      />
      <circle
        class="fill-slate-50"
        cx="70"
        cy="55"
        r="2.8"
      />

      <!--
        KOVOQLAR — pirillash uchun. Odatda `scaleY(0)` (ko'rinmaydi) va
        animatsiya ularni qisqa vaqtga to'liq ochadi. Tana rangida
        bo'lgani uchun yopilganda ko'z "yumilgandek" ko'rinadi.
      -->
      <circle
        class="nuri-eyelid fill-brand-vivid"
        cx="45"
        cy="57"
        r="17.5"
      />
      <circle
        class="nuri-eyelid fill-brand-vivid"
        cx="75"
        cy="57"
        r="17.5"
      />

      <!--
        Tumshuq KOVOQLARDAN KEYIN: aks holda ko'z yumilganda kovoq
        tumshuqning uchini ham bosib qo'yardi.
      -->
      <path
        class="fill-brand-500"
        d="M53 62 L67 62 L60 77 Z"
      />

      <!-- Oyoqlar. -->
      <ellipse
        class="fill-brand-500"
        cx="47"
        cy="117"
        rx="9"
        ry="4.5"
      />
      <ellipse
        class="fill-brand-500"
        cx="73"
        cy="117"
        rx="9"
        ry="4.5"
      />
    </svg>
  </div>
</template>

<style scoped>
/*
  ★ NEGA `style.css` GA EMAS, SHU YERGA: bu ikki animatsiya FAQAT shu
  maskotga tegishli va boshqa hech qayerda ishlatilmaydi. Umumiy faylga
  qo'shilsa, u yerdagi 11 ta `@keyframes` "ilova bo'ylab ishlatiladigan
  harakatlar" ro'yxati bo'lishdan to'xtardi.

  🔴 `prefers-reduced-motion` MUAMMOSI BU YERDA YO'Q: `style.css` dagi
  global qoida (`*` selektori) `animation-duration` va
  `animation-iteration-count` ni HAR BIR elementda bosadi, jumladan
  scoped uslublarda ham. Bu yerdagi animatsiyalarning HECH BIRIDA
  `fill-mode` yo'q, ya'ni to'xtaganda har element BAZAVIY holatiga
  qaytadi: kovoq — ko'rinmas (`scaleY(0)`), qanot va shox pat — 0°,
  qorachiq — joyida, boyqush — o'z o'rnida. Harakat kamaytirilganda
  ekran statik va TO'G'RI ko'rinadi.
*/

/* Yengil suzish — "tirik" taassurot. 6px dan oshmaydi: kattaroq harakat
   ekranda "sakrayotgan" bo'lib ko'rinardi. */
/*
  ════════════════════════════════════════════════════════════════════
  QO'SHIMCHA HARAKAT (2026-08-30) — maskot "tirikroq" bo'lsin
  ════════════════════════════════════════════════════════════════════

  Loyiha egasining talabi: boyqush biroz ko'proq harakatlansin.

  🔴 HAMMASI `transform-box: fill-box` BILAN. SVG'da `transform-origin`
     sukut bo'yicha BUTUN viewBox ga nisbatan hisoblanadi — ya'ni
     `bottom` degani boyqushning emas, chizmaning pastki qirrasi
     bo'lardi va qanot ekrandan chiqib ketardi. `fill-box` har shaklni
     O'Z chegarasiga nisbatan burardi.

  ★ DAVRLAR ATAYLAB TUB SONLARGA YAQIN (3.6 / 4.3 / 5.2 / 6.1 s):
    ular bir-biriga tushmaydi, shuning uchun harakat takrorlanadigan
    "mexanik" naqsh emas, tabiiy ko'rinadi. Bir xil davr qo'yilsa
    hamma qism bir vaqtda qimirlab, o'yinchoqdek chiqardi.

  ⚠️ BURCHAKLAR KICHIK (3-6°). Kattaroq qilinsa boyqush "qanot
     qoqayotgan" bo'lib ko'rinardi — bu esa e'tiborni gapdan tortadi,
     ekranning maqsadi esa MATNNI o'qitish.
*/

/* Qanotlar — yelka atrofida yengil tebranish, ikkalasi qarama-qarshi. */
@keyframes nuri-wing-l {
  0%, 100% { transform: rotate(0deg); }
  50%      { transform: rotate(-6deg); }
}
@keyframes nuri-wing-r {
  0%, 100% { transform: rotate(0deg); }
  50%      { transform: rotate(6deg); }
}

.nuri-wing {
  transform-box: fill-box;
  /* Aylanish markazi — qanotning YUQORI qirrasi (yelka), pastki emas:
     qanot yelkadan osilib tebranadi, o'z markazidan emas. */
  transform-origin: 50% 10%;
}
.nuri-wing--l { animation: nuri-wing-l 4.3s ease-in-out 0.9s infinite; }
.nuri-wing--r { animation: nuri-wing-r 4.3s ease-in-out 0.9s infinite; }

/* Shox patlar — juda kichik qimirlash, "quloq" taassuroti. */
@keyframes nuri-tuft-l {
  0%, 100% { transform: rotate(0deg); }
  40%      { transform: rotate(-4deg); }
}
@keyframes nuri-tuft-r {
  0%, 100% { transform: rotate(0deg); }
  40%      { transform: rotate(4deg); }
}

.nuri-tuft {
  transform-box: fill-box;
  /* Asos — patning PASTKI qismi, ya'ni tanaga ulangan joyi. */
  transform-origin: 50% 100%;
}
.nuri-tuft--l { animation: nuri-tuft-l 6.1s ease-in-out 1.8s infinite; }
.nuri-tuft--r { animation: nuri-tuft-r 6.1s ease-in-out 2.1s infinite; }

/*
  Qorachiq — vaqti-vaqti bilan yon tomonga qaraydi.
  Davrning atigi ~12% ida siljiydi (pirillash bilan bir xil naqsh):
  doim harakatlansa ko'z "chayqalayotgan" bo'lib ko'rinardi.
*/
@keyframes nuri-look {
  0%, 70%, 100% { transform: translateX(0); }
  76%, 84%      { transform: translateX(1.6px); }
}

.nuri-pupil {
  transform-box: fill-box;
  animation: nuri-look 7.4s ease-in-out 2.6s infinite;
}

@keyframes nuri-float {
  0%,
  100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-6px);
  }
}

.nuri-float {
  /*
    Kechikish KIRISH animatsiyasi (`animate-pop`, 0.5s) tugagandan keyin:
    ikkalasi ustma-ust tushsa maskot paydo bo'lishda "titrab" ko'rinardi.
  */
  animation: nuri-float 3.6s ease-in-out 0.6s infinite;
}

/*
  Pirillash: davrning atigi ~4% ida ko'z yumiladi. Shuning uchun
  keyframe'lar 92%/95%/98% da zich joylashgan — qolgan vaqt ko'z ochiq.
*/
@keyframes nuri-blink {
  0%,
  92%,
  100% {
    transform: scaleY(0);
  }
  95%,
  98% {
    transform: scaleY(1);
  }
}

.nuri-eyelid {
  /*
    `transform-box: fill-box` — SVG'da `transform-origin` sukut bo'yicha
    BUTUN chizma koordinatalariga nisbatan hisoblanadi, ya'ni `center`
    ikki ko'z uchun ham viewBox markazini bildirardi va kovoq ko'zdan
    tashqarida "yopilardi". `fill-box` bilan har doira O'Z markazi
    atrofida siqiladi.
  */
  transform-box: fill-box;
  transform-origin: center;
  transform: scaleY(0);
  animation: nuri-blink 5.2s ease-in-out 1.2s infinite;
}
</style>
