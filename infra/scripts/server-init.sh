#!/usr/bin/env bash
# =============================================================================
#  ZIN-NUR v2 — SERVERNI NOLDAN TAYYORLASH VA BIRINCHI DEPLOY
#
#  Ishga tushirish (loyiha ildizidan):
#      ./infra/scripts/server-init.sh
#      ./infra/scripts/server-init.sh zinnur.uz        # o'z domeningiz bilan
#
#  Uzilishga chidamli qilib (TAVSIYA ETILADI):
#      nohup ./infra/scripts/server-init.sh > ~/init.log 2>&1 &
#      tail -f ~/init.log
#
#  Nima qiladi (har bosqich IDEMPOTENT — bajarilganini o'tkazib yuboradi):
#      1. Docker      5. TLS sertifikat (Let's Encrypt)
#      2. Swap        6. nginx konfiguratsiyasi
#      3. .env        7. Deploy
#      4. Firewall
#
#  ★ NEGA ALOHIDA SKRIPT: bu buyruqlarni terminalga qo'lda tashlash ishonchsiz —
#  DigitalOcean web konsoli uzilib qoladi va blok yarim yo'lda to'xtaydi
#  (amalda bir necha marta shunday bo'ldi). Skript `nohup` bilan seansdan
#  ajraladi va uzilishdan omon qoladi.
#
#  ⚠️ Domen berilmasa `sslip.io` ishlatiladi — serverning IP siga avtomatik
#  yechiladigan TEKIN domen, DNS sozlash kerak emas. Bu SINOV uchun; haqiqiy
#  foydalanuvchilarga chiqarganda o'z domeningizni bering.
# =============================================================================

set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m  ✓ %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m  ! %s\033[0m\n' "$*"; }
fail() { printf '\033[1;31m  ✗ %s\033[0m\n' "$*" >&2; }

trap 'fail "TO'\''XTADI (satr $LINENO). Yuqoridagi xatoni o'\''qing."' ERR

# ---------------------------------------------------------------- 0. Domen
# ⚠️ `-4` MAJBURIY: usiz `ifconfig.me` IPv6 qaytarishi mumkin va sslip.io
# uchun nom buzilib ketadi (IPv6 da nuqta emas, ikki nuqta ishlatiladi —
# `2a03:b0c0:...sslip.io` degan yaroqsiz nom hosil bo'ladi va curl uni
# "Port number was not a decimal number" deb rad etadi).
ARG_DOMEN="${1:-}"

IPV4_RE='^([0-9]{1,3}\.){3}[0-9]{1,3}$'
IP=""

for src in https://api.ipify.org https://ifconfig.me https://icanhazip.com; do
    candidate="$(curl -4 -fsS --max-time 10 "$src" 2>/dev/null | tr -d '[:space:]' || true)"
    if [[ "$candidate" =~ $IPV4_RE ]]; then IP="$candidate"; break; fi
done

# Zaxira: mahalliy interfeysdagi birinchi IPv4.
if [[ -z "$IP" ]]; then
    IP="$(hostname -I 2>/dev/null | tr ' ' '\n' | grep -E "$IPV4_RE" | head -1 || true)"
fi

# ★ DOMEN va LKDOMEN SHU YERDA, har qanday sharoitda o'rnatiladi.
# Ilgari ular `if/else` ichida edi va `set -u` bilan birga "unbound variable"
# xatosiga olib keldi: IP aniqlanmagan tarmoqda tarmoq blokidan chiqib
# ketilganda o'zgaruvchilar umuman yaratilmasdi.
if [[ -n "$ARG_DOMEN" ]]; then
    DOMEN="$ARG_DOMEN"
elif [[ "$IP" =~ $IPV4_RE ]]; then
    # sslip.io: 1.2.3.4 -> 1-2-3-4.sslip.io (DNS sozlash KERAK EMAS)
    DOMEN="${IP//./-}.sslip.io"
else
    fail "Serverning IPv4 manzili aniqlanmadi."
    fail "Domenni qo'lda bering:"
    fail "    ./infra/scripts/server-init.sh 134-122-66-200.sslip.io"
    exit 1
fi
LKDOMEN="livekit.$DOMEN"

# Domen berilgan, lekin IP topilmagan bo'lsa — `SERVER_PUBLIC_IP` uchun kerak.
if [[ ! "$IP" =~ $IPV4_RE ]]; then
    warn "IPv4 aniqlanmadi; SERVER_PUBLIC_IP bo'sh qoladi (LiveKit o'zi aniqlaydi)."
    IP=""
fi

log "Server: ${IP:-aniqlanmadi}  |  Domen: $DOMEN  |  LiveKit: $LKDOMEN"

# ---------------------------------------------------------------- 1. Docker
log "1/7 Docker"
if command -v docker >/dev/null && docker compose version >/dev/null 2>&1; then
    ok "allaqachon o'rnatilgan ($(docker --version | cut -d, -f1))"
else
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl gnupg
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
        | gpg --dearmor -o /etc/apt/keyrings/docker.gpg --yes
    chmod a+r /etc/apt/keyrings/docker.gpg
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
        > /etc/apt/sources.list.d/docker.list
    apt-get update -qq
    apt-get install -y -qq docker-ce docker-ce-cli containerd.io \
        docker-buildx-plugin docker-compose-plugin
    systemctl enable --now docker
    ok "o'rnatildi"
fi

# ---------------------------------------------------------------- 2. Swap
log "2/7 Swap"
# ⚠️ Swap MAVJUDLIGI `free` dan o'qiladi, `swapon --show` dan EMAS: oxirgisi
# ba'zi muhitlarda (konteyner, cheklangan tty) bo'sh qaytaradi va skript
# mavjud swap ustiga yana yozishga urinib "Text file busy" bilan yiqilardi.
SWAP_KB="$(awk '/^SwapTotal:/{print $2}' /proc/meminfo 2>/dev/null || echo 0)"
if [[ "${SWAP_KB:-0}" -gt 0 ]]; then
    ok "allaqachon bor ($(free -h | awk '/Swap/{print $2}'))"
elif [[ -e /swapfile ]]; then
    warn "/swapfile bor, lekin yoqilmagan — yoqilmoqda"
    swapon /swapfile 2>/dev/null && ok "yoqildi" || warn "yoqib bo'lmadi, davom etamiz"
else
    # Swap MAJBURIY emas: 4 GB RAM da usiz ham build o'tadi. Shuning uchun
    # bu blok yiqilsa butun deploy to'xtamasin.
    if fallocate -l 4G /swapfile 2>/dev/null && chmod 600 /swapfile \
       && mkswap -q /swapfile 2>/dev/null && swapon /swapfile 2>/dev/null; then
        grep -q '/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
        ok "4G qo'shildi"
    else
        warn "swap qo'shilmadi — RAM yetarli bo'lsa muammo emas"
        rm -f /swapfile 2>/dev/null || true
    fi
fi
free -h | head -2 | sed 's/^/     /'

# ---------------------------------------------------------------- 3. .env
log "3/7 .env"
[[ -f .env ]] || cp .env.example .env

# Sirlar FAQAT hali almashtirilmagan bo'lsa generatsiya qilinadi — qayta
# yurgizishda mavjud parollar SAQLANADI (aks holda baza ochilmay qolardi:
# postgres volume eski parol bilan initsializatsiya qilingan bo'ladi).
if grep -qE '^(JWT_SECRET|POSTGRES_PASSWORD|LIVEKIT_API_SECRET)=.*(change_me|dev_only)' .env; then
    sed -i "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=')|" .env
    sed -i "s|^JWT_SECRET=.*|JWT_SECRET=$(openssl rand -base64 48 | tr -d '/+=')|" .env
    sed -i "s|^LIVEKIT_API_SECRET=.*|LIVEKIT_API_SECRET=$(openssl rand -hex 32)|" .env
    sed -i "s|^LIVEKIT_API_KEY=.*|LIVEKIT_API_KEY=zinnur-prod|" .env
    ok "sirlar generatsiya qilindi"
else
    ok "sirlar allaqachon o'rnatilgan (saqlab qolindi)"
fi

sed -i "s|^ASPNETCORE_ENVIRONMENT=.*|ASPNETCORE_ENVIRONMENT=Production|" .env
sed -i "s|^DOMAIN=.*|DOMAIN=$DOMEN|" .env
sed -i "s|^LIVEKIT_DOMAIN=.*|LIVEKIT_DOMAIN=$LKDOMEN|" .env
sed -i "s|^SERVER_PUBLIC_IP=.*|SERVER_PUBLIC_IP=$IP|" .env
sed -i "s|^LIVEKIT_PUBLIC_URL=.*|LIVEKIT_PUBLIC_URL=wss://$LKDOMEN|" .env
sed -i "s|^VITE_API_URL=.*|VITE_API_URL=https://$DOMEN|" .env
sed -i "s|^VITE_HUB_URL=.*|VITE_HUB_URL=https://$DOMEN/hubs/live|" .env

# 🔴 CORS — BUNI UNUTISH DEPLOY'NI TO'XTATADI (2026-08-27 da qo'shildi).
#    `.env.example` da `CORS_ORIGIN_0=http://localhost:5173` turadi va
#    prod overlay'i uni QAYTA YOZMAYDI. `ProductionSecretsGuard` esa
#    `Cors:AllowedOrigins` ichida `localhost` topsa ilovani ATAYLAB
#    ko'tarmaydi — ya'ni bu qatorsiz birinchi deploy har safar yiqilardi.
sed -i "s|^CORS_ORIGIN_0=.*|CORS_ORIGIN_0=https://$DOMEN|" .env
sed -i "s|^CORS_ORIGIN_1=.*|CORS_ORIGIN_1=|" .env

# 🔴 BOSH ADMINISTRATOR TELEFONI — BO'SH BAZADA MAJBURIY.
#    Prod'da standarti ATAYLAB yo'q (`BootstrapAdmin.Read`): "hammaga
#    ma'lum raqam" administrator hisobini istalgan odamga ochib qo'yardi.
#    Berilmasa `DbInitializer` ishga tushishdayoq yiqiladi.
if ! grep -q '^Bootstrap__AdminPhone=' .env; then
    printf '\n# Boshlang'"'"'ich administrator telefoni (bo'"'"'sh bazada MAJBURIY).\n' >> .env
    printf 'Bootstrap__AdminPhone=%s\n' "${ADMIN_PHONE:-}" >> .env
fi

if ! grep -qE '^Bootstrap__AdminPhone=\+?[0-9]{9,}$' .env; then
    fail ".env da Bootstrap__AdminPhone to'ldirilmagan."
    fail "Bu raqamga bog'langan Telegram hisobiga kirish kodi keladi —"
    fail "usiz platformaga HECH KIM kira olmaydi."
    fail ""
    fail "Yo'l qo'ying:  ADMIN_PHONE=+998901234567 $0 $DOMEN"
    fail "yoki .env dagi Bootstrap__AdminPhone qatorini qo'lda to'ldiring."
    exit 1
fi

if grep -qE 'change_me|dev_only' .env; then
    fail ".env da hali dev qiymatlari bor"; exit 1
fi
ok "domen, manzillar, CORS va administrator raqami yozildi"

# ---------------------------------------------------------------- 4. Firewall
log "4/7 Firewall"
if command -v ufw >/dev/null; then
    ufw allow 22/tcp   >/dev/null
    ufw allow 80/tcp   >/dev/null
    ufw allow 443/tcp  >/dev/null
    # ⚠️ UDP SIZ MEDIA UMUMAN ISHLAMAYDI — deploy'dagi eng ko'p uchraydigan xato
    ufw allow 7882/udp >/dev/null
    ufw allow 3478/udp >/dev/null
    ufw --force enable >/dev/null
    ok "22/80/443 tcp + 7882/3478 udp ochildi"
else
    warn "ufw yo'q — firewall qo'lda sozlansin"
fi

# ---------------------------------------------------------------- 5. Sertifikat
log "5/7 TLS sertifikat"
if [[ -d "/etc/letsencrypt/live/$DOMEN" ]]; then
    ok "allaqachon bor"
else
    export DEBIAN_FRONTEND=noninteractive
    apt-get install -y -qq nginx certbot
    mkdir -p /var/www/certbot
    rm -f /etc/nginx/sites-enabled/default /etc/nginx/sites-enabled/zinnur.conf

    # Vaqtinchalik konfiguratsiya — FAQAT ACME tekshiruvi uchun.
    # Loyihaning asosiy konfigi sertifikat FAYLLARIGA murojaat qiladi, ular esa
    # hali yo'q — shuning uchun avval shu minimal blok qo'yiladi, aks holda
    # nginx `cannot load certificate` bilan umuman ishga tushmaydi.
    cat > /etc/nginx/sites-available/acme.conf <<EOF
server {
    listen 80 default_server;
    server_name $DOMEN $LKDOMEN;
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 200 'ok'; add_header Content-Type text/plain; }
}
EOF
    ln -sf /etc/nginx/sites-available/acme.conf /etc/nginx/sites-enabled/acme.conf
    nginx -t && systemctl restart nginx

    # Certbot'ni chaqirishdan OLDIN webroot haqiqatan ishlashini tekshiramiz —
    # aks holda Let's Encrypt urinishlari bekorga sarflanadi (soatlik limit bor
    # va unga yetsangiz bir necha soat kutishga to'g'ri keladi).
    # ⚠️ FAYL YO'LI: nginx `root` bilan to'liq URI ni qo'shadi, ya'ni
    # `/.well-known/acme-challenge/probe` -> `/var/www/certbot/.well-known/acme-challenge/probe`.
    # Faylni to'g'ridan-to'g'ri `/var/www/certbot/probe` ga yozish 404 beradi
    # (certbot ham AYNI shu chuqur yo'lga yozadi — tekshiruv unga mos bo'lsin).
    mkdir -p /var/www/certbot/.well-known/acme-challenge
    echo probe > /var/www/certbot/.well-known/acme-challenge/probe
    if curl -fsS --max-time 15 "http://$DOMEN/.well-known/acme-challenge/probe" >/dev/null; then
        ok "webroot tekshiruvdan o'tdi"
        rm -f /var/www/certbot/.well-known/acme-challenge/probe
    else
        fail "webroot internetdan ochilmadi:"
        fail "  http://$DOMEN/.well-known/acme-challenge/probe"
        fail "Tekshiring:"
        fail "  dig +short $DOMEN            # IP chiqishi kerak"
        fail "  curl -I http://$DOMEN/       # 200 qaytishi kerak"
        fail "  ufw status                   # 80/tcp ochiqmi"
        exit 1
    fi

    certbot certonly --webroot -w /var/www/certbot --non-interactive --agree-tos \
        --register-unsafely-without-email -d "$DOMEN" -d "$LKDOMEN"
    ok "sertifikat olindi"
fi

# 🔴 YANGILANISH HOOK'I — SERTIFIKAT BIR MARTA OLINSA YETARLI EMAS.
#
#    Sertifikat 90 kun amal qiladi, certbot 30 kun qolganda uni o'zi
#    yangilaydi (paket bilan kelgan systemd taymer). LEKIN nginx yangi
#    faylni O'ZI OLMAYDI — u ishga tushganda o'qigan nusxani xotirada
#    ushlab turadi. Reload qilinmasa, ~60-kunda brauzer "sertifikat
#    muddati tugagan" deb ogohlantiradi, serverda esa hech qanday xato
#    ko'rinmaydi: certbot "renewed" deb yozadi va hammasi joyida
#    ko'rinadi. Aynan shu sabab bu qadam ATAYLAB avtomatlashtirildi —
#    hujjatdagi qo'lda bajariladigan qadam uch oydan keyin unutiladi.
#
#    `renewal-hooks/deploy/` — faqat sertifikat HAQIQATAN yangilanganda
#    ishlaydi (`pre`/`post` esa har urinishda), ya'ni keraksiz reload yo'q.
mkdir -p /etc/letsencrypt/renewal-hooks/deploy
cat > /etc/letsencrypt/renewal-hooks/deploy/00-reload-nginx.sh <<'HOOK'
#!/usr/bin/env bash
set -euo pipefail
# Konfiguratsiya buzuq bo'lsa reload QILMAYMIZ — ishlab turgan nginx yiqilmasin.
if nginx -t 2>/dev/null; then
    systemctl reload nginx
    logger -t certbot-hook "nginx reloaded after cert renewal"
else
    logger -t certbot-hook "ERROR: nginx -t failed, reload skipped"
    exit 1
fi
HOOK
chmod +x /etc/letsencrypt/renewal-hooks/deploy/00-reload-nginx.sh
ok "sertifikat yangilanish hook'i o'rnatildi (nginx avtomatik reload)"

# ---------------------------------------------------------------- 6. nginx
log "6/7 nginx konfiguratsiyasi"
rm -f /etc/nginx/sites-enabled/acme.conf
# Almashtirish TARTIBI muhim: `livekit.zinnur.uz` ichida `zinnur.uz` bor,
# shuning uchun eng uzun nomdan boshlanadi.
sed -e "s|livekit\.zinnur\.uz|$LKDOMEN|g" \
    -e "s|www\.zinnur\.uz|$DOMEN|g" \
    -e "s|zinnur\.uz|$DOMEN|g" \
    infra/nginx/zinnur.conf > /etc/nginx/sites-available/zinnur.conf

# ⚠️ SERTIFIKAT YO'LI: `certbot -d A -d B` BITTA sertifikat yaratadi va uni
# BIRINCHI domen papkasiga qo'yadi. Ikkinchi nom sertifikat ichida (SAN)
# bor, lekin `live/<ikkinchi-nom>/` papkasi UMUMAN YARATILMAYDI.
# Konfiguratsiya esa ikkita alohida papka kutadi va nginx ishga tushmaydi:
#   [emerg] cannot load certificate ".../livekit.<domen>/fullchain.pem"
# Shuning uchun LiveKit blokini asosiy sertifikatga yo'naltiramiz.
if [[ ! -d "/etc/letsencrypt/live/$LKDOMEN" ]]; then
    sed -i "s|/etc/letsencrypt/live/$LKDOMEN/|/etc/letsencrypt/live/$DOMEN/|g" \
        /etc/nginx/sites-available/zinnur.conf
    ok "LiveKit bloki asosiy sertifikatga yo'naltirildi (SAN ichida)"
fi
ln -sf /etc/nginx/sites-available/zinnur.conf /etc/nginx/sites-enabled/zinnur.conf
nginx -t && systemctl reload nginx
ok "nginx tayyor"

# ---------------------------------------------------------------- 7. Deploy
log "7/7 Deploy (build 5-15 daqiqa olishi mumkin)"
./infra/scripts/deploy.sh

printf '\n\033[1;32m════════════════════════════════════════════\033[0m\n'
printf '\033[1;32m  TAYYOR:  https://%s\033[0m\n' "$DOMEN"
printf '\033[1;32m  Kirish:  %s (Telegram orqali kod)\033[0m\n' \
       "$(grep '^Bootstrap__AdminPhone=' .env | cut -d= -f2)"
printf '\033[1;32m════════════════════════════════════════════\033[0m\n\n'
