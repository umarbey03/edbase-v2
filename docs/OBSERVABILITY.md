# Kuzatuv (Observability)

> **Nima uchun kerak:** eski tizimda xatolarni yig'adigan hech narsa yo'q edi.
> Nosozlik yuz berganda birinchi "signal" — o'quvchi yoki ustozning shikoyati
> bo'lardi. Fon vazifalari to'xtasa, buni faqat "nega bugun eslatma kelmadi?"
> degan savoldan bilib olish mumkin edi.

---

## 1. Uch qatlam

| Qatlam | Vosita | Qachon ishlaydi |
|---|---|---|
| **Xato kuzatuvi** | Sentry (backend + frontend) | `Sentry:Dsn` / `VITE_SENTRY_DSN` berilganda |
| **Strukturali log** | Serilog → JSON (prod), matn (dev) | Har doim |
| **Sog'liq tekshiruvi** | `/health`, `/health/ready` | Har doim |

**Muhim:** Sentry **ixtiyoriy**. DSN bo'lmasa ilova normal ishlaydi va
frontend'da Sentry kodi **umuman yuklanmaydi** (0 bayt).

---

## 2. Sozlash

### Backend (`.env`)

```bash
Sentry__Dsn=                      # bo'sh = o'chiq
Sentry__Environment=production    # production | staging | development
Sentry__TracesSampleRate=0.1      # performance izlarining 10%i
```

### Frontend (`frontend/.env`)

```bash
VITE_SENTRY_DSN=                      # bo'sh = o'chiq, kod yuklanmaydi
VITE_SENTRY_ENVIRONMENT=production
VITE_RELEASE=2026.07.30               # backend'dagi reliz bilan BIR XIL bo'lsin
```

> `VITE_RELEASE` backend relizi bilan mos bo'lishi **muhim** — shundagina
> frontend va backend xatolari bitta hodisaga bog'lanadi.

### DSN qanday olinadi

1. `sentry.io` da ro'yxatdan o'ting (bepul tarif kichik loyihaga yetadi)
2. **Ikki alohida** proyekt yarating: `zinnur-api` (.NET) va `zinnur-web` (Vue)
3. Har birining **Settings → Client Keys (DSN)** dan DSN'ni oling

---

## 3. Nima YUBORILADI va nima TOZALANADI

### Yuboriladi
- Istisno turi, xabari, stack trace
- `traceId` (so'rovni logdan topish uchun)
- `userId` (raqam) — kim duch kelganini bilish uchun
- HTTP metod, yo'l, status kodi
- Muhit va reliz

### TOZALANADI (hech qachon ketmaydi)

| Nima | Qayerda |
|---|---|
| `Authorization` header | backend + frontend |
| `Cookie` / `Set-Cookie` | backend + frontend |
| URL'dagi `?access_token=` | backend + frontend |
| `password`, `secret`, `token` nomli maydonlar | backend |
| IP manzil, brauzer PII | `sendDefaultPii = false` |

`?access_token=` alohida ahamiyatli: SignalR ulanishi tokenni **URL'da**
yuboradi (WebSocket header qo'llab-quvvatlamaydi), shuning uchun u
breadcrumb'larga tushishi mumkin. Ikki tomonda ham `[yashirildi]` bilan
almashtiriladi.

### 4xx Sentry'ga YUBORILMAYDI

Kutilgan xatolar (404 topilmadi, 403 ruxsat yo'q, 400 validatsiya) shovqin
hosil qilardi va haqiqiy nosozliklarni ko'rinmas qilardi. Faqat **5xx**
yuboriladi.

---

## 4. Strukturali log

### Prod: JSON (CLEF formati)

```json
{"@t":"2026-07-30T02:19:25.57Z","@l":"Information","@m":"Darsga qo'shildi: session=1 user=4 jami=12",
 "SessionId":1,"UserId":4,"Count":12,"traceId":"00-a609ee...-9c2da8f4-00","Environment":"Production"}
```

Har yozuvda: `traceId`, `userId` (autentifikatsiya bo'lsa), muhit, reliz.

### Dev: o'qiladigan matn

Ishlab chiqishda JSON o'qishga noqulay, shuning uchun konsolga oddiy matn.

### Log darajalari

`appsettings.json` → `Serilog:MinimumLevel`. Standart:
`Microsoft.AspNetCore` = Warning, `EntityFrameworkCore.Database.Command` = Warning
(aks holda har SQL so'rov logga tushib, muhim narsalarni ko'mib tashlaydi).

---

## 5. Sog'liq tekshiruvi

### `/health` — tirikmi (liveness)

Faqat jarayon javob berayotganini tekshiradi. Docker `healthcheck` va
Kubernetes `livenessProbe` shuni ishlatadi. Bog'liqliklarni tekshirmaydi —
aks holda Postgres bir soniya sekinlashsa konteyner qayta ishga tushib ketardi.

### `/health/ready` — xizmatga tayyormi (readiness)

Har bog'liqlikni alohida ko'rsatadi:

```json
{
  "status": "Healthy",
  "totalDurationMs": 12,
  "checks": [
    { "name": "postgres", "status": "Healthy", "durationMs": 4 },
    { "name": "redis",    "status": "Healthy", "durationMs": 2 },
    { "name": "livekit",  "status": "Healthy", "durationMs": 6 }
  ]
}
```

LiveKit tekshiruvi **qisqa timeout** bilan ishlaydi — aks holda LiveKit
javob bermasa butun health check osilib qolardi.

---

## 6. Foydalanuvchi shikoyat qilganda

Foydalanuvchi xato ekranidagi kodni aytadi (`traceId`). Uni topish:

```bash
# Loglardan
docker compose logs api | grep "a609ee9347b4649edba98fd7d8cd9ba1"

# JSON loglarda aniqroq
docker compose logs api --no-log-prefix \
  | python3 -c "
import sys,json
for line in sys.stdin:
    try:
        e = json.loads(line)
        if 'a609ee93' in json.dumps(e): print(json.dumps(e, indent=2, ensure_ascii=False))
    except: pass"
```

Sentry'da: **Search → `traceId:a609ee...`**

---

## 7. Ishlayotganini tekshirish

### Sentry o'chiq holatda (dev)

```bash
docker compose up -d api
docker compose logs api | grep -i "Kuzatuv:"
# -> Kuzatuv: Sentry=o'chirilgan, log=matn, reliz=dev
curl -s localhost:5080/health/ready    # Healthy bo'lishi kerak
```

### Sentry yoqilgan holatda

```bash
# .env ga DSN qo'ying, keyin:
docker compose up -d api
docker compose logs api | grep -i "Kuzatuv:"
# -> Kuzatuv: Sentry=yoqilgan, ...
```

Frontend'da: brauzer DevTools → Network → `sentry-*.js` bo'lagi yuklanganini
ko'rasiz. DSN bo'sh bo'lsa u **umuman yuklanmaydi**.

---

## 8. Frontend bundle haqida muhim qaror

Sentry brauzer SDK'si ~450 KB (gzip'da ~49 KB). Agar u asosiy bundle'ga
tushsa, **har foydalanuvchi, har sahifa yuklashida** uni yuklab olardi —
hatto Sentry o'chiq bo'lganda ham.

Foydalanuvchilarimiz Telegram Mini App'ni **mobil internetda** ochadi, shuning
uchun:

- `main.ts` da Sentry **dinamik import** qilinadi
- `vite.config.ts` da alohida `sentry` bo'lagiga ajratilgan
- DSN bo'lmasa — **0 bayt yuklanadi**

Tekshirish:

```bash
cd frontend && npm run build
ls -lh dist/assets/vendor-*.js dist/assets/sentry-*.js
# vendor ~67 KB, sentry ~450 KB (alohida, lazy)
```

---

## 9. Keyingi qadamlar (hozir yo'q)

- **Metrikalar** (Prometheus/OpenTelemetry) — so'rov davomiyligi, pool holati,
  SignalR ulanishlar soni. Yuklama testidan keyin qo'shiladi.
- **Ogohlantirish** (alerting) — Sentry'da xato tezligi oshsa Telegram'ga xabar.
- **Log agregatsiyasi** — hozir loglar konteynerda. Ko'p serverga o'tganda
  Loki yoki shunga o'xshash vosita kerak bo'ladi.
