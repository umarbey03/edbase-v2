#!/usr/bin/env bash
# =============================================================================
#  ZIN-NUR v2 — production deploy va yangilash (BITTA BUYRUQ)
#
#  Ishga tushirish (server'da, loyiha ildizidan):
#      ./infra/scripts/deploy.sh
#
#      SKIP_BACKUP=1 ./infra/scripts/deploy.sh    # zaxirasiz (TAVSIYA ETILMAYDI)
#      SKIP_PULL=1   ./infra/scripts/deploy.sh    # `git pull` siz (lokal o'zgarish bilan)
#
#  Nima qiladi:
#      1. git pull                 — yangi kodni oladi
#      2. baza zaxirasi            — migratsiyalar QO'LLANISHIDAN OLDIN
#      3. api va web ni QURADI     — ikkalasini BIRGA (sabab pastda)
#      4. up -d                    — ko'taradi, migratsiya avtomatik qo'llanadi
#      5. salomatlik tekshiruvi    — muvaffaqiyatsiz bo'lsa NOL BO'LMAGAN kod
#
#  ★ NEGA UMUMAN SKRIPT KERAK (qo'lda `docker compose up -d --build` YETARLI EMAS):
#
#    1) `api` va `web` DOIM BIRGA qurilishi shart. SignalR hub metodlarining
#       imzosi shartnoma: argument soni qat'iy tekshiriladi. Masalan
#       `SendMessage(sessionId, body)` -> `SendMessage(sessionId, body, clientId)`
#       o'zgarishida ESKI frontend YANGI api bilan ishlamaydi — jonli dars chati
#       jimgina buziladi. Faqat bittasini qurish shu sababli TAQIQLANADI.
#
#    2) Frontend muhit o'zgaruvchilari BUILD PAYTIDA JS ga "quyiladi"
#       (`VITE_API_URL`, `VITE_HUB_URL`). `.env` ni o'zgartirib konteynerni
#       qayta ishga tushirish YETARLI EMAS — `web` образini qayta qurish kerak.
#
#    3) Migratsiyalar ilova ishga tushganda avtomatik qo'llanadi
#       (`DbInitializer.MigrateAsync`). Ya'ni `up -d` — bu baza sxemasini
#       o'zgartiradigan amal. Zaxirasiz qilish xavfli.
#
#    4) `up -d` konteyner "ko'tarildi" deganда ilova hali TAYYOR emas.
#       Migratsiya yiqilsa yoki `PendingModelChangesWarning` chiqsa jarayon
#       o'ladi, lekin `up -d` allaqachon 0 qaytargan bo'ladi. Shuning uchun
#       salomatlik tekshiruvi MAJBURIY.
# =============================================================================

set -Eeuo pipefail

# Loyiha ildiziga o'tamiz (skript qayerdan chaqirilgan bo'lsa ham).
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.prod.yml)

# Salomatlik tekshiruvi uchun. Api porti 127.0.0.1 ga bog'langan — tashqaridan
# emas, SERVER ICHIDAN so'raymiz (nginx'siz, ya'ni TLS ishtirok etmaydi).
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:5080/health/ready}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-120}"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m  ✓ %s\033[0m\n' "$*"; }
fail() { printf '\033[1;31m  ✗ %s\033[0m\n' "$*" >&2; }

trap 'fail "Deploy TO'\''XTADI (satr $LINENO). Tizim eski holatida qolgan bo'\''lishi mumkin — `docker compose ps` bilan tekshiring."' ERR

# ---------------------------------------------------------------- 0. Tekshiruv
log "Muhit tekshirilmoqda"

[[ -f .env ]] || { fail ".env fayli yo'q. Avval: cp .env.example .env va qiymatlarni to'ldiring."; exit 1; }

# ⚠️ Eng ko'p uchraydigan deploy xatosi: dev qiymatlari bilan prod'ga chiqish.
if grep -qE '^(JWT_SECRET|POSTGRES_PASSWORD|LIVEKIT_API_SECRET)=.*(change_me|dev_only)' .env; then
    fail ".env da HALI DEV SIRLARI turibdi (change_me / dev_only)."
    fail "Prod'da bu — ochiq eshik. Yarating: openssl rand -base64 48"
    exit 1
fi

if grep -qE '^ASPNETCORE_ENVIRONMENT=Development' .env; then
    fail ".env da ASPNETCORE_ENVIRONMENT=Development. Prod uchun: Production"
    exit 1
fi

ok "sirlar almashtirilgan, muhit Production"

# ---------------------------------------------------- 0b. LiveKit prod yaml
# ═══════════════════════════════════════════════════════════════════════════
# PROD LiveKit konfiguratsiyasi SHABLONDAN yasaladi.
#
# NEGA: `webhook.api_key` `.env` dagi `LIVEKIT_API_KEY` bilan AYNAN bir xil
# bo'lishi SHART (aks holda LiveKit hodisani imzolay olmaydi va uni JIMGINA
# tashlab yuboradi), LiveKit esa yaml ichida `${...}` ni KENGAYTIRMAYDI.
# Qiymatni shablonga qo'lda yozib qo'yish ikki narsani buzardi: sir git'ga
# tushardi va serverdagi qo'lda tahrir `git pull --ff-only` ni to'xtatardi.
#
# ⚠️ BU QADAM `up -d` DAN OLDIN BO'LISHI SHART. Fayl bo'lmasa Docker uning
#    o'rniga BO'SH PAPKA yaratadi va LiveKit "is a directory" bilan yiqiladi
#    — ya'ni JONLI DARSLAR to'xtaydi.
# ═══════════════════════════════════════════════════════════════════════════
log "LiveKit prod konfiguratsiyasi yasalmoqda"

LK_TEMPLATE="infra/livekit/livekit.prod.yaml"
LK_GENERATED="infra/livekit/livekit.prod.generated.yaml"

[[ -f "$LK_TEMPLATE" ]] || { fail "$LK_TEMPLATE topilmadi."; exit 1; }

# `.env` ni O'QIYMIZ, lekin faqat shu qadam uchun (subshell EMAS — qiymat
# kerak). `set -a` bilan hamma o'zgaruvchi eksport bo'ladi; keyin darhol
# o'chiramiz, aks holda `.env` dagi hamma narsa skript muhitiga oqib ketardi.
set -a
# shellcheck disable=SC1091
. ./.env
set +a

if [[ -z "${LIVEKIT_API_KEY:-}" ]]; then
    fail ".env da LIVEKIT_API_KEY bo'sh. Usiz webhook imzolanmaydi va"
    fail "dars yozuvi JIMGINA ishlamaydi (hech qayerda xato chiqmaydi)."
    exit 1
fi

sed "s|__LIVEKIT_API_KEY__|${LIVEKIT_API_KEY}|" "$LK_TEMPLATE" > "$LK_GENERATED"

# Shablon almashtirilmay qolgan bo'lsa (masalan kimdir belgini o'zgartirgan)
# LiveKit ko'tariladi-yu, webhook JIMGINA ishlamasdi. Balandlashtiramiz.
if grep -q '__LIVEKIT_API_KEY__' "$LK_GENERATED"; then
    fail "$LK_GENERATED da hali ham __LIVEKIT_API_KEY__ turibdi."
    exit 1
fi
ok "$LK_GENERATED tayyor (webhook api_key = .env dagi LIVEKIT_API_KEY)"

# ---------------------------------------------------------------- 1. Kod
if [[ "${SKIP_PULL:-0}" != "1" ]]; then
    log "Yangi kod olinmoqda (git pull)"
    BEFORE="$(git rev-parse --short HEAD)"
    git pull --ff-only
    AFTER="$(git rev-parse --short HEAD)"
    if [[ "$BEFORE" == "$AFTER" ]]; then
        ok "o'zgarish yo'q ($AFTER)"
    else
        ok "$BEFORE -> $AFTER"
        git --no-pager log --oneline "$BEFORE..$AFTER" | sed 's/^/     /'
    fi
else
    ok "git pull o'tkazib yuborildi (SKIP_PULL=1)"
fi

# ---------------------------------------------------------------- 2. Zaxira
# Migratsiyalar `up -d` da avtomatik qo'llanadi va ularni ORQAGA QAYTARISH
# odatda mumkin emas. Shuning uchun zaxira SHU YERDA, qurishdan oldin.
if [[ "${SKIP_BACKUP:-0}" != "1" ]]; then
    if "${COMPOSE[@]}" ps --status running --services 2>/dev/null | grep -qx postgres; then
        log "Baza zaxirasi olinmoqda (migratsiyalardan OLDIN)"
        # ⚠️ `PROJECT_DIR` MAJBURIY: `backup-db.sh` standart qiymat sifatida
        # `/opt/zinnur` ni oladi (cron uchun qulay), lekin loyiha boshqa
        # papkada bo'lsa compose faylini topa olmay `exit 2` bilan yiqiladi
        # va butun deploy to'xtaydi. Haqiqiy yo'lni SHU YERDA beramiz.
        if PROJECT_DIR="$ROOT" ./infra/scripts/backup-db.sh; then
            ok "zaxira tayyor"
        else
            # Zaxira yiqilishi deploy'ni TO'XTATMASLIGI kerak edi — lekin
            # migratsiyalar orqaga qaytmaydi, shuning uchun to'xtatamiz va
            # sababni ko'rsatamiz. Ataylab davom etish uchun: SKIP_BACKUP=1
            fail "Zaxira olinmadi. Sabab yuqorida (log: /var/log/zinnur-backup.log)."
            fail "Ataylab zaxirasiz davom etish:  SKIP_BACKUP=1 ./infra/scripts/deploy.sh"
            exit 1
        fi
    else
        ok "postgres ishlamayapti — birinchi deploy, zaxira kerak emas"
    fi
else
    fail "ZAXIRA O'TKAZIB YUBORILDI (SKIP_BACKUP=1) — migratsiya buzilsa qaytarib bo'lmaydi"
fi

# ---------------------------------------------------------------- 3. Qurish
# 🔴 `api` va `web` BIRGA. Sabab yuqoridagi izohning 1-bandida.
#
# 🔴 `compositor` HAM SHU YERDA (2026-09-05 da qo'shildi). U `api` bilan
#    AYNI manbadan quriladi, faqat boshqa Dockerfile bosqichidan
#    (`runtime-media` — ffmpeg bilan). Ro'yxatga qo'shilmasa nosozlik
#    JIMGINA bo'lardi: `up -d` mavjud image'ni QAYTA QURMAYDI, ya'ni
#    kodlovchi ESKI kod bilan ishlayverardi va buni faqat kechasi,
#    yig'ish natijasidan bilib olish mumkin bo'lardi.
log "Образlar qurilmoqda (api, web, compositor)"
"${COMPOSE[@]}" build api web compositor
ok "образlar tayyor"

# 🔴 `api` IMAGE'IDA ffmpeg BO'LMASLIGI KERAK (SPEC-RECORDING-V2 §8).
#
# `backend/Dockerfile` da `runtime` dan KEYIN `runtime-media` bosqichi bor.
# `api` ning `build.target: runtime` qatori tushib qolsa Docker OXIRGI
# bosqichni quradi va internetga ochiq konteyner ichiga ~110 MB media
# kutubxonasi tushardi. HECH NARSA YIQILMAYDI — shuning uchun tekshiruv
# aynan shu yerda, avtomatik.
if docker run --rm --entrypoint sh zinnur/api:dev -c 'command -v ffmpeg' >/dev/null 2>&1; then
    fail "api image'ida ffmpeg BOR — `docker-compose.yml` dagi"
    fail "`api.build.target: runtime` tushib qolgan. Deploy TO'XTATILDI."
    exit 1
fi
ok "api image'ida ffmpeg yo'q (target: runtime saqlangan)"

# ---------------------------------------------------------------- 4. Ko'tarish
log "Xizmatlar ko'tarilmoqda (migratsiyalar avtomatik qo'llanadi)"
"${COMPOSE[@]}" up -d --remove-orphans
ok "konteynerlar ishga tushdi"

# ---------------------------------------------------------------- 5. Salomatlik
# `up -d` 0 qaytargani ilova TAYYOR degani EMAS — migratsiya yiqilsa jarayon
# keyinroq o'ladi. Shuning uchun haqiqiy javobni kutamiz.
log "Salomatlik kutilmoqda (eng ko'pi ${HEALTH_TIMEOUT}s)"

DEADLINE=$(( SECONDS + HEALTH_TIMEOUT ))
while (( SECONDS < DEADLINE )); do
    if curl -fsS --max-time 5 "$HEALTH_URL" >/dev/null 2>&1; then
        BODY="$(curl -fsS --max-time 5 "$HEALTH_URL")"
        ok "sog'lom: $BODY"

        log "Yakuniy holat"
        "${COMPOSE[@]}" ps
        printf '\n\033[1;32m✓ DEPLOY MUVAFFAQIYATLI\033[0m\n\n'
        exit 0
    fi
    sleep 3
done

# ---------------------------------------------------------------- Muvaffaqiyatsiz
fail "Ilova ${HEALTH_TIMEOUT}s ichida sog'lom bo'lmadi."
fail "Eng ko'p uchraydigan sabab — migratsiya yoki konfiguratsiya xatosi."
printf '\n--- api loglari (oxirgi 60 qator) ---\n'
"${COMPOSE[@]}" logs --tail 60 api || true
printf '\n--- konteynerlar ---\n'
"${COMPOSE[@]}" ps || true
printf '\nOrqaga qaytarish:\n'
printf '  git reset --hard <oldingi-commit> && ./infra/scripts/deploy.sh\n'
printf '  Baza zaxirasidan tiklash: docs/DEPLOY_UBUNTU.md — 7.7\n\n'
exit 1
