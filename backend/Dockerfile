# =============================================================================
#  ZIN-NUR v2 — BACKEND (.NET 9 Web API + SignalR)
# =============================================================================
#
#  Build konteksti: ./backend   (docker-compose.yml -> api -> build.context)
#  Ya'ni bu yerdagi barcha yo'llar `backend/` papkasiga NISBATAN yoziladi.
#
#  Yig'ish:  docker compose build api
#
#  TO'RT BOSQICHLI (multi-stage) qurilish:
#     restore -> build -> runtime -> runtime-media
#  Yakuniy image'da SDK ham, manba kod ham, NuGet keshi ham QOLMAYDI.
#  Natija: ~110 MB (SDK bilan bo'lsa ~800 MB) va hujum yuzasi ancha kichik.
#
#  🔴 IKKITA TURLI IMAGE, IKKITA TURLI XIZMAT — `--target` MAJBURIY:
#     `api`        -> `--target runtime`        (ffmpeg YO'Q, internetga ochiq)
#     `compositor` -> `--target runtime-media`  (ffmpeg BOR, tashqariga yopiq)
#
#     `--target` berilmasa Docker OXIRGI bosqichni quradi — ya'ni
#     `runtime-media` ni. Shuning uchun `docker-compose.yml` dagi `api`
#     xizmatida `build.target: runtime` YOZILGAN va uni olib tashlash
#     api image'iga ffmpeg'ni JIMGINA qo'shib qo'yardi (4-bosqich izohi).
# =============================================================================


# =============================================================================
#  BOSQICH 1 — RESTORE (NuGet paketlarini yuklash)
# =============================================================================
#  BU BOSQICHNING BUTUN MA'NOSI — DOCKER QATLAM KESHI.
#
#  Docker har `COPY`/`RUN` uchun qatlam yaratadi va kirish o'zgarmasa
#  qatlamni QAYTA ISHLATADI. Agar biz avval butun manbani ko'chirib,
#  keyin `dotnet restore` qilsak — HAR BIR .cs faylni o'zgartirganda
#  NuGet paketlari qaytadan yuklanadi (1-3 daqiqa, internetga bog'liq).
#
#  Shuning uchun tartib qat'iy:
#     1) faqat .csproj + Directory.Build.props ni ko'chirish   (kam o'zgaradi)
#     2) dotnet restore                                        <- KESHLANADI
#     3) endi qolgan manbani ko'chirish                        (tez-tez o'zgaradi)
#  Natijada kundalik ishda restore bosqichi umuman qayta bajarilmaydi.
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS restore

WORKDIR /src

# --- Directory.Build.props — MAJBURIY, TUSHIRIB QOLDIRIB BO'LMAYDI ---
# MSBuild bu faylni papkalar bo'ylab YUQORIGA qarab qidiradi va topgan
# joyida to'xtaydi. Unda TargetFramework=net9.0, Nullable, LangVersion va
# TreatWarningsAsErrors turibdi (SPEC 9.10).
# Agar ko'chirilmasa: `dotnet restore` "TargetFramework aniqlanmadi" degan
# tushunarsiz xato bilan yiqiladi. Bu — eng ko'p uchraydigan xato.
COPY Directory.Build.props ./

# --- Faqat loyiha fayllari (manba EMAS) ---
# Har birini ALOHIDA qatorda ko'chiramiz. `COPY src/ ./src/` deb
# yozib bo'lmaydi — u .cs fayllarni ham olib kelib, keshni buzadi.
# Yo'llar .csproj dagi <ProjectReference> bilan AYNAN mos bo'lishi shart.
COPY src/Zinnur.Domain/Zinnur.Domain.csproj                 src/Zinnur.Domain/
COPY src/Zinnur.Application/Zinnur.Application.csproj       src/Zinnur.Application/
COPY src/Zinnur.Infrastructure/Zinnur.Infrastructure.csproj src/Zinnur.Infrastructure/
COPY src/Zinnur.WebApi/Zinnur.WebApi.csproj                 src/Zinnur.WebApi/

# WebApi eng tashqi loyiha (SPEC 1): WebApi -> Infrastructure -> Application
# -> Domain. Uni restore qilsak, butun zanjir avtomatik restore bo'ladi.
#
# Zinnur.sln ATAYLAB ishlatilmadi: sln tests/ loyihalarini ham tortadi,
# ular prod image uchun keraksiz (xUnit va h.k. — ortiqcha yuklash va vaqt).
RUN dotnet restore src/Zinnur.WebApi/Zinnur.WebApi.csproj


# =============================================================================
#  BOSQICH 2 — BUILD + PUBLISH
# =============================================================================
FROM restore AS build

WORKDIR /src

# Endi butun manbani ko'chiramiz. .dockerignore bin/, obj/, .git/ ni
# to'sib turadi — aks holda host'dagi obj/ konteynerdagisini buzadi
# (turli platforma yo'llari => "Assets file not found" xatosi).
COPY . .

# `dotnet publish` — kompilyatsiya + barcha bog'liqliklarni bir papkaga yig'ish.
#
#   -c Release        optimallashtirilgan kod, DEBUG ramkalari yo'q.
#                     (SPEC talabi. Debug'da EF Core sezilarli sekin ishlaydi.)
#   --no-restore      1-bosqichda restore qilingan; qayta qilish vaqtni yeydi.
#   -o /app/publish   yakuniy natija shu papkada.
#   UseAppHost=false  native ishga tushirgich (apphost) yaratilmaydi.
#                     Bizga kerak emas: konteynerda `dotnet Zinnur.WebApi.dll`
#                     bilan ishga tushiramiz. Image ~5 MB kichrayadi va
#                     platformalararo muammolar yo'qoladi.
#
#  ESLATMA: Directory.Build.props da TreatWarningsAsErrors=true. Ya'ni
#  bitta ogohlantirish ham build'ni to'xtatadi — bu ATAYLAB (SPEC 9.10),
#  chala kod prod'ga chiqmaydi.
RUN dotnet publish src/Zinnur.WebApi/Zinnur.WebApi.csproj \
      -c Release \
      --no-restore \
      -o /app/publish \
      /p:UseAppHost=false


# =============================================================================
#  BOSQICH 3 — RUNTIME (yakuniy, eng kichik image)
# =============================================================================
#  aspnet:9.0-alpine — faqat ishga tushirish uchun kerakli komponentlar.
#  Alpine (musl libc) tanlandi: Debian variantidan ~3 barobar kichik.
#  Kichik image = tez deploy, kam disk, kam CVE.
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime

# --- ICU (xalqaro matn qo'llab-quvvatlash) ---
# Alpine image standart holatda "invariant globalization" rejimida keladi:
# ICU kutubxonasi YO'Q, barcha madaniyatlar "invariant" deb hisoblanadi.
# Bizga bu TO'G'RI KELMAYDI:
#   * o'zbek/kirill ismlarini to'g'ri solishtirish va tartiblash,
#   * sana/vaqtni mahalliy formatda ko'rsatish,
#   * `ToUpper()`/`ToLower()` ning madaniyatga bog'liq to'g'ri ishlashi.
# tzdata — vaqt zonalari bazasi. Usiz `TimeZoneInfo.FindSystemTimeZoneById`
# ("Asia/Tashkent") xato beradi, jadval vaqtlari esa aynan shunga tayanadi.
RUN apk add --no-cache icu-libs icu-data-full tzdata

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# --- ROOT'SIZ FOYDALANUVCHI (xavfsizlik talabi) ---
# Konteyner ichida root bo'lish — buzib kirilganda zarar ko'lamini keskin
# oshiradi. Tizimsiz (-S) guruh va foydalanuvchi yaratamiz.
# UID 10001: odatiy tizim UID'lari (0-999) bilan to'qnashmaydi.
RUN addgroup -g 10001 -S zinnur \
 && adduser  -u 10001 -S zinnur -G zinnur

WORKDIR /app

# Publish natijasini ko'chiramiz va egasini darrov zinnur'ga o'tkazamiz.
# --chown COPY paytida bajariladi — alohida `RUN chown` qatlami kerak emas
# (u image hajmini fayllar hajmicha oshirib yuborardi).
COPY --from=build --chown=zinnur:zinnur /app/publish ./

# ASP.NET Data Protection kalit halqasi uchun papka.
# Konteyner qayta yaratilganda kalitlar yo'qolsa, shifrlangan
# cookie/antiforgery tokenlar yaroqsiz bo'ladi. Compose bu yerga
# nomlangan volume ulaydi.
# BACKEND TOMONIDAN SOZLANISHI KERAK:
#     builder.Services.AddDataProtection()
#            .PersistKeysToFileSystem(new DirectoryInfo("/app/keys"));
RUN mkdir -p /app/keys && chown zinnur:zinnur /app/keys

# --- MUHIT ---
# SPEC talabi: barcha interfeyslarda 8080 portini tinglash.
# `+` = "hamma interfeys". `localhost` deb yozilsa konteyner tashqarisidan
# UMUMAN ulanib bo'lmaydi — klassik "konteyner javob bermayapti" xatosi.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# TieredPGO: ishlash paytida "qaysi kod issiq" ekanini o'lchab, uni qayta
# optimallashtiradi. 200 foydalanuvchi ostidagi uzoq ishlaydigan server
# uchun aynan foydali (o'tkazuvchanlik ~10% gacha oshadi).
ENV DOTNET_TieredPGO=1

# Konteynerda diagnostika soketini ochmaymiz — keraksiz hujum yuzasi.
ENV DOTNET_EnableDiagnostics=0

# Hujjatlashtiruvchi ko'rsatma: bu konteyner 8080 ni tinglaydi.
# (O'zi port ochmaydi — buni compose'dagi `ports:` qiladi.)
EXPOSE 8080

# --- SOG'LIQ TEKSHIRUVI ---
# SPEC 5-bo'lim: /health/ready — baza va Redis ulanishini ham tekshiradi
# (shunchaki "process tirikmi" emas).
# wget: alpine'dagi busybox tarkibida bor. curl YO'Q va uni o'rnatish
# shart emas — ortiqcha 3 MB va yana bitta CVE manbai.
# start-period=40s: EF Core migratsiyasi va JIT "isishi" uchun muhlat;
# bu davrdagi muvaffaqiyatsiz urinishlar hisobga olinmaydi.
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
  CMD wget -q -O /dev/null http://127.0.0.1:8080/health/ready || exit 1

# Root'dan tushamiz. BU QATORDAN KEYINGI HAMMA NARSA zinnur nomidan ketadi.
# 8080 > 1024 bo'lgani uchun root'siz ham portni tinglash mumkin.
USER zinnur

# UseAppHost=false bo'lgani uchun .dll ni `dotnet` orqali ishga tushiramiz.
# ENTRYPOINT (CMD emas): argumentlarni almashtirib yuborish qiyinlashadi,
# ya'ni konteyner "faqat shu ishni bajaradi".
ENTRYPOINT ["dotnet", "Zinnur.WebApi.dll"]


# =============================================================================
#  BOSQICH 4 — RUNTIME-MEDIA (FAQAT `compositor` konteyneri uchun)
# =============================================================================
#  Bu bosqich `runtime` NING USTIGA ffmpeg + ffprobe qo'shadi va BOSHQA
#  HECH NARSA qilmaydi. Kod, ENTRYPOINT, foydalanuvchi, HEALTHCHECK —
#  hammasi `runtime` dan meros.
#
#  NIMA UCHUN ALOHIDA IMAGE, api'ga ffmpeg qo'shib qo'ya qolmadikmi:
#    1) `api` INTERNETGA ochiq konteyner (host nginx orqali). ffmpeg —
#       katta, tez-tez CVE chiqadigan media kutubxonalar to'plami
#       (libav*, x264, ...). Uni hujum yuzasiga qo'shishning ma'nosi yo'q:
#       api hech qachon media kodlamaydi.
#    2) Image hajmi: ffmpeg + bog'liqliklari ~110 MB. `api` har deploy'da
#       tortiladi/qayta quriladi, `compositor` esa kamdan-kam.
#
#  🔴 BU BOSQICH `runtime` DAN KEYIN TURADI, YA'NI U ENDI OXIRGI BOSQICH.
#     Docker `--target` berilmasa OXIRGI bosqichni quradi. Shuning uchun
#     `docker-compose.yml` dagi `api` xizmatiga `target: runtime` YOZILDI
#     (AYNI KOMMITDA). Usiz `api` JIMGINA shu yerga ko'chib o'tardi:
#     hech narsa yiqilmaydi, testlar yashil qoladi, api shunchaki ichida
#     ffmpeg bilan ishlay boshlardi.
#
#     TEKSHIRUV (deploy'dan keyin MAJBURIY, docs/SPEC-RECORDING-V2.md §8):
#       docker run --rm --entrypoint sh zinnur/api:dev -c 'command -v ffmpeg'
#       -> BO'SH chiqishi va nol bo'lmagan kod qaytishi SHART.
# =============================================================================
FROM runtime AS runtime-media

# `apk` root talab qiladi; `runtime` bosqichi allaqachon `USER zinnur` ga
# o'tgan, shuning uchun vaqtincha root'ga qaytamiz va DARROV tushamiz.
USER root

# ffmpeg paketi ffprobe'ni HAM olib keladi (alpine'da ular bitta paketda).
# `FfmpegComposerSettings.Default` ikkalasini PATH dan nom bilan qidiradi
# ("ffmpeg" / "ffprobe"), shuning uchun qo'shimcha ENV kerak emas.
#
# ⚠️ Versiya QOTIRILMAGAN va bu ATAYLAB: alpine base image'i o'zi bilan
# birga yangilanadi, `apk` esa qotirilgan versiyani bir necha oydan keyin
# umuman topa olmay qoladi ("unable to select packages") va build JIMGINA
# yiqiladi. Filtr grafigi ffmpeg 6/7 da bir xil ishlaydi (o'lchandi:
# `RecordingCompositionPlannerTests` golden satrlari versiyaga bog'liq emas).
RUN apk add --no-cache ffmpeg

# Ish papkasining ildizi (`Composition__ScratchPath`). Compose bu yerga
# nomlangan volume ulaydi; volume `zinnur` egaligida bo'lishi uchun papka
# IMAGE ichida oldindan to'g'ri egalik bilan yaratiladi — aks holda Docker
# uni root nomidan yaratadi va root'siz jarayon unga YOZA OLMAYDI
# ("Permission denied", ffmpeg umuman ishga tushmaydi).
RUN mkdir -p /var/lib/zinnur/compose \
 && chown -R zinnur:zinnur /var/lib/zinnur

USER zinnur

# ENTRYPOINT/HEALTHCHECK/EXPOSE — `runtime` dan meros, qayta yozilmaydi.
