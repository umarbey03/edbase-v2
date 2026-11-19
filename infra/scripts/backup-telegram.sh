#!/usr/bin/env bash
# =============================================================================
#  ZIN-NUR — kunlik to'liq backup -> Telegram (shifrlangan)
#
#  Nima yuboradi: ikkala baza (pg_dump -Fc), rollar, .env + loyiha papkasi,
#  nginx va Let's Encrypt sozlamalari. Arxiv GPG AES-256 bilan shifrlanadi.
#
#  Sozlama fayli /root/.zinnur-backup.env (chmod 600):
#    BACKUP_BOT_TOKEN=...      BACKUP_CHAT_ID=-100...
#    BACKUP_PASSPHRASE=...     (yo'qolsa backup'ni OCHIB BO'LMAYDI)
#
#  Ochish (istalgan kompyuterda):
#    gpg --decrypt zinnur-XXXX.tar.gpg > zinnur-XXXX.tar   # parol so'raydi
#    (bo'laklangan bo'lsa avval: cat zinnur-XXXX.tar.gpg.part-* > zinnur-XXXX.tar.gpg)
#
#  Cron: 0 22 * * * (UTC) = 03:00 Toshkent
# =============================================================================
set -euo pipefail

CONF=/root/.zinnur-backup.env
PROJECT_DIR=/root/edbase-v2
WORK=/var/backups/zinnur-telegram
KEEP_LOCAL=7
PART_SIZE=45M          # Telegram bot API: bitta fayl <= 50 MB
LOCK=/var/lock/zinnur-telegram-backup.lock

# shellcheck disable=SC1090
. "$CONF"
: "${BACKUP_BOT_TOKEN:?}" "${BACKUP_CHAT_ID:?}" "${BACKUP_PASSPHRASE:?}"

API="https://api.telegram.org/bot${BACKUP_BOT_TOKEN}"
HOST=$(hostname)
TS=$(TZ=Asia/Tashkent date +%Y%m%d-%H%M)
NAME="zinnur-${TS}"

tg_msg() {
  curl -sS --max-time 30 -o /dev/null "${API}/sendMessage" \
    --data-urlencode "chat_id=${BACKUP_CHAT_ID}" --data-urlencode "text=$1" || true
}

fail() {
  tg_msg "❌ ZIN-NUR backup XATO (${HOST}, ${TS}): $1"
  echo "[$(date -u +%FT%TZ)] ERROR: $1" >&2
  exit 1
}
trap 'fail "skript ${LINENO}-qatorda to xtadi"' ERR

exec 9>"$LOCK"; flock -n 9 || fail "oldingi backup hali tugamagan"

mkdir -p "$WORK"; chmod 700 "$WORK"
STAGE=$(mktemp -d "$WORK/stage.XXXX")
trap 'rm -rf "$STAGE"' EXIT
mkdir "$STAGE/$NAME"; D="$STAGE/$NAME"

# 1) Bazalar
for db in zinnur zinnur_migr; do
  if docker exec zinnur-v2-postgres psql -U zinnur -d postgres -Atc \
       "select 1 from pg_database where datname='${db}'" | grep -q 1; then
    docker exec zinnur-v2-postgres pg_dump -U zinnur -Fc -d "$db" > "$D/$db.dump"
    [ "$(stat -c %s "$D/$db.dump")" -gt 10240 ] || fail "$db dump juda kichik"
    docker exec -i zinnur-v2-postgres pg_restore -l < "$D/$db.dump" > /dev/null \
      || fail "$db dump o qilmaydi"
  fi
done
docker exec zinnur-v2-postgres pg_dumpall -U zinnur --globals-only > "$D/globals.sql"

# 2) Loyiha (+ .env), nginx, sertifikatlar
tar --warning=no-file-changed -C / -czf "$D/project.tgz" "${PROJECT_DIR#/}" || [ $? -eq 1 ]
tar --warning=no-file-changed -C / -czf "$D/host-config.tgz" etc/nginx etc/letsencrypt || [ $? -eq 1 ]
(cd "$D" && sha256sum ./* > SHA256SUMS)

# 3) Shifrlash
OUT="$STAGE/${NAME}.tar.gpg"
tar -C "$STAGE" -cf - "$NAME" | gpg --batch --yes --quiet --pinentry-mode loopback \
  --passphrase-fd 3 --symmetric --cipher-algo AES256 -o "$OUT" 3<<<"$BACKUP_PASSPHRASE"
SIZE=$(du -h "$OUT" | cut -f1)

# 4) Telegram'ga yuborish (kerak bo'lsa bo'laklab)
cd "$STAGE"
if [ "$(stat -c %s "$OUT")" -gt $((45*1024*1024)) ]; then
  split -b "$PART_SIZE" -d "$(basename "$OUT")" "$(basename "$OUT").part-"
  FILES=$(ls "$(basename "$OUT")".part-*)
else
  FILES=$(basename "$OUT")
fi
DBROWS=$(docker exec zinnur-v2-postgres psql -U zinnur -d zinnur -Atc \
  'select count(*) from "Attendances"' 2>/dev/null || echo "?")
N=0; TOTAL=$(echo "$FILES" | wc -w)
for f in $FILES; do
  N=$((N+1))
  CAP="✅ ZIN-NUR backup ${TS} (${HOST}) — ${SIZE}, qism ${N}/${TOTAL}, davomat yozuvlari: ${DBROWS}"
  for attempt in 1 2 3; do
    if curl -sS --max-time 600 -F "chat_id=${BACKUP_CHAT_ID}" -F "caption=${CAP}" \
         -F "document=@${f}" "${API}/sendDocument" | grep -q '"ok":true'; then
      break
    fi
    [ "$attempt" -eq 3 ] && fail "Telegram'ga yuborilmadi (${f})"
    sleep 20
  done
done

# 5) Mahalliy nusxalar (oxirgi KEEP_LOCAL kun)
mv "$OUT" "$WORK/"
ls -1t "$WORK"/zinnur-*.tar.gpg 2>/dev/null | tail -n +$((KEEP_LOCAL+1)) | xargs -r rm -f
echo "[$(date -u +%FT%TZ)] OK ${NAME} ${SIZE}"
