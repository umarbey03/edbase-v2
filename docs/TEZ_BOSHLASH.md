# Tez boshlash — lokal ishga tushirish

> **Kim uchun:** loyihani birinchi marta ko'tarayotgan hamkasb.
> **Sana:** 2026-08-27 · **Holat:** namunaviy ma'lumotsiz (pastdagi 🔴 ga qarang)

---

## 🔴 NAMUNAVIY MA'LUMOT YO'Q (2026-08-27 dan)

Ilgari bu hujjat "uch buyruq — va tayyor demo markaz" deb boshlanardi:
bo'sh bazaga 18 foydalanuvchi, guruhlar, darslar va to'lovlar yozilardi,
kirish sahifasida esa rol tugmalari bor «Sinov rejimi» paneli chiqardi.

**Ikkalasi ham kod bazasidan butunlay olib tashlandi** (loyiha egasining
qarori: tizim faqat haqiqiy ishlab chiqarishda ishlatiladi). `SEED_DEMO`
va `DEV_QUICK_LOGIN` kalitlari endi mavjud emas — `.env` ga yozsangiz ham
hech narsa qilmaydi.

**Ya'ni bo'sh bazaga faqat BITTA yozuv tushadi:** bosh administrator.
Guruh, o'quvchi, kurs — hammasi interfeys orqali qo'lda kiritiladi.

---

## Uch buyruq

```bash
git clone https://github.com/umarbey03/edbase-v2 && cd edbase-v2
cp .env.example .env
docker compose up -d --build
```

Birinchi ko'tarilish ~2–4 daqiqa (image yig'iladi, migratsiyalar qo'llanadi).

| Manzil | Nima |
|---|---|
| http://localhost:5173 | Ilova |
| http://localhost:5080/swagger | API |
| http://localhost:9011 | MinIO (fayl ombori) |

---

## Kirish — telefon + kod

Kirish **faqat telefon raqami orqali**. Dev'da standart administrator
raqami — `+998900000001` (`DbInitializer.DevAdminPhone`). Prod'da standart
YO'Q: u yerda `.env` dagi `Bootstrap__AdminPhone` majburiy.

1. Kirish sahifasiga `+998900000001` ni kiriting.
2. Kod **telefonga kelmaydi** — dev'da haqiqiy Telegram boti yo'q, kod
   `MessageOutbox` navbatida qoladi. Uni bazadan o'qing:

```bash
docker compose exec -T postgres psql -U zinnur -d zinnur -c \
  "SELECT \"Body\" FROM \"MessageOutbox\" WHERE \"TemplateKey\"='auth_login_code' \
   ORDER BY \"Id\" DESC LIMIT 1;"
```

3. Kodni formaga yozing — kirdingiz.

★ `.env` da soxta `TELEGRAM_BOT_TOKEN` turibdi: usiz server "kodni
yubora olmayman" deb **503** qaytarardi.

### Telegram'ni qo'lda bog'lash (yana ham tez)

```bash
docker compose exec -T postgres psql -U zinnur -d zinnur -c \
  "UPDATE \"Users\" SET \"TelegramId\"=111111111, \"TelegramLinkedAt\"=now() \
   WHERE \"Email\"='admin@zinnur.uz';"
```

---

## Bazani noldan tiklash

Bosh administrator **faqat bo'sh bazaga** yoziladi (bitta foydalanuvchi
bo'lsa ham `DbInitializer` hech nima qilmaydi). Qaytadan boshlash:

```bash
docker compose down
docker volume rm zinnur-v2-postgres-data
docker compose up -d
```

---

## Muammo bo'lsa

| Belgi | Sabab |
|---|---|
| Telefon bilan kirishda **503** | `TELEGRAM_BOT_TOKEN` bo'sh. `.env.example` dagi soxta qiymatni ko'chiring |
| Kod kelmadi | Kutilgan — yuqoridagi `MessageOutbox` so'rovi bilan o'qing |
| API ko'tarilmayapti | `docker compose logs api` — odatda migratsiya yoki `.env` yetishmasligi |
| Ekranlar bo'sh | Kutilgan — namunaviy ma'lumot yo'q, ma'lumot qo'lda kiritiladi |

---

Serverga chiqarish uchun: [`DEPLOY_UBUNTU.md`](./DEPLOY_UBUNTU.md).
