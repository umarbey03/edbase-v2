# Ish jurnali — tunги sessiya (2026-07-29 → 30)

> Bu fayl **avtomatik yuritiladi**. Maqsad: sessiya uzilib qolsa ham nima
> qilinganini va keyingi qadam nima ekanini aniq bilish.
>
> Reja: `docs/ROADMAP.md` · Shartnoma: `docs/SPEC.md`

---

## ✅ FAZA 1.1 — EF Core migratsiyalari — **TUGADI**

### Qilingan ishlar

| Fayl | Nima |
|---|---|
| `backend/src/Zinnur.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` | `dotnet ef` uchun fabrika (ilovani ishga tushirmasdan migratsiya yaratish) |
| `.../Persistence/Migrations/20260729191315_Initial.cs` | Birinchi migratsiya (avtomatik generatsiya) |
| `.../Persistence/DbInitializer.cs` | `EnsureCreated` **olib tashlandi** — faqat `MigrateAsync` |
| `backend/.editorconfig` | Generatsiya qilingan migratsiya kodini analizdan chiqarish |
| `docs/MIGRATIONS.md` | Migratsiya qo'shish/bekor qilish/qo'llash tartibi |

### Nima uchun `EnsureCreated` olib tashlandi

U sxemani yaratadi, lekin `__EFMigrationsHistory` jadvalini **yozmaydi**.
Natijada keyinchalik birinchi migratsiya qo'llanganda EF "relation already
exists" bilan yiqiladi — ishlab chiqarish bazasida tuzatib bo'lmaydigan holat.

### Tasdiqlangan natija

```
docker compose down -v && docker compose up -d
→ __EFMigrationsHistory: 20260729191315_Initial | 9.0.0   ← ilgari BO'SH edi
→ 10 jadval, 29 indeks
→ /health/ready 200, login 200
```

Kritik indekslar bazada tasdiqlandi:
`UX_LiveSessions_RoomName` · `UX_Attendances_SessionId_StudentId` · `UX_GroupMembers_GroupId_StudentId`

### Yo'l-yo'lakay tuzatilgan

`CA1861` — EF generatsiya qilgan migratsiya kodida analizator xatosi. Kodni
qo'lda tuzatish noto'g'ri bo'lardi (keyingi migratsiyada yo'qoladi), shuning
uchun `.editorconfig` da `**/Migrations/*.cs` uchun qoida o'chirildi va sabab
izohlandi.

---

## ✅ CI liniyasi — yozildi

`.github/workflows/ci.yml` — 4 ta job:

| Job | Nima tekshiradi |
|---|---|
| `backend` | `dotnet build` (ogohlantirish = xato) + unit testlar + coverage |
| `integration` | Postgres 17 + Redis 7 xizmatlari bilan integratsiya testlari |
| `frontend` | `vue-tsc --noEmit` + lint + `npm run build` |
| `docker` | dev va prod compose config + shell skript sintaksisi |

> Hali GitHub'ga push qilinmagan — repo lokal.

---

## 🔄 Hozir parallel ishlamoqda

| Faza | Ish | Egasi |
|---|---|---|
| 1.2 | Domain unit testlari (`tests/Zinnur.UnitTests`) | agent |
| 1.3 | Sentry + strukturali log + health checks | agent |
| 2.1 | Foydalanuvchilar moduli (CRM) | agent |

---

## ⏭ Keyingi navbatda

1. **Faza 2.2/2.3** — Guruhlar + jadval generatsiyasi
2. **Faza 1.2** integratsiya testlari (migratsiyalar tayyor, endi mumkin)
3. **Faza 3** — o'quv jarayoni (kurs, vazifa, test, davomat)

---

## Muhim eslatmalar

### Build tezligi
NuGet keshi uchun `zinnur-nuget-cache` nomli Docker volume yaratilgan.
**Har `dotnet` buyrug'ida uni ulang**, aks holda build 3-4 daqiqa o'rniga
har safar paketlarni qaytadan yuklaydi:

```bash
docker run --rm -v "$PWD":/src -w /src \
  -v zinnur-nuget-cache:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet build Zinnur.sln -v q --nologo
```

Kesh bilan: **~3 sekund**. Keshsiz: ~4 daqiqa.

### Analizator tuzoqlari (agentlar doim urinadi)
- `CA1848` — `logger.LogX(...)` taqiqlangan, `[LoggerMessage]` ishlating (`ApiLog.cs`)
- `CA1305` — har `ToString()`/`Parse` ga `CultureInfo.InvariantCulture`
- `CA1711` — tur nomi `Queue`/`Flags` bilan tugamasin
- `CA1716` — zaxiralangan kalit so'zlar (`Module` → `CourseModule`)

### Docker loyihalari
- `zinnur-legacy` — eski Python tizimi, `localhost:8000`, **tegilmaydi**
- `zinnur-v2` — yangi C# tizimi, `localhost:5173` / `:5080`

Ikkalasi to'liq ajratilgan (konteyner, tarmoq, volume). Bir loyihadagi
`docker compose down` ikkinchisiga ta'sir qilmaydi — amalda sinalgan.
