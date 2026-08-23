#!/usr/bin/env bash
# =============================================================================
# ZIN-NUR v2 — RESURS CHEGARALARI DARVOZASI (CI)
#
# NIMA UCHUN BU SKRIPT BOR (2026-08-22 auditi)
# -----------------------------------------------------------------------------
# Audit shuni topdi: `docker-compose.prod.yml` dagi standart resurs qiymatlari
# loyihaning O'Z deploy hujjatiga (`docs/DEPLOY_UBUNTU.md`, 6.1/6.2) ZID edi —
# livekit 1.5 CPU / 768M (hujjat: 6.0 / 2g), api 1.5 / 1G (hujjat: 2.0 / 3g),
# postgres konteyneri 1G, ichkarisida esa shared_buffers=2GB.
#
# ENG MUHIMI — NEGA BUNI HECH KIM SEZMADI:
#   * bu o'zgaruvchilarning birortasi ham `.env.example` da yo'q edi, ya'ni
#     operator ularni o'zgartirish MUMKINLIGINI ham bilmasdi;
#   * nomuvofiqlik FAQAT YUK OSTIDA chiqadi. Postgres ishga tushishda
#     yiqilmaydi (shared_buffers sekin, sahifa tegilganda ajratiladi), ya'ni
#     `up -d`, healthcheck va smoke test — hammasi YASHIL. Portlash haqiqiy
#     dars paytida bo'ladi.
# Ya'ni bu sinf nosozlikni na compose validatsiyasi, na testlar, na smoke test
# tuta olmaydi. Yagona ishonchli tutuvchi — MATNLARNI BIR-BIRIGA SOLISHTIRISH.
#
# BU SKRIPT NIMA QILADI
# -----------------------------------------------------------------------------
# Uchta manbani solishtiradi va farq bo'lsa CI ni QIZIL qiladi:
#   1) docs/DEPLOY_UBUNTU.md 6.1/6.2 jadvallari  — HAQIQAT MANBASI
#   2) docker-compose.prod.yml dagi `${VAR:-standart}` standart qiymatlari
#   3) .env.example dagi qiymatlar
# Ustiga mantiqiy invariantlarni tekshiradi (rezerv <= limit, redis maxmemory
# < konteyner limiti, shared_buffers konteyner limitiga nisbatan, va h.k.).
#
# NEGA "HUJJAT — HAQIQAT MANBASI": qaror shu hujjatda muhokama qilinib qabul
# qilingan. Kod hujjatdan chetga chiqsa — bu xato, teskarisi emas. Qiymatni
# ATAYLAB o'zgartirmoqchi bo'lsangiz, avval 6.1/6.2 jadvalini yangilang.
#
# ISHLATISH:
#   ./infra/scripts/check-resource-limits.sh          # tekshirish
#   ./infra/scripts/check-resource-limits.sh -v       # har bir qiymatni ko'rsatib
# Chiqish kodi: 0 — hammasi mos, 1 — nomuvofiqlik bor.
# =============================================================================

set -uo pipefail

VERBOSE=0
[ "${1:-}" = "-v" ] && VERBOSE=1

# Repo ildizi — skript qayerdan chaqirilishidan qat'i nazar to'g'ri ishlasin.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

DOC="$ROOT/docs/DEPLOY_UBUNTU.md"
PROD="$ROOT/docker-compose.prod.yml"
ENVEX="$ROOT/.env.example"

FAILURES=0

red()   { printf '\033[31m%s\033[0m\n' "$*"; }
green() { printf '\033[32m%s\033[0m\n' "$*"; }
dim()   { [ "$VERBOSE" = "1" ] && printf '    %s\n' "$*"; return 0; }

fail() {
  FAILURES=$((FAILURES + 1))
  red "  ✗ $*"
}

for f in "$DOC" "$PROD" "$ENVEX"; do
  if [ ! -f "$f" ]; then
    red "FATAL: fayl topilmadi: $f"
    exit 1
  fi
done

# -----------------------------------------------------------------------------
# Yordamchi: xotira qiymatini BAYTGA o'giradi.
# NEGA KERAK: uchta manba uchta xil imloda yozadi — hujjatda `2g`, compose'da
# `2G`, redis'da `768mb`. Matn sifatida solishtirish soxta xato berardi.
# -----------------------------------------------------------------------------
to_bytes() {
  local v num unit
  v="$(printf '%s' "$1" | tr -d '[:space:]' | tr 'A-Z' 'a-z')"
  v="${v%b}"                                    # 768mb -> 768m
  # ⚠️ 2026-08-23: son qismi KASRLI ham bo'lishi mumkin (`1.5g`). Ilgari
  #    bu yerda `sed 's/[^0-9].*$//'` turardi, ya'ni `1.5g` -> `1` va
  #    ko'paytirish bash butun-son arifmetikasida bajarilardi. Kasrli
  #    qiymat "format tushunarsiz" degan toza xato o'rniga JIMGINA
  #    noto'g'ri natija berardi (1.5G -> 1 GiB deb hisoblanardi).
  #    Endi son to'liq olinadi va ko'paytirish awk'da (kasrni biladi).
  num="$(printf '%s' "$v" | sed -E 's/^([0-9]+(\.[0-9]+)?).*$/\1/')"
  unit="$(printf '%s' "$v" | sed -E 's/^[0-9]+(\.[0-9]+)?//')"
  case "$num" in
    ''|*[!0-9.]*) printf 'NaN'; return ;;
  esac
  case "$unit" in
    g)  awk -v n="$num" 'BEGIN{ printf "%d", n * 1024 * 1024 * 1024 }' ;;
    m)  awk -v n="$num" 'BEGIN{ printf "%d", n * 1024 * 1024 }' ;;
    k)  awk -v n="$num" 'BEGIN{ printf "%d", n * 1024 }' ;;
    "") awk -v n="$num" 'BEGIN{ printf "%d", n }' ;;   # birliksiz son (masalan 100)
    *)  printf 'NaN' ;;
  esac
}

# CPU qiymatlari kasrli (0.5, 2.0, 6.0) — bash butun sonlar bilan ishlaydi,
# shuning uchun solishtirish awk orqali.
num_eq() { awk -v a="$1" -v b="$2" 'BEGIN{exit !(a+0==b+0)}'; }

# -----------------------------------------------------------------------------
# MANBA 1: docs/DEPLOY_UBUNTU.md 6.1-jadvali
#   | `livekit` | **6.0** (yumshoq) | `2g` | `512m` | CPU headroom eng muhim |
# Ustunlar: 1=xizmat 2=cpus 3=mem_limit 4=mem_reservation 5=asosiy sozlama
# -----------------------------------------------------------------------------
doc61_cell() {
  local svc="$1" col="$2"
  awk -F'|' -v svc="$svc" -v col="$col" '
    $0 ~ ("^\\| `" svc "`") {
      cell = $(col + 1)
      gsub(/[`*]/, "", cell)              # backtick va **qalin** belgilarni olib tashlash
      gsub(/\([^)]*\)/, "", cell)         # "(yumshoq)", "(nginx)" izohlarini olib tashlash
      gsub(/^[ \t]+|[ \t]+$/, "", cell)
      print cell
      exit
    }' "$DOC"
}

# -----------------------------------------------------------------------------
# MANBA 1b: 6.2-jadval (postgres ichki sozlamalari)
#   | `shared_buffers` | `768MB` | ... |
# -----------------------------------------------------------------------------
doc62_value() {
  local param="$1"
  awk -F'|' -v p="$param" '
    $0 ~ ("^\\| `" p "`") {
      cell = $3
      gsub(/[`*]/, "", cell)
      gsub(/^[ \t]+|[ \t]+$/, "", cell)
      print cell
      exit
    }' "$DOC"
}

# -----------------------------------------------------------------------------
# MANBA 2: docker-compose.prod.yml dagi `${VAR:-standart}` standarti
# -----------------------------------------------------------------------------
compose_default() {
  local var="$1"
  grep -o "\${${var}:-[^}]*}" "$PROD" | head -1 | sed -E "s/^\\\$\{${var}:-//; s/}$//"
}

# -----------------------------------------------------------------------------
# MANBA 3: .env.example dagi qiymat
# -----------------------------------------------------------------------------
env_value() {
  local var="$1"
  grep -E "^${var}=" "$ENVEX" | head -1 | cut -d= -f2- | tr -d '[:space:]'
}

# -----------------------------------------------------------------------------
# Uchta manbani solishtiruvchi asosiy tekshiruv
#   check3 <sarlavha> <hujjat qiymati> <compose standarti> <env qiymati> <tur>
#   tur: mem | cpu | raw
# -----------------------------------------------------------------------------
check3() {
  local label="$1" d="$2" c="$3" e="$4" kind="$5"

  if [ -z "$d" ]; then fail "$label — hujjatda (6.1/6.2) qiymat topilmadi"; return; fi
  if [ -z "$c" ]; then fail "$label — docker-compose.prod.yml da \${...:-standart} topilmadi"; return; fi
  if [ -z "$e" ]; then fail "$label — .env.example da o'zgaruvchi yo'q (audit aynan shu sababdan sezilmagan edi)"; return; fi

  local dn cn en
  case "$kind" in
    mem)
      dn="$(to_bytes "$d")"; cn="$(to_bytes "$c")"; en="$(to_bytes "$e")"
      if [ "$dn" = "NaN" ] || [ "$cn" = "NaN" ] || [ "$en" = "NaN" ]; then
        fail "$label — xotira formati tushunarsiz (hujjat=$d compose=$c env=$e). To'g'ri imlo: 2G / 512M / 768mb"
        return
      fi
      ;;
    cpu) dn="$d"; cn="$c"; en="$e" ;;
    *)   dn="$d"; cn="$c"; en="$e" ;;
  esac

  local ok=1
  if [ "$kind" = "cpu" ]; then
    num_eq "$dn" "$cn" || ok=0
    num_eq "$dn" "$en" || ok=0
  else
    [ "$dn" = "$cn" ] || ok=0
    [ "$dn" = "$en" ] || ok=0
  fi

  if [ "$ok" = "1" ]; then
    dim "✓ $label: hujjat=$d compose=$c env=$e"
  else
    fail "$label — MOS EMAS: DEPLOY_UBUNTU.md='$d'  docker-compose.prod.yml='$c'  .env.example='$e'"
  fi
}

echo "=== ZIN-NUR resurs chegaralari darvozasi ==="
echo "    haqiqat manbasi: docs/DEPLOY_UBUNTU.md 6.1 / 6.2"
echo

# =============================================================================
# 1-QISM: 6.1-jadval — konteyner chegaralari (cpus / mem / mem_reservation)
# =============================================================================
echo "[1/5] 6.1-jadval: konteyner chegaralari"

# xizmat:CPUS_VAR:MEM_VAR:RESERVATION_VAR
SERVICES="
livekit:LIVEKIT_CPUS:LIVEKIT_MEM:LIVEKIT_MEM_RESERVATION
api:API_CPUS:API_MEM:API_MEM_RESERVATION
postgres:POSTGRES_CPUS:POSTGRES_MEM:POSTGRES_MEM_RESERVATION
redis:REDIS_CPUS:REDIS_MEM:REDIS_MEM_RESERVATION
web:WEB_CPUS:WEB_MEM:WEB_MEM_RESERVATION
"

for row in $SERVICES; do
  svc="${row%%:*}"; rest="${row#*:}"
  cpu_var="${rest%%:*}"; rest="${rest#*:}"
  mem_var="${rest%%:*}"
  res_var="${rest##*:}"

  check3 "$svc cpus"            "$(doc61_cell "$svc" 2)" "$(compose_default "$cpu_var")" "$(env_value "$cpu_var")" cpu
  check3 "$svc memory"          "$(doc61_cell "$svc" 3)" "$(compose_default "$mem_var")" "$(env_value "$mem_var")" mem
  check3 "$svc mem_reservation" "$(doc61_cell "$svc" 4)" "$(compose_default "$res_var")" "$(env_value "$res_var")" mem
done

# =============================================================================
# 2-QISM: 6.2-jadval — postgres ICHKI sozlamalari
#
# NEGA ALOHIDA QISM: audit topgan eng jiddiy nosozlik aynan shu yerda edi —
# konteyner limiti 1G, ichkarida esa shared_buffers=2GB. Ya'ni konteyner
# chegarasi to'g'ri bo'lishining O'ZI YETARLI EMAS; ichki sozlama ham unga
# mos kelishi shart.
#
# ATAYLAB TEKSHIRILMAYDI (hozircha):
#   * log_min_duration_statement — hujjat 500ms, compose 1000. Xotira/CPU ga
#     aloqasi yo'q, shuning uchun audit uni o'zgartirmadi.
#   * random_page_cost / effective_io_concurrency / checkpoint_completion_target
#     — hujjat ularni `infra/postgres/postgresql.conf` ga havola qiladi, u fayl
#     esa hali mavjud emas. Alohida vazifa sifatida ko'tarilgan.
# =============================================================================
echo
echo "[2/5] 6.2-jadval: postgres ichki sozlamalari"

# parametr:ENV_VAR
PG_PARAMS="
shared_buffers:POSTGRES_SHARED_BUFFERS
effective_cache_size:POSTGRES_EFFECTIVE_CACHE_SIZE
work_mem:POSTGRES_WORK_MEM
maintenance_work_mem:POSTGRES_MAINTENANCE_WORK_MEM
max_wal_size:POSTGRES_MAX_WAL_SIZE
"

for row in $PG_PARAMS; do
  param="${row%%:*}"; var="${row##*:}"
  check3 "postgres $param" "$(doc62_value "$param")" "$(compose_default "$var")" "$(env_value "$var")" mem
done

# max_connections — son, xotira emas
check3 "postgres max_connections" \
  "$(doc62_value max_connections)" \
  "$(compose_default POSTGRES_MAX_CONNECTIONS)" \
  "$(env_value POSTGRES_MAX_CONNECTIONS)" raw

# `command:` dagi qiymat HAQIQATAN o'zgaruvchidan olinayotganini tekshiramiz.
# NEGA: qattiq yozilgan qiymat (masalan `shared_buffers=2GB`) .env orqali
# BOSHQARILMAYDI — operator POSTGRES_MEM ni tushirsa ham u joyida qolardi va
# aynan shu audit topgan OOM qaytardi.
for row in $PG_PARAMS; do
  param="${row%%:*}"; var="${row##*:}"
  if ! grep -q -- "- ${param}=\${${var}:-" "$PROD"; then
    fail "postgres $param — docker-compose.prod.yml da QATTIQ yozilgan (\${${var}:-...} emas), ya'ni .env orqali boshqarilmaydi"
  fi
done

# =============================================================================
# 3-QISM: 6.1 "Asosiy sozlama" ustuni — redis maxmemory
# =============================================================================
echo
echo "[3/5] redis maxmemory (6.1 'Asosiy sozlama' ustuni)"

# Ustun matni: `maxmemory 768mb` -> faqat qiymatni ajratamiz
DOC_REDIS_MAXMEM="$(doc61_cell redis 5 | sed -E 's/^maxmemory[[:space:]]*//')"
check3 "redis maxmemory" "$DOC_REDIS_MAXMEM" "$(compose_default REDIS_MAXMEMORY)" "$(env_value REDIS_MAXMEMORY)" mem

# 6.1 postgres qatoridagi `shared_buffers=768MB` ham shu yerda tekshiriladi —
# 6.1 va 6.2 bir-biriga zid bo'lib qolmasin (hujjat ichidagi nomuvofiqlik).
DOC61_SB="$(doc61_cell postgres 5 | sed -E 's/^shared_buffers=//')"
if [ -n "$DOC61_SB" ]; then
  if [ "$(to_bytes "$DOC61_SB")" != "$(to_bytes "$(doc62_value shared_buffers)")" ]; then
    fail "HUJJAT ICHIDA ZIDDIYAT: 6.1 shared_buffers='$DOC61_SB', 6.2 shared_buffers='$(doc62_value shared_buffers)'"
  else
    dim "✓ 6.1 va 6.2 shared_buffers bo'yicha mos: $DOC61_SB"
  fi
fi

# =============================================================================
# 4-QISM: MANTIQIY INVARIANTLAR
#
# Bular jadvallardan KELIB CHIQMAYDI — ular "qiymatlar bir-biriga zid emasmi"
# degan savolga javob beradi. Audit topgan ikkita bug aynan shu turdagi edi:
# postgres ichki rejasi konteynerdan katta, redis maxmemory konteynerdan katta.
# =============================================================================
echo
echo "[4/5] Mantiqiy invariantlar (qiymatlar bir-biriga zid emasmi)"

# (a) Rezerv limitdan katta bo'lmasin.
#     Buzilsa Docker konteynerni UMUMAN ko'tarmaydi:
#     "Minimum memory limit can not be less than memory reservation limit"
for row in $SERVICES; do
  svc="${row%%:*}"; rest="${row#*:}"
  rest="${rest#*:}"
  mem_var="${rest%%:*}"; res_var="${rest##*:}"
  m="$(to_bytes "$(env_value "$mem_var")")"
  r="$(to_bytes "$(env_value "$res_var")")"
  if [ "$m" != "NaN" ] && [ "$r" != "NaN" ] && [ "$r" -gt "$m" ]; then
    fail "$svc — rezerv limitdan KATTA ($res_var=$(env_value "$res_var") > $mem_var=$(env_value "$mem_var")). Konteyner umuman ko'tarilmaydi."
  else
    dim "✓ $svc: rezerv <= limit"
  fi
done

# (b) Redis o'z chegarasi konteyner limitidan KICHIK bo'lsin.
#     Teskari bo'lsa `noeviction` himoyasi ishga tushmaydi — Redis ochiq xato
#     bermay turib, cgroup uni o'ldiradi (SignalR backplane + presence bilan).
RMEM="$(to_bytes "$(env_value REDIS_MEM)")"
RMAX="$(to_bytes "$(env_value REDIS_MAXMEMORY)")"
if [ "$RMEM" != "NaN" ] && [ "$RMAX" != "NaN" ]; then
  if [ "$RMAX" -ge "$RMEM" ]; then
    fail "redis — REDIS_MAXMEMORY ($(env_value REDIS_MAXMEMORY)) konteyner limitidan ($(env_value REDIS_MEM)) kichik EMAS. noeviction himoyasi ishlamaydi, o'rniga OOM kill bo'ladi."
  else
    dim "✓ redis: maxmemory < konteyner limiti"
  fi
fi

# (c) Postgres shared_buffers — konteyner limitining ~25% i (6.2 formulasi).
#     30% dan oshsa xato: qolgan xotira backend'lar, work_mem, autovacuum va
#     parallel worker'lar uchun yetmaydi va OOM-killer BACKEND ni oladi —
#     bu esa "terminating connection because of crash of another server
#     process" bo'lib butun ulanishlar to'plamini uzadi.
PGMEM="$(to_bytes "$(env_value POSTGRES_MEM)")"
PGSB="$(to_bytes "$(env_value POSTGRES_SHARED_BUFFERS)")"
if [ "$PGMEM" != "NaN" ] && [ "$PGSB" != "NaN" ] && [ "$PGMEM" -gt 0 ]; then
  PCT=$(( PGSB * 100 / PGMEM ))
  if [ "$PCT" -gt 30 ]; then
    fail "postgres — shared_buffers konteyner limitining ${PCT}% i ($(env_value POSTGRES_SHARED_BUFFERS) / $(env_value POSTGRES_MEM)). 6.2 formulasi: ~25%. Postgres HOST RAM'ini emas, cgroup limitini yashaydi!"
  else
    dim "✓ postgres: shared_buffers = konteyner limitining ${PCT}% i (6.2: ~25%)"
  fi
fi

# (d) Ulanishlar shifti pool'dan katta bo'lsin.
#     Buzilsa api ishga tushishi bilanoq "FATAL: sorry, too many clients already"
PGCONN="$(env_value POSTGRES_MAX_CONNECTIONS)"
PGPOOL="$(env_value POSTGRES_MAX_POOL_SIZE)"
if [ -n "$PGCONN" ] && [ -n "$PGPOOL" ]; then
  if [ "$PGCONN" -le $(( PGPOOL + 10 )) ]; then
    fail "postgres — POSTGRES_MAX_CONNECTIONS ($PGCONN) <= POSTGRES_MAX_POOL_SIZE ($PGPOOL) + 10. 'FATAL: sorry, too many clients already' kafolatlangan."
  else
    dim "✓ postgres: max_connections ($PGCONN) > pool ($PGPOOL) + 10"
  fi
fi

# (e) Barcha xotira limitlari yig'indisi mo'ljaldagi 16 GB serverga sig'sinmi.
#     6.1: yig'indi ~9.25 GB, qolgan ~6 GB ATAYIN page cache uchun bo'sh.
#     12 GB dan oshsa page cache uchun joy qolmaydi va postgres sekinlashadi.
TOTAL=0
for row in $SERVICES; do
  rest="${row#*:}"; rest="${rest#*:}"; mem_var="${rest%%:*}"
  b="$(to_bytes "$(env_value "$mem_var")")"
  [ "$b" != "NaN" ] && TOTAL=$(( TOTAL + b ))
done
TOTAL_MB=$(( TOTAL / 1024 / 1024 ))
if [ "$TOTAL_MB" -gt 12288 ]; then
  fail "Xotira limitlari yig'indisi ${TOTAL_MB} MB — 12 GB dan oshdi. 16 GB serverda page cache uchun joy qolmaydi (6.1: yig'indi ~9.25 GB bo'lishi kerak)."
else
  dim "✓ Xotira limitlari yig'indisi: ${TOTAL_MB} MB (6.1 mo'ljali ~9.25 GB, shift 12 GB)"
fi

# =============================================================================
# 5-QISM: ALOHIDA TUZOQLAR
# =============================================================================
echo
echo "[5/5] Alohida tuzoqlar"

# (a) `DOTNET_GCHeapHardLimitPercent` qaytib kelmasin.
#     SABAB: bu o'zgaruvchi MUHIT orqali berilganda ONALTILIK o'qiladi.
#     "75" = 0x75 = 117% => runtime uni butunlay e'tiborsiz qoldiradi (no-op,
#     o'lchangan), lekin o'qigan odamni aldaydi. Kimdir uni "60" ga
#     o'zgartirsa 0x60 = 96% bo'ladi va GC konteyner limitining deyarli
#     hammasini heap uchun olib, OOM kill (exit 137) ni O'ZI keltirib
#     chiqaradi. .NET standarti (~75%) allaqachon to'g'ri.
#     Izohlarda eslatib qo'yish MUMKIN va kerak (tarix saqlansin) — bu tekshiruv
#     faqat FAOL (izohsiz) qatorni qidiradi.
GCHITS="$(grep -hn "DOTNET_GCHeapHardLimitPercent" "$ROOT/docker-compose.yml" "$PROD" 2>/dev/null | grep -vE '^[0-9]+:[[:space:]]*#' || true)"
if [ -n "$GCHITS" ]; then
  fail "DOTNET_GCHeapHardLimitPercent yana faol qator sifatida qo'shilgan. U hex o'qiladi: '75'=0x75=117% (e'tiborsiz), '60'=0x60=96% (OOM). Olib tashlang — .NET standarti ~75% allaqachon to'g'ri."
else
  dim "✓ DOTNET_GCHeapHardLimitPercent faol qator sifatida yo'q (faqat izohlarda)"
fi

# (b) LiveKit'da `cpu_shares` bo'lsin va u `deploy:` ICHIDA bo'lmasin.
#     6.1/6.5: qattiq kvota media serverda eshitiladigan uzilish beradi,
#     shuning uchun ustunlik ikkinchi qatlam sifatida kerak.
if grep -qE '^\s{4}cpu_shares:' "$PROD"; then
  dim "✓ livekit cpu_shares xizmat darajasida yozilgan"
else
  fail "docker-compose.prod.yml da livekit uchun xizmat darajasidagi 'cpu_shares' topilmadi (DEPLOY_UBUNTU.md 6.1/6.5)."
fi

# (c) Prod postgres/redis `command:` bloklari BAZAVIY faylni ALMASHTIRADI.
#     Ya'ni bazaviy faylda ataylab qo'yilgan sozlama prod'da JIMGINA yo'qoladi.
#     Audit ikkita shunday yo'qolishni topdi: postgres max_wal_size/min_wal_size
#     va redis `--save ""` (RDB o'chirilishi). Ular qaytarildi — qaytadan
#     yo'qolmasin.
# -----------------------------------------------------------------------------
# 🔴 IZOHLAR TEKSHIRUVNI QANOATLANTIRMASIN (2026-08-23 da tuzatildi)
#
# Bu uch tekshiruv ilgari butun xizmat blokiga `grep` qilardi. Blok esa
# IZOHLARNI ham o'z ichiga oladi va o'sha izohlarda aynan qidirilayotgan
# matn bor edi (`--save`, `max_wal_size=`). Natijada tekshiruv O'Z
# OGOHLANTIRISH MATNI bilan qanoatlanardi.
#
# EMPIRIK ISBOTLANGAN (tekshiruvchi agent, 2026-08-23):
#   * prod command'dan `- --save` / `- ""` juftligi BUTUNLAY o'chirildi
#     -> darvoza baribir "✅ Hammasi mos" dedi, exit=0;
#   * `- --save` / `- "3600 1"` qilib RDB snapshot QAYTA YOQILDI
#     (izohning o'zi aynan shundan ogohlantiradi) -> darvoza yana yashil.
#
# Ya'ni darvoza soxta ishonch berardi — bu darvozasizlikdan YOMONROQ.
#
# YECHIM: xizmatning `command:` argumentlarini IZOHSIZ ajratib olamiz va
# faqat argumentlar ustida ishlaymiz. Qiymat ham tekshiriladi, mavjudligi
# emas.
# -----------------------------------------------------------------------------

# Xizmatning `command:` ro'yxati — har argument alohida qatorda, `- ` siz.
# To'liq izoh qatorlari tashlanadi; qiymat ICHIDAGI `#` ga tegilmaydi.
cmd_args() {
  awk -v svc="$1" '
    /^[[:space:]]*#/            { next }               # butun boshli izoh
    $0 ~ "^  " svc ":[[:space:]]*$" { insvc = 1; next }
    insvc && /^  [a-z_-]+:/     { insvc = 0; incmd = 0 }
    insvc && /^    command:/    { incmd = 1; next }
    incmd && /^      - /        { sub(/^      - /, ""); print; next }
    incmd && /^    [a-z_]/      { incmd = 0 }
  ' "$PROD"
}

PG_CMD="$(cmd_args postgres)"
REDIS_CMD="$(cmd_args redis)"

printf '%s\n' "$PG_CMD" | grep -q '^min_wal_size=' \
  || fail "postgres min_wal_size prod command'ida yo'q — prod'da postgres standarti (80MB) ishlaydi (bazaviy fayldagi qiymat MEROS QOLMAYDI)."
printf '%s\n' "$PG_CMD" | grep -q '^max_wal_size=' \
  || fail "postgres max_wal_size prod command'ida yo'q — dars boshida checkpoint 'to'lqini' qaytadi."

# --- redis `--save` — MAVJUDLIGI HAM, QIYMATI HAM ---------------------------
#
# `--save` dan KEYINGI argument RDB nuqtalarini belgilaydi. U BO'SH bo'lishi
# shart (`""`), aks holda snapshot yoqiladi va `fork()` Redis'ni bir necha
# yuz millisekundga qotiradi — 200 kishilik darsda chat va presence muzlaydi
# (sabab `docker-compose.yml` dagi redis izohida).
redis_save_value="$(printf '%s\n' "$REDIS_CMD" | awk '/^--save$/ { getline v; print v; found=1; exit } END { if (!found) print "__YOQ__" }')"

case "$redis_save_value" in
  __YOQ__)
    fail "redis prod command'ida '--save' argumenti yo'q — prod'da RDB standart nuqtalari (3600 1 / 300 100 / 60 10000) YOQILADI. fork() Redis'ni bir necha yuz ms QOTIRADI." ;;
  '""'|"''"|"")
    dim "✓ redis --save bo'sh (RDB fork pauzasi o'chirilgan)" ;;
  *)
    fail "redis '--save' qiymati BO'SH EMAS ($redis_save_value) — RDB snapshot yoqilgan. fork() pauzasi SignalR backplane va presence'ni muzlatadi. Kutilgan: --save \"\"" ;;
esac

# =============================================================================
# XULOSA
# =============================================================================
echo
if [ "$FAILURES" -eq 0 ]; then
  green "✅ Hammasi mos: DEPLOY_UBUNTU.md 6.1/6.2  ==  docker-compose.prod.yml  ==  .env.example"
  exit 0
fi

red "❌ $FAILURES ta nomuvofiqlik topildi."
cat <<'HINT'

   NIMA QILISH KERAK
   -----------------
   Bu darvoza uch manbani solishtiradi. Qaysi biri xato ekanini O'ZINGIZ
   hal qilasiz, lekin tartib SHU:

     1) Qiymatni ATAYLAB o'zgartirmoqchimisiz?
        -> avval docs/DEPLOY_UBUNTU.md 6.1/6.2 jadvalini yangilang (u yerda
           SABAB ham yozilsin), keyin compose va .env.example ni.
     2) Compose yoki .env.example hujjatdan orqada qolganmi?
        -> ularni hujjatga moslang.

   ⚠️ UCHTA JOYNI HAM YANGILANG. 2026-08-22 auditi topgan nosozlik aynan
   shundan paydo bo'lgan edi: hujjat bir narsani, compose boshqa narsani
   yozgan, .env.example da esa o'zgaruvchining o'zi yo'q edi — natijada
   operator standart (noto'g'ri) qiymat bilan ishlagan va buni faqat
   haqiqiy dars paytidagi OOM orqali bilib olardi.
HINT
exit 1
