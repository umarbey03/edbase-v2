/*
  ══════════════════════════════════════════════════════════════════════════
  SAHIFA SARLAVHALARI — YAGONA MANBA
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA UCHUN ALOHIDA MODUL (2026-08-30):
    Bosh sahifa sarlavhasi IKKI joyda kerak bo'ladi va ikkalasi ham
    boshqa-boshqa dunyoda yashaydi:

      1) `index.html` dagi `<title>` — qidiruv tizimi VA prerender
         qilingan sahifa shuni ko'radi;
      2) `app/router` — foydalanuvchi ilova ichidan bosh sahifaga
         QAYTGANDA sarlavhani tiklashi kerak.

    Ikkinchisi 2026-08-30 da yo'q edi va aynan shuning uchun xato bor
    edi: router `/` marshrutida sarlavhaga UMUMAN tegmasdi, natijada
    `/login` dan `/` ga o'tilganda tabda «Kirish — ZIN-NUR ONLINE»
    qotib qolardi.

  🔴 `LANDING_TITLE` `index.html` DAGI `<title>` BILAN HARFMA-HARF BIR
     XIL BO'LISHI SHART. Ular ajralib ketmasligi uchun `scripts/
     prerender.mjs` build paytida IKKALASINI SOLISHTIRADI va farq
     bo'lsa build'ni YIQITADI. Ya'ni bu yerni o'zgartirsangiz,
     `index.html` ni ham o'zgartirasiz — aks holda build o'tmaydi.
*/

/** Brend nomi — barcha sarlavhalarning oxirgi bo'lagi. */
export const BRAND_NAME = 'ZIN-NUR ONLINE'

/** Bosh sahifa sarlavhasi. `index.html` dagi `<title>` bilan AYNI. */
export const LANDING_TITLE
  = 'Online arab tili kursi — 8 oyda mustaqil o‘qishni o‘rganasiz | ZIN-NUR ONLINE'
