#!/usr/bin/env bash
# =============================================================================
#  ZIN-NUR v2 — PostgreSQL nightly backup
#
#  Nima qiladi:
#    1. `docker compose exec -T postgres pg_dump` orqali to'liq logical dump
#    2. gzip bilan siqadi, vaqt tamg'ali nom beradi
#    3. eski nusxalarni RETENTION_DAYS dan keyin o'chiradi
#    4. natijani log faylga yozadi, xatoda NOL BO'LMAGAN kod bilan chiqadi
#
#  Nega konteyner ICHIDA pg_dump?
#    Host'dagi pg_dump versiyasi server versiyasidan (PostgreSQL 17) eski
#    bo'lsa, dump "server version mismatch" bilan yiqiladi. Konteyner ichidagi
#    pg_dump har doim server bilan bir xil versiya — ya'ni hech qachon
#    yangilanishdan keyin buzilmaydi.
#
#  Ishga tushirish:
#    ./backup-db.sh                       # default sozlamalar bilan
#    RETENTION_DAYS=30 ./backup-db.sh     # saqlash muddatini o'zgartirib
#
#  Cron (har kuni 03:15, qo'llanmaning 7.7-bo'limiga qarang):
#    15 3 * * * /opt/zinnur/infra/scripts/backup-db.sh >> /var/log/zinnur-backup.log 2>&1
# =============================================================================

set -euo pipefail

# --------------------------- SOZLAMALAR (env orqali o'zgartiriladi) ----------
# Compose loyihasi joylashgan katalog (docker-compose.yml shu yerda).
PROJECT_DIR="${PROJECT_DIR:-/opt/zinnur}"
COMPOSE_FILE="${COMPOSE_FILE:-${PROJECT_DIR}/docker-compose.yml}"

# SPEC 8-bo'lim: xizmat nomi O'ZGARMAYDI.
PG_SERVICE="${PG_SERVICE:-postgres}"

# SPEC 8-bo'lim: ConnectionStrings__Postgres -> Database=zinnur;Username=zinnur
DB_NAME="${DB_NAME:-zinnur}"
DB_USER="${DB_USER:-zinnur}"

BACKUP_DIR="${BACKUP_DIR:-/var/backups/zinnur}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"
LOG_FILE="${LOG_FILE:-/var/log/zinnur-backup.log}"

# Dump shubhali darajada kichik bo'lsa xato deb hisoblaymiz (bayt).
# Bo'sh/buzilgan dump ham gzip'lanadi va 20-100 bayt bo'ladi — shu bilan
# "muvaffaqiyatli" backup illyuziyasi paydo bo'ladi. Shundan himoya.
MIN_SIZE_BYTES="${MIN_SIZE_BYTES:-1024}"

# Bir vaqtda ikkita backup ishlamasligi uchun lock fayl.
LOCK_FILE="${LOCK_FILE:-/var/lock/zinnur-backup.lock}"

DOCKER_BIN="${DOCKER_BIN:-$(command -v docker || echo /usr/bin/docker)}"

# --------------------------- LOG FUNKSIYALARI --------------------------------
_ts() { date '+%Y-%m-%d %H:%M:%S%z'; }

log()  { printf '[%s] [INFO]  %s\n' "$(_ts)" "$*" | tee -a "$LOG_FILE" >&2; }
warn() { printf '[%s] [WARN]  %s\n' "$(_ts)" "$*" | tee -a "$LOG_FILE" >&2; }
err()  { printf '[%s] [ERROR] %s\n' "$(_ts)" "$*" | tee -a "$LOG_FILE" >&2; }

TMP_FILE=""
cleanup() {
    local code=$?
    if [[ -n "$TMP_FILE" && -f "$TMP_FILE" ]]; then
        rm -f "$TMP_FILE"
        warn "Chala backup fayli o'chirildi: ${TMP_FILE}"
    fi
    if (( code != 0 )); then
        err "BACKUP MUVAFFAQIYATSIZ (exit=${code})"
    fi
    exit "$code"
}
trap cleanup EXIT

# --------------------------- TEKSHIRUVLAR ------------------------------------
# Log faylni yozib bo'ladimi? (cron root'dan ishlaydi, lekin qo'lda ham
# ishga tushirilishi mumkin)
if ! touch "$LOG_FILE" 2>/dev/null; then
    LOG_FILE="/tmp/zinnur-backup.log"
    touch "$LOG_FILE"
    printf 'Log fayl yozilmadi, /tmp/zinnur-backup.log ga o'\''tildi\n' >&2
fi

log "=== ZIN-NUR DB backup boshlandi ==="

if [[ ! -x "$DOCKER_BIN" ]]; then
    err "docker topilmadi (DOCKER_BIN=${DOCKER_BIN}). Cron'da PATH cheklangan — DOCKER_BIN ni to'liq yo'l bilan bering."
    exit 2
fi

if [[ ! -f "$COMPOSE_FILE" ]]; then
    err "compose fayl topilmadi: ${COMPOSE_FILE}"
    exit 2
fi

mkdir -p "$BACKUP_DIR"
chmod 750 "$BACKUP_DIR"

# --------------------------- LOCK --------------------------------------------
# flock: oldingi backup hali tugamagan bo'lsa (masalan baza juda katta),
# ikkinchisini ishga tushirmaymiz.
exec 9>"$LOCK_FILE"
if ! flock -n 9; then
    err "Boshqa backup jarayoni ishlayapti (${LOCK_FILE}). Chiqildi."
    exit 3
fi

# --------------------------- POSTGRES TAYYORMI? ------------------------------
compose() { "$DOCKER_BIN" compose -f "$COMPOSE_FILE" "$@"; }

if ! compose ps --status running --services 2>/dev/null | grep -qx "$PG_SERVICE"; then
    err "'${PG_SERVICE}' xizmati ishlamayapti. Backup bekor qilindi."
    exit 4
fi

if ! compose exec -T "$PG_SERVICE" pg_isready -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; then
    err "pg_isready muvaffaqiyatsiz — baza ulanishga tayyor emas."
    exit 4
fi

# --------------------------- DUMP --------------------------------------------
STAMP="$(date '+%Y%m%d-%H%M%S')"
OUT_FILE="${BACKUP_DIR}/zinnur-${DB_NAME}-${STAMP}.sql.gz"
TMP_FILE="${OUT_FILE}.partial"

log "Dump: service=${PG_SERVICE} db=${DB_NAME} -> ${OUT_FILE}"

# --no-owner / --no-privileges: dump'ni boshqa serverga/boshqa role bilan
# tiklash osonlashadi.
# --clean --if-exists: tiklashda eski obyektlarni tushiradi (restore paytida
# "already exists" xatolari bo'lmasin).
# -Z bo'lmagan plain SQL + tashqi gzip: oqim (stream) bo'ylab siqiladi,
# konteyner ichida vaqtinchalik fayl yaratilmaydi.
#
# pipefail yoqilgani uchun pg_dump yiqilsa butun quvur (pipeline) yiqiladi.
set -o pipefail
if ! compose exec -T "$PG_SERVICE" \
        pg_dump -U "$DB_USER" -d "$DB_NAME" \
                --format=plain --clean --if-exists \
                --no-owner --no-privileges \
        | gzip -9 > "$TMP_FILE"; then
    err "pg_dump yoki gzip xato bilan tugadi."
    exit 5
fi

# Siqilgan fayl butunligini tekshiramiz.
if ! gzip -t "$TMP_FILE" 2>/dev/null; then
    err "gzip butunlik testi (gzip -t) muvaffaqiyatsiz — fayl buzuq."
    exit 6
fi

SIZE_BYTES="$(stat -c %s "$TMP_FILE" 2>/dev/null || stat -f %z "$TMP_FILE")"
if (( SIZE_BYTES < MIN_SIZE_BYTES )); then
    err "Backup juda kichik: ${SIZE_BYTES} bayt (< ${MIN_SIZE_BYTES}). Bo'sh dump deb hisoblandi."
    exit 7
fi

mv "$TMP_FILE" "$OUT_FILE"
TMP_FILE=""
chmod 640 "$OUT_FILE"

SIZE_HUMAN="$(du -h "$OUT_FILE" | awk '{print $1}')"
log "Backup tayyor: ${OUT_FILE} (${SIZE_HUMAN}, ${SIZE_BYTES} bayt)"

# --------------------------- RETENTION ---------------------------------------
# DIQQAT: bu qadam FAYL O'CHIRADI. Faqat BACKUP_DIR ichidagi
# `zinnur-*.sql.gz` shablonga mos, RETENTION_DAYS kundan eski fayllar.
DELETED=0
while IFS= read -r -d '' old; do
    rm -f -- "$old"
    log "O'chirildi (eski): ${old}"
    DELETED=$(( DELETED + 1 ))
done < <(find "$BACKUP_DIR" -maxdepth 1 -type f \
              -name "zinnur-*.sql.gz" \
              -mtime "+${RETENTION_DAYS}" -print0)

REMAINING="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name "zinnur-*.sql.gz" | wc -l | tr -d ' ')"
TOTAL_SIZE="$(du -sh "$BACKUP_DIR" 2>/dev/null | awk '{print $1}')"

log "Retention: ${RETENTION_DAYS} kun | o'chirildi=${DELETED} | qoldi=${REMAINING} | jami=${TOTAL_SIZE}"

# Disk bo'sh joyini ogohlantirish sifatida chiqaramiz.
AVAIL_PCT="$(df --output=pcent "$BACKUP_DIR" 2>/dev/null | tail -1 | tr -dc '0-9' || echo 0)"
if [[ -n "$AVAIL_PCT" ]] && (( AVAIL_PCT > 85 )); then
    warn "Disk ${AVAIL_PCT}% band — backup diskini tozalash yoki RETENTION_DAYS ni kamaytirish kerak."
fi

log "=== ZIN-NUR DB backup muvaffaqiyatli tugadi ==="
exit 0
