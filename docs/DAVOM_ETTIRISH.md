# Yangi sessiyada davom ettirish — TOPSHIRIQ

> Bu fayl **kontekst tugaganda** ish uzilib qolmasligi uchun yuritiladi.
> Yangi sessiya shu faylni o'qib, oldingi suhbatsiz davom eta oladi.
>
> **Oxirgi yangilanish:** 2026-07-30, kechki sessiya oxiri
> **Oxirgi commitlar:** `edbf1d0` (backend) · `c9fa062` (frontend) ·
> `b28e767` (hujjatlar) — ✅ ish tarixga yozilgan, working tree TOZA.
> Push qilinmagan (repo lokal).
>
> **Ekran suratlari:** `~/Documents/Projects/zinnur-v2-shots/` (57 ta PNG —
> parite tekshiruvi va brauzer sinovlari; repoga qo'shilmadi, 9.8 MB).
>
> Batafsil jurnal: `docs/PROGRESS.md` · Reja: `docs/ROADMAP.md` ·
> Dizayn ko'chirish: `docs/DIZAYN_KOCHIRISH_REJASI.md` · Shartnoma: `docs/SPEC.md`

---

## 1. HOLATNI TIKLASH

```bash
cd ~/Documents/Projects/zinnur-v2
docker compose up -d
docker compose ps                      # 5 xizmat healthy
curl -s localhost:5080/health/ready
```

| | |
|---|---|
| Frontend | http://localhost:5173 |
| API + Swagger | http://localhost:5080/swagger |
| Admin | `admin@zinnur.uz` / `Admin!2345` |
| Ustoz | `teacher@zinnur.uz` / `Demo!2345` |
| O'quvchi | `student@zinnur.uz` / `Demo!2345` |
| Postgres / Redis (host) | 5440 / 6390 |
| Eski loyiha (TEGILMAYDI) | http://localhost:8000 (`zinnur-legacy`) |

### ⚠️ Образ eskirmaganmi — HAR SAFAR tekshiring

```bash
docker inspect zinnur/api:dev --format '{{.Created}}'
docker inspect zinnur/web:dev --format '{{.Created}}'
docker compose build api web && docker compose up -d api web   # kerak bo'lsa
```

Bugun ikki marta shu holat bo'ldi: kod yozilgan, lekin konteyner eski образdan
ishlab, endpoint **404** qaytardi (javob tanasi ham bo'sh — UI'da tushunarli
xato ko'rinmaydi).

---

## 2. BUGUN NIMA QILINDI (2026-07-30)

### Tuzatilgan HAQIQIY xatolar (hammasi jonli isbotlangan)

| Xato | Oqibati |
|---|---|
| `GroupFormDialog` `courseId`/`curatorGroupId` yubormasdi | Har tahrirlashda guruh kursi uzilardi → o'quvchilarda gating `NotInCourse`, butun kurs qulflanardi |
| `useLiveHub` `Array.isArray(result)` | Ishtirokchilar ro'yxati HECH QACHON to'ldirilmasdi (server obyekt qaytaradi) |
| `SessionEnded` backendda yuborilmasdi | Ustoz darsni yakunlaganda o'quvchi ekranida hech nima o'zgarmasdi |
| Qidiruvda 1 belgi → 400 → jadval yo'qolardi | `USER/GROUP/COURSE_SEARCH_MIN` bilan yopildi |
| **Kirish tokeni bekor qilinmasdi** (`ver` tekshirilmasdi) | O'chirilgan o'quvchi 15 daqiqa video xonaga kira olardi |
| `RedisCacheService` kalitlari makonsiz | Testlarda bazalararo to'qnashuv (9 test yiqilgandi) |
| `Payment.Validate` da `Amount = BaseAmount − DiscountAmount` yo'q | Moliya hisoboti uydirmaga aylanardi |
| `outstanding` holatga qaramaydi | Kechirilgan oy jadvalda "qarz" bo'lib turardi — kassir yana pul so'rardi |
| `BaseModal` qatlamlari | Hisob oynasidan ochilgan to'lov oynasi ORTIDA chizilardi |
| Tema karkas `<div>` ida edi | Teleport qilingan modal/toast temadan chiqib ketardi |

### Qo'shilgan funksiyalar

- **FAZA 4 (moliya) to'liq:** Domain + sxema (11 `CHECK`) + `PaymentService` +
  ~20 endpoint + **To'lovlar va Moliya UI**
- **FAZA 3.4 (testlar):** tuzish, yechish (taymer bilan), natijalar, CSV
- **Uy vazifalari:** yaratish/tahrirlash, o'quvchi topshirishi (multipart)
- **Guruh a'zoligi:** qo'shish, pauza, ko'chirish, chiqarish + arxiv/jadval
- **Reyting va kurator chati (DM)** — backend + ekranlar
- **Davomatni qo'lda tuzatish** + audit izi
- **Kalendar** (`/live-sessions/calendar`), dars `completed` bayrog'i

### Dizayn ko'chirish (1–4 to'lqin)

Eski ilova dizayni v2 ga ko'chirildi. Ildiz sabab: v2 rangni
`app/static/app.css` dan (bazaviy fayl, yashil `#2f9e41`) olgan edi, holbuki
eski loyihada har panel uni inline ustidan yozadi.

| Rol | Tema | Karkas |
|---|---|---|
| O'quvchi | `#051e2d` / `#f5b731` | 390px, pastda 5 tab |
| Ustoz/kurator | `#092235` / `#ffcc33` | yon menyu |
| O'quv bo'limi | `#0f2d48` / `#f2c84b` | yon menyu |
| Jonli dars | `teacher` temasi (eskisida ham aynan shu) | to'liq ekran |

**Holat:** 614 test yashil (408 unit + 206 integratsiya), build 0 ogohlantirish,
`vue-tsc` + `eslint` toza, api va web образlari yangi.

---

## 3. QOLGAN ISH — DIZAYN PARITETI

Mustaqil tekshiruvchi bahosi: o'quvchi ~90% · kurator ~80% · ustoz ~75% ·
o'quv bo'limi ~60% · jonli dars ~40% (tema tuzatilgandan keyin yuqoriroq).

### 3.1. Funksional yo'qotishlar — QAROR KERAK

| # | Eski ilovada bor | v2 da | Nima kerak |
|---|---|---|---|
| 1 | Ustozda **"Chatlar" hubi** — barcha guruh chatlari bitta joyda | Yo'q | Backendda **guruh chati umuman yo'q** (faqat dars ichidagi SignalR va kurator DM). Yangi modul kerak |
| 2 | **Guruh chati** o'quvchida ham | Yo'q | Yuqoridagi bilan bir xil |
| 3 | **Moliya dashboard'i**: KPI, "Qarz yoshi", "Oxirgi 12 oy", guruh/usul kesimlari | Faqat sozlama sahifasi | `GET /payments/summary` yig'ma endpointi |
| 4 | **"Dars yozuvlari"** bo'limi | Yo'q | FAZA 5.3 (LiveKit Egress → R2) |
| 5 | **"Qarorlar / Xabarlar"** bo'limi | Yo'q | FAZA 5.2 (notifikatsiya) |
| 6 | **"Tekshirish" navbati** — to'liq ekran, klaviatura yorliqlari (`1–5/Enter/→`) | Oddiy sahifa + modal | Navbat endpointi + UI |
| 7 | Guruhlar sahifasida **4 ta KPI kartochka** | Yo'q | Yig'ma so'rov |
| 8 | O'quvchi profil modali (statistika, baholar, izohlar) | Yo'q | `notes` endpointi yo'q |
| 9 | To'lovlarda **global tranzaksiyalar tarixi**, **"Xabar matnlari"** | Yo'q | Audit o'qish endpointi + shablonlar |
| 10 | Excel eksporti (moliya) | Yo'q | `http.download` tayyor, endpoint kerak |

### 3.2. Kichik chekinishlar (tuzatilishi mumkin)

- Sahifa **tavsif matnlari** almashtirilgan (masalan To'lovlar: eskisida
  "Har 8 dars uchun 540 000 so'm...", hozir "Har oy uchun yozuv ochiladi...")
- Guruh ichidagi "Vazifalar" tabi va menyudagi "Tekshirish" — nomlar yaqin,
  chalkashishi mumkin
- `💬` emoji sarlavhalardan olib tashlangan

### 3.3. Tekshirilmagan

- **To'ldirilgan** davomat/baholar jadvali (30 o'quvchi × 70 dars) — lokal
  bazada bunday ma'lumot yo'q. Reja 6-bo'limida **6 ta ekran surati**
  so'ralgan (eski ilovadan) — hali berilmagan
- Haqiqiy LiveKit video oqimi (WS 404 qaytardi)
- Eski ilovaning brauzerdagi ko'rinishi — parol yo'q, solishtirish faqat
  shablon kodi bo'yicha qilingan

---

## 4. QAROR KUTAYOTGAN SAVOLLAR

1. **Kurator baho qo'ya oladimi?** Eski tizim taqiqlagan (K2), v2 serveri
   ruxsat beradi (`GradeRoles` da `Assistant` bor). Menyudan olib tashlandi,
   lekin marshrut ochiq. Serverda ham taqiqlansinmi?
2. **Refresh token qayta ishlatish aniqlanmaydi** — `jti` saqlanmaydi.
   Muddat 7 kunga qisqartirildi (yarim yechim). To'liq yechim kerakmi?
3. **Kvitansiya raqami** — Postgres `SEQUENCE` kerakmi? Hozir ikki kassir bir
   vaqtda ishlasa 409 chiqadi (pul yo'qolmaydi, qayta urinish kerak).
4. **Qarzdorga kurs daraxti butunlay yopiladi** (`Video` qamrovi) — o'quvchi
   dars nomlarini ham ko'rmaydi. Yumshoqroq variant kerakmi?
5. **Test taymeri 60 s tolerantlikni ko'rsatadi** (10 daqiqalik testda 11:00
   dan sanaydi). Alohida "ko'rsatiladigan muddat" yuborilsinmi?
6. **Davomatda "avtomatikka qaytarish"** tugmasi yo'q — `isManual` abadiy
   qoladi. Qo'shilsinmi?
7. **Guruh × kurs davomat matritsasi** endpointi yo'q (birlik — dars).
8. **`completed` hozir "hamma yashil"**: video kontenti modellashtirilmagani
   uchun vazifasi/testi yo'q darslar darhol tugatilgan hisoblanadi.

---

## 5. KEYINGI FAZALAR (ROADMAP bo'yicha)

| Faza | Ish | Holat |
|---|---|---|
| 5.1 | **Telegram bot va Mini App** — o'quvchilar uchun YAGONA kirish yo'li | ❌ boshlanmagan · **eski tizimni o'chirish uchun SHART** |
| 5.2 | Notifikatsiya (outbox + worker, commit-then-send) | ❌ |
| 5.3 | Dars yozuvi (LiveKit Egress → R2, webhook imzosi) | ❌ |
| 5.4 | Fayl ombori (R2) — hozir fayl yuklashda **503** | ❌ |
| 5.5 | Fon vazifalari (avto-yakunlash, oylik yozuvlar, DB leader lock) | ❌ |
| 6 | Frontend qolgan qismi (3-bo'limdagi ro'yxat) | qisman |
| 7 | **Ma'lumot ko'chirish** + staging + prod deploy | ❌ |

> **Eski tizimni o'chirish uchun minimal to'plam:** 5.1 (Telegram) + 5.4 (fayl)
> + 7 (ma'lumot ko'chirish va deploy).

---

## 6. TUZOQLAR — vaqt yo'qotmaslik uchun

1. **`PUT` = TO'LIQ ALMASHTIRISH.** Yuborilmagan maydon `null` bo'lib bazaga
   tushadi. Har tahrirlash formasi mavjud qiymatlarni yuklab, HAMMASINI
   qaytarsin.
2. **Migratsiyasiz model o'zgarishi** → `PendingModelChangesWarning` → ilova
   UMUMAN ko'tarilmaydi (build yashil bo'lsa ham).
3. **Npgsql 9 da `UseXminAsConcurrencyToken()` YO'Q** →
   `Property<uint>("xmin").IsRowVersion()`.
4. **400 va 409 boshqa joyda:** 400 da sabab `problem.errors` da, 409 da
   `detail` to'liq. `toUserMessage(error)` ikkalasini to'g'ri o'qiydi.
5. **Qidiruv minimal uzunligi:** foydalanuvchi 3, guruh 2, kurs 2 belgi.
6. **`reorder` TO'LIQ ro'yxat kutadi** (yetishmasa 400).
7. **Redis kalitlari makon bilan** (`Redis:KeyPrefix`) — bitta Redis'ni ikki
   muhit baham ko'rsa aralashmaydi.
8. **Tema `<html>` ga qo'yiladi**, karkas `<div>` iga emas (teleport).
9. **`DayOfWeek`:** eski Python dushanba=0, .NET yakshanba=0 →
   `dotnet = (python + 1) % 7`.
10. **LiveKit ICE:** prod'da `NODE_IP=127.0.0.1` qolib ketsa media hech qachon
    ishlamaydi.
11. **`CA1848`** (`[LoggerMessage]`), **`CA1305`** (`CultureInfo.InvariantCulture`).
12. **Kalendarda `localDate`** ishlatilsin — `scheduledStart` dan brauzerda
    sana chiqarilsa, boshqa vaqt mintaqasida dars kechagi kunga tushadi.
13. **zsh:** `GID` — tizim o'zgaruvchisi, skriptda ishlatib bo'lmaydi.

---

## 7. COMMIT TARIXI (bajarildi)

```
b28e767  Hujjatlar: ish jurnali, dizayn ko'chirish rejasi va sessiya topshirig'i
c9fa062  Frontend: eski ilova dizayni ko'chirildi + moliya, testlar, a'zolik
edbf1d0  Backend: moliya moduli, testlar oqimi, reyting/DM, davomat, xavfsizlik
8b17815  (oldingi sessiya)
```

Working tree toza. Repo LOKAL — push qilinmagan, GitHub remote sozlanmagan.

## 8. ISH USLUBI

Bugun agentlar bilan ishlash yaxshi natija berdi, lekin **ikki qoida** bilan:

1. **Buyruq bermang, muammoni tushuntiring.** Agentlar shu tufayli mustaqil
   xato topdi: `outstanding` holatga qaramasligi, modal qatlamlari, tema
   teleportdan chiqib ketishi — hech biri topshiriqda yo'q edi.
2. **Hisobotni DALIL bilan tekshiring.** Bugun bir necha marta agent xulosasi
   noto'g'ri chiqdi (bir agent men bergan namunaviy URL'ni sinab "xato bor"
   dedi; boshqasi seed ma'lumotini o'zgartirdi). Har hisobotdan keyin build,
   test va jonli tekshiruv MENING zimmamda bo'ldi.

**Parallel ishlashda:** umumiy fayllarni (`router`, `navigation`,
`shared/types`) oldindan o'zim ulab, agentlarga "tegma" deb aytdim — aks holda
ikki agent bir faylni bosib ketardi.
