# =============================================================================
#  ZIN-NUR v2 — FRONTEND (Vue 3 + TypeScript + Vite -> nginx)
# =============================================================================
#
#  Build konteksti: ./frontend
#  Yig'ish:  docker compose build web
#
#  IKKI BOSQICH:
#     build (node:22-alpine)  ->  runtime (nginx:alpine)
#
#  MUHIM TUSHUNCHA: Vue SPA — bu SERVER EMAS, balki STATIK fayllar to'plami
#  (index.html + JS + CSS). Node yakuniy image'da KERAK EMAS.
#  Shuning uchun runtime — oddiy nginx (~50 MB), node bilan bo'lsa ~400 MB
#  bo'lardi. Bu 200 foydalanuvchi uchun ham muhim: nginx statikani
#  Node'dan ancha tez va kam xotira bilan tarqatadi.
# =============================================================================


# =============================================================================
#  BOSQICH 1 — BUILD
# =============================================================================
FROM node:22-alpine AS build

WORKDIR /app

# --- Bog'liqliklar: kesh uchun ALOHIDA qadam ---
# Avval faqat manifest fayllarni ko'chiramiz. Ular kam o'zgaradi, ya'ni
# `npm ci` qatlami keshda qoladi va .vue/.ts faylni tahrirlaganda
# qayta bajarilmaydi (1-2 daqiqa tejaladi).
#
# `package*.json` — bu package.json VA package-lock.json ni oladi.
COPY package*.json ./

# `npm ci` (`npm install` EMAS) — ATAYLAB:
#   * package-lock.json ga QAT'IY amal qiladi. Ya'ni bugun va uch oydan
#     keyin yig'ilgan image AYNAN bir xil paketlarga ega bo'ladi.
#     `npm install` esa lock faylni jimgina yangilab yuborishi mumkin —
#     "mening mashinamda ishlaydi" muammosining klassik manbai.
#   * node_modules ni avval tozalab tashlaydi => toza holat.
#   * CI uchun optimallashtirilgan, sezilarli tezroq.
#
# TALAB: package-lock.json REPOZITORIYADA bo'lishi SHART.
# Bo'lmasa `npm ci` ochiq xato bilan to'xtaydi — bu ATAYLAB shunday
# (jimgina noto'g'ri versiya o'rnatilgandan ko'ra yaxshi).
#
# --no-audit / --no-fund: build loglarini tozalaydi va tarmoqqa
# keraksiz so'rov yubormaydi (build biroz tezlashadi).
RUN npm ci --no-audit --no-fund

# --- Manba kod ---
# Bu qatlam tez-tez o'zgaradi, shuning uchun `npm ci` DAN KEYIN turadi.
COPY . .

# --- VITE BUILD-TIME O'ZGARUVCHILARI ---
#
#  ENG MUHIM TUSHUNCHA: Vite `import.meta.env.VITE_*` qiymatlarini
#  kompilyatsiya paytida JS faylga MATN sifatida ALMASHTIRADI.
#  Ya'ni ular runtime'da O'QILMAYDI — konteynerga `-e VITE_API_URL=...`
#  berish HECH NARSA QILMAYDI.
#
#  Shuning uchun ular ARG orqali (build vaqtida) keladi:
#      docker compose build web        <- .env dagi qiymatlar bilan
#      docker build --build-arg VITE_API_URL=https://zinnuronline.uz/api ...
#
#  ARG'lar aynan shu yerda — `COPY . .` dan KEYIN, `npm run build` dan
#  OLDIN turadi. Sabab: ARG qiymati o'zgarsa faqat build qatlami
#  qayta bajariladi, `npm ci` keshda qoladi.
#
#  Standart qiymatlar dev uchun (SPEC 8-bo'lim).
ARG VITE_API_URL=http://localhost:5080
ARG VITE_HUB_URL=http://localhost:5080/hubs/live

#  VITE_SITE_URL — saytning KANONIK manzili. robots.txt, sitemap.xml,
#  <link rel="canonical"> va Open Graph teglari SHUNDAN yasaladi.
#  Sabab va dev/staging qoidasi — .env dagi izohda.
#
#  🔴 STANDART QIYMAT ATAYLAB BO'SH (2026-08-30). Ilgari bu yerda
#     `=https://zinnuronline.uz` turardi va u IKKI TOMONLAMA XAVFLI edi:
#
#     1) `docker build frontend/` to'g'ridan-to'g'ri chaqirilsa (masalan
#        sinov nusxasini yig'ishda), tayyor image O'ZINI PROD deb e'lon
#        qilardi: robots.txt `Allow: /`, sitemap prod manzillari bilan.
#        Sinov nusxasi indeksga tushib, asosiy sayt bilan raqobatlashardi.
#
#     2) Teskari tomoni ham yomon edi — bu standart QUTQARMASDI: Compose
#        `${VITE_SITE_URL}` ni bo'sh satr sifatida uzatsa, bo'sh
#        argument ARG standartini BEKOR QILADI va sayt sinov rejimida
#        yig'ilardi (`Disallow: /`).
#
#     Endi qiymat berilmasa build YIQILADI (`scripts/seo-files-plugin.ts`)
#     — ya'ni ikkala holat ham jimgina o'tib ketmaydi.
ARG VITE_SITE_URL=

#  Qidiruv tizimida tasdiqlash kodlari va IndexNow kaliti.
#  Bo‘sh bo‘lsa teg ham, kalit fayli ham YASALMAYDI (sabab .env izohida).
ARG VITE_GOOGLE_SITE_VERIFICATION=
ARG VITE_YANDEX_VERIFICATION=
ARG VITE_INDEXNOW_KEY=

# ARG -> ENV: Vite o'zgaruvchilarni MUHITDAN o'qiydi, ARG esa
# `npm run build` jarayoniga avtomatik ko'rinmaydi. Bu qator — ko'prik.
ENV VITE_API_URL=$VITE_API_URL
ENV VITE_HUB_URL=$VITE_HUB_URL
ENV VITE_SITE_URL=$VITE_SITE_URL
ENV VITE_GOOGLE_SITE_VERIFICATION=$VITE_GOOGLE_SITE_VERIFICATION
ENV VITE_YANDEX_VERIFICATION=$VITE_YANDEX_VERIFICATION
ENV VITE_INDEXNOW_KEY=$VITE_INDEXNOW_KEY

# Ishlab chiqarish rejimi: Vite minifikatsiya qiladi, tree-shaking
# bajaradi va fayl nomlariga hash qo'shadi (app.4f2c9a1b.js).
# Hash — pastdagi nginx keshlash strategiyasining asosi.
ENV NODE_ENV=production

# `npm run build` odatda: `vue-tsc --build && vite build`
# Ya'ni TypeScript xatolari ham build'ni to'xtatadi — SPEC 9-bo'limidagi
# sifat talabiga mos (chala kod prod'ga chiqmaydi).
RUN npm run build


# =============================================================================
#  BOSQICH 2 — RUNTIME (nginx)
# =============================================================================
FROM nginx:alpine AS runtime

# Bizning to'liq nginx konfiguratsiyamiz (SPA fallback, gzip, keshlash).
# Standart faylning USTIGA yoziladi.
COPY nginx.conf /etc/nginx/nginx.conf

# Standart nginx sahifasini (index.html "Welcome to nginx") olib tashlaymiz,
# so'ng o'z bundle'imizni qo'yamiz. Tozalash muhim: eski fayllar qolib
# ketsa, ular ham tarqatiladi.
RUN rm -rf /usr/share/nginx/html/*
COPY --from=build /app/dist /usr/share/nginx/html

# Hujjatlashtiruvchi ko'rsatma. Compose bu portni host'dagi 5173 ga bog'laydi.
EXPOSE 80

# nginx statikani tarqatayotganini tekshiramiz.
# busybox wget alpine tarkibida bor.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1/ || exit 1

# ROOT FOYDALANUVCHI HAQIDA IZOH:
# nginx master jarayoni root'dan boshlanadi (80-portni ochish uchun), lekin
# ishchi (worker) jarayonlar darrov `nginx` foydalanuvchisiga tushadi —
# buni nginx.conf dagi `user nginx;` direktivasi ta'minlaydi.
# Haqiqiy so'rovlarni ishlovchi kod hech qachon root huquqi bilan ishlamaydi.
# Butunlay root'siz variant (nginx-unprivileged) ham bor, lekin u
# 8080-portda ishlaydi va SPEC 8-bo'limidagi "ichki port 80" ni buzadi.

# Image'ning standart CMD'si: nginx -g "daemon off;" — o'zgartirish shart emas.
