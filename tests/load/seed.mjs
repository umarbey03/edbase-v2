/**
 * ZIN-NUR — yuklama testi uchun HAQIQIY o'quvchilarni tayyorlaydi (idempotent).
 *
 * ========================================================================
 * NIMA UCHUN BU FAYL KERAK BO'LDI
 * ========================================================================
 * Yuklama skripti ilgari BITTA admin tokenini 200 klientga ulashardi. Server
 * esa ikki narsani ham FOYDALANUVCHI bo'yicha kalitlaydi:
 *
 *   • rate-limit  — `chatrate:{sessionId}:{userId}`  (LiveClassHub.SendMessage)
 *   • presence    — `presence.AddAsync(sessionId, entry)`, entry kaliti UserId
 *
 * Ya'ni bitta token bilan o'lchov IKKI joyda yolg'on chiqadi:
 *
 *  1) 200 ta ulanish bitta "1 xabar / 2 sekund" budjetini bo'lishadi.
 *     Amalda tekshirildi: 5 klient 20 sekundda 8 xabar yubordi va 12 tasi
 *     rate-limit bo'ldi — ya'ni chat kechikishi deyarli o'lchanmaydi.
 *
 *  2) Presence to'plamida 200 ta yozuv o'rniga BITTA yozuv qoladi. Natijada
 *     `JoinSession` javobi (to'liq ro'yxat!) va delta broadcast REAL
 *     narxidan bir necha barobar arzon ko'rinadi — aynan o'sha narx esa
 *     "200 kishi bitta xonada" da'vosining o'zagi.
 *
 * Shuning uchun har klient O'Z foydalanuvchisi va O'Z tokeni bilan ulanadi.
 *
 * ========================================================================
 * NIMA UCHUN PAROL BILAN KIRISH (token o'zimiz imzolamaymiz)
 * ========================================================================
 * Tokenni `Jwt:Secret` bilan lokal imzolash tezroq bo'lardi, lekin u holda
 * skript ishlab turgan tizimning haqiqiy kirish yo'lini chetlab o'tardi va
 * sirni fayldan o'qishga majbur bo'lardi. Bitta kirish ~120 ms
 * (BCrypt WorkFactor=11), 200 ta kirish esa cheklangan parallellik bilan
 * ~3 sekund — o'lchovdan OLDIN tugaydi va natijaga ta'sir qilmaydi.
 */

const PREFIX = 'zload';
const DOMAIN = 'zinnur.test';

/** Yuklama foydalanuvchilarining paroli — bu HAQIQIY tizimda ishlatilmaydi. */
export const LOAD_PASSWORD = 'ZinnurLoad!2345';

const emailFor = (i) => `${PREFIX}-${String(i).padStart(4, '0')}@${DOMAIN}`;
const nameFor = (i) => `Yuklama Oquvchi ${i}`;

// ---------------------------------------------------------------- HTTP yordamchi

const call = async (api, path, { token, method = 'GET', body } = {}) => {
  const res = await fetch(`${api}${path}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });

  const text = await res.text();
  const data = text ? JSON.parse(text) : null;

  if (!res.ok) {
    const detail = data?.detail ?? data?.title ?? text.slice(0, 200);
    const error = new Error(`${method} ${path} -> ${res.status}: ${detail}`);
    error.status = res.status;
    throw error;
  }

  return data;
};

/**
 * Cheklangan parallellik. 200 so'rovni bir zumda otish lokal port va
 * Postgres ulanish hovuzini (30) keraksiz band qiladi — bu yuklama testi
 * EMAS, shunchaki tayyorgarlik.
 */
const mapLimit = async (items, limit, fn) => {
  const out = new Array(items.length);
  let next = 0;

  const worker = async () => {
    while (next < items.length) {
      const i = next++;
      out[i] = await fn(items[i], i);
    }
  };

  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return out;
};

// ---------------------------------------------------------------- nishon dars

/**
 * Yuklama uchun dars tanlaydi.
 *
 * KURATOR guruhi ATAYLAB chetlab o'tiladi: unga o'quvchi TO'G'RIDAN-TO'G'RI
 * qo'shilmaydi (`GroupService.EnsureAcceptsDirectMembers` 409 beradi) — uning
 * o'quvchilari bog'langan ustoz guruhlaridan keladi.
 */
export const pickTarget = async (api, token) => {
  if (process.env.SESSION_ID && process.env.GROUP_ID) {
    return {
      sessionId: Number(process.env.SESSION_ID),
      groupId: Number(process.env.GROUP_ID),
      title: '(env orqali berilgan)',
    };
  }

  const groups = await call(api, '/api/v1/groups?page=1&pageSize=100', { token });

  const usable = new Map(
    groups.items
      .filter((g) => g.isActive && g.type !== 'Curator')
      .map((g) => [g.id, g]),
  );

  const sessions = await call(api, '/api/v1/live-sessions', { token });

  // Jonli dars birinchi navbatda — u eng real stsenariy.
  const ordered = [...sessions].sort(
    (a, b) => (b.status === 'Live') - (a.status === 'Live'),
  );

  const target = ordered.find((s) => usable.has(s.groupId));

  if (!target) {
    throw new Error(
      'Yuklama uchun mos dars topilmadi: kurator bo\'lmagan FAOL guruhda dars yo\'q. '
      + 'SESSION_ID va GROUP_ID ni qo\'lda bering.',
    );
  }

  return {
    sessionId: target.id,
    groupId: target.groupId,
    title: `${target.title} (${target.status})`,
  };
};

// ---------------------------------------------------------------- foydalanuvchilar

/** Mavjud yuklama foydalanuvchilarini email -> id xaritasi qilib qaytaradi. */
const existingUsers = async (api, token) => {
  const found = new Map();

  for (let page = 1; ; page++) {
    const res = await call(
      api,
      `/api/v1/users?search=${PREFIX}&page=${page}&pageSize=100`,
      { token },
    );

    for (const u of res.items) found.set(u.email, u.id);

    if (page >= res.totalPages || res.items.length === 0) break;
  }

  return found;
};

/**
 * `count` ta o'quvchi MAVJUDLIGINI kafolatlaydi va guruhga a'zo qiladi.
 * Idempotent: ikkinchi yugurtirishda hech nima yaratilmaydi.
 */
export const ensureUsers = async (api, token, count, groupId, onProgress) => {
  const found = await existingUsers(api, token);

  const wanted = Array.from({ length: count }, (_, i) => i + 1);
  const missing = wanted.filter((i) => !found.has(emailFor(i)));

  let created = 0;

  await mapLimit(missing, 10, async (i) => {
    const res = await call(api, '/api/v1/users', {
      token,
      method: 'POST',
      body: {
        fullName: nameFor(i),
        email: emailFor(i),
        role: 'Student',
        password: LOAD_PASSWORD,
        isActive: true,
      },
    });

    found.set(emailFor(i), res.user.id);
    onProgress?.('yaratildi', ++created, missing.length);
  });

  const users = wanted.map((i) => ({
    index: i,
    email: emailFor(i),
    id: found.get(emailFor(i)),
  }));

  // ---- guruhga a'zolik ----
  const members = await call(api, `/api/v1/groups/${groupId}/members`, { token });
  const active = new Set(
    members.filter((m) => m.status === 'Active').map((m) => m.studentId),
  );

  const toAdd = users.filter((u) => !active.has(u.id));
  let added = 0;

  await mapLimit(toAdd, 10, async (u) => {
    try {
      await call(api, `/api/v1/groups/${groupId}/members`, {
        token,
        method: 'POST',
        body: { studentId: u.id },
      });
    } catch (e) {
      // 409 = allaqachon a'zo (parallel qo'shishda bo'lishi mumkin) — zararsiz.
      if (e.status !== 409) throw e;
    }

    onProgress?.('a\'zo qilindi', ++added, toAdd.length);
  });

  return { users, created, added };
};

/** Har foydalanuvchi uchun HAQIQIY kirish tokeni oladi. */
export const loginAll = async (api, users, onProgress) => {
  let done = 0;

  return mapLimit(users, 20, async (u) => {
    const res = await call(api, '/api/v1/auth/login', {
      method: 'POST',
      body: { email: u.email, password: LOAD_PASSWORD },
    });

    onProgress?.(++done, users.length);
    return { ...u, token: res.accessToken, fullName: res.user.fullName };
  });
};
