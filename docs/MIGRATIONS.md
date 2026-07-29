# Ma'lumot bazasi migratsiyalari

> **Qoida:** sxema FAQAT migratsiya orqali o'zgaradi. Bazaga qo'lda `ALTER TABLE`
> yozish taqiqlanadi — aks holda muhitlar (dev/staging/prod) bir-biridan
> ajralib ketadi va buni keyin tuzatib bo'lmaydi.

---

## Nima uchun `EnsureCreated` ishlatilmaydi

`EnsureCreated()` sxemani modeldan bir marta yaratadi, lekin
`__EFMigrationsHistory` jadvalini **yozmaydi**. Natijada:

- keyinchalik birinchi migratsiya qo'llanganda EF "relation already exists" bilan yiqiladi;
- yoki sxema va migratsiya tarixi bir-biriga mos kelmay qoladi.

Ishlab chiqarish bazasida bu **tuzatib bo'lmaydigan** holat. Shuning uchun
`DbInitializer` faqat `MigrateAsync()` chaqiradi.

---

## Lokal muhit (kompyuterga .NET o'rnatmasdan)

Barcha buyruqlar Docker orqali. Ish papkasi: `zinnur-v2/backend`.

NuGet keshi uchun nomlangan volume ishlatiladi — aks holda har chaqiruv
paketlarni qaytadan yuklaydi (3-4 daqiqa farq).

```bash
docker volume create zinnur-nuget-cache    # bir marta
```

### Yangi migratsiya qo'shish

```bash
cd ~/Documents/Projects/zinnur-v2/backend

docker run --rm -v "$PWD":/src -w /src \
  -v zinnur-nuget-cache:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:9.0 bash -c '
    dotnet tool install --global dotnet-ef --version 9.* >/dev/null 2>&1 || true
    export PATH="$PATH:/root/.dotnet/tools"
    dotnet ef migrations add <MigratsiyaNomi> \
      -p src/Zinnur.Infrastructure \
      -s src/Zinnur.WebApi \
      -o Persistence/Migrations'
```

`<MigratsiyaNomi>` — nima o'zgarganini aytadigan nom:
`AddPaymentTables`, `AddUserPhoneIndex`, `RenameLessonTitle`.

### Oxirgi migratsiyani bekor qilish (hali qo'llanmagan bo'lsa)

```bash
docker run --rm -v "$PWD":/src -w /src \
  -v zinnur-nuget-cache:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:9.0 bash -c '
    dotnet tool install --global dotnet-ef --version 9.* >/dev/null 2>&1 || true
    export PATH="$PATH:/root/.dotnet/tools"
    dotnet ef migrations remove -p src/Zinnur.Infrastructure -s src/Zinnur.WebApi'
```

### SQL ni ko'rish (qo'llashdan oldin tekshirish)

Ishlab chiqarishga chiqarishdan **oldin** har doim SQL ni ko'rib chiqing:

```bash
docker run --rm -v "$PWD":/src -w /src \
  -v zinnur-nuget-cache:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:9.0 bash -c '
    dotnet tool install --global dotnet-ef --version 9.* >/dev/null 2>&1 || true
    export PATH="$PATH:/root/.dotnet/tools"
    dotnet ef migrations script --idempotent \
      -p src/Zinnur.Infrastructure -s src/Zinnur.WebApi -o /src/migration.sql'
```

`--idempotent` — skript qaysi migratsiya qo'llanganini tekshiradi, shuning
uchun uni bir necha marta ishga tushirish xavfsiz.

---

## Qo'llash

### Lokal / dev

Avtomatik: `api` konteyneri ko'tarilganda `DbInitializer` kutilayotgan
migratsiyalarni qo'llaydi.

```bash
docker compose up -d
docker compose logs api | grep -i migrat
```

### Ishlab chiqarish

Migratsiya **ilova ishga tushishidan alohida** bajarilishi tavsiya etiladi —
aks holda bir necha replika bir vaqtda migratsiya qilishga urinadi.

```bash
# 1. Zaxira nusxa (MAJBURIY)
./infra/scripts/backup-db.sh

# 2. SQL ni ko'rib chiqish
#    (yuqoridagi `migrations script --idempotent`)

# 3. Bitta konteynerda qo'llash
docker compose run --rm api dotnet Zinnur.WebApi.dll --migrate-only

# 4. Ilovani yangilash
docker compose up -d
```

> `--migrate-only` bayrog'i hali qo'shilmagan — Faza 1 oxirida qo'shiladi.
> Hozircha `api` ko'tarilganda avtomatik qo'llanadi.

---

## Bo'sh bazadan qayta qurish (tekshirish)

Har katta o'zgarishdan keyin shuni bajaring — sxema noldan to'g'ri
qurilishiga ishonch hosil qilish uchun:

```bash
cd ~/Documents/Projects/zinnur-v2
docker compose down -v      # DIQQAT: barcha ma'lumot o'chadi (faqat dev!)
docker compose up -d
docker compose logs -f api  # migratsiya qo'llanganini kuzatish
```

---

## Muhim tekshiruvlar (migratsiyada bo'lishi SHART)

Bu constraint'lar eski tizimdagi haqiqiy nosozliklardan kelib chiqqan:

| Constraint | Nima uchun |
|---|---|
| `UX_LiveSessions_RoomName` (unique) | Xona nomi to'qnashganda ikki dars bitta LiveKit xonasiga tushardi va davomat butunlay to'xtardi |
| `UX_Attendances_SessionId_StudentId` | Bir o'quvchiga bir darsda ikkita davomat yozuvi bo'lmasin |
| `UX_GroupMembers_GroupId_StudentId` | Takroriy a'zolik |
| `IX_Users_Email` (unique) | — |
| `IX_Users_Phone` (unique, `WHERE Phone IS NOT NULL`) | Telefonsiz foydalanuvchilar ko'p bo'lishi mumkin — filtrsiz unikal indeks ularni bloklardi |
| `IX_Users_TelegramId` (unique, filtrlangan) | Xuddi shu sabab |

Tekshirish:

```bash
docker compose exec postgres psql -U zinnur -d zinnur -c "\di"
```
