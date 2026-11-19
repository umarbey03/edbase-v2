#!/usr/bin/env bash
# =============================================================================
#  ZIN-NUR — ilova uchun SUPERUSER BO'LMAGAN Postgres roli (2026-11-19 auditi)
#
#  MUAMMO: api va compositor bazaga `POSTGRES_USER` (zinnur) bo'lib ulanardi.
#  Bu rol — klasterning SUPERUSER'i (rasmiy postgres image'i bootstrap
#  foydalanuvchini shunday yaratadi). Ya'ni ilovadagi bitta SQL injection
#  yoki buzilgan konteyner butun klasterni, shu jumladan `COPY ... PROGRAM`
#  orqali konteyner ichida buyruq bajarishni qo'lga olardi.
#
#  YECHIM: `POSTGRES_APP_USER` (standart `zinnur_app`):
#    * LOGIN, lekin NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
#    * `POSTGRES_DB` bazasining va undagi `public` obyektlarining EGASI —
#      chunki migratsiyalar ilova ishga tushganda (DbInitializer) qo'llanadi
#      va ular ALTER/CREATE qiladi. Ega bo'lmasa birinchi migratsiyada yiqilardi;
#    * boshqa bazalarga (postgres, zinnur_migr) ulanish huquqi YO'Q.
#  `POSTGRES_USER` superuser bo'lib QOLADI — faqat zaxira (pg_dump) va qo'lda
#  texnik ish uchun; ilova konteynerlari uni endi bilmaydi.
#
#  IDEMPOTENT: xohlagancha qayta ishga tushiring (parolni almashtirish ham
#  shu skript bilan — `.env` dagi yangi qiymat qo'llanadi).
#
#  ISHLATISH (loyiha ildizida, postgres konteyneri ishlab turganida):
#      ./infra/scripts/db-app-role.sh
#  Keyin api/compositor'ni qayta yarating (docker-compose.prod.yml ular uchun
#  POSTGRES_APP_USER / POSTGRES_APP_PASSWORD ni ishlatadi).
#
#  ⚠️ KELAJAKDAGI MIGRATSIYA uchun: `CREATE EXTENSION` faqat "trusted"
#  kengaytmalar uchun ishlaydi (pg_trgm — trusted). Trusted bo'lmagan
#  kengaytma kerak bo'lsa, uni `POSTGRES_USER` bilan qo'lda yarating.
# =============================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# .env dan faqat kerakli kalitlarni o'qiymiz (butun faylni `source` qilmaymiz —
# unda qo'shtirnoqsiz bo'sh joyli qiymatlar bo'lishi mumkin).
env_value() { grep -E "^$1=" .env | tail -1 | cut -d= -f2- | sed -E 's/^"(.*)"$/\1/'; }

SUPER_USER="$(env_value POSTGRES_USER)"
DB_NAME="$(env_value POSTGRES_DB)"
APP_USER="$(env_value POSTGRES_APP_USER)"; APP_USER="${APP_USER:-zinnur_app}"
APP_PASSWORD="$(env_value POSTGRES_APP_PASSWORD)"
PG_CONTAINER="${PG_CONTAINER:-zinnur-v2-postgres}"

[ -n "$SUPER_USER" ] && [ -n "$DB_NAME" ] || { echo "POSTGRES_USER/POSTGRES_DB .env da yo'q" >&2; exit 1; }
[ "${#APP_PASSWORD}" -ge 24 ] || { echo "POSTGRES_APP_PASSWORD .env da yo'q yoki 24 belgidan qisqa (openssl rand -hex 32)" >&2; exit 1; }
[[ "$APP_USER" =~ ^[a-z_][a-z0-9_]*$ ]] || { echo "POSTGRES_APP_USER noto'g'ri: $APP_USER" >&2; exit 1; }
[ "$APP_USER" != "$SUPER_USER" ] || { echo "POSTGRES_APP_USER superuser bilan bir xil bo'lmasin" >&2; exit 1; }

psql_super() { docker exec -i "$PG_CONTAINER" psql -v ON_ERROR_STOP=1 -q -U "$SUPER_USER" "$@"; }

# 1) Rol (parol stdin orqali — buyruq qatorida/`ps` da ko'rinmaydi).
psql_super -d postgres -v app="$APP_USER" -v pw="$APP_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN', :'app')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app') \gexec
SELECT format('ALTER ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
              :'app', :'pw') \gexec
SQL

# 2) Baza egasi + boshqa bazalarga ulanishni yopish.
psql_super -d postgres -v app="$APP_USER" -v db="$DB_NAME" <<'SQL'
SELECT format('ALTER DATABASE %I OWNER TO %I', :'db', :'app') \gexec
SELECT format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', datname)
  FROM pg_database WHERE datname <> :'db' AND NOT datistemplate \gexec
SQL

# 3) `public` dagi obyektlar egaligi (kengaytma a'zolari — pg_trgm — tegilmaydi).
psql_super -d "$DB_NAME" -v app="$APP_USER" -v su="$SUPER_USER" <<'SQL'
SELECT set_config('zinnur.app', :'app', false) AS _app, set_config('zinnur.su', :'su', false) AS _su \gset
DO $$
DECLARE
  app text := current_setting('zinnur.app');
  su  oid  := current_setting('zinnur.su')::regrole;
  r record;
BEGIN
  -- Jadvallar, view'lar (jadvalga bog'langan sequence va indekslar o'zi ergashadi)
  FOR r IN
    SELECT c.oid::regclass AS obj, c.relkind
      FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE n.nspname = 'public' AND c.relowner = su AND c.relkind IN ('r','p','v','m','f')
       AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_class'::regclass
                         AND d.objid = c.oid AND d.deptype = 'e')
  LOOP
    EXECUTE format('ALTER %s %s OWNER TO %I',
      CASE r.relkind WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW'
                     WHEN 'f' THEN 'FOREIGN TABLE' ELSE 'TABLE' END, r.obj, app);
  END LOOP;

  -- Hech qaysi jadvalga bog'lanmagan sequence'lar
  FOR r IN
    SELECT c.oid::regclass AS obj
      FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE n.nspname = 'public' AND c.relowner = su AND c.relkind = 'S'
       AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_class'::regclass
                         AND d.objid = c.oid AND d.deptype IN ('a','i','e'))
  LOOP
    EXECUTE format('ALTER SEQUENCE %s OWNER TO %I', r.obj, app);
  END LOOP;

  -- Funksiyalar (kengaytmaniki emas)
  FOR r IN
    SELECT p.oid::regprocedure AS obj, p.prokind
      FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
     WHERE n.nspname = 'public' AND p.proowner = su
       AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_proc'::regclass
                         AND d.objid = p.oid AND d.deptype = 'e')
  LOOP
    EXECUTE format('ALTER %s %s OWNER TO %I',
      CASE r.prokind WHEN 'p' THEN 'PROCEDURE' WHEN 'a' THEN 'AGGREGATE' ELSE 'FUNCTION' END, r.obj, app);
  END LOOP;

  -- Mustaqil tiplar (enum/domain/composite; jadval qatori tipi emas)
  FOR r IN
    SELECT t.oid::regtype AS obj, t.typtype
      FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
     WHERE n.nspname = 'public' AND t.typowner = su AND t.typtype IN ('e','d','c')
       AND NOT EXISTS (SELECT 1 FROM pg_class c WHERE c.reltype = t.oid AND c.relkind <> 'c')
       AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_type'::regclass
                         AND d.objid = t.oid AND d.deptype = 'e')
  LOOP
    EXECUTE format('ALTER %s %s OWNER TO %I',
      CASE r.typtype WHEN 'd' THEN 'DOMAIN' ELSE 'TYPE' END, r.obj, app);
  END LOOP;
END $$;
SQL

# 4) Tekshiruv: superuser'da qolgan (kengaytmadan tashqari) obyekt bo'lmasligi kerak.
LEFT=$(psql_super -d "$DB_NAME" -At -v su="$SUPER_USER" <<'SQL'
SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE n.nspname = 'public' AND c.relowner = (:'su')::regrole AND c.relkind IN ('r','p','v','m','S','f')
   AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid='pg_class'::regclass AND d.objid=c.oid AND d.deptype='e');
SQL
)
IS_SUPER=$(psql_super -d postgres -At -v app="$APP_USER" <<<"SELECT rolsuper FROM pg_roles WHERE rolname = :'app';")

if [ "$LEFT" != "0" ] || [ "$IS_SUPER" != "f" ]; then
  echo "XATO: superuser'da qolgan obyektlar=$LEFT, ${APP_USER}.rolsuper=$IS_SUPER" >&2
  exit 1
fi
echo "OK: ${APP_USER} — superuser emas, ${DB_NAME} va undagi barcha obyektlar egasi."
