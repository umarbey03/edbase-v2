#!/usr/bin/env bash
# =============================================================================
#  ZIN-NUR v2 — Ubuntu 24.04 LTS production bootstrap
#
#  Nima qiladi (har bir qadam IDEMPOTENT — skriptni xohlagancha qayta
#  ishga tushirsangiz bo'ladi, ikkinchi marta hech narsani buzmaydi):
#
#    1.  Tizim paketlarini yangilash + zarur utilitalar
#    2.  Timezone
#    3.  Non-root sudo foydalanuvchi + SSH kalitini ko'chirish
#    4.  SSH hardening (key-only, root login off, port)      [tasdiq so'raydi]
#    5.  unattended-upgrades (faqat security)
#    6.  Swap fayl                                            [tasdiq so'raydi]
#    7.  Docker CE (rasmiy repo) + compose plugin             [tasdiq so'raydi]
#    8.  /etc/docker/daemon.json (log rotation, ulimits)      [tasdiq so'raydi]
#    9.  systemd override: dockerd LimitNOFILE
#    10. sysctl-zinnur.conf -> /etc/sysctl.d/99-zinnur.conf
#    11. /etc/security/limits.d/99-zinnur.conf
#    12. UFW qoidalari                                        [tasdiq so'raydi]
#    13. DOCKER-USER zanjiri (Docker UFW'ni chetlab o'tishiga qarshi)
#    14. Katalog tuzilmasi (/opt/zinnur, /var/backups/zinnur)
#    15. Yakuniy hisobot + tekshirish buyruqlari
#
#  ISHLATISH:
#      sudo ./server-setup.sh                       # interaktiv
#      sudo ADMIN_USER=zinnur SSH_PORT=2222 ./server-setup.sh
#      sudo ./server-setup.sh --yes                 # hech nima so'ramaydi (CI)
#      sudo ./server-setup.sh --dry-run             # faqat ko'rsatadi
#
#  OGOHLANTIRISH: SSH portini o'zgartirsangiz, JORIY SSH SESSIYASINI
#  YOPMANG. Yangi terminaldan yangi port bilan kira olganingizga ishonch
#  hosil qilmaguningizcha eski sessiya sizning "zaxira kalitingiz".
# =============================================================================

set -euo pipefail

# ----------------------------------------------------------------------------
# SOZLAMALAR — env orqali o'zgartiriladi
# ----------------------------------------------------------------------------
ADMIN_USER="${ADMIN_USER:-zinnur}"
SSH_PORT="${SSH_PORT:-2222}"
TIMEZONE="${TIMEZONE:-Asia/Tashkent}"
SWAP_SIZE_GB="${SWAP_SIZE_GB:-auto}"     # "auto" = RAM ga qarab hisoblanadi
APP_DIR="${APP_DIR:-/opt/zinnur}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/zinnur}"

# UFW: LiveKit 7880 (WS/API) ni to'g'ridan-to'g'ri ochish kerakmi?
# Tavsiya: NO — host nginx wss://livekit.domen.uz (443) ni 127.0.0.1:7880 ga
# proxy qiladi. Faqat nginx'siz topologiyada YES qiling.
EXPOSE_LIVEKIT_7880="${EXPOSE_LIVEKIT_7880:-no}"

ASSUME_YES="${ASSUME_YES:-no}"
DRY_RUN="${DRY_RUN:-no}"

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
SYSCTL_SRC="${SCRIPT_DIR}/sysctl-zinnur.conf"
SYSCTL_DST="/etc/sysctl.d/99-zinnur.conf"

for arg in "$@"; do
    case "$arg" in
        -y|--yes)     ASSUME_YES="yes" ;;
        -n|--dry-run) DRY_RUN="yes" ;;
        -h|--help)    sed -n '2,45p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "Noma'lum argument: $arg (--help)"; exit 1 ;;
    esac
done

# ----------------------------------------------------------------------------
# CHIQISH / HELPER
# ----------------------------------------------------------------------------
if [[ -t 1 ]]; then
    C_RED=$'\033[0;31m'; C_GRN=$'\033[0;32m'; C_YLW=$'\033[0;33m'
    C_BLU=$'\033[0;34m'; C_BLD=$'\033[1m';    C_OFF=$'\033[0m'
else
    C_RED=''; C_GRN=''; C_YLW=''; C_BLU=''; C_BLD=''; C_OFF=''
fi

STEP_N=0
declare -a SUMMARY=()

step()   { STEP_N=$((STEP_N+1)); printf '\n%s==> [%02d] %s%s\n' "$C_BLU$C_BLD" "$STEP_N" "$*" "$C_OFF"; }
info()   { printf '     %s\n' "$*"; }
ok()     { printf '     %s✔%s %s\n' "$C_GRN" "$C_OFF" "$*"; SUMMARY+=("OK    | $*"); }
skip()   { printf '     %s•%s %s\n' "$C_YLW" "$C_OFF" "$*"; SUMMARY+=("SKIP  | $*"); }
warn()   { printf '     %s!%s %s\n' "$C_YLW" "$C_OFF" "$*"; SUMMARY+=("WARN  | $*"); }
die()    { printf '\n%sXATO:%s %s\n' "$C_RED$C_BLD" "$C_OFF" "$*" >&2; exit 1; }

# Xavfli qadamlar uchun tasdiq.
confirm() {
    local prompt="$1"
    if [[ "$DRY_RUN" == "yes" ]]; then
        printf '     %s[dry-run]%s %s -> o'\''tkazib yuborildi\n' "$C_YLW" "$C_OFF" "$prompt"
        return 1
    fi
    if [[ "$ASSUME_YES" == "yes" ]]; then
        printf '     %s[--yes]%s %s\n' "$C_YLW" "$C_OFF" "$prompt"
        return 0
    fi
    local ans
    printf '     %s?%s %s [y/N]: ' "$C_YLW" "$C_OFF" "$prompt"
    read -r ans </dev/tty || ans=""
    [[ "$ans" =~ ^[Yy]$ ]]
}

run() {
    if [[ "$DRY_RUN" == "yes" ]]; then
        printf '     %s[dry-run]%s %s\n' "$C_YLW" "$C_OFF" "$*"
        return 0
    fi
    "$@"
}

# Fayl mazmuni o'zgargan bo'lsagina yozadi + eski nusxani saqlaydi.
write_file() {
    local dst="$1" mode="${2:-0644}"
    local tmp; tmp="$(mktemp)"
    cat > "$tmp"
    if [[ -f "$dst" ]] && cmp -s "$tmp" "$dst"; then
        rm -f "$tmp"
        return 1            # o'zgarish yo'q
    fi
    if [[ "$DRY_RUN" == "yes" ]]; then
        printf '     %s[dry-run]%s yozilardi: %s\n' "$C_YLW" "$C_OFF" "$dst"
        echo "     --- boshi ---"; sed 's/^/     /' "$tmp"; echo "     --- oxiri ---"
        rm -f "$tmp"; return 0
    fi
    if [[ -f "$dst" ]]; then
        cp -a "$dst" "${dst}.zinnur-bak.$(date +%Y%m%d%H%M%S)"
    fi
    install -m "$mode" "$tmp" "$dst"
    rm -f "$tmp"
    return 0
}

# ----------------------------------------------------------------------------
# ODDIY TEKSHIRUVLAR
# ----------------------------------------------------------------------------
[[ $EUID -eq 0 ]] || die "Skript root huquqi bilan ishlashi kerak: sudo $0"

if [[ -r /etc/os-release ]]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    if [[ "${ID:-}" != "ubuntu" ]]; then
        warn "Bu skript Ubuntu uchun. Aniqlangan: ${PRETTY_NAME:-aniqlanmadi}"
    elif [[ "${VERSION_ID:-}" != "24.04" ]]; then
        warn "Mo'ljal — Ubuntu 24.04 LTS. Aniqlangan: ${PRETTY_NAME:-?}. Davom etiladi."
    fi
else
    warn "/etc/os-release o'qilmadi — OS aniqlanmadi."
fi

RAM_MB="$(awk '/MemTotal/ {printf "%d", $2/1024}' /proc/meminfo)"
CPU_N="$(nproc)"
WAN_IF="$(ip -4 route show default 2>/dev/null | awk '/default/ {print $5; exit}')"
[[ -n "$WAN_IF" ]] || WAN_IF="eth0"

printf '%s\n' "${C_BLD}================================================================${C_OFF}"
printf '%s\n' "${C_BLD} ZIN-NUR v2 — server bootstrap${C_OFF}"
printf '%s\n' "${C_BLD}================================================================${C_OFF}"
info "OS            : ${PRETTY_NAME:-aniqlanmadi}"
info "CPU / RAM     : ${CPU_N} vCPU / ${RAM_MB} MB"
info "WAN interfeys : ${WAN_IF}"
info "ADMIN_USER    : ${ADMIN_USER}"
info "SSH_PORT      : ${SSH_PORT}"
info "TIMEZONE      : ${TIMEZONE}"
info "APP_DIR       : ${APP_DIR}"
[[ "$DRY_RUN" == "yes" ]] && printf '%s DRY-RUN REJIMI — hech narsa o'\''zgartirilmaydi %s\n' "$C_YLW$C_BLD" "$C_OFF"

if (( CPU_N < 4 )) || (( RAM_MB < 7500 )); then
    warn "200 bir vaqtdagi foydalanuvchi uchun tavsiya: >= 8 vCPU / 16 GB. Hozirgi server kichik."
fi

# =============================================================================
step "Tizim paketlari va bazaviy utilitalar"
# =============================================================================
export DEBIAN_FRONTEND=noninteractive
run apt-get update -qq
run apt-get install -y -qq \
    ca-certificates curl gnupg lsb-release \
    ufw unattended-upgrades apt-listchanges \
    jq git rsync htop vnstat iproute2 dnsutils \
    conntrack net-tools iptables-persistent >/dev/null
ok "Bazaviy paketlar o'rnatildi"

# =============================================================================
step "Timezone: ${TIMEZONE}"
# =============================================================================
CUR_TZ="$(timedatectl show -p Timezone --value 2>/dev/null || echo '')"
if [[ "$CUR_TZ" == "$TIMEZONE" ]]; then
    skip "Timezone allaqachon ${TIMEZONE}"
else
    run timedatectl set-timezone "$TIMEZONE"
    ok "Timezone ${CUR_TZ:-?} -> ${TIMEZONE}"
fi
# NTP: vaqt siljishi JWT (`nbf`/`exp`) va LiveKit token'larini buzadi.
run timedatectl set-ntp true || true
ok "NTP sinxronizatsiyasi yoqildi (JWT nbf/exp uchun kritik)"

# =============================================================================
step "Non-root sudo foydalanuvchi: ${ADMIN_USER}"
# =============================================================================
if id -u "$ADMIN_USER" >/dev/null 2>&1; then
    skip "Foydalanuvchi ${ADMIN_USER} allaqachon mavjud"
else
    run adduser --disabled-password --gecos "" "$ADMIN_USER"
    ok "Foydalanuvchi ${ADMIN_USER} yaratildi (parolsiz, faqat SSH kalit)"
fi

run usermod -aG sudo "$ADMIN_USER"
ok "${ADMIN_USER} sudo guruhiga qo'shildi"

# SSH kalitini root'dan ko'chirish (cloud image'larda kalit root'da bo'ladi).
ADMIN_HOME="$(getent passwd "$ADMIN_USER" | cut -d: -f6)"
ADMIN_KEYS="${ADMIN_HOME}/.ssh/authorized_keys"
if [[ -s "$ADMIN_KEYS" ]]; then
    skip "authorized_keys allaqachon mavjud: ${ADMIN_KEYS}"
elif [[ -s /root/.ssh/authorized_keys ]]; then
    run install -d -m 700 -o "$ADMIN_USER" -g "$ADMIN_USER" "${ADMIN_HOME}/.ssh"
    run install -m 600 -o "$ADMIN_USER" -g "$ADMIN_USER" \
        /root/.ssh/authorized_keys "$ADMIN_KEYS"
    ok "SSH kalit root'dan ${ADMIN_USER} ga ko'chirildi"
else
    warn "SSH kalit topilmadi! SSH hardening'dan OLDIN kalit qo'shing:
            ssh-copy-id -p 22 ${ADMIN_USER}@<SERVER_IP>
          Aks holda serverga kira olmay qolasiz."
fi

# =============================================================================
step "SSH hardening (key-only, root login off, port ${SSH_PORT})"
# =============================================================================
# UBUNTU 24.04 MUHIM XUSUSIYATI:
#   sshd socket-activation (ssh.socket) bilan ishga tushadi. Bu holda
#   sshd_config ichidagi `Port` direktivasi E'TIBORGA OLINMAYDI — portni
#   ssh.socket override'ida berish kerak.
SSH_SOCKET_ACTIVE="no"
if systemctl is-enabled ssh.socket >/dev/null 2>&1; then
    SSH_SOCKET_ACTIVE="yes"
fi
info "ssh.socket (socket activation): ${SSH_SOCKET_ACTIVE}"

if [[ ! -s "$ADMIN_KEYS" && "$ASSUME_YES" != "yes" ]]; then
    warn "authorized_keys bo'sh — SSH hardening O'TKAZIB YUBORILDI (o'zingizni qulflab qo'ymaslik uchun)"
elif confirm "SSH sozlamalari o'zgartirilsinmi? (parol bilan kirish O'CHADI, port -> ${SSH_PORT}). JORIY SESSIYANI YOPMANG!"; then

    if write_file /etc/ssh/sshd_config.d/99-zinnur.conf 0644 <<EOF
# ZIN-NUR v2 — SSH hardening. server-setup.sh tomonidan boshqariladi.
# Ubuntu'da bu katalog asosiy sshd_config'dan OLDIN o'qiladi (Include).

# Faqat SSH kalit. Parol bilan brute-force butunlay yopiladi.
PasswordAuthentication no
KbdInteractiveAuthentication no
ChallengeResponseAuthentication no
PubkeyAuthentication yes
PermitEmptyPasswords no

# root bilan to'g'ridan-to'g'ri kirish yo'q — audit izi yo'qoladi.
PermitRootLogin no

# Faqat shu foydalanuvchi.
AllowUsers ${ADMIN_USER}

# Port. DIQQAT: ssh.socket faol bo'lsa bu satr E'TIBORGA OLINMAYDI,
# port ssh.socket override'idan olinadi (pastda).
Port ${SSH_PORT}

# Sessiya gigiyenasi
MaxAuthTries 3
LoginGraceTime 20
ClientAliveInterval 300
ClientAliveCountMax 2
X11Forwarding no
AllowAgentForwarding no
AllowTcpForwarding yes
EOF
    then ok "/etc/ssh/sshd_config.d/99-zinnur.conf yozildi"
    else skip "sshd_config drop-in o'zgarmadi"
    fi

    if [[ "$SSH_SOCKET_ACTIVE" == "yes" ]]; then
        run mkdir -p /etc/systemd/system/ssh.socket.d
        if write_file /etc/systemd/system/ssh.socket.d/override.conf 0644 <<EOF
# Ubuntu 24.04: sshd socket-activation bilan ishlaydi.
# Bo'sh ListenStream= — meros qilib olingan ro'yxatni TOZALAYDI,
# keyingi satr yangi portni belgilaydi.
[Socket]
ListenStream=
ListenStream=${SSH_PORT}
EOF
        then ok "ssh.socket override yozildi (port ${SSH_PORT})"
        else skip "ssh.socket override o'zgarmadi"
        fi
    fi

    # Konfiguratsiyani QAYTA YUKLASHDAN OLDIN tekshirish.
    if [[ "$DRY_RUN" != "yes" ]]; then
        if sshd -t; then
            run systemctl daemon-reload
            # UFW qoidasi hali qo'shilmagan bo'lishi mumkin — oldindan ochamiz.
            run ufw allow "${SSH_PORT}/tcp" comment 'SSH' >/dev/null 2>&1 || true
            if [[ "$SSH_SOCKET_ACTIVE" == "yes" ]]; then
                run systemctl restart ssh.socket
            else
                run systemctl restart ssh
            fi
            ok "SSH qayta ishga tushirildi. HOZIR yangi terminalda tekshiring: ssh -p ${SSH_PORT} ${ADMIN_USER}@<SERVER_IP>"
        else
            die "sshd -t xato berdi. SSH qayta ishga tushirilmadi (bu sizni himoya qildi). Konfiguratsiyani tuzating."
        fi
    fi
else
    skip "SSH hardening o'tkazib yuborildi"
fi

# =============================================================================
step "unattended-upgrades (faqat xavfsizlik yangilanishlari)"
# =============================================================================
if write_file /etc/apt/apt.conf.d/20auto-upgrades 0644 <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
APT::Periodic::AutocleanInterval "7";
EOF
then ok "20auto-upgrades yozildi"; else skip "20auto-upgrades o'zgarmadi"; fi

if write_file /etc/apt/apt.conf.d/52unattended-upgrades-zinnur 0644 <<'EOF'
// ZIN-NUR v2 — faqat security yangilanishlari avtomatik o'rnatiladi.
Unattended-Upgrade::Allowed-Origins {
    "${distro_id}:${distro_codename}-security";
    "${distro_id}ESMApps:${distro_codename}-apps-security";
    "${distro_id}ESM:${distro_codename}-infra-security";
};

// Docker paketlarini avtomatik yangilamaymiz: dockerd restart => barcha
// konteynerlar (jonli dars ham!) uziladi. Docker'ni qo'lda, oyna vaqtida.
Unattended-Upgrade::Package-Blacklist {
    "docker-ce";
    "docker-ce-cli";
    "containerd.io";
    "docker-compose-plugin";
    "docker-buildx-plugin";
};

// Avtomatik reboot O'CHIRILGAN. Video platformada reboot = dars uzilishi.
// Reboot kerakligini `/var/run/reboot-required` faylidan tekshiring va
// qo'lda, dars bo'lmagan vaqtda qiling.
Unattended-Upgrade::Automatic-Reboot "false";

Unattended-Upgrade::Remove-Unused-Kernel-Packages "true";
Unattended-Upgrade::Remove-Unused-Dependencies "true";
EOF
then ok "52unattended-upgrades-zinnur yozildi (auto-reboot OFF, docker blacklist)"
else skip "unattended-upgrades konfiguratsiyasi o'zgarmadi"
fi
run systemctl enable --now unattended-upgrades >/dev/null 2>&1 || true

# =============================================================================
step "Swap"
# =============================================================================
# Tavsiya (16 GB RAM): 4 GB swap. Swap "qo'shimcha RAM" emas — u OOM killer
# postgres/livekit'ni o'ldirishidan oldin sizga SSH bilan kirib, muammoni
# hal qilish uchun bir necha soniya beradigan "xavfsizlik yostig'i".
if [[ "$SWAP_SIZE_GB" == "auto" ]]; then
    if   (( RAM_MB <= 4096  )); then SWAP_SIZE_GB=2
    elif (( RAM_MB <= 8192  )); then SWAP_SIZE_GB=4
    elif (( RAM_MB <= 32768 )); then SWAP_SIZE_GB=4
    else                             SWAP_SIZE_GB=8
    fi
    info "SWAP_SIZE_GB=auto -> ${SWAP_SIZE_GB} GB (RAM ${RAM_MB} MB uchun)"
fi

CUR_SWAP_KB="$(awk '/SwapTotal/ {print $2}' /proc/meminfo)"
if (( CUR_SWAP_KB > 0 )); then
    skip "Swap allaqachon faol: $(( CUR_SWAP_KB / 1024 )) MB"
elif (( SWAP_SIZE_GB == 0 )); then
    skip "SWAP_SIZE_GB=0 — swap yaratilmadi"
elif confirm "/swapfile (${SWAP_SIZE_GB} GB) yaratilsinmi? Diskda shuncha joy band bo'ladi."; then
    if [[ -f /swapfile ]]; then
        warn "/swapfile mavjud, lekin faol emas — qo'lda tekshiring."
    else
        run fallocate -l "${SWAP_SIZE_GB}G" /swapfile
        run chmod 600 /swapfile
        run mkswap /swapfile >/dev/null
        run swapon /swapfile
        if ! grep -q '^/swapfile' /etc/fstab; then
            run bash -c "printf '/swapfile none swap sw 0 0\n' >> /etc/fstab"
        fi
        ok "Swap ${SWAP_SIZE_GB} GB yaratildi va /etc/fstab ga qo'shildi"
    fi
else
    skip "Swap yaratish o'tkazib yuborildi"
fi

# =============================================================================
step "Docker CE (rasmiy repozitoriy)"
# =============================================================================
# NEGA `apt install docker.io` EMAS:
#   `docker.io` — Ubuntu'ning o'z paketi, odatda bir necha versiya orqada,
#   `docker compose` (v2 plugin) va yangi buildx bilan kelmaydi. Rasmiy
#   Docker repo esa joriy stabil versiyani va compose plugin'ini beradi.
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    skip "Docker + compose plugin allaqachon o'rnatilgan: $(docker --version | sed 's/,.*//')"
elif confirm "Docker CE rasmiy repodan o'rnatilsinmi? (eski docker.io/docker-compose paketlari olib tashlanadi)"; then
    for pkg in docker.io docker-doc docker-compose docker-compose-v2 podman-docker containerd runc; do
        run apt-get remove -y -qq "$pkg" >/dev/null 2>&1 || true
    done
    run install -m 0755 -d /etc/apt/keyrings
    if [[ ! -f /etc/apt/keyrings/docker.asc ]]; then
        run bash -c 'curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc'
        run chmod a+r /etc/apt/keyrings/docker.asc
    fi
    CODENAME="$(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")"
    ARCH="$(dpkg --print-architecture)"
    if write_file /etc/apt/sources.list.d/docker.list 0644 <<EOF
deb [arch=${ARCH} signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${CODENAME} stable
EOF
    then info "docker.list yangilandi"; fi
    run apt-get update -qq
    run apt-get install -y -qq \
        docker-ce docker-ce-cli containerd.io \
        docker-buildx-plugin docker-compose-plugin >/dev/null
    ok "Docker CE o'rnatildi"
else
    skip "Docker o'rnatish o'tkazib yuborildi"
fi

if getent group docker >/dev/null 2>&1 && id -u "$ADMIN_USER" >/dev/null 2>&1; then
    if id -nG "$ADMIN_USER" | tr ' ' '\n' | grep -qx docker; then
        skip "${ADMIN_USER} allaqachon docker guruhida"
    else
        run usermod -aG docker "$ADMIN_USER"
        ok "${ADMIN_USER} docker guruhiga qo'shildi (qayta login qiling)"
        warn "XAVFSIZLIK: docker guruhi = amalda root huquqi. Faqat ishonchli foydalanuvchini qo'shing."
    fi
fi

# =============================================================================
step "/etc/docker/daemon.json — log rotation va ulimits"
# =============================================================================
# NEGA BU KRITIK:
#   Docker'ning default `json-file` drayveri loglarni CHEKSIZ yozadi. Bitta
#   gapiruvchan konteyner (LiveKit debug log, .NET request log) bir necha
#   haftada /var/lib/docker/containers/... ni o'nlab GB ga to'ldiradi.
#   Disk to'lganda: postgres yozolmaydi, docker yangi konteyner ochmaydi,
#   ssh ham kirmasligi mumkin. Bu server o'limining eng keng tarqalgan,
#   eng "jimgina" sababi.
DAEMON_CHANGED="no"
if write_file /etc/docker/daemon.json 0644 <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "20m",
    "max-file": "5",
    "compress": "true"
  },
  "default-ulimits": {
    "nofile": { "Name": "nofile", "Soft": 65535, "Hard": 262144 },
    "nproc":  { "Name": "nproc",  "Soft": 32768, "Hard": 65536  }
  },
  "live-restore": true,
  "userland-proxy": false,
  "default-address-pools": [
    { "base": "172.30.0.0/16", "size": 24 }
  ]
}
EOF
then DAEMON_CHANGED="yes"; ok "daemon.json yozildi (log: 20m x 5 = konteynerga maks 100 MB)"
else skip "daemon.json o'zgarmadi"
fi

# =============================================================================
step "systemd override: dockerd LimitNOFILE"
# =============================================================================
# Shell'dagi `ulimit -n` systemd xizmatlariga ta'sir qilmaydi. dockerd
# systemd tomonidan ishga tushadi va o'z chegarasini systemd'dan oladi.
run mkdir -p /etc/systemd/system/docker.service.d
if write_file /etc/systemd/system/docker.service.d/override.conf 0644 <<'EOF'
# ZIN-NUR v2 — dockerd resurs chegaralari.
# Konteynerlar ichidagi chegara daemon.json -> default-ulimits dan keladi,
# bu esa dockerd'ning O'ZIGA tegishli.
[Service]
LimitNOFILE=1048576
LimitNPROC=infinity
LimitCORE=infinity
TasksMax=infinity
EOF
then DAEMON_CHANGED="yes"; ok "docker.service override yozildi (LimitNOFILE=1048576)"
else skip "docker.service override o'zgarmadi"
fi

if [[ "$DAEMON_CHANGED" == "yes" ]]; then
    if confirm "Docker qayta ishga tushirilsinmi? (live-restore yoqilgani uchun konteynerlar ishlab turadi, lekin TARMOQ sozlamasi o'zgargani sababli ularni keyin qayta yaratish kerak)"; then
        run systemctl daemon-reload
        run systemctl restart docker
        ok "Docker qayta ishga tushirildi"
    else
        warn "daemon.json/override o'zgardi, lekin Docker qayta ishga tushirilmadi. Keyin: sudo systemctl daemon-reload && sudo systemctl restart docker"
    fi
fi

# =============================================================================
step "Kernel tuning: ${SYSCTL_DST}"
# =============================================================================
if [[ ! -f "$SYSCTL_SRC" ]]; then
    warn "sysctl-zinnur.conf topilmadi: ${SYSCTL_SRC} — kernel tuning o'tkazib yuborildi"
else
    if [[ "$DRY_RUN" == "yes" ]]; then
        info "[dry-run] ${SYSCTL_SRC} -> ${SYSCTL_DST}"
    elif cmp -s "$SYSCTL_SRC" "$SYSCTL_DST"; then
        skip "sysctl drop-in allaqachon dolzarb"
    else
        run install -m 0644 "$SYSCTL_SRC" "$SYSCTL_DST"
        ok "${SYSCTL_DST} yangilandi"
    fi
    # conntrack va bbr modullari — sysctl kalitlari mavjud bo'lishi uchun.
    run modprobe nf_conntrack 2>/dev/null || true
    run modprobe tcp_bbr 2>/dev/null || true
    if write_file /etc/modules-load.d/zinnur.conf 0644 <<'EOF'
nf_conntrack
tcp_bbr
EOF
    then info "modules-load.d/zinnur.conf yozildi"; fi

    if [[ "$DRY_RUN" != "yes" ]]; then
        sysctl --system >/dev/null 2>&1 || warn "sysctl --system ba'zi kalitlarda ogohlantirish berdi (yuqoridagi chiqishga qarang)"
        RMEM="$(sysctl -n net.core.rmem_max)"
        if [[ "$RMEM" == "16777216" ]]; then
            ok "net.core.rmem_max = ${RMEM} (LiveKit UDP uchun eng muhim qiymat)"
        else
            warn "net.core.rmem_max = ${RMEM} — kutilgan 16777216. Boshqa sysctl fayli ustidan yozgan bo'lishi mumkin: grep -r rmem_max /etc/sysctl*"
        fi
    fi
fi

# =============================================================================
step "/etc/security/limits.d/99-zinnur.conf"
# =============================================================================
# DIQQAT: bu fayl FAQAT PAM orqali kiruvchi sessiyalarga (ssh login, su)
# ta'sir qiladi. systemd xizmatlariga ham, Docker konteynerlariga ham
# TA'SIR QILMAYDI. Konteynerlar uchun daemon.json -> default-ulimits.
if write_file /etc/security/limits.d/99-zinnur.conf 0644 <<'EOF'
# ZIN-NUR v2 — foydalanuvchi sessiyalari uchun chegaralar.
# Faqat interaktiv login (PAM) uchun. Konteynerlar uchun EMAS.
*     soft  nofile  65535
*     hard  nofile  262144
root  soft  nofile  65535
root  hard  nofile  262144
*     soft  nproc   32768
*     hard  nproc   65536
EOF
then ok "limits.d/99-zinnur.conf yozildi (yangi login sessiyasidan kuchga kiradi)"
else skip "limits.d/99-zinnur.conf o'zgarmadi"
fi

# =============================================================================
step "UFW — firewall"
# =============================================================================
if confirm "UFW qoidalari qo'llanilsinmi? DIQQAT: MAVJUD UFW QOIDALARI TOZALANADI (ufw --force reset), keyin quyidagi qoidalar qayta yoziladi. Qo'lda qo'shgan qoidalaringiz bo'lsa — avval 'ufw status numbered' bilan saqlab oling."; then
    run ufw --force reset >/dev/null 2>&1 || true
    run ufw default deny incoming  >/dev/null
    run ufw default allow outgoing >/dev/null

    # --- SSH (birinchi bo'lib! aks holda o'zingizni qulflaysiz) ---
    run ufw limit "${SSH_PORT}/tcp" comment 'SSH (rate-limited)' >/dev/null
    if [[ "$SSH_PORT" != "22" ]]; then
        # Eski portni ham vaqtincha ochiq qoldiramiz — yangi port ishlaganiga
        # ishonch hosil qilgach qo'lda yoping: sudo ufw delete allow 22/tcp
        run ufw allow 22/tcp comment 'SSH (eski port - tekshirgach O CHIRING)' >/dev/null
        warn "22/tcp vaqtincha ochiq qoldirildi. ${SSH_PORT} ishlaganiga ishonch hosil qilib: sudo ufw delete allow 22/tcp"
    fi

    # --- HTTP / HTTPS ---
    run ufw allow 80/tcp  comment 'HTTP - redirect + ACME http-01' >/dev/null
    run ufw allow 443/tcp comment 'HTTPS - app.domen.uz + livekit.domen.uz (WSS)' >/dev/null

    # --- LiveKit ---
    if [[ "$EXPOSE_LIVEKIT_7880" == "yes" ]]; then
        run ufw allow 7880/tcp comment 'LiveKit WS/API (TLSsiz - faqat nginx yo q topologiyada)' >/dev/null
        warn "7880/tcp ochildi. Bu shifrlanmagan WS — brauzer HTTPS sahifadan unga ulanmaydi. nginx orqali WSS tavsiya etiladi."
    else
        info "7880/tcp OCHILMADI (tavsiya). Host nginx 443 -> 127.0.0.1:7880 proxy qiladi."
    fi
    run ufw allow 7881/tcp comment 'LiveKit ICE/TCP fallback' >/dev/null
    run ufw allow 7882/udp comment 'LiveKit RTC media (UDP mux)' >/dev/null

    run ufw --force enable >/dev/null
    ok "UFW yoqildi"
    [[ "$DRY_RUN" != "yes" ]] && ufw status numbered | sed 's/^/     /'
else
    skip "UFW sozlash o'tkazib yuborildi"
fi

# =============================================================================
step "DOCKER-USER — Docker'ning UFW'ni chetlab o'tishiga qarshi"
# =============================================================================
# MUAMMO: `docker run -p 5432:5432` iptables'ning `nat`/`DOCKER` zanjiriga
# DNAT qoidasi qo'shadi. Bu qoida UFW'ning FILTER qoidalaridan OLDIN
# ishlaydi — natijada `ufw deny 5432` bo'lsa ham port BUTUN INTERNETGA ochiq
# bo'ladi. Bu Docker'ning ma'lum va hujjatlashtirilgan xatti-harakati.
#
# YECHIM (ikki qatlam):
#   1) BIRLAMCHI: docker-compose.yml da portlarni 127.0.0.1 ga bog'lash
#      ("127.0.0.1:5080:8080"). Umuman tashqariga chiqmaydi.
#   2) ZAXIRA: DOCKER-USER zanjirida tashqi interfeysdan kelayotgan yangi
#      ulanishlarni bloklash (LiveKit portlaridan tashqari).
if confirm "DOCKER-USER himoya qoidalari o'rnatilsinmi? (WAN=${WAN_IF}; 7881/tcp va 7882/udp ochiq qoladi)"; then
    if write_file /usr/local/sbin/zinnur-docker-firewall.sh 0755 <<EOF
#!/usr/bin/env bash
# ZIN-NUR v2 — DOCKER-USER zanjiri. server-setup.sh tomonidan yaratilgan.
# Docker port publishing UFW'ni chetlab o'tadi; bu skript uni to'sadi.
#
# Usul: o'z zanjirimiz (ZINNUR-DOCKER) yaratiladi, har safar TOZALANADI va
# qayta to'ldiriladi — shuning uchun bu skript idempotent. DOCKER-USER ga
# faqat BITTA jump qo'shiladi (mavjud bo'lmasa).
set -euo pipefail
WAN="\$(ip -4 route show default | awk '/default/ {print \$5; exit}')"
[[ -n "\$WAN" ]] || WAN="${WAN_IF}"

iptables -N ZINNUR-DOCKER 2>/dev/null || true
iptables -F ZINNUR-DOCKER

# Tartib muhim: RETURN (ruxsat) qoidalari DROP'dan OLDIN turishi kerak.
# RETURN => DOCKER-USER ga qaytadi => Docker o'z marshrutini davom ettiradi.
iptables -A ZINNUR-DOCKER -i "\$WAN" -p udp --dport 7882 -j RETURN   # LiveKit media
iptables -A ZINNUR-DOCKER -i "\$WAN" -p tcp --dport 7881 -j RETURN   # LiveKit ICE/TCP
# Qolgan hamma YANGI ulanish tashqi interfeysdan konteynerga kira olmaydi.
# ESTABLISHED/RELATED tegilmaydi => konteynerlarning chiquvchi trafigi ishlaydi.
iptables -A ZINNUR-DOCKER -i "\$WAN" -m conntrack --ctstate NEW -j DROP

# Jump faqat bir marta qo'shiladi.
iptables -C DOCKER-USER -j ZINNUR-DOCKER 2>/dev/null \\
    || iptables -I DOCKER-USER 1 -j ZINNUR-DOCKER
EOF
    then ok "/usr/local/sbin/zinnur-docker-firewall.sh yozildi"; fi

    if write_file /etc/systemd/system/zinnur-docker-firewall.service 0644 <<'EOF'
[Unit]
Description=ZIN-NUR DOCKER-USER firewall rules
After=docker.service
Requires=docker.service
PartOf=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/usr/local/sbin/zinnur-docker-firewall.sh

[Install]
WantedBy=multi-user.target
EOF
    then ok "zinnur-docker-firewall.service yozildi"; fi

    if [[ "$DRY_RUN" != "yes" ]]; then
        run systemctl daemon-reload
        run systemctl enable zinnur-docker-firewall.service >/dev/null 2>&1 || true
        if systemctl start zinnur-docker-firewall.service 2>/dev/null; then
            ok "DOCKER-USER qoidalari qo'llandi (docker restart'da avtomatik qayta qo'llanadi)"
        else
            warn "DOCKER-USER qoidalari qo'llanmadi (Docker hali o'rnatilmagan bo'lishi mumkin). Keyin: sudo systemctl start zinnur-docker-firewall"
        fi
    fi
else
    skip "DOCKER-USER himoyasi o'tkazib yuborildi — compose'da portlarni 127.0.0.1 ga bog'lash MAJBURIY"
fi

# =============================================================================
step "Kataloglar"
# =============================================================================
run install -d -m 0755 "$APP_DIR"
if id -u "$ADMIN_USER" >/dev/null 2>&1; then
    run chown "${ADMIN_USER}:${ADMIN_USER}" "$APP_DIR"
fi
run install -d -m 0750 "$BACKUP_DIR"
run install -d -m 0755 /etc/letsencrypt
run touch /var/log/zinnur-backup.log
run chmod 0640 /var/log/zinnur-backup.log
ok "Kataloglar tayyor: ${APP_DIR}, ${BACKUP_DIR}"

if write_file /etc/logrotate.d/zinnur 0644 <<'EOF'
/var/log/zinnur-backup.log {
    weekly
    rotate 8
    compress
    delaycompress
    missingok
    notifempty
    create 0640 root root
}
EOF
then ok "logrotate: /var/log/zinnur-backup.log"; else skip "logrotate o'zgarmadi"; fi

# =============================================================================
# YAKUNIY HISOBOT
# =============================================================================
printf '\n%s================================================================%s\n' "$C_BLD" "$C_OFF"
printf '%s HISOBOT%s\n' "$C_BLD" "$C_OFF"
printf '%s================================================================%s\n' "$C_BLD" "$C_OFF"
for line in "${SUMMARY[@]}"; do
    case "$line" in
        OK*)   printf '  %s%s%s\n' "$C_GRN" "$line" "$C_OFF" ;;
        WARN*) printf '  %s%s%s\n' "$C_YLW" "$line" "$C_OFF" ;;
        *)     printf '  %s\n' "$line" ;;
    esac
done

cat <<EOF

${C_BLD}KEYINGI QADAMLAR${C_OFF}
  1. YANGI terminal ochib SSH'ni tekshiring (eski sessiyani yopmang!):
         ssh -p ${SSH_PORT} ${ADMIN_USER}@<SERVER_IP>
     Ishlagach eski portni yoping:
         sudo ufw delete allow 22/tcp
  2. Docker guruhi kuchga kirishi uchun ${ADMIN_USER} qayta login qilsin.
  3. Loyihani joylashtiring:  ${APP_DIR}
  4. TLS: docs/DEPLOY_UBUNTU.md — 4-bo'lim (certbot, app.domen.uz + livekit.domen.uz)
  5. Backup cron:
         echo '15 3 * * * ${APP_DIR}/infra/scripts/backup-db.sh' | sudo tee /etc/cron.d/zinnur-backup

${C_BLD}TEKSHIRISH${C_OFF}
  sysctl net.core.rmem_max net.core.somaxconn fs.file-max
  systemctl show docker -p LimitNOFILE
  docker run --rm alpine sh -c 'ulimit -n'
  ufw status numbered
  sudo iptables -S DOCKER-USER
  timedatectl

EOF

exit 0
