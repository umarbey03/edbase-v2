# Yuklama testi — natijalar

> **Sana:** 2026-07-30 · **Skript:** `tests/load/signalr-load.mjs`
>
> Bu hujjat `docs/DAVOM_ETTIRISH.md` → "6. HALI ISBOTLANMAGAN DA'VOLAR" ning
> **1-bandini yopadi**: "200 foydalanuvchi" da'vosi endi nazariya emas.

---

## 1. QISQA JAVOB

**200 foydalanuvchi tasdiqlandi — zaxira juda katta.** Tizim 800 tagacha
bemalol ko'taradi, 1500 da esa API'ning PROTSESSORI to'yinadi.

| Klient | Ulanish | Uzilish | Chat p50/p95/p99 | API CPU (barqaror) | Baho |
|---|---|---|---|---|---|
| **200** | 200/200 | 0 | 6 / 13 / 18 ms | **~0.35 yadro** (10 dan) | ✅ katta zaxira |
| **400** | 400/400 | 0 | 7 / 16 / 21 ms | — | ✅ |
| **800** | 800/800 | 0 | 7 / 17 / 24 ms | — | ✅ |
| **1500** | 1500/1500 | 0 | 23 / 179 / **4403 ms** | **~10 yadro (100%)** | ⚠️ dumli kechikish yiqildi |

200 klientda `JoinSession` p95 = 18 ms, ulanish p95 = 8 ms, chat xabari
1 sekundlik chegaradan **77 barobar** past.

---

## 2. BIRINCHI NIMA YIQILADI (ROADMAP FAZA 7 savoli)

**Javob: API konteynerining PROTSESSORI.** Baza ham, Redis ham emas.

1500 klient ostidagi o'lchov (14 marta namuna olindi):

```
zinnur-v2-api       cpu=994..1049%   mem=390 MiB   ← TO'YINGAN (10 yadro)
zinnur-v2-postgres  cpu=2..4%        mem=95 MiB    ← deyarli bo'sh
zinnur-v2-redis     cpu=0.3..0.8%    mem=11 MiB    ← deyarli bo'sh
```

Ya'ni cheklovchi omil — SignalR fan-out'ining **serializatsiyasi**:
1500 kishilik xonada bitta xabar 1500 marta yuboriladi. 45 sekundda
**1 282 340** ta yetkazish bo'ldi (~28 500 yetkazish/sekund).

Muhim xulosalar:

- **Xotira muammo emas** — 200 da ham, 1500 da ham ~390 MiB, o'smaydi.
- **Baza muammo emas** — hub bazaga deyarli tegmaydi (presence Redis'da,
  chat fon navbatida paketlab yoziladi). Bu `LiveClassHub` dagi 5 ta
  loyihalash qarori AMALDA ishlayotganining isboti.
- **Redis muammo emas** — 1500 klientda ham 1% dan kam.

Ko'lamni oshirish kerak bo'lsa yo'l aniq: **ikkinchi API instansiyasi**
(Redis backplane allaqachon ulangan), vertikal o'sish emas.

---

## 3. SKRIPTDAGI IKKI XATO TUZATILDI

Test yugurtirilmagani uchun skriptning o'zida ikkita jiddiy xato qolgan edi.
Ikkalasi ham natijani **yolg'on** qilardi.

### 3.1. Test HECH QACHON "muvaffaqiyatli" chiqmasdi

```js
conn.onclose(() => { stats.disconnects++; });   // ESKI
```

`onclose` NORMAL `conn.stop()` da ham chaqiriladi. Har klient test oxirida
o'zini "uzilgan" deb sanardi, ya'ni `disconnects` **doimo** `USERS` ga teng
bo'lardi va baho sharti (`disconnects > USERS * 0.05`) har yugurtirishda
yiqilardi.

Amalda ko'rildi: 5 klient, 0 xato, 100% ulanish — natija esa
"⚠️ 5 ta kutilmagan uzilish".

**Tuzatildi:** ataylab yopish `closing` bayrog'i bilan ajratildi.

### 3.2. 200 klient BITTA foydalanuvchi bo'lib ulanardi

Skript bitta admin tokenini 200 klientga ulashardi. Server esa ikki narsani
ham **foydalanuvchi** bo'yicha kalitlaydi:

| Nima | Kalit | Bitta token bilan oqibat |
|---|---|---|
| Chat rate-limit | `chatrate:{sessionId}:{userId}` | 200 ulanish bitta "1 xabar / 2 sek" budjetini bo'lishardi |
| Presence | `presence.AddAsync(sessionId, entry)` | 200 yozuv o'rniga **1 ta** yozuv qolardi |

Natijada:

- chat kechikishi deyarli o'lchanmasdi (o'lchangan 5 klientli sinovda
  8 xabar o'tdi, **12 tasi rate-limit** bo'ldi);
- `JoinSession` javobi (to'liq ro'yxat!) va delta broadcast REAL narxidan
  bir necha barobar arzon ko'rinardi — aynan o'sha narx esa "200 kishi
  bitta xonada" da'vosining o'zagi.

**Tuzatildi:** `tests/load/seed.mjs` kerakli sonda haqiqiy o'quvchi yaratadi
(idempotent), guruhga a'zo qiladi va har biri uchun HAQIQIY kirish tokeni
oladi. Har klient o'z foydalanuvchisi bilan ulanadi.

Endi hisobotda **`xonadagi eng ko'p ishtirokchi`** ko'rsatkichi bor — u
presence to'plami haqiqatan to'lganining isboti (200 da 200, 1500 da 1500).
To'lmasa test o'zini ISHONCHSIZ deb belgilaydi.

---

## 4. ISHLATISH

```bash
cd ~/Documents/Projects/zinnur-v2

node tests/load/signalr-load.mjs                      # 200 klient (default)
USERS=800 DURATION=45 node tests/load/signalr-load.mjs
SESSION_ID=282 GROUP_ID=4 node tests/load/signalr-load.mjs
```

Skript o'zi: kurator BO'LMAGAN faol guruhdagi darsni tanlaydi, yetishmayotgan
o'quvchilarni yaratadi (`zload-0001@zinnur.test` ...), guruhga qo'shadi va
kirish tokenlarini oladi. Ikkinchi yugurtirishda hech nima yaratilmaydi.

Yuklama foydalanuvchilarini tozalash uchun `zload-` prefiksi bo'yicha
qidiring (`GET /api/v1/users?search=zload`).

---

## 5. NIMA O'LCHANMADI — buni "ishlaydi" deb hisoblamang

1. **LiveKit media oqimi.** Video/audio backend'dan O'TMAYDI, shuning uchun
   bu test unga umuman tegmaydi. `DAVOM_ETTIRISH.md` → 6.2 bandi OCHIQ
   qolmoqda: ikki brauzer ochib media kelishini hech kim sinamagan.

2. **Tarmoq.** Yuklama generatori va server AYNI mashinada — ulanish
   loopback orqali ketadi. Haqiqiy tarmoqda kechikish yuqoriroq bo'ladi.

3. **1500 dagi raqamlar IFLOSLANGAN.** API 10 yadroning hammasini egallagan
   holatda Node klienti ham o'sha protsessorda ishlagan, ya'ni o'lchangan
   dumli kechikishning bir qismi klientning O'Z navbati. 200/400/800 da
   bunday muammo yo'q (API ~0.35 yadro).

4. **Bitta instance.** Redis backplane ulangan, lekin ikki API instansiyasi
   o'rtasidagi fan-out sinalmagan.

5. **Bitta xona.** 1500 klient BITTA darsda edi. Haqiqiy foydalanishda
   yuk ko'p xonaga taqsimlanadi — bu esa yengilroq stsenariy.

**Sinov mashinasi:** 10 yadro, 16 GB (Docker VM'ga 7.75 GiB).

---

## 6. ALOHIDA TOPILMA — kirish endpointi himoyalanmagan ⚠️

Yuklama testiga tayyorgarlik paytida aniqlandi (test bilan bog'liq emas,
lekin xavfsizlik masalasi).

`Program.cs` da parol topishga qarshi rate-limit siyosati **e'lon
qilingan**:

```csharp
options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, ... }));
```

Izohda "Kirish endpointi parol topishga qarshi cheklanadi" deb yozilgan.

**Lekin bu siyosat HECH QAYERGA qo'llanmagan.** Butun `src/` bo'ylab
`EnableRateLimiting` ham, `RequireRateLimiting` ham topilmadi — ya'ni
`app.UseRateLimiter()` chaqirilsa ham `POST /api/v1/auth/login` cheklanmaydi.

Amalda tasdiqlandi: bu test bitta yugurtirishda **1500 ta kirish** so'rovini
(20 tadan parallel) bitta IP'dan hech qanday to'siqsiz bajardi. Siyosat
ishlaganda ular daqiqasiga 10 tadan keyin 429 olishi kerak edi.

Bu `DAVOM_ETTIRISH.md` da sanab o'tilgan eski tizim xatolari bilan bir
turkumdagi nuqson: **"chegara bor edi, lekin FOYDASIZ edi"**.

**Tuzatish** (bir qator, `AuthController.Login` ustiga):

```csharp
[HttpPost("login")]
[EnableRateLimiting("auth")]      // ← yetishmayotgan qator
```

> ⚠️ Bu tuzatish ATAYLAB qo'llanmadi: `Program.cs`/`AuthController.cs`
> hozir boshqa sessiya ishlayotgan hududda. Tuzatishdan keyin yuklama
> skriptining tayyorgarlik bosqichi (200+ kirish) chegaraga uriladi —
> u holda skript tokenlarni `Jwt:Secret` bilan o'zi imzolashi yoki
> tayyorgarlik uchun chegara oshirilishi kerak bo'ladi.
