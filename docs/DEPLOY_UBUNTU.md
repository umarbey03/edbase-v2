# ZIN-NUR v2 — Ubuntu 24.04 LTS production deploy va server tuning

> **Kimga:** kuchli dasturchi, lekin Linux sysadmin emas.
> Shuning uchun har bir buyruq ostida **nega** shundayligi yozilgan.
> Buyruqlarni ko'r-ko'rona nusxa ko'chirmang — avval "nega" qismini o'qing.
>
> **Mo'ljal:** bitta Ubuntu 24.04 LTS serveri, **8 vCPU / 16 GB RAM**,
> **200+ bir vaqtdagi o'quvchi**, bir nechta parallel jonli dars,
> **self-hosted LiveKit** (LiveKit Cloud EMAS), hammasi Docker Compose'da.
>
> **Shartnoma:** portlar va xizmat nomlari [`docs/SPEC.md`](./SPEC.md) 8-bo'limidan
> olingan va **o'zgarmaydi**.

---

## Mundarija

| # | Bo'lim |
|---|---|
| 0 | [Nimalar kerak — oldindan tekshirish](#0-nimalar-kerak--oldindan-tekshirish) |
| 1 | [Serverni tayyorlash](#1-serverni-tayyorlash) |
| 2 | [Docker o'rnatish](#2-docker-ornatish) |
| 3 | [Firewall (UFW) va Docker muammosi](#3-firewall-ufw-va-docker-muammosi) |
| 4 | [TLS — Let's Encrypt](#4-tls--lets-encrypt) |
| 5 | [Kernel / OS tuning (eng muhim bo'lim)](#5-kernel--os-tuning-eng-muhim-bolim) |
| 6 | [Docker resurs limitlari](#6-docker-resurs-limitlari) |
| 7 | [Operatsiyalar: deploy, rollback, log, backup](#7-operatsiyalar-deploy-rollback-log-backup) |
| 8 | [Sig'im (capacity) — halol tahlil](#8-sigim-capacity--halol-tahlil) |
| 9 | [compose / nginx / livekit egasi uchun talablar](#9-compose--nginx--livekit-egasi-uchun-talablar) |
| 10 | [Xatolarni bartaraf etish](#10-xatolarni-bartaraf-etish) |
| A | [SPEC bo'yicha risklar](#ilova-a--spec-boyicha-risklar) |

**Bu qo'llanmaga tegishli fayllar:**

```
infra/scripts/server-setup.sh        # bootstrap (idempotent)
infra/scripts/sysctl-zinnur.conf     # kernel tuning drop-in
infra/scripts/backup-db.sh           # tungi pg_dump + retention
```

**Boshqa agent egalik qiladigan fayllar** (bu yerda faqat yo'l bo'yicha
havola qilinadi, mazmuni yozilmaydi):

```
docker-compose.yml
backend/Dockerfile , frontend/Dockerfile
infra/nginx/zinnur.conf              # host nginx (TLS termination)
infra/livekit/livekit.yaml
infra/postgres/postgresql.conf
```

---

## 0. Nimalar kerak — oldindan tekshirish

| Talab | Minimum | Tavsiya (200 foydalanuvchi) | Nima uchun |
|---|---|---|---|
| CPU | 4 vCPU | **8 vCPU** | LiveKit paketlarni uzatadi + SRTP shifrlash |
| RAM | 8 GB | **16 GB** | postgres + redis + .NET + LiveKit + page cache |
| Disk | 60 GB SSD | **160 GB NVMe** | DB, backuplar, docker image'lar, loglar |
| Tarmoq | 1 Gbps | **1 Gbps, cheklanmagan trafik** | 8-bo'limga qarang — oyiga **4-15 TB** chiqadi |
| OS | Ubuntu 24.04 LTS | — | skriptlar shu versiyaga sozlangan |
| Domen | `app.domen.uz`, `livekit.domen.uz` | — | ikkalasi ham server IP'ga `A` yozuv |

**Trafik cheklovini ALDAMASDAN tekshiring.** Ko'p VPS provayderlar "1 Gbps port"
beradi, lekin oyiga 2-5 TB dan keyin tezlikni pasaytiradi yoki pul oladi.
200 o'quvchi 360p'da kuniga 4 soat dars = **oyiga ~4.3 TB**. Hisob 8-bo'limda.

DNS to'g'ri sozlanganini tekshiring (TLS'dan **oldin**):

```bash
dig +short app.domen.uz
dig +short livekit.domen.uz
curl -s ifconfig.me; echo            # serverning tashqi IP'si
```

Uchala natija bir xil IP bo'lishi kerak.

---

## 1. Serverni tayyorlash

Quyidagi qadamlarning hammasi `infra/scripts/server-setup.sh` da avtomatlashtirilgan.
Avval **qo'lda nima bo'layotganini tushunib oling**, keyin skriptni ishlating.

```bash
# Loyihani serverga olib keling
sudo mkdir -p /opt/zinnur && sudo chown "$USER" /opt/zinnur
git clone <repo-url> /opt/zinnur
cd /opt/zinnur

# Avval nima o'zgarishini KO'RING (hech narsa o'zgartirmaydi)
sudo ./infra/scripts/server-setup.sh --dry-run

# Keyin haqiqiy ishga tushiring
sudo ADMIN_USER=zinnur SSH_PORT=2222 TIMEZONE=Asia/Tashkent \
     ./infra/scripts/server-setup.sh
```

Skript **idempotent** — xohlagancha qayta ishga tushirsangiz bo'ladi.
Xavfli qadamlar (SSH o'zgartirish, swap yaratish, UFW reset, Docker restart)
oldidan **tasdiq so'raydi**.

### 1.1. Tizimni yangilash

```bash
sudo apt-get update && sudo apt-get upgrade -y
```

### 1.2. Non-root sudo foydalanuvchi

**Nega:** `root` bilan ishlash — har bir xato qaytarib bo'lmaydigan bo'lishi
demakdir. Bundan tashqari, kim nima qilganini `sudo` loglaridan ko'rib
bo'lmaydi. Bitta oddiy foydalanuvchi + `sudo` = xatolar oldini olish + audit izi.

```bash
sudo adduser --disabled-password --gecos "" zinnur
sudo usermod -aG sudo zinnur

# SSH kalitni ko'chirish (server'da root kalit bilan kirgan bo'lsangiz)
sudo install -d -m 700 -o zinnur -g zinnur /home/zinnur/.ssh
sudo install -m 600 -o zinnur -g zinnur \
     /root/.ssh/authorized_keys /home/zinnur/.ssh/authorized_keys
```

`--disabled-password` — foydalanuvchida **parol umuman yo'q**. Faqat SSH kalit
bilan kiriladi. Bu brute-force hujumini butunlay yo'q qiladi.

### 1.3. SSH hardening

Uchta o'zgarish: **parol bilan kirish o'chadi**, **root kira olmaydi**,
**port o'zgaradi**.

> **Port o'zgartirish nima beradi?** Xavfsizlik emas — 22-port doim
> avtomatik skanerlar nishonida bo'ladi va loglar minglab "invalid user"
> yozuvlari bilan to'ladi. Port o'zgarishi shu shovqinni ~99% kamaytiradi.
> Asosiy himoya — bu **parolni butunlay o'chirish**.

```bash
sudo tee /etc/ssh/sshd_config.d/99-zinnur.conf >/dev/null <<'EOF'
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes
PermitRootLogin no
AllowUsers zinnur
Port 2222
MaxAuthTries 3
LoginGraceTime 20
ClientAliveInterval 300
ClientAliveCountMax 2
X11Forwarding no
EOF
```

#### ⚠️ Ubuntu 24.04'ning katta tuzog'i: `ssh.socket`

Ubuntu 24.04'da sshd **socket activation** orqali ishga tushadi. Bu holda
`sshd_config` ichidagi `Port` direktivasi **butunlay e'tiborga olinmaydi** —
portni `systemd` soketida berish kerak. Buni bilmagan odam "port o'zgarmadi,
nega?" deb soatlab qidiradi.

```bash
# Socket activation yoqilganmi?
systemctl is-enabled ssh.socket     # "enabled" bo'lsa — ha

sudo mkdir -p /etc/systemd/system/ssh.socket.d
sudo tee /etc/systemd/system/ssh.socket.d/override.conf >/dev/null <<'EOF'
[Socket]
ListenStream=
ListenStream=2222
EOF
```

Birinchi bo'sh `ListenStream=` — meros qilib olingan portlar ro'yxatini
**tozalaydi** (bo'lmasa 22 ham ochiq qoladi).

```bash
sudo sshd -t                        # SINTAKSISNI TEKSHIRING (majburiy!)
sudo ufw allow 2222/tcp             # portni OLDIN oching
sudo systemctl daemon-reload
sudo systemctl restart ssh.socket   # yoki: systemctl restart ssh
```

> 🔴 **JORIY SSH SESSIYANGIZNI YOPMANG.** Yangi terminal oching va
> `ssh -p 2222 zinnur@<IP>` bilan kiring. **Faqat kirgandan keyin** eski
> sessiyani yoping va `sudo ufw delete allow 22/tcp` qiling.
> Aks holda o'zingizni serverdan qulflab qo'yishingiz mumkin.

Tekshirish:

```bash
sudo ss -tlnp | grep -E 'ssh|:2222'
```

### 1.4. Xavfsizlik yangilanishlari (unattended-upgrades)

```bash
sudo apt-get install -y unattended-upgrades
sudo tee /etc/apt/apt.conf.d/20auto-upgrades >/dev/null <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
```

Ikkita muhim qaror:

```bash
sudo tee /etc/apt/apt.conf.d/52unattended-upgrades-zinnur >/dev/null <<'EOF'
Unattended-Upgrade::Allowed-Origins {
    "${distro_id}:${distro_codename}-security";
};
// 1) Docker paketlari avtomatik yangilanmaydi
Unattended-Upgrade::Package-Blacklist {
    "docker-ce"; "docker-ce-cli"; "containerd.io";
    "docker-compose-plugin"; "docker-buildx-plugin";
};
// 2) Avtomatik reboot O'CHIQ
Unattended-Upgrade::Automatic-Reboot "false";
EOF
```

* **Nega Docker blacklist'da?** `docker-ce` yangilanishi `dockerd` ni qayta
  ishga tushiradi. Bu **hamma konteynerni** — shu jumladan jonli darsni —
  uzadi. Docker'ni faqat qo'lda, dars bo'lmagan vaqtda yangilang.
* **Nega avtomatik reboot yo'q?** Kernel yangilanishi kechasi soat 6:00 da
  serverni qayta yuklashi mumkin. Video platformada bu qabul qilib bo'lmas.
  Reboot kerakligini o'zingiz tekshiring:

```bash
ls /var/run/reboot-required 2>/dev/null && echo "REBOOT KERAK"
cat /var/run/reboot-required.pkgs 2>/dev/null
```

### 1.5. Vaqt zonasi va NTP

```bash
sudo timedatectl set-timezone Asia/Tashkent
sudo timedatectl set-ntp true
timedatectl                                # Tekshirish
```

**Nega bu kritik:** JWT (SPEC 7-bo'lim) `nbf` va `exp` claim'lariga tayanadi.
Server soati bir necha daqiqaga siljisa, LiveKit token'lari "not yet valid"
yoki "expired" bo'lib qoladi va **hech kim darsga kira olmaydi**. NTP
sinxronizatsiyasi shart.

### 1.6. Swap — qancha va nega

**Swap "qo'shimcha RAM" EMAS.** Diskda ishlaydigan xotira RAM'dan ~1000 marta
sekin. Agar LiveKit yoki Postgres jiddiy swap'ga tushsa — audio uziladi,
so'rovlar sekinlashadi. Swap'ning haqiqiy vazifasi: **xotira to'satdan
tugaganda kernel'ning OOM killer'i Postgres'ni o'ldirishidan oldin sizga
serverga kirib muammoni hal qilish uchun bir necha soniya berish.**

| RAM | Tavsiya etilgan swap | Sabab |
|---|---|---|
| 8 GB | 4 GB | Kichik server, bufer kerakroq |
| **16 GB** | **4 GB** | Yostiq sifatida yetarli, ko'pi keraksiz |
| 32 GB+ | 8 GB | — |

```bash
sudo fallocate -l 4G /swapfile
sudo chmod 600 /swapfile              # ⚠️ MAJBURIY: xotira nusxasi hammaga o'qilmasin
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

`vm.swappiness = 10` qiymati `sysctl-zinnur.conf` da (5-bo'lim) — kernel
imkon qadar RAM'da ushlab tursin.

```bash
swapon --show
free -h
```

---

## 2. Docker o'rnatish

### 2.1. Nega `apt install docker.io` EMAS

| | `docker.io` (Ubuntu paketi) | `docker-ce` (rasmiy repo) |
|---|---|---|
| Versiya | Odatda bir necha reliz orqada | Joriy stabil |
| `docker compose` (v2 plugin) | Yo'q / alohida eski `docker-compose` | Bor (`docker-compose-plugin`) |
| `buildx` | Yo'q yoki eski | Bor |
| Yangilanish | Ubuntu relizi bilan bog'langan | Docker chiqarganda |

Bu loyihada `docker compose` (v2, plugin) **majburiy** — eski `docker-compose`
(Python, defis bilan) ba'zi compose spetsifikatsiya xususiyatlarini
qo'llab-quvvatlamaydi.

### 2.2. O'rnatish

```bash
# Eski/mos kelmaydigan paketlarni olib tashlash
for p in docker.io docker-doc docker-compose docker-compose-v2 podman-docker containerd runc; do
    sudo apt-get remove -y "$p" 2>/dev/null || true
done

# Docker GPG kaliti
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
     -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc

# Repozitoriy
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
| sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io \
                        docker-buildx-plugin docker-compose-plugin
```

Tekshirish:

```bash
docker --version
docker compose version          # "Docker Compose version v2.x.x"
sudo docker run --rm hello-world
```

### 2.3. `docker` guruhi

```bash
sudo usermod -aG docker zinnur
# Kuchga kirishi uchun QAYTA LOGIN qiling (yoki: newgrp docker)
```

> ⚠️ **`docker` guruhi = amalda root huquqi.** Bu guruhdagi foydalanuvchi
> `docker run -v /:/host` bilan butun fayl tizimini o'zgartira oladi.
> Faqat ishonchli operator akkauntini qo'shing.

### 2.4. `/etc/docker/daemon.json` — bu bo'limni O'TKAZIB YUBORMANG

**Docker sukut bo'yicha konteyner loglarini CHEKSIZ yozadi.** Rotatsiya yo'q.
Bitta gapiruvchan konteyner (LiveKit'ning debug logi, .NET'ning har bir
so'rov logi) bir necha hafta ichida `/var/lib/docker/containers/…` ni o'nlab
gigabaytga to'ldiradi.

Disk 100% to'lganda nima bo'ladi:

* Postgres yozolmaydi → API 500 qaytaradi
* Docker yangi konteyner ocha olmaydi
* `journald` yozolmaydi → nima bo'lganini bilib ham bo'lmaydi
* Ba'zan SSH ham kirmaydi (sessiya fayllari yozilmaydi)

Bu **server o'limining eng keng tarqalgan va eng "jimgina" sababi.**

```bash
sudo mkdir -p /etc/docker
sudo tee /etc/docker/daemon.json >/dev/null <<'EOF'
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
```

Har bir kalit nima qiladi:

| Kalit | Ma'nosi | Nega |
|---|---|---|
| `log-opts.max-size: 20m` | Bitta log fayli 20 MB | Rotatsiya chegarasi |
| `log-opts.max-file: 5` | 5 ta fayl saqlanadi | **Konteynerga maksimum 100 MB.** 5 ta xizmat = 500 MB shift |
| `log-opts.compress` | Eski loglar gzip | Diskda ~10x kam joy |
| `default-ulimits.nofile` | **Konteynerlar** ichidagi FD chegarasi | `ulimit -n` ni shell'da oshirish konteynerga ta'sir qilmaydi (5.4-bo'limga qarang) |
| `live-restore: true` | `dockerd` restart bo'lganda konteynerlar ishlashda davom etadi | Docker'ni yangilash darsni uzmasin |
| `userland-proxy: false` | Port publishing `docker-proxy` jarayoni orqali emas, iptables DNAT orqali | **UDP media uchun muhim** — har bir paket userland'ga chiqib qaytmaydi |
| `default-address-pools` | Docker tarmoqlari uchun IP diapazoni | Ofis/VPN tarmog'i `172.17/16` bilan to'qnashmasin |

Qo'llash (**konteynerlar qayta ishga tushadi — dars vaqtida qilmang**):

```bash
sudo systemctl daemon-reload
sudo systemctl restart docker
```

Tekshirish:

```bash
docker info | grep -Ei 'logging driver|native overlay'
docker run --rm alpine sh -c 'ulimit -n'        # -> 65535
# Ishlab turgan konteynerning log sozlamasi:
docker inspect -f '{{json .HostConfig.LogConfig}}' \
  "$(docker compose ps -q api)" | jq .
```

---

## 3. Firewall (UFW) va Docker muammosi

### 3.1. Port jadvali — to'liq

| Port | Proto | Nima uchun | Ommaviy? | Izoh |
|---|---|---|---|---|
| **22** | tcp | SSH (standart) | Ha → keyin **yoping** | Yangi port ishlagach `ufw delete allow 22/tcp` |
| **2222** | tcp | SSH (o'zgartirilgan) | Ha (`limit` bilan) | `ufw limit` = brute-force rate-limit |
| **80** | tcp | HTTP → 443 redirect + ACME `http-01` | **Ha** | Let's Encrypt yangilash uchun ochiq turishi SHART |
| **443** | tcp | HTTPS: `app.domen.uz` (Vue + API + SignalR WSS) va `livekit.domen.uz` (LiveKit WSS) | **Ha** | Host nginx TLS'ni shu yerda tugatadi |
| **7880** | tcp | LiveKit WS / HTTP API | **YO'Q** (tavsiya) | nginx `443 → 127.0.0.1:7880` proxy qiladi. Faqat nginx'siz topologiyada oching |
| **7881** | tcp | LiveKit **ICE/TCP fallback** | **Ha** | UDP bloklangan tarmoqlarda (korporativ, ba'zi mobil operatorlar) yagona yo'l |
| **7882** | udp | LiveKit **RTC media (UDP mux)** | **Ha** | **Barcha audio/video shu bitta portdan.** Yopilsa video umuman ishlamaydi |
| 5432 | tcp | PostgreSQL | **HECH QACHON** | Faqat `zinnur-net` ichida |
| 6379 | tcp | Redis | **HECH QACHON** | Faqat `zinnur-net` ichida |
| 8080 | tcp | `api` konteyner ichki porti | Yo'q | nginx `127.0.0.1` orqali |
| 80 (`web`) | tcp | `web` konteyner ichki porti | Yo'q | nginx `127.0.0.1` orqali |
| 5440 / 6390 / 5080 / 5173 | tcp | SPEC'dagi **dev** host portlari | **Yo'q** | Prod'da umuman map qilinmaydi yoki `127.0.0.1:` ga bog'lanadi |

> **`postgres` va `redis` nima uchun hech qachon ochilmaydi?**
> Ikkalasi ham sukut bo'yicha kuchsiz himoyalangan: Postgres'da parol bor,
> lekin internetdan kelgan brute-force'ni hech nima to'xtatmaydi; Redis'da esa
> parol umuman bo'lmasligi mumkin va `CONFIG SET dir` + `SAVE` kombinatsiyasi
> orqali serverga fayl yozib, to'liq nazoratni olish mumkin. Internetga ochiq
> Redis — soatlar ichida buziladi. Ular **faqat** `zinnur-net` bridge
> tarmog'ida, konteyner nomi orqali (`postgres:5432`, `redis:6379`) ishlaydi —
> host portiga umuman ehtiyoj yo'q.

### 3.2. UFW buyruqlari

> ⚠️ `ufw --force reset` **mavjud barcha qoidalarni o'chiradi.** Avval
> `sudo ufw status numbered` bilan borini saqlab oling.

```bash
sudo ufw --force reset
sudo ufw default deny incoming
sudo ufw default allow outgoing

# 1) SSH — ENG BIRINCHI (aks holda o'zingizni qulflaysiz)
sudo ufw limit 2222/tcp comment 'SSH (rate-limited)'
sudo ufw allow 22/tcp   comment 'SSH eski port - tekshirgach OCHIRING'

# 2) Web
sudo ufw allow 80/tcp   comment 'HTTP redirect + ACME http-01'
sudo ufw allow 443/tcp  comment 'HTTPS app.domen.uz + livekit.domen.uz'

# 3) LiveKit
sudo ufw allow 7881/tcp comment 'LiveKit ICE/TCP fallback'
sudo ufw allow 7882/udp comment 'LiveKit RTC media (UDP mux)'
# 7880 OCHILMAYDI — nginx 443 -> 127.0.0.1:7880

sudo ufw enable
sudo ufw status numbered
```

`ufw limit` va `ufw allow` farqi: `limit` — bitta IP'dan 30 soniyada 6 tadan
ortiq ulanish bo'lsa bloklaydi. SSH uchun ideal, HTTPS uchun **yaramaydi**
(200 o'quvchi bir vaqtda ulanadi).

### 3.3. 🔴 Docker UFW'ni CHETLAB O'TADI — buni bilishingiz shart

Bu Docker'ning ma'lum, hujjatlashtirilgan xatti-harakati va juda ko'p
serverning buzilishiga sabab bo'lgan.

**Nima bo'ladi:** `docker-compose.yml` da `ports: ["5432:5432"]` yozsangiz,
Docker `iptables` ning **`nat` jadvali**ga `DNAT` qoidasi qo'shadi. Paketlar
`nat/PREROUTING` → `filter/FORWARD` yo'lidan o'tadi. UFW esa o'z qoidalarini
`filter/INPUT` zanjiriga yozadi. Ya'ni:

```
sudo ufw deny 5432          ✅ qoida bor
docker run -p 5432:5432     ❌ port BUTUN INTERNETGA OCHIQ
```

`ufw status` sizga "5432 DENY" deb ko'rsatadi, lekin port **haqiqatda ochiq**.
Tashqaridan tekshirsangiz (`nmap -p 5432 <IP>`) — `open` chiqadi.

#### Yechim 1 (BIRLAMCHI, eng ishonchli): portni `127.0.0.1` ga bog'lash

`docker-compose.yml` da (bu fayl boshqa agentda — unga shu talabni bering):

```yaml
ports:
  - "127.0.0.1:5080:8080"     # api
  - "127.0.0.1:5173:80"       # web
  # postgres / redis: prod'da `ports` UMUMAN yozilmaydi
```

`127.0.0.1:` prefiksi bilan Docker DNAT qoidasini faqat loopback uchun
yaratadi — tashqi interfeysdan hech kim yeta olmaydi. **Firewall'ga bog'liq
emas, shuning uchun ishonchli.**

#### Yechim 2 (ZAXIRA qatlam): `DOCKER-USER` zanjiri

Docker `filter` jadvalida `DOCKER-USER` degan **bo'sh zanjir** qoldiradi va
uni o'z qoidalaridan **oldin** tekshiradi. Bu — foydalanuvchi qoidalari uchun
maxsus joy; Docker uni hech qachon o'chirmaydi.

`server-setup.sh` shu skriptni yaratadi (`/usr/local/sbin/zinnur-docker-firewall.sh`):

```bash
WAN="$(ip -4 route show default | awk '/default/ {print $5; exit}')"

iptables -N ZINNUR-DOCKER 2>/dev/null || true
iptables -F ZINNUR-DOCKER

# LiveKit portlari ochiq qoladi
iptables -A ZINNUR-DOCKER -i "$WAN" -p udp --dport 7882 -j RETURN
iptables -A ZINNUR-DOCKER -i "$WAN" -p tcp --dport 7881 -j RETURN
# Qolgan hamma YANGI ulanish tashqi interfeysdan konteynerga kira olmaydi
iptables -A ZINNUR-DOCKER -i "$WAN" -m conntrack --ctstate NEW -j DROP

iptables -C DOCKER-USER -j ZINNUR-DOCKER 2>/dev/null \
  || iptables -I DOCKER-USER 1 -j ZINNUR-DOCKER
```

Nozik nuqtalar:

* `--ctstate NEW` faqat **yangi** ulanishni to'sadi. `ESTABLISHED,RELATED`
  tegilmaydi — shuning uchun konteynerlarning **chiquvchi** trafigi
  (apt, NuGet, npm, DNS) buzilmaydi.
* `-i "$WAN"` — faqat tashqi interfeysdan. Konteynerlararo trafik
  (`api → postgres`) `br-*` interfeysida bo'ladi, tegilmaydi.
* `RETURN` — `DOCKER-USER` ga qaytaradi, keyin Docker o'z marshrutini
  davom ettiradi (ya'ni "ruxsat").
* Qoidalar **reboot'da yo'qoladi**, shuning uchun `systemd` unit orqali
  `docker.service` dan keyin qayta qo'llanadi:

```bash
sudo systemctl status zinnur-docker-firewall
sudo systemctl restart zinnur-docker-firewall     # qo'lda qayta qo'llash
```

#### Tekshirish (majburiy)

Ichkaridan emas, **tashqaridan** tekshiring — boshqa mashinadan:

```bash
# Ochiq bo'lishi KERAK
nc -zv <SERVER_IP> 443 && echo "443 OK"
nc -zvu <SERVER_IP> 7882                        # UDP — javob har doim aniq emas

# YOPIQ bo'lishi KERAK (hech qanday javob bo'lmasin)
nc -zv -w 3 <SERVER_IP> 5432 && echo "🔴 XAVF: postgres ochiq!"
nc -zv -w 3 <SERVER_IP> 6379 && echo "🔴 XAVF: redis ochiq!"
nc -zv -w 3 <SERVER_IP> 5080 && echo "🔴 XAVF: api to'g'ridan-to'g'ri ochiq!"
```

Serverda:

```bash
sudo iptables -S DOCKER-USER
sudo iptables -S ZINNUR-DOCKER
sudo ss -tlnp | grep -v '127.0.0.1'     # tashqariga eshitayotgan portlar
```

> **IPv6:** agar serveringizda IPv6 bo'lsa va Docker'da IPv6 yoqilmagan bo'lsa
> (sukut bo'yicha o'chiq), konteynerlar IPv6'dan yetib bo'lmaydi — muammo yo'q.
> IPv6'ni yoqsangiz, yuqoridagi qoidalarni `ip6tables` uchun ham takrorlang va
> `sudo ufw status` da IPv6 qoidalari borligini tekshiring.

---

## 4. TLS — Let's Encrypt

### 4.1. Nega LiveKit'ga prod'da WSS majburiy

SPEC 8-bo'limida dev uchun `LiveKit__Url=ws://livekit:7880` yozilgan.
**Prod'da bu ishlamaydi** va sabab kod emas, brauzer:

Brauzerlar **mixed content** qoidasini qo'llaydi. `https://app.domen.uz`
sahifasidan `ws://…` (shifrlanmagan WebSocket) ochish **bloklanadi** —
Chrome/Firefox konsolda `SecurityError: insecure WebSocket connection may not
be initiated from a page loaded over HTTPS` yozadi va ulanish umuman
boshlanmaydi. Buni JS bilan ham, LiveKit sozlamasi bilan ham aylanib
o'tib bo'lmaydi.

Demak:

| | Dev | Prod |
|---|---|---|
| Sahifa | `http://localhost:5173` | `https://app.domen.uz` |
| SignalR hub | `ws://localhost:5080/hubs/live` | `wss://app.domen.uz/hubs/live` |
| LiveKit `ServerUrl` | `ws://localhost:7880` | **`wss://livekit.domen.uz`** |

`LiveKitJoinDto.ServerUrl` (SPEC 5-bo'lim) klientga **`wss://`** bilan
qaytishi shart. Media (UDP 7882) esa shifrlashni **DTLS-SRTP** orqali o'zi
qiladi va TLS'ga bog'liq emas — ya'ni sertifikat faqat signalizatsiya
(WebSocket) uchun kerak.

### 4.2. certbot o'rnatish

Ikki yo'l bor. **snap** — EFF tavsiya qiladigan usul, doim eng yangi versiya
va o'zi yangilanadi:

```bash
sudo snap install core && sudo snap refresh core
sudo snap install --classic certbot
sudo ln -sf /snap/bin/certbot /usr/bin/certbot
```

**apt** — snap ishlatmoqchi bo'lmasangiz (masalan minimal image'da):

```bash
sudo apt-get install -y certbot
```

Ikkalasi ham ishlaydi. Farqi: apt versiyasi Ubuntu relizi bilan bog'langan
(eskiroq bo'lishi mumkin), snap versiyasi doim joriy.

### 4.3. Sertifikat olish — `--webroot` usuli

> **Nega `--nginx` plugin emas?** `certbot --nginx` sizning nginx
> konfiguratsiyangizni **avtomatik tahrirlaydi**. `infra/nginx/zinnur.conf`
> boshqa agent tomonidan boshqariladi va git'da turadi — certbot uni
> o'zgartirsa, keyingi deploy'da o'zgarish yo'qoladi yoki konflikt bo'ladi.
> `--webroot` esa hech qanday konfiguratsiyani tegmaydi, faqat fayl yozadi.

Nginx'da (boshqa agent fayli) quyidagi blok bo'lishi kerak — bu talabni
unga bering:

```nginx
# 80-portda, HAR IKKALA domen uchun:
location ^~ /.well-known/acme-challenge/ {
    root /var/www/certbot;
    default_type "text/plain";
}
# qolgan hamma narsa https ga redirect
```

Katalogni yarating va nginx ishlab turganiga ishonch hosil qiling:

```bash
sudo mkdir -p /var/www/certbot
sudo chown -R www-data:www-data /var/www/certbot
sudo nginx -t && sudo systemctl reload nginx
```

Ikkita **alohida** sertifikat oling:

```bash
sudo certbot certonly --webroot -w /var/www/certbot \
     -d app.domen.uz \
     --email admin@domen.uz --agree-tos --no-eff-email

sudo certbot certonly --webroot -w /var/www/certbot \
     -d livekit.domen.uz \
     --email admin@domen.uz --agree-tos --no-eff-email
```

**Nega alohida, bitta ko'p-SAN sertifikat emas?** Kelajakda LiveKit'ni
alohida serverga ko'chirish ehtimoli yuqori (8-bo'limga qarang). Alohida
sertifikat bo'lsa — shunchaki fayllarni ko'chirasiz. Bitta umumiy sertifikat
bo'lsa — qayta chiqarish kerak bo'ladi.

> ℹ️ Avval `--dry-run` bilan sinab ko'ring. Let's Encrypt'ning **rate limit**'i
> bor (bir domen uchun haftasiga cheklangan miqdorda sertifikat). Sozlashda
> xato qilib bir necha marta urinsangiz, bir haftaga bloklanib qolishingiz
> mumkin:
> ```bash
> sudo certbot certonly --webroot -w /var/www/certbot -d app.domen.uz --dry-run
> ```

Fayllar shu yerda paydo bo'ladi:

```
/etc/letsencrypt/live/app.domen.uz/fullchain.pem
/etc/letsencrypt/live/app.domen.uz/privkey.pem
/etc/letsencrypt/live/livekit.domen.uz/fullchain.pem
/etc/letsencrypt/live/livekit.domen.uz/privkey.pem
```

`live/` ichidagilar — **symlink**. Nginx konfiguratsiyasida aynan shu
yo'llarni ko'rsating (`archive/` emas), shunda yangilanishdan keyin fayl
o'zgarmaydi.

### 4.4. Avtomatik yangilash + deploy hook

Sertifikat 90 kun amal qiladi; certbot 30 kun qolganda yangilaydi.
Yangilangan sertifikatni nginx **avtomatik olmaydi** — uni `reload` qilish
kerak. Buning uchun **deploy hook**:

```bash
sudo mkdir -p /etc/letsencrypt/renewal-hooks/deploy
sudo tee /etc/letsencrypt/renewal-hooks/deploy/00-reload-nginx.sh >/dev/null <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
# Konfiguratsiya buzuq bo'lsa reload QILMAYMIZ — ishlab turgan nginx yiqilmasin
if nginx -t 2>/dev/null; then
    systemctl reload nginx
    logger -t certbot-hook "nginx reloaded after cert renewal"
else
    logger -t certbot-hook "ERROR: nginx -t failed, reload skipped"
    exit 1
fi
EOF
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/00-reload-nginx.sh
```

`renewal-hooks/deploy/` ichidagi skriptlar **faqat sertifikat haqiqatan
yangilanganda** ishlaydi (`pre`/`post` esa har urinishda). Shuning uchun
nginx keraksiz reload bo'lmaydi.

Tekshirish:

```bash
sudo certbot renew --dry-run            # to'liq simulyatsiya, hook ham ishlaydi
systemctl list-timers | grep -i certbot # taymer bormi?
sudo certbot certificates               # muddat va yo'llar
```

`--dry-run` "Congratulations, all simulated renewals succeeded" desa — tayyor.

### 4.5. Tekshirish

```bash
curl -sSI https://app.domen.uz | head -3
curl -sSI https://livekit.domen.uz | head -3

# TLS zanjiri va muddat
echo | openssl s_client -connect app.domen.uz:443 -servername app.domen.uz 2>/dev/null \
  | openssl x509 -noout -dates -subject -issuer

# WebSocket upgrade ishlayaptimi (101 kutiladi)
curl -sS -o /dev/null -w '%{http_code}\n' \
  -H "Connection: Upgrade" -H "Upgrade: websocket" \
  -H "Sec-WebSocket-Key: $(openssl rand -base64 16)" \
  -H "Sec-WebSocket-Version: 13" \
  https://livekit.domen.uz/rtc
```

---

## 5. Kernel / OS tuning (eng muhim bo'lim)

Bu bo'lim 200 foydalanuvchi maqsadiga **eng ko'p ta'sir qiladigan** qism.
Tuning qilinmagan Ubuntu'da LiveKit ~50-100 foydalanuvchidan keyin paket
yo'qota boshlaydi va video "sinadi" — CPU va RAM esa bo'sh turadi.
Ya'ni muammo resursda emas, **kernel sozlamasida**.

### 5.1. Fayl: `infra/scripts/sysctl-zinnur.conf`

To'liq mazmuni va har bir qiymatning izohi shu faylda. Bu yerda **eng
muhimlarini** qisqacha ko'rib chiqamiz.

#### 5.1.1. UDP buferlari — №1 muammo

```conf
net.core.rmem_max     = 16777216      # 16 MB
net.core.wmem_max     = 16777216
net.core.rmem_default = 1048576       # 1 MB
net.core.wmem_default = 1048576
```

**Nega bu eng muhim:** SPEC 8-bo'limi LiveKit'ni **UDP mux** rejimida
ishlatadi — ya'ni 200 o'quvchining **hamma** audio/video paketlari
**bitta** UDP soketdan (port 7882) o'tadi.

Bu soketning kernel'dagi qabul navbati (`receive queue`) chegaralangan.
Ubuntu'da `net.core.rmem_max` sukut bo'yicha **~208 KB**. Paket oqimi
tez kelib, LiveKit ularni o'qib ulgurmasa, navbat to'ladi va kernel
paketni **jimgina tashlab yuboradi**. Hech qanday xato yo'q, faqat:

* video kadri "sinadi", bloklarga bo'linadi
* ovoz uziladi, "robot" bo'lib eshitiladi
* qayta ulanishlar ko'payadi
* LiveKit logida: `failed to sufficiently increase receive buffer size`

`*_max` — bu shunchaki **shift**. Xotira faqat dastur `setsockopt(SO_RCVBUF)`
bilan so'raganda ajratiladi. LiveKit (Pion) aynan shuni qiladi, shuning uchun
16 MB qo'yish **xavfsiz** — bo'sh turgan serverda hech narsa sarflanmaydi.

`*_default` esa **har bir** soketning boshlang'ich hajmi — barcha jarayonlarga
(postgres, nginx, ssh) ta'sir qiladi. Shuning uchun uni 16 MB qilib
qo'ymaymiz; 1 MB — xavfsiz oraliq.

#### 5.1.2. Ulanish navbatlari

```conf
net.core.somaxconn          = 4096
net.ipv4.tcp_max_syn_backlog = 8192
net.core.netdev_max_backlog  = 16384
```

Dars boshlanganda 200 o'quvchi **10-20 soniya ichida** ulanadi: TLS
handshake → WebSocket upgrade → SignalR → LiveKit signaling. Bu
"thundering herd". Navbat kichik bo'lsa kernel SYN paketini tashlaydi,
brauzer 1-3 soniya kutib qayta uradi — foydalanuvchi uchun bu "sayt
ochilmayapti".

* `somaxconn` — `accept()` navbati. nginx/Kestrel `listen(backlog)` ni shundan oladi.
* `tcp_max_syn_backlog` — yarim ochiq (SYN_RECV) ulanishlar.
* `netdev_max_backlog` — NIC'dan kelgan, hali CPU qayta ishlamagan paketlar.
  LiveKit **juda ko'p kichik paket** yuboradi (paket/sekund yuqori), shuning
  uchun default 1000 kam.

#### 5.1.3. Portlar va TIME_WAIT

```conf
net.ipv4.ip_local_port_range = 10240 65535
net.ipv4.tcp_fin_timeout     = 15
net.ipv4.tcp_tw_reuse        = 1
```

Host nginx har bir so'rovni konteynerga proxy qiladi va har bir proxy
ulanishi bitta **ephemeral port** yeydi. Port tugasa — `Cannot assign
requested address`. Diapazonni kengaytiramiz (~55k port).

`tcp_tw_reuse=1` — `TIME_WAIT` holatidagi portni **chiquvchi** ulanish uchun
qayta ishlatishga ruxsat. Timestamp'lar bilan xavfsiz.

> 🔴 **`tcp_tw_recycle` NI HECH QACHON QO'YMANG.** Internetdagi eski
> maqolalarda u tavsiya qilinadi. U NAT ortidagi klientlarni buzadi
> (bir IP ortidagi bir necha foydalanuvchi ulana olmay qoladi) va
> zamonaviy kernellardan **butunlay olib tashlangan**.

#### 5.1.4. Fayl deskriptorlari

```conf
fs.file-max = 2097152
fs.nr_open  = 1048576
```

Har bir soket = 1 fayl deskriptor. 200 o'quvchi × (SignalR WebSocket +
LiveKit) + postgres pool + redis + nginx = oson **20-40 mingdan** oshadi.
Chegara past bo'lsa `too many open files` xatosi keladi va konteyner
**yangi ulanish qabul qilmay qo'yadi** — eski ulanishlar esa ishlab
turaveradi, shuning uchun muammoni tashxislash qiyin.

### 5.2. Qo'llash

```bash
sudo cp /opt/zinnur/infra/scripts/sysctl-zinnur.conf /etc/sysctl.d/99-zinnur.conf
sudo modprobe nf_conntrack       # conntrack kalitlari mavjud bo'lishi uchun
sudo modprobe tcp_bbr
sudo sysctl --system
```

`sysctl --system` `/etc/sysctl.d/` ichidagi hamma faylni **raqam tartibida**
o'qiydi. `99-` prefiksi — bizniki oxirgi bo'lib qo'llanadi, ya'ni ustunlik
bizda.

> Bu o'zgarishlar **darhol** kuchga kiradi va reboot talab qilmaydi. Lekin
> **allaqachon ochiq** soketlarga ta'sir qilmaydi — shuning uchun qo'llagandan
> keyin konteynerlarni qayta ishga tushiring:
> `docker compose restart livekit`

### 5.3. TEKSHIRISH — har bir qiymat haqiqatan qo'llandimi

Bu jadval eng muhim qism. **"Yozdim" ≠ "ishladi".** Boshqa `sysctl` fayli
(masalan cloud-provider'niki) sizning qiymatingizni ustidan yozgan bo'lishi
mumkin.

| Nima | Buyruq | Kutilgan natija |
|---|---|---|
| UDP receive buffer | `sysctl net.core.rmem_max` | `16777216` |
| UDP send buffer | `sysctl net.core.wmem_max` | `16777216` |
| Default buferlar | `sysctl net.core.rmem_default net.core.wmem_default` | `1048576` |
| Accept navbati | `sysctl net.core.somaxconn` | `4096` |
| SYN navbati | `sysctl net.ipv4.tcp_max_syn_backlog` | `8192` |
| NIC navbati | `sysctl net.core.netdev_max_backlog` | `16384` |
| Port diapazoni | `sysctl net.ipv4.ip_local_port_range` | `10240 65535` |
| FIN timeout | `sysctl net.ipv4.tcp_fin_timeout` | `15` |
| TW reuse | `sysctl net.ipv4.tcp_tw_reuse` | `1` |
| Tizim FD chegarasi | `sysctl fs.file-max` | `2097152` |
| Congestion control | `sysctl net.ipv4.tcp_congestion_control` | `bbr` |
| Swappiness | `sysctl vm.swappiness` | `10` |
| Conntrack shift | `cat /proc/sys/net/netfilter/nf_conntrack_max` | `262144` |

**Kim ustidan yozganini topish:**

```bash
grep -rn 'rmem_max\|somaxconn' /etc/sysctl.conf /etc/sysctl.d/ /usr/lib/sysctl.d/ 2>/dev/null
```

**Haqiqiy ta'sirni o'lchash** (raqamni emas, natijani):

```bash
# 1) LiveKit bufer haqida shikoyat qilyaptimi?
docker compose logs livekit 2>&1 | grep -i 'buffer'
#    Hech nima chiqmasa — YAXSHI.

# 2) UDP paket yo'qotishlari (bu raqamlar O'SMASLIGI kerak)
netstat -su | grep -Ei 'receive buffer errors|packet receive errors'
#    Dars davomida 2 marta olib, farqini solishtiring.

# 3) NIC darajasidagi tashlab yuborishlar (2-ustun 0 bo'lishi kerak)
awk '{print "cpu"NR-1": dropped="$2}' /proc/net/softnet_stat

# 4) Conntrack to'lib ketyaptimi?
echo "count=$(cat /proc/sys/net/netfilter/nf_conntrack_count) \
max=$(cat /proc/sys/net/netfilter/nf_conntrack_max)"
#    count max'ning 60% dan oshmasin.

# 5) Kernel loglarida to'lish belgilari
sudo dmesg -T | grep -Ei 'conntrack|nf_conntrack|out of memory|TCP: drop'
```

### 5.4. ulimit — uchta ALOHIDA joy (eng ko'p adashiladigan nuqta)

> 🔴 **Shell'da `ulimit -n 65535` yozish konteynerlarga UMUMAN ta'sir
> qilmaydi.** Bu eng keng tarqalgan xato. Sabab: konteyner jarayonlari
> sizning shell'ingizdan emas, `dockerd`/`containerd` dan tug'iladi va
> chegaralarni **ulardan** meros qilib oladi.

Uchta bir-biriga bog'liq bo'lmagan joy bor:

| # | Joy | Kimga ta'sir qiladi | Kimga ta'sir QILMAYDI |
|---|---|---|---|
| 1 | `/etc/security/limits.d/99-zinnur.conf` | PAM orqali kiruvchi sessiyalar (`ssh`, `su`) | systemd xizmatlari, **konteynerlar** |
| 2 | `/etc/systemd/system/docker.service.d/override.conf` | `dockerd` jarayonining o'zi | Konteyner **ichidagi** jarayonlar |
| 3 | `/etc/docker/daemon.json` → `default-ulimits` | **Konteynerlar ichidagi jarayonlar** | Host jarayonlari |

#### 1) Login sessiyalari

```bash
sudo tee /etc/security/limits.d/99-zinnur.conf >/dev/null <<'EOF'
*     soft  nofile  65535
*     hard  nofile  262144
root  soft  nofile  65535
root  hard  nofile  262144
*     soft  nproc   32768
*     hard  nproc   65536
EOF
```

Kuchga kirishi uchun **chiqib qayta kiring** (yangi PAM sessiyasi kerak).

#### 2) `dockerd` ning o'zi

```bash
sudo mkdir -p /etc/systemd/system/docker.service.d
sudo tee /etc/systemd/system/docker.service.d/override.conf >/dev/null <<'EOF'
[Service]
LimitNOFILE=1048576
LimitNPROC=infinity
LimitCORE=infinity
TasksMax=infinity
EOF
sudo systemctl daemon-reload
sudo systemctl restart docker
```

#### 3) Konteynerlar (asosiysi)

`/etc/docker/daemon.json` dagi `default-ulimits` (2.4-bo'limda yozilgan).
Alternativ: har bir xizmat uchun `docker-compose.yml` da `ulimits:` bloki —
lekin `daemon.json` global bo'lgani uchun ishonchliroq.

#### TEKSHIRISH

```bash
# dockerd chegarasi
systemctl show docker -p LimitNOFILE
#   -> LimitNOFILE=1048576

# YANGI konteynerlarning default chegarasi
docker run --rm alpine sh -c 'ulimit -n; ulimit -Hn'
#   -> 65535 / 262144

# ISHLAB TURGAN konteyner (eng ishonchli tekshiruv)
PID=$(docker inspect -f '{{.State.Pid}}' "$(docker compose ps -q livekit)")
sudo cat /proc/$PID/limits | grep -E 'Max open files|Max processes'
#   Max open files   65535   262144   files

# Konteyner qancha FD ishlatyapti (chegaraga yaqinmi?)
sudo ls /proc/$PID/fd | wc -l
```

Oxirgi buyruq — **haqiqiy foydalanish**. Agar u chegaraning 70% iga
yaqinlashsa, chegarani oshirish yoki ulanishlar sonini kamaytirish kerak.

### 5.5. Transparent Huge Pages (ixtiyoriy, lekin tavsiya etiladi)

Redis THP yoqilganda ishga tushishda ogohlantirish yozadi; Postgres uchun ham
THP kutilmagan latency sakrashlariga sabab bo'ladi.

```bash
sudo tee /etc/systemd/system/disable-thp.service >/dev/null <<'EOF'
[Unit]
Description=Disable Transparent Huge Pages
DefaultDependencies=no
After=sysinit.target local-fs.target
Before=docker.service

[Service]
Type=oneshot
ExecStart=/bin/sh -c 'echo never > /sys/kernel/mm/transparent_hugepage/enabled'
RemainAfterExit=yes

[Install]
WantedBy=basic.target
EOF
sudo systemctl daemon-reload
sudo systemctl enable --now disable-thp

# Tekshirish (kvadrat qavs [never] da bo'lsin)
cat /sys/kernel/mm/transparent_hugepage/enabled
```

---

## 6. Docker resurs limitlari

**Mo'ljal:** 8 vCPU / 16 GB RAM, 200 bir vaqtdagi foydalanuvchi.

### 6.1. Umumiy taqsimot

| Xizmat | `cpus` | `mem_limit` | `mem_reservation` | Asosiy sozlama |
|---|---|---|---|---|
| `livekit` | **6.0** (yumshoq) | `2g` | `512m` | CPU headroom eng muhim |
| `livekit-egress` | `3.0` | `3g` | `512m` | ~2 parallel yozuv; Chrome + ffmpeg |
| `api` | `2.0` | `3g` | `512m` | `DOTNET_gcServer=1` |
| `postgres` | `2.0` | `3g` | `1g` | `shared_buffers=768MB` |
| `redis` | `0.5` | `1g` | `256m` | `maxmemory 768mb` |
| `web` (nginx) | `0.5` | `256m` | `64m` | statik fayllar |
| — host nginx | — | ~`200m` | — | TLS termination |
| — OS + page cache | — | **~6 GB qoladi** | — | Postgres uchun kritik |

**Limitlar yig'indisi ≈ 9.25 GB** — 16 GB dan kam. Qolgan ~6 GB **atayin**
bo'sh qoldirilgan: Linux uni **page cache** sifatida ishlatadi va Postgres'ning
haqiqiy tezligi asosan shunga bog'liq (`effective_cache_size` shuni nazarda
tutadi).

**CPU limitlari yig'indisi 11 > 8** — bu **ataylab**. `cpus` — bu *shift*
(ceiling), *rezerv* emas. Hamma xizmat bir vaqtda cho'qqiga chiqmaydi.

> ⚠️ **LiveKit uchun qattiq CPU quota xavfli.** Docker'ning `cpus:` sozlamasi
> CFS **quota** mexanizmini ishlatadi: har 100 ms davrida ajratilgan vaqt
> tugasa, jarayon **davr oxirigacha to'xtatiladi** (throttling). Media
> serverda bu bir necha o'n millisekundlik pauza = **eshitiladigan uzilish**.
> Shuning uchun LiveKit'ga yo umuman limit qo'ymang, yoki juda keng qo'ying
> (6.0), va o'rniga **nisbiy ustunlik** ishlating:
> ```yaml
> # docker-compose.prod.yml — DIQQAT: `cpu_shares` `deploy:` blokining ICHIDA
> # EMAS, xizmatning O'ZIDA turadi (deploy ichiga yozilsa schema xatosi).
> livekit:
>   cpu_shares: ${LIVEKIT_CPU_SHARES:-2048}
> ```
>
> ⚠️ **Ikkita aniqlik (2026-08-22 da o'lchandi, Compose v5.2.0 / Docker
> 29.6.1, cgroup v2):**
> 1. **Boshqa xizmatlarga `cpu_shares: 1024` yozish shart emas** — u
>    Docker'ning standarti: `1024` yozilgan va umuman yozilmagan konteyner
>    bir xil `cpu.weight=100` beradi. Shuning uchun faqat LiveKit'ga
>    yoziladi.
> 2. **2048 "aniq 2 barobar" emas.** cgroup v2 shares'ni `cpu.weight` ga
>    chiziqsiz o'giradi: 512 → 59, 1024 → 100, 2048 → 174 (ya'ni ~1.7x), va
>    koeffitsient runtime versiyasiga bog'liq. Serverda o'zingiz ko'ring:
>    `docker exec $(docker compose ps -q livekit) cat /sys/fs/cgroup/cpu.weight`
>
> `cpu_shares` `deploy.resources` bilan **to'qnashmaydi** — ikkalasi birga
> qo'llanadi (`HostConfig.CpuShares` + `NanoCpus`).
>
> Throttling bo'layotganini tekshirish:
> ```bash
> cat /sys/fs/cgroup/system.slice/docker-$(docker compose ps -q livekit).scope/cpu.stat
> #   throttled_usec o'sib borsa — limit juda tor
> ```
> **Kvotani butunlay o'chirish** kerak bo'lsa qo'llab-quvvatlanadigan yagona
> yo'l — `.env` da `LIVEKIT_CPUS=0`: compose `cpus` ni rendered
> konfiguratsiyaga umuman yubormaydi va konteyner `cpu.max = max` bilan
> ko'tariladi (xotira limiti joyida qoladi). O'zgaruvchini **bo'sh
> qoldirish bu emas** — bo'sh qiymat standart `6.0` ni qaytaradi.

---

#### 🔒 Bu jadval endi CI bilan MAJBURLANADI (2026-08-22 auditi)

Bu jadval uzoq vaqt **faqat hujjatda** yashagan edi. Audit natijasi:
`docker-compose.prod.yml` dagi standartlar unga zid edi —
`livekit` 1.5 CPU / 768M (jadvalda 6.0 / 2g), `api` 1.5 / 1G (jadvalda
2.0 / 3g), `postgres` konteyneri 1G, ichkarisida esa `shared_buffers=2GB`.
Buning ustiga bu o'zgaruvchilarning **birortasi ham `.env.example` da yo'q
edi**, ya'ni operator ularni o'zgartirish mumkinligini ham bilmasdi.

**Nega hech kim sezmadi:** nomuvofiqlik faqat **yuk ostida** chiqadi.
Postgres ishga tushishda yiqilmaydi (`shared_buffers` — mmap qilingan
xotira, cgroup unga sahifa birinchi marta tegilganda haq yozadi), ya'ni
`up -d`, `pg_isready` healthcheck'i va smoke test — hammasi **yashil**.
Portlash haqiqiy dars paytida bo'ladi va o'zini yashiradi: OOM-killer
odatda `postmaster` ni emas, bitta **backend** ni oladi, postgres esa
barcha ulanishlarni uzib crash recovery bilan qaytadi.

Shuning uchun endi darvoza bor:

```bash
./infra/scripts/check-resource-limits.sh -v
```

U **uchta manbani** solishtiradi va farq bo'lsa CI ni qizil qiladi:

| Manba | Roli |
|---|---|
| `docs/DEPLOY_UBUNTU.md` 6.1 / 6.2 jadvallari | **haqiqat manbasi** |
| `docker-compose.prod.yml` dagi `${VAR:-standart}` | qo'llanadigan standart |
| `.env.example` dagi qiymatlar | operator ko'radigan hujjat |

Ustiga mantiqiy invariantlarni ham tekshiradi: `mem_reservation ≤ mem_limit`,
`redis maxmemory < redis konteyner limiti`, `shared_buffers ≤ konteyner
limitining 30% i`, `max_connections > pool + 10`, xotira limitlari yig'indisi
≤ 12 GB, `DOTNET_GCHeapHardLimitPercent` qaytib kelmagani, hamda prod
`command:` bloklarida `max_wal_size` / `min_wal_size` va redis `--save`
saqlanib turgani (bular bir marta **jimgina yo'qolgan** edi — pastdagi
eslatmaga qarang).

> 🔴 **Qiymatni o'zgartirish tartibi:** avval **shu jadvalni** (sabab bilan)
> yangilang, keyin `docker-compose.prod.yml` va `.env.example` ni.
> Teskari tartib CI'ni qizil qiladi — ataylab.

> ⚠️ **`command:` MEROS QOLMAYDI.** Compose overlay'i `command:` ro'yxatini
> **birlashtirmaydi, butunlay almashtiradi**. Ya'ni bazaviy
> `docker-compose.yml` da ataylab qo'yilgan har bir bayroq prod'da
> **jimgina yo'qoladi**. Audit ikkita shunday yo'qolishni topdi:
> `postgres` da `max_wal_size` / `min_wal_size` (prod postgres standarti
> 1GB/80MB bilan ishlagan → dars boshida checkpoint "to'lqini") va `redis`
> da `--save ""` (prod'da standart RDB nuqtalari yoqilgan → `fork()`
> pauzasi, ya'ni backplane va presence bir necha yuz millisekundga
> muzlashi). Ikkalasi ham qaytarildi va CI ular yana yo'qolmasligini
> tekshiradi.

**Operator uchun:** har bir o'zgaruvchining ma'nosi, kichikroq serverga
tushirish uchun **mos to'plamlar jadvali** va deploy'dan keyingi tekshiruv
buyruqlari — `.env.example` ning **10-bo'limida**.

### 6.2. PostgreSQL

Sozlamalar `infra/postgres/postgresql.conf` da (boshqa agent fayli).
Bu yerda **qiymatlar va sabablar**:

| Parametr | Qiymat | Formula / sabab |
|---|---|---|
| `shared_buffers` | `768MB` | **Konteynerga berilgan** xotiraning ~25% (3 GB × 0.25). Host RAM'dan emas! |
| `effective_cache_size` | `6GB` | OS page cache bahosi. **Xotira ajratmaydi**, faqat planner'ga maslahat |
| `work_mem` | `8MB` | Har bir sort/hash **node** uchun. Eng yomon holat: `max_connections × work_mem × node_soni` |
| `maintenance_work_mem` | `256MB` | `VACUUM`, `CREATE INDEX` uchun |
| `max_connections` | `100` | Quyidagi ogohlantirishga qarang |
| `max_wal_size` | `2GB` | Kam checkpoint = kam I/O tishlari |
| `checkpoint_completion_target` | `0.9` | Checkpoint I/O ni vaqtga yoyadi |
| `random_page_cost` | `1.1` | SSD/NVMe uchun (HDD default 4.0) |
| `effective_io_concurrency` | `200` | NVMe parallel I/O |
| `log_min_duration_statement` | `500ms` | Sekin so'rovlarni topish |

> 🔴 **Ulanish pooli tuzog'i.** Npgsql'ning `Maximum Pool Size` sukut
> bo'yicha **100**. SPEC 8-bo'limidagi connection string'da pool sozlamasi
> yo'q. Bitta `api` konteyneri = 100 ta ulanish = `max_connections=100`
> ning **hammasi**. Ikkinchi replika qo'shsangiz — `FATAL: sorry, too many
> clients already`.
>
> Yechim — connection string'ga aniq qiymat qo'shing:
> ```
> ConnectionStrings__Postgres=Host=postgres;Port=5432;Database=zinnur;Username=zinnur;Password=…;Maximum Pool Size=40;Minimum Pool Size=5;Timeout=15;Command Timeout=30
> ```
> **200 o'quvchi ≠ 200 ta DB ulanishi.** So'rovlar millisekundlarda tugaydi;
> 40 ta ulanish 200 foydalanuvchiga yetadi. Ko'proq ulanish — ko'proq
> kontekst almashinuvi, ya'ni **sekinroq**.

Tekshirish:

```bash
docker compose exec -T postgres psql -U zinnur -d zinnur -c "SHOW shared_buffers;"
docker compose exec -T postgres psql -U zinnur -d zinnur \
  -c "SELECT count(*), state FROM pg_stat_activity GROUP BY state;"
# Cache hit ratio — 0.99 dan yuqori bo'lsin
docker compose exec -T postgres psql -U zinnur -d zinnur -c \
  "SELECT sum(blks_hit)::float/nullif(sum(blks_hit+blks_read),0) AS cache_hit FROM pg_stat_database;"
```

### 6.3. Redis

Redis bu loyihada **uch xil** vazifani bajaradi (SPEC 4 va 6-bo'limlar):

1. `ICacheService` — oddiy kesh (yo'qolsa qayta hisoblanadi)
2. `IPresenceService` — darsdagi ishtirokchilar (**funksional ma'lumot**)
3. SignalR backplane + chat rate-limit hisoblagichi

```conf
maxmemory 768mb
maxmemory-policy volatile-lru
appendonly no
save ""
tcp-keepalive 60
```

> 🔴 **`maxmemory-policy allkeys-lru` NI ISHLATMANG.** Ko'p qo'llanmalarda
> shu tavsiya qilinadi, lekin bu yerda u **presence ma'lumotini jimgina
> o'chiradi**. Natija: o'quvchi darsda o'tirgan bo'lsa ham ro'yxatdan
> yo'qoladi, `PresenceChanged` sanog'i noto'g'ri bo'ladi, davomat buziladi.
> Xato hech qayerda ko'rinmaydi — faqat "ba'zan davomat noto'g'ri" degan
> shikoyat keladi.
>
> `volatile-lru` — **faqat TTL qo'yilgan** kalitlarni o'chiradi. Demak:
> kesh kalitlariga TTL qo'ying, presence kalitlariga esa TTL'ni faqat
> heartbeat bilan yangilanadigan qilib qo'ying (yoki umuman qo'ymang).
> Ishonch kerak bo'lsa — `noeviction` qo'ying va xotirani monitoring qiling
> (xotira tugasa yozish xatosi keladi, bu **jim o'chirilishdan yaxshiroq**:
> xatoni ko'rasiz).

`appendonly no` + `save ""` — persistence o'chiq. **Nima yo'qotamiz:** Redis
qayta ishga tushsa presence bo'shab qoladi. **Nima yutamiz:** `BGSAVE`
paytidagi `fork()` va copy-on-write xotira sakrashi yo'q (bu 768 MB
ma'lumotda 1.5 GB gacha ko'tarilishi mumkin). Presence — vaqtinchalik
ma'lumot; klientlar SignalR bilan qayta ulanganda o'zi tiklanadi.
**Agar chat tarixi Redis'da saqlansa — bu qaror noto'g'ri bo'lardi**, lekin
SPEC 6-bo'limi bo'yicha chat Postgres'ga yoziladi.

```bash
docker compose exec -T redis redis-cli INFO memory | grep -E 'used_memory_human|maxmemory_human'
docker compose exec -T redis redis-cli CONFIG GET maxmemory-policy
docker compose exec -T redis redis-cli INFO stats | grep evicted_keys   # 0 bo'lsin
```

### 6.4. `api` (.NET 9)

```yaml
environment:
  DOTNET_gcServer: "1"
  ASPNETCORE_URLS: "http://+:8080"
mem_limit: 3g
cpus: 2.0
```

#### Nega `DOTNET_gcServer=1` muhim

.NET'da ikkita GC rejimi bor:

| | **Workstation GC** | **Server GC** |
|---|---|---|
| Heap | Bitta umumiy heap | **Har bir yadro uchun alohida heap** |
| Ajratish (allocation) | Bir nechta thread bitta heap uchun raqobatlashadi (lock contention) | Har bir thread o'z heap'ida — raqobat yo'q |
| GC | Ko'proq to'xtatadi | Parallel, throughput'ga sozlangan |
| Xotira | Kamroq | **Ko'proq** (har heap alohida) |
| Kimga | Desktop, kichik xizmat | **Ko'p bir vaqtdagi so'rovli API** |

SPEC bo'yicha `api` bir vaqtning o'zida 200 ta SignalR ulanishini, chat
broadcast'ini va `Channel<T>` fon navbatini boshqaradi — bu klassik yuqori
konkurentlikli yuk. Workstation GC'da thread'lar bitta heap uchun navbatga
turadi va **kechikish (latency) sakrab ketadi**.

`DOTNET_gcServer=1` ni **aniq yozing**, SDK default'iga tayanmang — loyiha
tuzilishi yoki base image o'zgarsa default ham o'zgarishi mumkin.

#### Xotira limiti va OOM

.NET **cgroup limitini o'zi o'qiydi** va GC heap chegarasini konteyner
limitining ~75% iga qo'yadi. `mem_limit: 3g` → GC ~2.25 GB gacha o'sadi,
qolgani stack, JIT, native kutubxonalar uchun.

* Limit **juda kichik** bo'lsa (< 512 MB): Server GC doim ishlaydi, CPU
  yonadi, oxiri kernel jarayonni o'ldiradi (`exit code 137`).
* Limit **umuman yo'q** bo'lsa: bitta memory leak butun serverni yiqitadi.

```bash
# OOM bilan o'ldirilganmi?
docker inspect -f '{{.State.OOMKilled}} {{.State.ExitCode}}' "$(docker compose ps -q api)"
#   "true 137" -> xotira limiti kichik yoki leak bor

docker stats --no-stream
```

Kerak bo'lsa GC heap chegarasini aniq boshqarish mumkin
(`DOTNET_GCHeapHardLimitPercent`).

> ⚠️ **Tuzoq:** .NET GC konfiguratsiyasi **muhit o'zgaruvchisi** orqali
> berilganda qiymat **o'n oltilik (hex)** sifatida o'qiladi, `runtimeconfig.json`
> orqali berilganda esa o'nlik. Ya'ni `DOTNET_GCHeapHardLimitPercent=50`
> "50%" **emas**. Bu sozlamani ishlatishdan oldin joriy .NET hujjatidan
> aniq formatni tekshiring va natijani o'lchang:
> ```bash
> docker compose exec -T api sh -c 'cat /sys/fs/cgroup/memory.max'
> docker stats --no-stream "$(docker compose ps -q api)"
> ```
>
> Default (konteyner limitining ~75%) aksariyat holatda to'g'ri —
> **o'lchamasdan o'zgartirmang.**

### 6.5. `livekit`

| Nima | Qiymat | Sabab |
|---|---|---|
| CPU | **6.0 shift / `cpu_shares: 2048`** | SFU **transkodlash qilmaydi**, lekin SRTP shifrlash + juda ko'p kichik paket = real yuk. Throttling bo'lmasin |
| Xotira | `2g` | LiveKit xotirani tejamkor ishlatadi; 2 GB — keng zaxira |
| Tarmoq | `network_mode: host` (tavsiya) | 9-bo'limga qarang |

**LiveKit CPU'ni qanday kuzatish:**

```bash
docker stats --no-stream "$(docker compose ps -q livekit)"
```

Qoida: dars cho'qqisida LiveKit CPU **60% dan doimiy oshsa** — ikkinchi node
haqida o'ylash vaqti keldi (8-bo'lim).

> ⚠️ **Yozuv (egress) yuqoridagi LiveKit hisobiga KIRMAYDI — u ALOHIDA
> xizmat va alohida sarf.** 2026-08-24 dan `livekit-egress` compose'da
> bor (ilgari yo'q edi — Ilova A, 5-risk).
>
> Yozib olish — bu **transkodlash**, ya'ni SFU'dan tubdan farq qiladigan,
> CPU'ni yeydigan ish: xona Chrome ichida chiziladi va ffmpeg bilan MP4
> ga siqiladi. Bitta kompozit yozuv **~1-2 vCPU**.
>
> Shuning uchun egress'ga alohida kvota qo'yilgan (`EGRESS_CPUS`,
> standarti `3.0` ≈ **2 parallel yozuv**). 🔴 Kvota **majburiy**:
> chegarasiz egress dars payti protsessorni egallab olardi va birinchi
> bo'lib **jonli dars** sinardi — ikkilamchi funksiya asosiysini
> o'ldirardi.
>
> ⚠️ Chegaraga tegilganda yozuv "sekinlashmaydi" — **kadrlar tashlanadi**,
> ya'ni sifat jimgina tushadi. 8 vCPU serverda bir vaqtda **2-3 tadan
> ko'p yozuv rejalashtirmang**.

### 6.6. `web` (nginx, statik)

Vue build natijasi — statik fayllar. `256m` / `0.5 cpu` yetarli.
Agar host nginx to'g'ridan-to'g'ri statik fayllarni bersa, bu konteyner
umuman kerak bo'lmasligi mumkin — lekin SPEC 8-bo'limi uni majburiy qilgan,
shuning uchun qoldiramiz.

---

## 7. Operatsiyalar: deploy, rollback, log, backup

### 7.1. Katalog tuzilmasi va sirlar

```
/opt/zinnur/
├── docker-compose.yml
├── .env                      # ⚠️ chmod 600, git'ga TUSHMAYDI
├── backend/  frontend/
└── infra/{nginx,livekit,postgres,scripts}
```

Sirlarni generatsiya qilish (SPEC: `Jwt__Secret` va `LiveKit__ApiSecret`
**32+ bayt**):

```bash
cd /opt/zinnur
umask 077

JWT_SECRET=$(openssl rand -base64 48)
LK_SECRET=$(openssl rand -base64 48)
PG_PASSWORD=$(openssl rand -base64 32 | tr -d '/+=' | head -c 32)
LK_KEY="zinnur$(openssl rand -hex 6)"      # 'devkey' EMAS!

cat > .env <<EOF
POSTGRES_PASSWORD=${PG_PASSWORD}
ConnectionStrings__Postgres=Host=postgres;Port=5432;Database=zinnur;Username=zinnur;Password=${PG_PASSWORD};Maximum Pool Size=40;Minimum Pool Size=5
ConnectionStrings__Redis=redis:6379
Jwt__Issuer=zinnur
Jwt__Audience=zinnur-web
Jwt__Secret=${JWT_SECRET}
Jwt__AccessMinutes=15
Jwt__RefreshDays=14
LiveKit__Url=wss://livekit.domen.uz
LiveKit__ApiKey=${LK_KEY}
LiveKit__ApiSecret=${LK_SECRET}
Cors__AllowedOrigins__0=https://app.domen.uz
LIVEKIT_KEYS=${LK_KEY}: ${LK_SECRET}

# 🔴 BIRINCHI ADMINISTRATORNING TELEFONI — BO'SH BAZAGA MAJBURIY.
#    Bu raqamga bog'langan Telegram hisobiga kirish kodi keladi.
#    Yo'q bo'lsa API ATAYLAB ko'tarilmaydi (sabab: 7.1.1).
Bootstrap__AdminPhone=+998901234567
EOF

chmod 600 .env
ls -l .env      # -rw------- bo'lishi kerak
```

> 🔴 **`LIVEKIT_KEYS` dagi secret `LiveKit__ApiSecret` bilan BAYTMA-BAYT bir
> xil bo'lishi shart.** Aks holda backend token yaratadi, LiveKit uni rad
> etadi, klient esa faqat "could not connect" ko'radi — sabab hech qayerda
> aniq yozilmaydi. Tekshirish:
> ```bash
> grep -E '^(LiveKit__ApiSecret|LIVEKIT_KEYS)=' .env
> ```
>
> 🔴 **`devkey` ni prod'da ishlatmang.** SPEC 8-bo'limidagi `devkey` —
> LiveKit misollarida keng tarqalgan qiymat. Uni qoldirsangiz, kimdir
> secret'ni topsa, **istalgan xonaga host huquqi bilan kira oladi**.

---

### 7.1.0. 🔴 ILOVA NAMUNA SIRLARI BILAN KO'TARILMAYDI (2026-08-22)

`ASPNETCORE_ENVIRONMENT=Production` bo'lganda `ProductionSecretsGuard`
ishga tushishda quyidagilarni tekshiradi va **birortasi namuna qiymatda
qolsa ilovani to'xtatadi** (port ochilgunga va migratsiya qo'llangunga
qadar):

| Kalit | Nima rad etiladi |
|---|---|
| `Jwt:Secret` | ichida `dev_only` yoki `change_me` bo'lsa |
| `LiveKit:ApiSecret` | ayni marker |
| `LiveKit:ApiKey` | aynan `devkey` |
| `Storage:AccessKey` / `Storage:SecretKey` | ayni marker |
| `Storage:ServiceUrl` | ichida `minio` bo'lsa (prod'da R2 bo'lishi kerak) |
| `Storage:ServiceUrl` | `localhost` / `127.0.0.1` / `0.0.0.0` / `::1` |
| `Storage:PublicUrl` | `localhost` / `127.0.0.1` / `0.0.0.0` / `::1` |
| `Cors:AllowedOrigins` | `localhost` / `127.0.0.1` / `0.0.0.0` / `::1` |

> **Oxirgi ikki qator 2026-08-24 da qo'shildi** va sababi shu hujjatdagi
> eng qimmat turdagi nosozlik: `Storage:PublicUrl` prod overlay'ida
> **umuman yo'q edi**, ya'ni bazaviy `.env` dagi `http://localhost:9010`
> prod'da qolib ketardi. Dars yozuvining imzolangan havolasi `localhost`
> ga ishora qilardi — **serverning logida hech narsa ko'rinmasdi**, chunki
> so'rov brauzerdan bizga umuman kelmaydi. Yagona alomat: "video
> ochilmayapti". Endi ilova bunday sozlama bilan **ko'tarilmaydi**.

⚠️ **`POSTGRES_PASSWORD` bu ro'yxatda YO'Q** — uni darvoza tekshira
olmaydi (sabab kod izohida). Uning tasodifiyligi yuqoridagi `openssl
rand` qadamining o'ziga kiritilgan; **namuna parolni prod `.env` ga
ko'chirmang**. Xavf darajasi pastroq: prod'da Postgres tashqariga
umuman chiqarilmaydi (`docker-compose.prod.yml` da unga `ports` yo'q).

**Nima uchun marker bo'yicha, ro'yxat bo'yicha emas:** `.env.example`
dagi har bir dev standarti ataylab `dev_only_...` yoki `..._change_me`
ko'rinishida yozilgan. Aniq qiymatlar ro'yxati eskirardi — yangi dev
standarti qo'shilganda kimdir uni darvozaga qo'shishni unutardi.

Xato xabari **hamma muammoni birdaniga** sanab beradi, ya'ni sirlarni
bittalab tuzatib qayta-qayta deploy qilish shart emas:

```bash
# Deploydan keyin ilova ko'tarilmasa — sabab shu yerda ochiq yoziladi:
docker compose -f docker-compose.yml -f docker-compose.prod.yml logs api | head -40
```

⚠️ Bu tekshiruvni **o'chirish yo'li yo'q** — u ataylab shunday. Namuna
sir bilan ishlab turgan tizim hech qanday belgi bermasdi: xato ham,
ogohlantirish ham chiqmasdi.

`Cors__AllowedOrigins__0` prod'da `https://app.domen.uz` bo'lishi kerak —
SPEC'dagi `http://localhost:5173` faqat dev uchun.

---

### 7.1.1. 🔴 KIRISH — FAQAT TELEFON ORQALI (2026-08-13 dan)

Email va parol bilan kirish **butunlay olib tashlandi** (loyiha egasining
qarori). Platformaga kirishning ikki eshigi qoldi va **ikkalasi ham
Telegram botiga tayanadi**:

| Eshik | Kim ishlatadi | Nimaga tayanadi |
|---|---|---|
| Mini App (`initData` imzosi) | o'quvchi, telefonda | bot tokeni |
| Telefon + bir martalik kod | **hamma**, istalgan brauzerda | bot tokeni |

**Operator uchun bu nimani anglatadi:** bot tokeni buzilsa — **hech kim,
hech qayerdan kira olmaydi.** Bu holat uchun ikkita mexanizm qurilgan;
ikkalasini ham *avariyadan oldin* o'qib chiqing.

#### (a) Boshlang'ich administrator — `Bootstrap__AdminPhone`

Bo'sh bazaga birinchi marta ko'tarilganda `DbInitializer` administrator
yaratadi. Ilgari u **telefonsiz** yaratilardi — endi bu yangi
o'rnatishni o'zini o'zi qulflab qo'yishga olib kelardi:

```
kirish uchun raqam kerak  ->  raqamni kiritish uchun kirish kerak
```

Shuning uchun raqam muhitdan olinadi va **u yo'q bo'lsa API ATAYLAB
ko'tarilmaydi**:

```bash
Bootstrap__AdminPhone=+998901234567       # MAJBURIY (bo'sh bazada)
Bootstrap__AdminTelegramId=123456789      # ixtiyoriy, TAVSIYA ETILMAYDI
```

> ⚠️ **Tekshiruv faqat baza BO'SH bo'lganda ishlaydi.** Ishlab turgan
> o'rnatishda bu o'zgaruvchi kerak emas va uni qo'shmaslik hech narsani
> buzmaydi — administrator allaqachon bazada.

> ⚠️ **`Bootstrap__AdminTelegramId` ni qo'ymaganingiz ma'qul.** Xato ID
> berilsa administrator hisobi **boshqa odamga** bog'lanib qoladi. Raqam
> yetarli: admin botda «📱 Raqamni ulashish» tugmasini bosadi.

Raqam **administratorning Telegram hisobiga ro'yxatdan o'tgan** raqami
bo'lishi shart. Deploy'dan keyin darhol tekshiring:

```bash
docker compose logs api | grep "Boshlang'ich ma'lumotlar yozildi"
# -> Admin: admin@zinnur.uz, telefon: +998901234567
```

#### (b) 🔴 BOT TOKENI BUZILSA — "break-glass" o'zgaruvchilari

Bot tokeni va webhook siri **bazada** saqlanadi (admin paneldan
almashtirish uchun). Bu odatda foyda, lekin bitta halokatli holat bor:

```
token buzuq  ->  hech kim kira olmaydi  ->  tokenni tuzatadigan panel
             ->  o'sha kirish ortida qolgan  ->  faqat psql
```

Halqani uzish uchun **bazadagi qiymatdan USTUN turadigan** ikkita muhit
o'zgaruvchisi bor:

```bash
# /opt/zinnur/.env ga qo'shing
Telegram__BotTokenOverride=123456789:AA...          # @BotFather'dagi HAQIQIY token
Telegram__WebhookSecretOverride=zinnur_yangi_sir    # A-Za-z0-9_- belgilari

docker compose up -d api        # qayta ishga tushirish YETARLI
```

Shundan keyin buzuq baza qatori **e'tiborsiz qoladi** va tizim ochiladi.

> 🔴 **IKKALASINI HAM QO'YING.** `TelegramOptions.IsConfigured` ikkala
> qiymatni ham talab qiladi — faqat tokenni ustidan yozsangiz,
> integratsiya baribir "sozlanmagan" holatida qoladi.

> ⚠️ **WEBHOOK SIRINI O'ZGARTIRSANGIZ Telegram tomonida ham yangilang:**
> ```bash
> curl -sS "https://api.telegram.org/bot<TOKEN>/setWebhook" \
>   -d "url=https://api.domen.uz/api/v1/telegram/webhook" \
>   -d "secret_token=<YANGI_SIR>"
> ```

**Tiklangandan KEYIN o'zgaruvchilarni OLIB TASHLANG.** Ular turgan
ekan, tokenni paneldan almashtirib bo'lmaydi. Panel buni yashirmaydi:
maydon qulflanadi, yonida sababi chiqadi va manba `Shoshilinch
(muhitdan)` deb ko'rsatiladi (qizil belgi).

```bash
# tiklashdan keyin
sed -i '/^Telegram__BotTokenOverride=/d;/^Telegram__WebhookSecretOverride=/d' .env
docker compose up -d api
```

#### (c) 🔴 Xodim ishdan ketganda

Xodim uchun parol **ikkinchi omil** vazifasini bajarardi — endi u yo'q.
Hisobning butun xavfsizligi telefon raqamiga tayanadi, O'zbekistonda esa
operator ishlatilmagan raqamni **qayta sotadi**. Shuning uchun:

```
Xodim ishdan ketgan kuni:
  POST /api/v1/users/{id}/deactivate        # profilni yopish
  POST /api/v1/users/{id}/telegram/unlink   # bog'lanishni uzish (audit izi bilan)
```

Ikkalasi ham mavjud sessiyalarni **darhol** bekor qiladi. Faqat
birinchisini bajarish yetarli emas: profil qaytadan faollashtirilsa eski
bog'lanish tiklanib qolardi.

### 7.1.2. 🔴 CLOUDFLARE R2 — video va fayllar ombori

Prod'da **hech qanday fayl serverda saqlanmaydi**. Uch turdagi fayl ham
bitta R2 bucket'iga tushadi:

| Nima | Kalit (prefiks) | Kim yozadi | Kim o'qiydi |
|---|---|---|---|
| Jonli dars yozuvi (MP4) | `recordings/…` | **LiveKit Egress** — to'g'ridan R2 ga | brauzer, **imzolangan havola** bilan to'g'ridan R2 dan |
| Yuklangan dars videosi | `<prefiks>/…` | `api` (oqim bilan) | `api` orqali (chipta + `Range`) |
| Uy vazifasi fayli | `<prefiks>/…` | `api` | `api` orqali |

⚠️ **Ikkinchi qatorga e'tibor bering**: yuklangan dars videosi brauzerga
`api` orqali uzatiladi — ya'ni uning trafigi **sizning kanalingizdan
o'tadi** (yozuvniki esa yo'q). Bu ONGLI qaror: video ko'rish huquqi
(qarz, qulflangan dars) **har bir so'rovda** qayta tekshirilishi kerak.
Sabab va muqobili — `IMediaStorage` izohida. Sig'im hisobida buni
8.1-bo'limdagi matematikaga qo'shing.

---

**1. Bucket yaratish.** Cloudflare panel → R2 → *Create bucket*.

* Nom: masalan `zinnur-prod`.
* Joylashuv: *Automatic* (yoki `EEUR`).
* 🔴 **Public access — O'CHIQ qoldiring.** Ochiq bucket butun ruxsat
  modelini yo'q qiladi: havolani ushlagan har kim videoni ko'radi.

**2. API tokeni.** R2 → *Manage R2 API Tokens* → *Create API token*.

* Ruxsat: **Object Read & Write** (faqat `Read` bo'lsa — pastdagi
  ogohlantirishni o'qing).
* Bucket: **faqat yuqoridagi bucket** (hisobning hammasi emas).
* Natijada uchta qiymat beriladi: *Access Key ID*, *Secret Access Key*
  va *S3 endpoint* (`https://<hisob-id>.r2.cloudflarestorage.com`).
  **Secret faqat bir marta ko'rsatiladi.**

> 🔴 **Token FAQAT o'qish huquqiga ega bo'lsa nima bo'ladi:** dars
> odatdagidek o'tadi, yozuv "boshlandi" deb ko'rinadi, xato hech
> qayerda chiqmaydi — chunki faylni **Egress** yozadi, `api` emas.
> Nosozlik faqat dars tugagach, watchdog faylni ombordan topa
> olmaganda bilinadi. Ya'ni **bitta darsning yozuvi butunlay
> yo'qoladi**.

**3. `.env` ga yozish** (`/opt/zinnur/.env`, 9-bo'lim):

```bash
R2_SERVICE_URL=https://<hisob-id>.r2.cloudflarestorage.com
R2_BUCKET=zinnur-prod
R2_ACCESS_KEY=<Access Key ID>
R2_SECRET_KEY=<Secret Access Key>
R2_REGION=auto            # R2 uchun DOIM `auto`
R2_KEY_PREFIX=submissions
R2_PUBLIC_URL=            # BO'SH QOLDIRING — pastdagi izohni o'qing
```

> ⚠️ **"To'liq yoki bo'sh" qoidasi:** `R2_SERVICE_URL`, `R2_BUCKET`,
> `R2_ACCESS_KEY`, `R2_SECRET_KEY` — to'rttasi ham to'ldirilishi yoki
> to'rttasi ham bo'sh bo'lishi kerak. Yarim to'ldirilgan bo'lsa ilova
> **ataylab ko'tarilmaydi**.

---

#### 🔴 `R2_PUBLIC_URL` — bo'sh qoldiring, custom domen YOZMANG

Bu maydondan dars yozuvining **imzolangan (presigned)** havolasi
quriladi. Bo'sh bo'lsa `R2_SERVICE_URL` ishlatiladi — ya'ni imzo ham,
havola ham bitta xostga tegishli bo'ladi va nomuvofiqlik **hech qachon**
chiqmaydi. Aynan shuning uchun bo'sh qoldirish — tavsiya etilgan holat.

**Nega custom domen (`media.zinnur.uz`) yaramaydi:** R2 ning custom
domeni S3 API emas va imzoni **tekshirmaydi**.

* bucket ochiq bo'lsa — fayl **imzosiz ham** beriladi, ya'ni muddat,
  to'lov darvozasi va ruxsat tekshiruvi umuman ishlamaydi;
* bucket yopiq bo'lsa — **har bir** havola rad etiladi.

**Nega bu xatoni topish qiyin:** SigV4 imzosi URL'ning **host** qismini
ham qamrab oladi. Manzil noto'g'ri bo'lsa ombor havolani rad etadi,
so'rov esa brauzerdan **bizning serverimizga umuman kelmaydi** — logda
hech narsa yo'q, health-check yashil, o'quvchi esa "video ochilmadi"
deydi.

Shu sababli 7.1.0 dagi darvoza `Storage:PublicUrl` va
`Storage:ServiceUrl` mahalliy manzilga ishora qilsa **ilovani
ko'tarmaydi**.

---

#### Tekshirish — deploy'dan keyin

**a) Dev qiymati oqib o'tmaganiga ishonch:**

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml \
  config | grep -i storage
```

Chiqishda **`minio` so'zi ham, `localhost` ham bo'lmasligi kerak**.
`Storage__PublicUrl` bo'sh ko'rinsa — bu **to'g'ri**.

**b) Ombor haqiqatan yozadimi** — panelda alohida "ulanishni tekshirish"
tugmasi **yo'q**, shuning uchun eng ishonchli tekshiruv — kichik bir dars
videosini yuklash. Fayl R2 panelida `<prefiks>/YYYY-MM/…` yo'lida paydo
bo'lishi kerak. Yuklash `503` bersa — kalitlar yoki manzil noto'g'ri
(sabab `api` logida ko'rinadi: `Ombor media faylni rad etdi … status=…`).

**c) Yozuv yo'li** (eng muhimi, chunki uni **Egress** bajaradi): bitta
qisqa test darsini yozib, tugatib, R2 da `recordings/YYYY-MM/<sessionId>/`
papkasi paydo bo'lganini va yozuvni **o'quvchi hisobidan** ochib
ko'ring. Bu uchta narsani bir yo'la tasdiqlaydi: Egress internetga
chiqa oladi, token yozish huquqiga ega, presigned havola ishlaydi.

---

#### Sozlamalarni keyin o'zgartirish — qayta deploy SHART EMAS

Ombor ulanish nuqtalari (`manzil`, `bucket`, `access key`, `secret
key`, `region`, `brauzer manzili`) **bazadan** o'qiladi va **admin
panelidan** o'zgartiriladi (Sozlamalar → Ombor). Ustunlik: **baza →
muhit (`.env`) → standart**.

Ya'ni kalit sizib chiqsa uni almashtirish uchun serverga kirish shart
emas — bu ataylab shunday: kalit aylantirish eng shoshilinch daqiqada
kerak bo'ladi, `.env` ni tahrirlab qayta deploy qilish esa eng sekin
yo'l.

⚠️ `R2_KEY_PREFIX` bundan **istisno** — u paneldan o'zgarmaydi. U ombor
**ichidagi** joylashuv sxemasi: o'zgartirilsa allaqachon yuklangan
fayllarga yo'l uzilardi.

---

### 7.2. Birinchi deploy

```bash
cd /opt/zinnur

docker compose config              # 1) YAML va env to'g'rimi
docker compose build --pull        # 2) image'lar
docker compose up -d postgres redis
sleep 10
docker compose exec -T postgres pg_isready -U zinnur -d zinnur

docker compose up -d               # 3) hammasi
docker compose ps
```

**EF Core migratsiyalari.** Migratsiya yangi API kod ishlashidan **oldin**
qo'llanishi kerak. Ikki variant:

```bash
# A) Ishga tushishda avtomatik (bitta replika bo'lsa xavfsiz)
#    Program.cs da: db.Database.Migrate()

# B) Qo'lda, aniq nazorat bilan (tavsiya)
docker compose run --rm api dotnet ef database update
```

> ⚠️ **Bir nechta `api` replikasi bo'lsa avtomatik migratsiya XAVFLI** —
> ikkita instance bir vaqtda migratsiya qilmoqchi bo'lib, deadlock yoki
> yarim qo'llangan schema chiqadi. Bu holda faqat B variantini ishlating.

### 7.3. Yangilash (update)

```bash
cd /opt/zinnur

# 0) BACKUP — majburiy, migratsiya qaytarilmasligi mumkin
sudo ./infra/scripts/backup-db.sh

# 1) Kod
git fetch --all
git log --oneline HEAD..origin/main       # nima kelayotganini KO'RING
git checkout main && git pull

# 2) Build (eski konteynerlar hali ishlab turadi)
docker compose build --pull

# 3) Migratsiya
docker compose run --rm api dotnet ef database update

# 4) Almashtirish
docker compose up -d --remove-orphans

# 5) Tekshirish
docker compose ps
curl -fsS https://app.domen.uz/health/ready && echo " READY"
```

#### Zero-downtime haqida halol gap

**Bu arxitekturada haqiqiy zero-downtime YO'Q**, va buni bilib turish
uni xayol qilishdan yaxshiroq:

| Xizmat | `up -d` qilganda nima bo'ladi | Foydalanuvchi nimani sezadi |
|---|---|---|
| `web` | Konteyner almashadi (~2 s) | Sahifa yangilanmasa — hech nima |
| `api` | Konteyner almashadi (~5-15 s) | **SignalR uziladi**, chat/presence to'xtaydi. SignalR o'zi qayta ulanadi |
| `livekit` | Konteyner almashadi | 🔴 **HAMMA JONLI DARS UZILADI.** Xonalar LiveKit xotirasida |
| `postgres` | Konteyner almashadi | API xato qaytaradi (~10 s) |
| `redis` | Konteyner almashadi | Presence bo'shaydi, qayta ulanishda tiklanadi |

**Amaliy qoidalar:**

1. **`livekit` ni dars vaqtida HECH QACHON qayta ishga tushirmang.**
   Faqat `livekit` o'zgarmagan bo'lsa, faqat kerakli xizmatni yangilang:
   ```bash
   docker compose up -d --no-deps api web
   ```
2. **Video API'ga bog'liq emas.** `api` qayta ishga tushganda LiveKit
   ulanishi (media) **uzilmaydi** — faqat chat/presence to'xtaydi va
   SignalR bir necha soniyada o'zi qayta ulanadi. Bu yaxshi xabar:
   `api` deploy'i darsni buzmaydi.
3. Deploy'ni **dars jadvalidan tashqarida** qiling:
   ```bash
   docker compose exec -T postgres psql -U zinnur -d zinnur -c \
     "SELECT count(*) FROM \"LiveSessions\" WHERE \"Status\" = 1;"
   #  1 = Live (SPEC 2-bo'lim). 0 bo'lsa — deploy qilish mumkin.
   ```
4. Haqiqiy zero-downtime kerak bo'lsa: 2 ta `api` replikasi + host nginx
   upstream'da ikkalasi + navbat bilan yangilash. Bu compose faylini
   o'zgartirishni talab qiladi (boshqa agent).

### 7.4. Rollback

Rollback'ning muvaffaqiyati **oldindan tayyorgarlikka** bog'liq:

```bash
# Har bir deploy'da image'ni git sha bilan teglang
export IMAGE_TAG=$(git rev-parse --short HEAD)
docker compose build
docker tag zinnur-api:latest zinnur-api:${IMAGE_TAG}
```

Qaytish:

```bash
cd /opt/zinnur
git log --oneline -10                     # oldingi ishlagan commit'ni toping
git checkout <oldingi-sha>
docker compose up -d --force-recreate api web
```

> 🔴 **Baza migratsiyasi odatda QAYTARILMAYDI.** Kodni orqaga qaytarish
> oson, schema'ni emas. Agar migratsiya ustun o'chirgan yoki tur o'zgartirgan
> bo'lsa, eski kod yangi schema bilan ishlamaydi.
>
> **Yagona ishonchli yo'l — deploy oldidan olingan backup:**
> ```bash
> ls -lt /var/backups/zinnur/ | head
> ```
> Bazani tiklash 7.7-bo'limda (va u **destruktiv**).
>
> Shuning uchun migratsiyalarni **orqaga mos** (backward-compatible) yozing:
> ustun o'chirmang — avval ishlatishni to'xtating, bir necha reliz keyin
> o'chiring.

### 7.5. Loglar

```bash
# Jonli kuzatish
docker compose logs -f --tail=200 api
docker compose logs -f --tail=200 livekit

# Vaqt oralig'i bo'yicha
docker compose logs --since 30m --timestamps api
docker compose logs --since "2026-01-15T09:00:00" --until "2026-01-15T10:00:00" api

# Hamma xizmat, faqat xatolar
docker compose logs --since 1h 2>&1 | grep -Ei 'error|exception|fatal|panic'

# LiveKit'ning kritik ogohlantirishlari
docker compose logs livekit 2>&1 | grep -Ei 'buffer|ice|failed|dropped'

# Docker daemon'ning o'zi
sudo journalctl -u docker --since "1 hour ago" --no-pager

# Host nginx
sudo tail -f /var/log/nginx/error.log
sudo tail -f /var/log/nginx/access.log

# Loglar qancha joy egallagan
sudo du -sh /var/lib/docker/containers/*/*-json.log 2>/dev/null | sort -h | tail
```

### 7.6. Sog'liqni tekshirish (health)

Deploy'dan keyin **hammasini** o'tkazing:

```bash
#!/usr/bin/env bash
# health-check.sh — deploy'dan keyin ishlating
set -u
echo "== konteynerlar =="
docker compose ps

echo "== API =="
curl -fsS https://app.domen.uz/health       && echo " health OK"
curl -fsS https://app.domen.uz/health/ready && echo " ready OK"

echo "== Postgres =="
docker compose exec -T postgres pg_isready -U zinnur -d zinnur

echo "== Redis =="
docker compose exec -T redis redis-cli PING

echo "== LiveKit (host'dan) =="
curl -sS -o /dev/null -w 'HTTP %{http_code}\n' http://127.0.0.1:7880/

echo "== Egress (dars yozuvi) =="
# Konteyner "healthy" bo'lishi YETARLI EMAS — u Redis'ga ULANGANINI
# ko'rish kerak, chunki LiveKit bilan aloqa AYNAN Redis orqali ketadi.
# "service ready" qatori bo'lmasa yozuv JIMGINA boshlanmaydi.
docker compose logs --tail=20 livekit-egress | grep -E "service ready|connecting to redis" \
  && echo " egress OK" || echo " 🔴 EGRESS ULANMAGAN — yozuv ishlamaydi"

echo "== LiveKit WSS (tashqaridan) =="
curl -sS -o /dev/null -w 'HTTP %{http_code}\n' https://livekit.domen.uz/

echo "== Frontend =="
curl -sS -o /dev/null -w 'HTTP %{http_code}\n' https://app.domen.uz/

echo "== Resurslar =="
docker stats --no-stream
df -h /
free -h
```

**Haqiqiy end-to-end test** (buni hech narsa almashtira olmaydi): ikki xil
qurilmadan (biri mobil internetda) darsga kiring, ovoz va videoni tekshiring.
Mobil internet — UDP bloklanishini va 7881 fallback ishlashini tekshiradigan
yagona real sinov.

### 7.7. Tungi backup

Skript: `infra/scripts/backup-db.sh` (`docker compose exec -T postgres
pg_dump` → `gzip` → vaqt tamg'ali fayl → retention).

**Nega dump konteyner ichida?** Host'dagi `pg_dump` versiyasi serverdan
(PostgreSQL 17) eski bo'lsa, `server version mismatch` xatosi chiqadi.
Konteyner ichidagi `pg_dump` har doim server bilan bir xil versiyada.

Cron o'rnatish:

```bash
sudo tee /etc/cron.d/zinnur-backup >/dev/null <<'EOF'
# ZIN-NUR — tungi DB backup (03:15). Fayl oxirida yangi qator BO'LISHI SHART.
SHELL=/bin/bash
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
MAILTO=""
15 3 * * * root /opt/zinnur/infra/scripts/backup-db.sh >/dev/null 2>&1
EOF
sudo chmod 644 /etc/cron.d/zinnur-backup
```

> Cron'da `PATH` juda cheklangan bo'ladi — `docker` topilmasligi mumkin.
> Shuning uchun `PATH` aniq yozilgan. Skript ham `DOCKER_BIN` orqali
> to'liq yo'lni qabul qiladi.

Qo'lda sinash va tekshirish:

```bash
sudo /opt/zinnur/infra/scripts/backup-db.sh
echo "exit=$?"                                  # 0 bo'lishi kerak
ls -lh /var/backups/zinnur/
sudo tail -20 /var/log/zinnur-backup.log

# Boshqa retention bilan
sudo RETENTION_DAYS=30 /opt/zinnur/infra/scripts/backup-db.sh
```

Sozlanadigan o'zgaruvchilar: `PROJECT_DIR`, `COMPOSE_FILE`, `PG_SERVICE`,
`DB_NAME`, `DB_USER`, `BACKUP_DIR`, `RETENTION_DAYS` (default **14**),
`LOG_FILE`, `MIN_SIZE_BYTES`, `DOCKER_BIN`.

Chiqish kodlari: `2` konfiguratsiya, `3` boshqa backup ishlayapti,
`4` postgres tayyor emas, `5` `pg_dump` xatosi, `6` gzip buzuq,
`7` dump juda kichik.

#### ⚠️ Sinalmagan backup — backup emas

Oyiga bir marta tiklashni sinab ko'ring (**alohida, bo'sh bazada**):

```bash
docker compose exec -T postgres createdb -U zinnur zinnur_restore_test
gunzip -c /var/backups/zinnur/zinnur-zinnur-YYYYMMDD-HHMMSS.sql.gz \
  | docker compose exec -T postgres psql -U zinnur -d zinnur_restore_test

docker compose exec -T postgres psql -U zinnur -d zinnur_restore_test \
  -c '\dt' -c 'SELECT count(*) FROM "Users";'

docker compose exec -T postgres dropdb -U zinnur zinnur_restore_test
```

#### 🔴 HAQIQIY BAZAGA TIKLASH — DESTRUKTIV

Bu buyruq **joriy ma'lumotlarni o'chiradi va o'rniga backup'ni qo'yadi.**
Faqat haqiqatan kerak bo'lganda, ilova to'xtatilgan holda:

```bash
# 1) Ilovani to'xtating (baza bilan ishlaydiganlarni)
docker compose stop api

# 2) Joriy holatni SAQLANG (tiklash ham xato bo'lishi mumkin)
sudo /opt/zinnur/infra/scripts/backup-db.sh

# 3) Tiklash (dump `--clean --if-exists` bilan olingan — eski obyektlarni o'zi tushiradi)
gunzip -c /var/backups/zinnur/zinnur-zinnur-YYYYMMDD-HHMMSS.sql.gz \
  | docker compose exec -T postgres psql -U zinnur -d zinnur

# 4) Qaytarish
docker compose start api
curl -fsS https://app.domen.uz/health/ready
```

#### Backuplarni serverdan tashqariga chiqaring

Server yo'qolsa (disk, provayder, ransomware) — serverdagi backup ham
yo'qoladi. Kamida haftada bir marta boshqa joyga nusxalang:

```bash
rsync -avz --delete -e 'ssh -p 2222' \
  /var/backups/zinnur/ backup-user@backup-host:/srv/zinnur-backups/
```

---

## 8. Sig'im (capacity) — halol tahlil

### 8.1. Bandwidth matematikasi

**Farazlar** (aniq raqamlar sizning frontend sozlamangizga bog'liq —
ularni `videoEncoding` / `simulcast` sozlamasidan yoki LiveKit
statistikasidan **o'lchang**, bu yerdagilar rejalashtirish uchun oraliq):

| Oqim | Taxminiy bitrate |
|---|---|
| Video 720p (yuqori qatlam) | **1.5 – 2.0 Mbps** |
| Video 360p (o'rta qatlam) | **0.4 – 0.6 Mbps** |
| Video 180p (past qatlam) | **0.12 – 0.2 Mbps** |
| Audio (Opus) | **~0.04 Mbps** |

**Asosiy formula:**

```
Egress (chiquvchi) = obunachi_soni × (o'rtacha_qatlam_bitrate + audio) × ular_ko'rayotgan_publisher_soni
Ingress (kiruvchi) = publisher_soni × (barcha simulcast qatlamlar yig'indisi + audio)
```

**Tipik dars: 1 o'qituvchi nashr qiladi, o'quvchilar faqat tinglaydi.**

| Ssenariy | Hisob | Egress |
|---|---|---|
| 200 o'quvchi, hammasi **360p** | 200 × (0.5 + 0.04) | **≈ 108 Mbps** |
| 200 o'quvchi, hammasi **720p** | 200 × (1.8 + 0.04) | **≈ 368 Mbps** |
| 200 o'quvchi, aralash (70% 360p, 30% 720p) | 140×0.54 + 60×1.84 | **≈ 186 Mbps** |

Ingress (o'qituvchidan) ≈ 2.5 Mbps — e'tiborga olmasa ham bo'ladi.

> **Muhim tushuncha:** egress **umumiy o'quvchi soniga** bog'liq, parallel
> darslar soniga emas. 4 ta dars × 50 o'quvchi va 1 ta dars × 200 o'quvchi
> deyarli bir xil trafik beradi (har bir o'quvchi bitta o'qituvchi oqimini
> oladi).

**Trafik hajmi:**

```
GB/soat = Mbps ÷ 8 × 3600 ÷ 1000
```

| Rejim | Mbps | GB/soat | Kuniga 4 soat | **Oyiga (22 kun)** |
|---|---|---|---|---|
| 360p | 108 | ~48.6 | ~195 GB | **≈ 4.3 TB** |
| Aralash | 186 | ~83.7 | ~335 GB | **≈ 7.4 TB** |
| 720p | 368 | ~165.6 | ~662 GB | **≈ 14.6 TB** |

> 🔴 **Bu raqamlarni provayderingizning trafik limiti bilan solishtiring.**
> Ko'p VPS tariflari oyiga 2-10 TB beradi. Chiqib ketsangiz — yo pul,
> yo tezlik cheklovi (100 Mbps gacha tushirish keng tarqalgan), ya'ni
> **video sinadi va sababi serverda ko'rinmaydi**.

Trafikni o'lchash:

```bash
sudo vnstat -d          # kunlik
sudo vnstat -m          # oylik
sudo vnstat -l          # jonli
```

#### Video sifatini kamaytirish — eng arzon "optimizatsiya"

360p'ga o'tish trafikni **~3.4 barobar** kamaytiradi. 200 kishilik ma'ruzada
o'qituvchining boshini 720p'da ko'rsatish deyarli hech qanday pedagogik
qiymat bermaydi (ekran ulashish — boshqa masala, u aniqlikni talab qiladi).
Bu **eng katta ta'sirli va eng arzon** o'zgarish.

#### 🔴 O'quvchilar nashr qila boshlasa — hisob portlaydi

Yuqoridagi hisob "1 publisher" faraziga asoslangan. Agar 50 kishilik sinfda
6 ta o'quvchi kamerasini yoqsa, har bir ishtirokchi **7 ta** oqim oladi:

```
50 obunachi × 7 oqim × 0.54 Mbps ≈ 189 Mbps   (bitta sinf uchun!)
```

Bu **kvadratik** o'sish. Shuning uchun:

* `LiveKitTokenRequest.CanPublish` (SPEC 4-bo'lim) o'quvchilarga sukut
  bo'yicha **`false`** bo'lsin.
* Qo'l ko'targanda faqat **audio** publish'ga ruxsat bering (`0.04 Mbps`).
* Bir vaqtda nashr qiluvchilar sonini cheklang (masalan 1 o'qituvchi + 2 o'quvchi).

### 8.2. Nima birinchi bo'lib sinadi

#### 200 foydalanuvchida (maqsad)

| # | Nima sinadi | Belgisi | Yechim |
|---|---|---|---|
| 1 | **UDP receive buffer** | Video "sinadi", LiveKit logida `buffer` ogohlantirishi, `netstat -su` da `receive buffer errors` o'sadi | `net.core.rmem_max=16777216` (5-bo'lim) |
| 2 | **Provayder trafik limiti** | Kechqurun video yomonlashadi, server resursi bo'sh | 8.1-jadval bilan tarifni tekshiring, 360p'ga o'ting |
| 3 | **conntrack jadvali** | `dmesg` da `nf_conntrack: table full`, yangi ulanishlar tushadi | `nf_conntrack_max`, yoki LiveKit'ni `network_mode: host` ga o'tkazish (NAT umuman bo'lmaydi) |
| 4 | **Fayl deskriptorlari** | `too many open files`, eski ulanishlar ishlaydi, yangilari yo'q | `daemon.json` → `default-ulimits` (5.4) |
| 5 | **Dars boshidagi DB portlashi** | Birinchi 30 soniyada API sekin | SPEC 6-bo'lim: chat fon navbatida; `Attendance` upsert indeksli bo'lsin |
| 6 | **Docker konteyner loglari diskni to'ldiradi** | Bir necha haftadan keyin server "to'satdan" o'ladi | `daemon.json` log rotation (2.4) |

**Bu barcha nuqtalar shu qo'llanmada yopilgan.** Tuning bilan 8 vCPU / 16 GB
server 200 foydalanuvchini ko'taradi — cheklovchi omil **CPU emas, tarmoq**.

#### 500 foydalanuvchida

| Nima | Nega |
|---|---|
| **Tarmoq — asosiy to'siq** | 500 × 0.54 = **270 Mbps** (360p) yoki **920 Mbps** (720p). 1 Gbps port 720p'da **to'ladi** |
| **Bitta UDP soketning o'qish sikli** | Barcha media bitta soketdan o'tadi; uni o'qish asosan bitta CPU yadrosining softirq'iga tushadi. `/proc/net/softnet_stat` da drop paydo bo'ladi |
| **NIC navbatlari** | Ko'p navbatli (multi-queue) NIC va RSS kerak: `ethtool -l eth0` |
| **SignalR fan-out** | SPEC 6-bo'limining "faqat delta broadcast" qoidasi shu yerda hal qiluvchi bo'ladi. To'liq ro'yxat yuborilsa — 500² muammosi |
| **Postgres ulanishlari** | Bir necha `api` replikasi kerak bo'ladi → pool sozlamasi majburiy (6.2) |

Amaliy chegara: **360p'da 500 foydalanuvchi bitta yaxshi serverda mumkin,
720p'da esa yo'q.**

#### 1000+ foydalanuvchida

Bitta node yetmaydi. Kerak bo'ladi:

1. **Bir nechta LiveKit node** — Redis orqali klasterlangan, har biri
   o'z ommaviy IP va portlari bilan.
2. **Bir nechta `api` replikasi** — host nginx'da upstream, sticky
   session shart emas (SignalR backplane Redis'da, SPEC 6-bo'lim).
3. **Redis'ni ajratish** — LiveKit klasteri va SignalR backplane uchun
   alohida instance (yoki alohida DB raqami). Bitta Redis ikkisiga ham
   xizmat qilsa — `maxmemory` bosimi ikkalasini bir vaqtda buzadi.
4. **Postgres** — 1000 foydalanuvchi ham DB uchun katta yuk emas
   (SPEC'dagi yozuvlar juda kichik), lekin ulanishlar soni oshgani uchun
   **PgBouncer** (transaction pooling) foydali bo'ladi.
5. **Postgres uchun alohida server** — LiveKit bilan bir mashinada I/O va
   CPU uchun raqobat qilmasin.

### 8.3. Ikkinchi LiveKit node qachon kerak

Quyidagilardan **bittasi** doimiy kuzatilsa:

| Ko'rsatkich | Chegara | Buyruq |
|---|---|---|
| Chiquvchi trafik | NIC sig'imining **> 60-70%** | `vnstat -l`, `ip -s link show eth0` |
| LiveKit CPU | Dars cho'qqisida **> 60%** doimiy | `docker stats` |
| UDP paket yo'qotish | `receive buffer errors` **o'sib boryapti** | `netstat -su` |
| Foydalanuvchi shikoyati | Muntazam "video sinadi/muzlaydi" | — |
| Bir vaqtdagi ishtirokchi | **> 400-500** (360p) | LiveKit metrikalari |

**Ikkinchi node qo'shish nimani talab qiladi (oldindan biling):**

* LiveKit'ni **Redis bilan** klaster rejimida sozlash (`livekit.yaml`).
* Har bir node uchun **alohida ommaviy IP** va 7881/7882 portlari
  (media to'g'ridan-to'g'ri node'ga boradi, nginx orqali emas).
* Signalizatsiya (7880/WSS) uchun yuk taqsimlagich — lekin **oddiy
  round-robin yaramaydi**: bitta xonaning ishtirokchilari bir node'da
  bo'lishi kerak (yoki node'lararo yo'naltirish sozlanishi kerak).
* SPEC 5-bo'limidagi `LiveKitJoinDto.ServerUrl` **statik `wss://livekit.domen.uz`
  emas**, node'ga qarab dinamik bo'lishi kerak.
* **Bu SPEC o'zgarishini talab qiladi** — 1000 foydalanuvchiga rejalashtirsangiz,
  buni oldindan muhokama qiling.

### 8.4. Monitoring — minimal to'plam

Grafana/Prometheus o'rnatishdan oldin **hech bo'lmaganda** shular bo'lsin:

```bash
# Disk (eng ko'p unutiladigan va eng halokatli)
df -h / /var/lib/docker

# Trafik
vnstat -m

# Konteyner resurslari
docker stats --no-stream

# Yuk
uptime
```

Oddiy ogohlantirish (disk 85% dan oshsa xabar beradi):

```bash
sudo tee /etc/cron.d/zinnur-disk-alert >/dev/null <<'EOF'
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
0 * * * * root [ "$(df --output=pcent / | tail -1 | tr -dc 0-9)" -gt 85 ] && logger -p user.err -t zinnur "DISK > 85%"
EOF
```

LiveKit Prometheus metrikalarini yoqish (`infra/livekit/livekit.yaml`,
boshqa agent fayli) — bu real sig'imni **taxmin qilmasdan o'lchash**ning
yagona yo'li. Metrikalar portini **tashqariga chiqarmang**.

---

## 9. compose / nginx / livekit egasi uchun talablar

Bu fayllar boshqa agentda. Quyidagilar — bu qo'llanma ishlashi uchun
**majburiy** talablar.

### 9.1. `docker-compose.yml`

- [ ] Prod'da `postgres` va `redis` uchun `ports:` bloki **umuman yo'q**
      (SPEC 8-bo'lim: "prod'da chiqarilmaydi").
- [ ] `api` va `web` portlari **`127.0.0.1:` ga bog'langan**:
      `"127.0.0.1:5080:8080"`, `"127.0.0.1:5173:80"`.
- [ ] `livekit` uchun `"127.0.0.1:7880:7880"` (nginx proxy qilishi uchun),
      `"7881:7881"`, `"7882:7882/udp"` — yoki `network_mode: host` (9.3).
- [ ] Har bir xizmatda `restart: unless-stopped`.
- [ ] Har bir xizmatda `healthcheck` (`up -d` tartibi to'g'ri bo'lishi uchun).
- [ ] Resurs limitlari 6.1-jadval bo'yicha.
- [ ] `livekit` da **qattiq `cpus:` quota qo'ymaslik** yoki juda keng qo'yish
      (6.1 dagi throttling ogohlantirishi).
- [ ] `env_file: .env`, sirlar compose faylining o'zida **yozilmagan**.
- [ ] `postgres` uchun nomli volume + `infra/postgres/postgresql.conf` mount.

### 9.2. `infra/nginx/zinnur.conf` (host nginx)

- [ ] **`app.domen.uz`** → `/` → `127.0.0.1:5173`, `/api/` va `/hubs/` →
      `127.0.0.1:5080`.
- [ ] **`livekit.domen.uz`** → `127.0.0.1:7880`.
- [ ] WebSocket uchun (SignalR **va** LiveKit signalizatsiyasi):
      ```nginx
      proxy_http_version 1.1;
      proxy_set_header Upgrade    $http_upgrade;
      proxy_set_header Connection "upgrade";
      proxy_read_timeout  3600s;      # uzoq dars uzilmasin
      proxy_send_timeout  3600s;
      proxy_buffering     off;        # realtime uchun majburiy
      ```
- [ ] `X-Forwarded-Proto $scheme` + ASP.NET tarafida `ForwardedHeaders` —
      aks holda API `http://` havolalar generatsiya qiladi.
- [ ] ACME uchun: `location ^~ /.well-known/acme-challenge/ { root /var/www/certbot; }`
      **80-portda, HTTPS redirect'dan OLDIN**.
- [ ] Sertifikat yo'llari `/etc/letsencrypt/live/<domen>/` (symlink) orqali.
- [ ] `http` → `https` 301 redirect.

#### 9.2.1. Fayl yuklash va video oqimi chegaralari

Dars videosi (2 GB gacha), vazifa sharti biriktirmasi va o'quvchi javobi
API orqali yuklanadi/oqim qilinadi. nginx bu yo'llarni **standart
sozlamalarda to'sib qo'yadi**, shuning uchun ular `zinnur.conf` da
**alohida `location` bloklarida** turadi (5.2-bo'lim).

| Yo'l | `client_max_body_size` | Nega aynan shuncha |
|---|---|---|
| `POST /api/v1/lessons/{id}/assets` | `2049m` | Kestrel `MaxUploadBytes` = 2048 MiB + 1 MiB (multipart o'rami fayldan katta). `2048m` qo'yilsa maksimal hajmli video nginx'ning 413 si bilan qaytardi |
| `POST /api/v1/assignments/{id}/attachments` | `101m` | `lesson.image_max_mb` sozlamasining **maksimumi** (100 MB) + 1 MiB. Haqiqiy chegara sozlamadan keladi (standart 10 MB) va uni backend qo'llaydi |
| `POST /api/v1/assignments/{id}/submit` | `51m` | 5 fayl × 10 MB + 1 MiB (`Submission.MaxAttachments`). ⚠️ Bu yo'l **ilgari ham buzilgan edi**: backend 51 MB kutardi, nginx 10m da 413 berardi |
| **qolgan hamma joy** | **`10m`** (server darajasi) | 🔴 **O'ZGARTIRILMAYDI.** Server darajasida `2049m` qo'yish — xavfsizlik regressiyasi: u holda autentifikatsiyasiz endpointlar ham (masalan `/api/v1/auth/login`) 2 GB tana qabul qilardi |

Yuklash bloklarida majburiy:

```nginx
proxy_request_buffering off;   # 2 GB DISKKA buferlanmasin (2x I/O, disk to'ladi)
client_body_timeout 300s;      # bo'laklar ORASIDAGI tanaffus (umumiy vaqt EMAS)
proxy_send_timeout 3600s;      # 2 GB ni 10 Mbit/s da yuklash ~28 daqiqa
proxy_read_timeout 3600s;      # /api/ dagi 60s yuklashni o'rtasida uzardi (504)
```

Oqim (GET) bloklarida majburiy — `Range`/`206` (videoda oldinga o'tish)
buzilmasligi uchun:

```nginx
proxy_buffering off;           # javob temp faylga spool bo'lmasin, seek tez bo'lsin
proxy_max_temp_file_size 0;    # ikkinchi qulf
gzip off;                      # gzip `Content-Length` ni buzadi, bayt oraliqlari mos kelmaydi
```

🔴 **Nginx `location` larni MEROS QILMAYDI** — bu yerda eng ko'p xato qiladi:

- [ ] `limit_req` — `/api/` blokidagi cheklov yangi bloklarga **o'tmaydi**.
      Yuklash uchun alohida zona bor (`zinnur_upload`, `1r/s` + `burst=30`);
      u bo'lmasa yuklash endpointlari **umuman cheklovsiz** qolardi.
- [ ] `proxy_set_header` — blokda bittasi yozilsa tashqi darajadagi **butun
      to'plam** bekor bo'ladi, shuning uchun beshtasi ham takrorlanadi.
- [ ] `add_header` — yuklash/oqim bloklarida **ataylab yozilmagan**. Bitta
      `add_header` qo'shilsa, server darajasidagi **beshta xavfsizlik
      sarlavhasi** (HSTS, nosniff, X-Frame-Options, Referrer-Policy,
      Permissions-Policy) o'sha yo'llarda **jimgina yo'qoladi**.
- [ ] `location` **ustuvorligi tartibga bog'liq emas**: regex (`~`) oddiy
      prefiksdan (`/api/`) har qanday holatda ustun keladi; `= /aniq/yo'l`
      esa regexdan ham ustun.
- [ ] Deploy'dan keyingi tekshiruv buyruqlari — `infra/nginx/zinnur.conf`,
      7.5-bo'lim (413 sinovi + `Range` → 206 sinovi).

### 9.3. `infra/livekit/livekit.yaml` — 🔴 eng muhim tuzoq

**LiveKit ICE nomzodlarida serverning OMMAVIY IP'sini e'lon qilishi shart.**
Bridge tarmog'ida u faqat `172.x.x.x` ni ko'radi va shuni e'lon qiladi —
natijada **hech bir klient media ulana olmaydi**. Signalizatsiya ishlaydi,
xona ochiladi, ishtirokchilar ro'yxati ko'rinadi, lekin **ovoz ham, video
ham yo'q**. Bu self-hosted LiveKit'ning №1 nosozligi.

Uchta yechim (birini tanlang):

| Yechim | Qanday | Ijobiy | Salbiy |
|---|---|---|---|
| **A. `network_mode: host`** (tavsiya) | Konteyner host tarmog'ida ishlaydi | NAT yo'q → tez, conntrack bosimi yo'q, UFW to'g'ridan-to'g'ri ishlaydi | `api` konteyneri LiveKit'ga `livekit:7880` orqali yeta olmaydi (9.4) |
| **B. `rtc.use_external_ip: true`** | LiveKit STUN orqali o'z ommaviy IP'sini topadi | Bridge tarmoq saqlanadi | Ishga tushishda tashqi STUN kerak; NAT qatlami qoladi |
| **C. `--node-ip <PUBLIC_IP>`** | IP qo'lda beriladi | Eng aniq | IP o'zgarsa qo'lda yangilash kerak |

Boshqa talablar:

- [ ] `rtc.udp_port: 7882` va `rtc.tcp_port: 7881`, `rtc.port_range_*`
      **ishlatilmaydi** (SPEC: UDP mux).
- [ ] `port: 7880` — faqat `127.0.0.1` ga bog'lansin (nginx proxy qiladi).
- [ ] `keys:` — `LIVEKIT_KEYS` env orqali, faylga **yozilmasin**.
- [ ] `logging.level: info` (`debug` emas — disk to'ladi).
- [ ] `prometheus_port` yoqilsa — tashqariga chiqarilmasin.

### 9.4. `network_mode: host` tanlansa — natijalari

* SPEC 8-bo'limidagi `LiveKit__Url=ws://livekit:7880` **ishlamaydi**
  (Docker DNS'da `livekit` nomi bo'lmaydi).
* Prod'da bu o'zgaruvchi baribir `wss://livekit.domen.uz` bo'lishi kerak
  (klientga qaytariladi), shuning uchun ko'pincha muammo bo'lmaydi.
* **Lekin** backend LiveKit'ning HTTP API'siga murojaat qilsa (masalan
  dars tugaganda xonani yopish), unga alohida ichki manzil kerak:
  ```yaml
  extra_hosts:
    - "host.docker.internal:host-gateway"
  environment:
    LiveKit__ApiUrl: "http://host.docker.internal:7880"
  ```
  Bu **SPEC 8-bo'limiga qo'shimcha o'zgaruvchi** — SPEC egasi bilan
  kelishilishi kerak.

---

## 10. Xatolarni bartaraf etish

### "Darsga kiraman, ishtirokchilar ko'rinadi, lekin ovoz/video yo'q"

Eng keng tarqalgan holat. Tartib bilan tekshiring:

```bash
# 1) LiveKit qanday IP e'lon qilyapti?
docker compose logs livekit 2>&1 | grep -Ei 'node ip|external ip|ice'
#    172.x.x.x ko'rinsa -> 9.3-bo'lim, A/B/C yechimlaridan birini qo'llang

# 2) UDP 7882 tashqaridan ochiqmi?
sudo ufw status | grep 7882
sudo ss -ulnp | grep 7882

# 3) DOCKER-USER qoidalari media'ni to'smayaptimi?
sudo iptables -S ZINNUR-DOCKER
```

Brauzer tarafida: `chrome://webrtc-internals` → ICE candidate pair
`succeeded` bo'lishi kerak. `checking` da qotib qolsa — 7882 yetib
bormayapti degani.

### "Video sinadi / muzlaydi, lekin CPU bo'sh"

Klassik UDP buffer muammosi:

```bash
sysctl net.core.rmem_max                  # 16777216 bo'lishi kerak
docker compose logs livekit | grep -i buffer
netstat -su | grep -i 'receive buffer errors'
```

`rmem_max` to'g'ri bo'lsa ham xato bo'lsa — 5.2-bo'limdan keyin LiveKit
konteyneri qayta ishga tushirilganmi? Soket ochilgandan keyin buffer
o'zgarmaydi:

```bash
docker compose restart livekit
```

### "Video yuklanmayapman deydi" / `413 Request Entity Too Large`

Bu deyarli har doim **nginx**, backend emas. Ajratish oson: nginx 413 si —
`Server: nginx` bilan **HTML** sahifa, backend 413 si — **JSON**
(`ProblemDetails`).

```bash
# 1) Xato nginx logida bormi?
sudo grep -i 'client intended to send too large body' /var/log/nginx/zinnur.error.log

# 2) Yuklash bloklari serverda MAVJUDMI (deploy eski konfig bilan ketmadimi)?
sudo grep -n 'client_max_body_size' /etc/nginx/sites-available/zinnur.conf
#    Kutilgan: 10m (server), 2049m, 101m, 51m (location bloklari) — 9.2.1

# 3) Konfig yangilanganidan keyin reload qilinganmi?
sudo nginx -t && sudo systemctl reload nginx
```

`504 Gateway Time-out` yuklash **o'rtasida** chiqsa — so'rov yuklash blokiga
tushmayapti (`location` regexi yo'lga mos kelmayapti) va `/api/` dagi `60s`
ishlayapti. Yo'lni tekshiring: `/api/v1/lessons/<ID>/assets` da `<ID>`
**faqat raqam** bo'lishi kerak (regex `[0-9]+`).

### "Videoda oldinga o'ta olmayman (seek ishlamaydi)"

`Range` so'rovi `206` qaytarmayapti degani:

```bash
curl -s -D - -o /dev/null -H "Authorization: Bearer $TOKEN" \
     -H 'Range: bytes=100-199' https://<domen>/api/v1/lessons/assets/1 | head -20
```

Kutilgan: `HTTP/1.1 206`, `Content-Range: bytes 100-199/<TOTAL>`,
`Accept-Ranges: bytes` va **`Content-Encoding` BO'LMASLIGI**.

* `200` kelsa (206 emas) — so'rov `/api/` blokida qolgan yoki `Range`
  sarlavhasi backendga yetmagan (`proxy_set_header` to'plami buzilgan).
* `Content-Encoding: gzip` ko'rinsa — kimdir `gzip_types` ga
  `application/octet-stream` yoki `video/*` qo'shgan: **darhol olib
  tashlansin**, gzip bayt oraliqlarini buzadi (9.2.1).
* Video sekin boshlansa/seek qotib qolsa — `proxy_buffering off` oqim
  blokidan tushib qolgan (javob temp faylga spool bo'lyapti).

### "SSH kira olmayapman"

Provayder konsoli (VNC/rescue) orqali kiring va:

```bash
sudo ufw status
sudo ss -tlnp | grep ssh
sudo journalctl -u ssh -u ssh.socket -n 50
sudo sshd -t
```

Eng ko'p uchraydigan sabab: `ssh.socket` override yozilmagan, port
o'zgarmagan, lekin UFW'da 22 yopilgan (1.3-bo'lim).

### "Disk to'ldi"

```bash
sudo du -sh /var/lib/docker/* | sort -h
sudo du -sh /var/lib/docker/containers/*/*-json.log | sort -h | tail
docker system df

# Ishlatilmayotgan image/volume (⚠️ nomsiz volume'lar ham o'chadi!)
docker system prune -a --volumes --dry-run    # avval KO'RING
```

Log rotation sozlanmagan bo'lsa — 2.4-bo'lim.

### "`FATAL: sorry, too many clients already`"

Npgsql pool `max_connections` dan oshib ketgan — 6.2-bo'limdagi
`Maximum Pool Size` ogohlantirishiga qarang.

```bash
docker compose exec -T postgres psql -U zinnur -d zinnur \
  -c "SELECT count(*) FROM pg_stat_activity;" -c "SHOW max_connections;"
```

### "Davomat noto'g'ri hisoblanyapti"

Ikkita ehtimol:

1. Redis `allkeys-lru` bilan presence o'chirilgan — 6.3-bo'lim.
2. SPEC 3-bo'limidagi `FirstJoinAt` / `LastJoinAt` qoidasi buzilgan
   (kod muammosi, infratuzilma emas).

```bash
docker compose exec -T redis redis-cli CONFIG GET maxmemory-policy
docker compose exec -T redis redis-cli INFO stats | grep evicted_keys
```

---

## Ilova A — SPEC bo'yicha risklar

Quyidagilar `docs/SPEC.md` da yozilgan, lekin **self-hosted LiveKit prod'i
uchun xavfli yoki yetishmayotgan** joylar. SPEC majburiy shartnoma bo'lgani
uchun bu yerda faqat **qayd etilgan** — o'zgartirish SPEC egasining qaroriga
bog'liq.

> **2026-08-22 auditi.** 1, 3, 4 va 9-risklar yopildi (jadvalda ✅ bilan
> belgilangan). 3 va 9 endi **hujjatdagi maslahat emas, majburlanadigan
> qoida**: `ProductionSecretsGuard` ishga tushishda tekshiradi va namuna
> qiymat topilsa ilovani **umuman ko'tarmaydi**.
>
> Qolgan risklar (2, 5, 6, 7, 8, 10, 11, 12) — hamon ochiq va ular asosan
> **sozlama yoki sig'im qarori**, kod emas.

| # | Risk | Nima bo'ladi | Tavsiya |
|---|---|---|---|
| **1** | ~~**`LiveKit__Url` bitta o'zgaruvchi**~~ | ~~Prod'da `ws://livekit:7880` klientga qaytsa — brauzer mixed content sababli **bloklaydi**~~ | ✅ **HAL QILINGAN.** Manzillar ajratilgan: `LiveKit__Url` (ichki, konteyner tarmog'i) va `LiveKit__PublicUrl` (brauzerga). Kod `LiveKitOptions.EffectivePublicUrl` orqali klientga DOIM ikkinchisini beradi; `PublicUrl` bo'sh bo'lsa dev qulayligi uchun birinchisiga tushadi |
| **2** | **LiveKit bridge tarmoqda ICE nomzodlarini noto'g'ri e'lon qiladi** | Xona ochiladi, ishtirokchilar ko'rinadi, **media umuman ulanmaydi** | `network_mode: host` yoki `use_external_ip: true` (9.3) |
| **3** | **`LIVEKIT_KEYS=devkey: …`** | `devkey` — LiveKit misollaridagi ommaviy qiymat. Secret sizib chiqsa, kimdir **istalgan xonaga host huquqi bilan** kiradi | ✅ **MAJBURLANADI (2026-08-22).** `ProductionSecretsGuard` `Production` da `devkey` ni ko'rsa ilovani ISHGA TUSHIRMAYDI. Kalit nomini 7.1 bo'yicha yarating |
| **4** | ~~**LiveKit token TTL 6 soat**~~ (SPEC 4-bo'lim) | ~~Guruhdan chiqarilgan o'quvchi **6 soat davomida** xonaga kira oladi~~ | ✅ **HAL QILINGAN (2026-08-22).** Muddat endi darsga bog'langan: `LiveSessionService.JoinTokenTtl` = dars tugashi + 30 daqiqa (eng kami 15 daq, eng ko'pi 6 soat). Zaxira qiymat ham 6 → 2 soatga tushirildi. ⚠️ Tavsiyaning ikkinchi yarmi — **xona tugaganda LiveKit API orqali yopish** — hali BAJARILMAGAN |
| **5** | ~~**Recording uchun `egress` xizmati yo'q**~~ | Backend'da yozuv TO'LIQ yozilgan edi (FAZA 5.3), lekin yozuvni BAJARADIGAN xizmat compose'da yo'q edi. Oqibati: `api` "yozuvni boshla" deb so'rardi, LiveKit "egress xizmati javob bermadi" derdi, dars esa o'z yo'lida davom etardi — ya'ni **yozuv hech qachon paydo bo'lmasdi va buni hech kim sezmasdi** | ✅ **YOPILDI (2026-08-24).** `livekit-egress` xizmati qo'shildi (dev + prod), fayl to'g'ridan **R2** ga yoziladi (7.1.2). Kvota: `EGRESS_CPUS=3.0` ≈ **2 parallel yozuv**. 8 vCPU serverda bir vaqtda **ko'pi bilan 2-3 yozuv** |
| **6** | **TURN yo'q** | 7881 TCP fallback ko'p holatni qoplaydi, lekin faqat 443'ga ruxsat beradigan qattiq korporativ proxy ortidan ulanib bo'lmaydi | LiveKit'ning ichki TURN'ini yoqish. **Lekin 443 nginx tomonidan band** — TURN/TLS uchun alohida IP yoki 5349 port kerak |
| **7** | **Redis bitta instance, uch vazifada** (kesh + presence + SignalR backplane) | `maxmemory` bosimi yoki `allkeys-lru` presence'ni **jimgina** o'chiradi → davomat buziladi | `volatile-lru` yoki `noeviction` (6.3); kelajakda ajratish |
| **8** | **Connection string'da pool sozlamasi yo'q** | Npgsql default `Maximum Pool Size=100`; ikkinchi `api` replikasi qo'shilsa `too many clients` | `Maximum Pool Size=40` (6.2) |
| **9** | **`Cors__AllowedOrigins__0=http://localhost:5173`** | Prod'da qolib ketsa — frontend API'ga ulanolmaydi (yoki xavfsizlik teshigi) | ✅ **MAJBURLANADI (2026-08-22).** `ProductionSecretsGuard` `Production` da `localhost` / `127.0.0.1` / `0.0.0.0` / `::1` ni ko'rsa ilova ko'tarilmaydi. `.env` da `https://app.domen.uz` (7.1) |
| **10** | **UDP mux — bitta port, bitta soket** | 200 foydalanuvchida yaxshi ishlaydi (SPEC to'g'ri aytgan), lekin ~500 dan keyin bitta soketning o'qish sikli bitta CPU yadrosiga tayanadi | 500+ ga chiqishda port diapazoni yoki ikkinchi node |
| **11** | **`web` va `api` alohida host portlarida** (5173 / 5080) | Ikki xil origin → CORS va cookie murakkabligi; `0.0.0.0` ga bog'lansa Docker UFW'ni chetlab o'tadi (3.3) | Host nginx orqali **bitta origin** (`app.domen.uz`), portlar `127.0.0.1` da |
| **12** | **Migratsiyalarni kim qo'llashi aytilmagan** | Bir necha replikada avtomatik migratsiya deadlock beradi | Deploy quvurida aniq qadam (7.2) |

---

## Tez ma'lumotnoma

```bash
# Deploy
cd /opt/zinnur && sudo ./infra/scripts/backup-db.sh && git pull \
  && docker compose build --pull \
  && docker compose run --rm api dotnet ef database update \
  && docker compose up -d

# Holat
docker compose ps && docker stats --no-stream && df -h / && free -h

# Loglar
docker compose logs -f --tail=100 api livekit

# Tuning tekshiruvi
sysctl net.core.rmem_max net.core.somaxconn fs.file-max
systemctl show docker -p LimitNOFILE
docker run --rm alpine sh -c 'ulimit -n'

# Firewall
sudo ufw status numbered && sudo iptables -S ZINNUR-DOCKER

# TLS
sudo certbot certificates && sudo certbot renew --dry-run

# Backup
sudo ./infra/scripts/backup-db.sh && ls -lht /var/backups/zinnur | head
```
