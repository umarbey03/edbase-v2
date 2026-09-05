# Dars yozuvi v2 — deploy runbook

Yangi yozuv oqimini (`TrackComposition`) ishlab chiqarishga chiqarish tartibi.

**Loyihaviy asos:** `docs/SPEC-RECORDING-V2.md`.
**Server:** `ssh root@138.68.101.252`, loyiha `/root/edbase-v2`.

---

## 0. Nima o'zgaradi va nima uchun

Eski oqim (`RoomComposite`) har bir dars uchun serverda **headless Chrome +
realtime x264** yurgizadi — 1.0–1.6 yadro. 4 vCPU li mashinada bir vaqtda
**bitta** yozuv sig'adi, jadvalda esa piki **6 ta**. Ustma-ust tushgan darslar
jimgina yo'qoladi.

Yangi oqim dars paytida faqat **xom oqimlarni** yozadi (passthrough, qayta
siqishsiz, ~0.25 yadro/dars), butun og'irlik esa **kechasi 00:00–09:00**
oynasidagi ffmpeg montajiga o'tadi — o'shanda server bo'sh turadi.

---

## 1. Deploydan OLDIN

### 1.1 Vaqtni tanlash

🔴 **Jonli dars ketayotganda deploy qilinmaydi.** Yozuvi yoqilgan guruhlar
odatda 14:00, 20:00 va 21:30 da boshlanadi.

```bash
ssh root@138.68.101.252 \
  'docker exec zinnur-v2-postgres psql -U zinnur -d zinnur -t -A \
   -c "SELECT count(*) FROM \"LiveSessions\" WHERE \"Status\" = 1;"'
```

Natija `0` bo'lishi shart. Eng xavfsiz oyna — **yarim kechadan keyin**.

### 1.2 `.env` ni to'ldirish

Serverdagi `.env` ga `COMPOSITOR_*` o'zgaruvchilari qo'shiladi (nomlari va
izohlari `.env.example` da). `.env` **git'ga tushmaydi** — qo'lda tahrirlanadi.

```bash
ssh root@138.68.101.252 'grep -E "^COMPOSITOR_" /root/edbase-v2/.env'
```

Bo'sh chiqsa — `.env.example` dan ko'chirib qo'ying.

⚠️ `LIVEKIT_API_KEY` bo'sh bo'lmasligi SHART. `deploy.sh` uni webhook
shabloniga yozadi; bo'sh bo'lsa deploy **ataylab to'xtaydi**, chunki bo'sh
kalit bilan LiveKit hodisani imzolay olmaydi va uni **jimgina** tashlab
yuboradi — hech qayerda xato chiqmaydi.

### 1.3 Zaxira

`deploy.sh` bazani o'zi zaxiralaydi va zaxira olinmasa deployni to'xtatadi.
Qo'shimcha qadam kerak emas.

---

## 2. Deploy

```bash
ssh root@138.68.101.252
cd /root/edbase-v2 && ./infra/scripts/deploy.sh
```

Skript ketma-ketligi: `git pull --ff-only` → LiveKit prod yaml'ini shablondan
yasash → baza zaxirasi → `api`, `web`, `compositor` qurish → `up -d`
(migratsiyalar avtomatik) → salomatlik tekshiruvi.

---

## 3. Deploydan KEYIN — har birini tekshiring

Bu bo'limdagi to'rttala tekshiruv ham **jim ishlamay qolish** holatlarini
ushlaydi. Ularni o'tkazib yubormang: hammasi "ishlayapti" ko'rinadi-yu,
yozuvlar ertalab paydo bo'lmaydi.

### 3.1 Konteynerlar

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml ps
```

`zinnur-v2-compositor` ro'yxatda va `healthy` bo'lishi kerak.

### 3.2 🔴 Egress sig'imi — ENG MUHIM TEKSHIRUV

```bash
docker logs zinnur-v2-egress 2>&1 | grep -i "cpu available"
```

`available` qiymati eng qimmat `max cost` dan **katta** bo'lishi shart.
Aks holda egress ishni **jimgina rad etadi**: LiveKit so'rovni qabul qiladi,
ishchi javob bermaydi, API ning 10 soniyalik muhlati tugaydi va yozuv
"Yozuv xizmati javob bermadi (timeout)" bilan yiqiladi. Egress logida esa
**birorta job ko'rinmaydi**.

> Bu 2026-09-01 da aynan shu tarzda sodir bo'lgan va aniqlash uzoq vaqt
> olgan. `track_cpu_cost` standart qiymati **1.0** — sozlanmasa hozirgi
> `EGRESS_CPUS` da faqat BITTA trek egressi qabul qilinardi.

⚠️ **Sig'im yetmasa `EGRESS_CPUS` ni KO'TARMANG.** Mashinada jami 4 vCPU va
o'sha yerda SFU, API, Postgres, Redis ham ishlaydi. Egress ortiqcha olsa
birinchi qurbon yozuv emas — **jonli darsning o'zi** bo'ladi. Buning
o'rniga `cpu_cost` qiymatlarini qayta hisoblang.

### 3.3 Webhook haqiqatan kelayaptimi

```bash
docker logs zinnur-v2-api --since 10m 2>&1 | grep -i webhook
```

Endpoint va imzo tekshiruvi allaqachon yozilgan; bu qadam faqat
**jo'natuvchi** yoqilganini tasdiqlaydi. Hodisa kelmasa yangi oqim ekran
ulashish qachon boshlanganini bilmaydi.

Tez sinov: bitta test darsini boshlab, `track_published` kelishini kuzating.

### 3.4 Eski oqim buzilmaganini tasdiqlash

Sozlamasi o'zgarmagan guruh **eski xulqni bayt-mabayt** saqlashi kerak.
Birinchi oddiy darsdan keyin panelda yozuv "Tayyor" bo'lganini ko'ring.

---

## 4. Yoyish — BOSQICHMA-BOSQICH

🔴 **Hamma guruhni birdan o'tkazmang.**

### 4.1 1-bosqich: bitta guruh, yonma-yon

Sozlamalar panelidan:

| Kalit | Qiymat |
|---|---|
| `recordings.track_pipeline_enabled` | `true` |
| `recordings.track_pipeline_shadow_groups` | `7` |

Guruh **7 — ATF-97**, dushanba/payshanba 10:00. U jadvalda **yolg'iz**
turadi, ya'ni sinov boshqa darslarga umuman ta'sir qilmaydi.

Bu rejimda bitta dars **ikkita yozuv** beradi — eski va yangi oqim. Panelda
ular nishon bilan farqlanadi. **Bu nosozlik emas**, xodimlarni ogohlantiring.

### 4.2 Ertasi ertalab — solishtiring

Ikkala faylni **yonma-yon oching** va tekshiring:

- [ ] Doskadagi/slaydlardagi matn o'qiladimi (yangisi to'liq ruxsatda)
- [ ] Ovoz videoga mos keladimi (siljish yo'qmi)
- [ ] **Talaba ovozlari eshitiladimi** — savollar, gapirish mashqlari
- [ ] Ekran ulashish dars o'rtasida yoqilgan bo'lsa, u tushganmi
- [ ] Ustoz uzilib qayta ulangan bo'lsa, o'sha joy to'g'ri yig'ilganmi

Ovoz biroz siljigan bo'lsa — bu **sozlanadi**:
`recordings.compose_audio_offset_ms` (bitta raqam, deploysiz).

### 4.3 Egress narxini o'lchash

```bash
docker logs zinnur-v2-egress 2>&1 | grep "egress metrics"
```

Audio mikserning `avgCPU` qiymatiga qarang. SPEC uni **0.15 deb TAXMIN
qilgan** va bu raqam har-dars narxining ~60% i — ya'ni butun narx da'vosi
shu bitta o'lchanmagan qiymatga tayanadi.

| O'lchov | Qaror |
|---|---|
| ≤ 0.20 | davom etamiz |
| 0.20 – 0.35 | sig'im jadvalini qayta hisoblab, keyin yoyamiz |
| > 0.35 | **to'xtaymiz** |

Shu bilan birga manba turini tasdiqlang:

```bash
docker logs zinnur-v2-egress 2>&1 | grep "request validated" | grep -o 'sourceType[^,]*'
```

`EGRESS_SOURCE_TYPE_SDK` bo'lishi kerak. `WEB` chiqsa — Chrome ishga
tushgan, ya'ni arzonlik farazi noto'g'ri. U holda
`recordings.audio_capture_mode` ni `TeacherTrack` ga o'tkazing (deploysiz)
va qayta o'lchang.

### 4.4 Keyingi bosqichlar

Sinov muvaffaqiyatli bo'lsa `track_pipeline_shadow_groups` ro'yxatiga
guruhlarni **bir necha kunda birma-bir** qo'shing. Har qo'shimchadan keyin
tungi montaj oynaga sig'ayotganini tekshiring (5-bo'lim).

---

## 5. Tungi montajni kuzatish

```bash
docker logs zinnur-v2-compositor --since 12h 2>&1 | tail -50
```

Navbatda nima qolganini ko'rish:

```bash
docker exec zinnur-v2-postgres psql -U zinnur -d zinnur -c \
'SELECT "CompositionStatus", count(*) FROM "SessionRecordings"
 WHERE "Pipeline" = 1 GROUP BY 1 ORDER BY 1;'
```

`CompositionStatus`: 0 Collecting · 1 Queued · 2 Running · 3 Completed · 4 Failed.

**Navbat o'sib borayotgan bo'lsa** — montaj tungi oynaga sig'mayapti.
Tugallanmagan ish ertasi kechaga o'tadi (bu ataylab shunday), lekin har kuni
takrorlansa navbat cho'zilib ketadi. Yechimlar:

- `recordings.compose_preset` ni `medium` da qoldiring (`slow` sig'maydi)
- oynani kengaytiring: `recordings.compose_window_start` / `_end`
- yoki tezroq temir kerak

---

## 6. 🔴 ORQAGA QAYTISH

**Eng tez yo'l — deploy KERAK EMAS.** Sozlamalar panelidan:

```
recordings.track_pipeline_enabled = false
```

Bu kill switch: guruh ustunida nima yozilganidan qat'i nazar **hamma guruh**
darhol eski `RoomComposite` oqimiga qaytadi. Konteyner qayta ishga
tushirilmaydi, jonli darslar uzilmaydi.

Allaqachon yozib olingan xom bo'laklar joyida qoladi va tungi montaj ularni
baribir yig'adi — ya'ni kill switch bosilgani mavjud yozuvlarni yo'qotmaydi.

### Kodni qaytarish (kamdan-kam kerak)

```bash
cd /root/edbase-v2
git reset --hard <oldingi-commit> && ./infra/scripts/deploy.sh
```

⚠️ **Migratsiya qaytarilmaydi.** Yangi ustunlar va `RecordingTracks` jadvali
qoladi — ular additive, eski kodga xalaqit bermaydi. Bazani orqaga qaytarish
faqat deploy oldidagi zaxiradan tiklash bilan bo'ladi va bu **oxirgi chora**.

---

## 7. Sozlamalar ro'yxati

| Kalit | Standart | Vazifasi |
|---|---|---|
| `recordings.track_pipeline_enabled` | `false` | **Kill switch.** `false` — hamma guruh eski oqimda |
| `recordings.track_pipeline_shadow_groups` | bo'sh | Yangi oqim yoqiladigan guruh id'lari |
| `recordings.audio_capture_mode` | `RoomComposite` | Ovoz manbai; zaxira qiymat — `TeacherTrack` |
| `recordings.compose_window_start` | `00:00` | Montaj oynasining boshi (`HH:mm`, aynan 5 belgi) |
| `recordings.compose_window_end` | `09:00` | Oxiri |
| `recordings.compose_preset` | `medium` | x264 preseti |
| `recordings.compose_crf` | `21` | Sifat (kichikroq = yaxshiroq va kattaroq fayl) |
| `recordings.compose_audio_offset_ms` | `0` | Ovoz siljishini tuzatish |

---

## 8. Ma'lum tuzoqlar

| Tuzoq | Alomati | Sabab |
|---|---|---|
| `EGRESS_CPUS` < eng qimmat `cpu_cost` | "Yozuv xizmati javob bermadi", egress logida job yo'q | Ishchi vazifani jimgina rad etadi |
| `webhook.api_key` `.env` bilan mos emas | Hodisa umuman kelmaydi, xato yo'q | LiveKit imzolay olmaydi |
| `api` da `target: runtime` olib tashlansa | Sezilmaydi | API image jimgina ffmpeg'li image'ga ko'chadi |
| `livekit.prod.generated.yaml` yo'q | LiveKit "is a directory" bilan yiqiladi | Docker fayl o'rniga bo'sh papka yaratadi — **jonli darslar to'xtaydi** |
| `Composition__Enabled` ikkala konteynerda `true` | Buzilgan fayllar | Ikki kodlovchi bitta kalitga yozadi |
| ffmpeg yo'q mashinada test | Testlar "yashil" | `FfmpegCompositionTests` jimgina o'tib ketadi |
