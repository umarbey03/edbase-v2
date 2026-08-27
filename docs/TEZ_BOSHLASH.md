# Tez boshlash — sinov uchun

> **Kim uchun:** loyihani birinchi marta ko'tarayotgan hamkasb.
> **Sana:** 2026-08-14 · **Holat:** sinov bosqichi (pastdagi ⚠️ ga qarang)

---

## Uch buyruq

```bash
git clone https://github.com/umarbey03/edbase-v2 && cd edbase-v2
cp .env.example .env
docker compose up -d --build
```

Tamom. `.env` ni **tahrirlash shart emas** — sinov bosqichida namunaviy
ma'lumot ham, rol tugmalari ham standart bo'yicha YOQIQ.

Birinchi ko'tarilish ~2–4 daqiqa (image yig'iladi, migratsiyalar
qo'llanadi, namunaviy ma'lumot yoziladi).

| Manzil | Nima |
|---|---|
| http://localhost:5173 | Ilova |
| http://localhost:5080/swagger | API |
| http://localhost:9011 | MinIO (fayl ombori) |

---

## Kirish — parol ham, telefon ham kerak emas

Kirish sahifasining pastida **«SINOV REJIMI»** paneli bor. Rolni bosasiz —
kirdingiz:

| Tugma | Hisob |
|---|---|
| Administrator | Bosh administrator |
| O'quv bo'limi | Dilnoza Ergasheva |
| Ustoz | Bekzod Rahimov |
| Kurator | Javohir To'xtayev |
| O'quvchi | Ozodbek Yo'ldoshev |

### Haqiqiy oqimni (telefon + kod) sinash

Panel emas, **haqiqiy** kirishni sinamoqchi bo'lsangiz — telefon raqamini
kiriting (masalan `+998901110011`), so'ng kodni bazadan o'qing:

```bash
docker exec zinnur-v2-postgres psql -U zinnur -d zinnur \
  -c 'SELECT "Body" FROM "MessageOutbox" ORDER BY "Id" DESC LIMIT 1;'
```

★ Dev'da haqiqiy Telegram boti yo'q, shuning uchun kod yuborilmaydi —
navbat jadvaliga yoziladi. `.env` da soxta `TELEGRAM_BOT_TOKEN` turibdi:
usiz server "kodni yubora olmayman" deb **503** qaytarardi.

Barcha raqamlar API logida jadval bo'lib chiqadi:

```bash
docker compose logs api | grep -A 20 "Namunaviy"
```

---

## Nima yozilgan

18 foydalanuvchi · 5 guruh (faol / individual / kurator / arxiv,
yo'nalishlari bilan) · kurs → 2 modul → 6 dars (biri **3 qismli video**) ·
7 jonli dars · 24 davomat · 13 dars bahosi · 2 vazifa, 6 topshiriq ·
2 test · 26 to'lov · 12 guruh xabari · 8 shaxsiy xabar · 4 bildirishnoma ·
2 sifat nazorati xulosasi.

Qamrov ataylab «chekka holatlarni» ham o'z ichiga oladi — ularsiz ekranlar
bo'sh ko'rinardi:

- davomatda **Present / Late / Partial / Absent va belgilanmagan**;
- to'lovda **chegaradan oshgan qarzdor** — video darslarga kirishga
  urinsa **403** oladi (to'lov bloki shu bilan sinaladi);
- yozuvlarda **o'quvchidan yashirilgani** va **sifat xulosasi borlari**;
- topshiriqlarda tekshirilmagan / baholangan / qayta ochilgan / kechikkan.

### ⚠️ Video ochilmaydi

Dars videolari va dars yozuvlari — **faqat metama'lumot**. Repoga MP4
qo'yilmagan, shuning uchun pleyerni bosganda **404** bo'ladi.

Qismlar soni, tartibi, nomlari, davomiyligi, ruxsatlar va to'lov bloki
esa haqiqiy va sinab ko'rsa bo'ladi. **Rasm va hujjatlar haqiqiy** —
ular MinIO'ga yuklangan va ochiladi.

---

## Bazani noldan tiklash

Namunaviy ma'lumot **faqat bo'sh bazaga** yoziladi (`Users > 3` bo'lsa
seeder ishlamaydi — ishlab turgan markazga soxta o'quvchi tushmasligi
uchun). Qaytadan boshlash:

```bash
docker compose down
docker volume rm zinnur-v2-postgres-data
docker compose up -d
```

---

## ⚠️ BU SOZLAMALAR SINOV BOSQICHI UCHUN

Ikki kalit sinov qulayligi uchun bor va ular **`.env` da** yoqiladi
(`.env.example` da ikkalasi ham `true` — shuning uchun yuqoridagi uch
buyruq ishlaydi):

| Kalit | Nima qiladi |
|---|---|
| `SEED_DEMO` | Bo'sh bazaga namunaviy ma'lumot yozadi |
| `DEV_QUICK_LOGIN` | Kirish sahifasidagi rol tugmalari |

🔴 **`DEV_QUICK_LOGIN` — autentifikatsiyani chetlab o'tish.** Uni ikki
darvoza ushlab turadi:

1. muhit `Production` bo'lsa — tugmalar **umuman chiqmaydi**;
2. faqat **namunaviy** hisoblarga kiradi (soxta Telegram ID diapazoni) —
   haqiqiy markazning administratoriga bu yo'l bilan **hech qachon**
   kirib bo'lmaydi.

---

### ✅ 2026-08-22: standartlar `false` ga qaytarildi

Ilgari bu ikki kalitning standarti `docker-compose.yml` da `:-true` edi
va bu yerda "`docker-compose.prod.yml` ularni umuman bilmaydi" deb
yozilgan edi. **Bu xato edi** — Compose overlay'lari `environment` ni
BIRLASHTIRADI, ya'ni ikkalasi ham prod konfiguratsiyasiga `true` bo'lib
tushardi:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml config \
  | grep -E 'Seed__Demo|Dev__QuickLogin'
```

`SEED_DEMO` uchun bu haqiqiy xavf edi: "bazada 3 tadan ko'p foydalanuvchi
bo'lsa seeder ishlamaydi" kafolati **ishlab turgan** markaz uchun to'g'ri,
lekin **yangi server** uchun emas — u yerda baza aynan 3 ta foydalanuvchi
bilan boshlanadi va shart `> 3` bo'lgani uchun darvoza ochiq qolardi.

Endi:

* `docker-compose.yml` → `${SEED_DEMO:-false}` va `${DEV_QUICK_LOGIN:-false}`;
* `docker-compose.prod.yml` ham ikkalasini **aniq** `:-false` qilib yozadi
  (serverdagi `.env` da eski qator qolib ketsa ham himoya turishi uchun).

**Dev'da hech narsa o'zgarmadi** — `.env` ikkalasini yoqib turadi. Yangi
serverda namunani ko'rmoqchi bo'lsangiz, `.env` ga `SEED_DEMO=true`
yozish yetarli (bu yo'l ataylab ochiq qoldirilgan).

---

## Muammo bo'lsa

| Belgi | Sabab |
|---|---|
| Kirish sahifasida panel yo'q | Baza bo'sh emas edi → seeder ishlamagan. Yuqoridagi «noldan tiklash» ni bajaring |
| Telefon bilan kirishda **503** | `TELEGRAM_BOT_TOKEN` bo'sh. `.env.example` dagi soxta qiymatni ko'chiring |
| Video **404** | Kutilgan — yuqoriga qarang |
| API ko'tarilmayapti | `docker compose logs api` — odatda migratsiya yoki `.env` yetishmasligi |
