# Plan — uninterrupted audio in live lessons

Status: Phases 0–3 implemented 2026-09-14 on branch `fix/yozuv-xona-qayta-ochilishi`,
uncommitted, NOT deployed. Phase 3 also added Screen Wake Lock ahead of the
measurement (see `docs/ASSUMPTIONS.md`, 2026-09-14).

## Goal

Voice must keep flowing — the teacher's **and** the students' — including the
moment a teacher asks a student to turn on the camera and answer. Video is the
first thing to sacrifice on a weak link; audio is the last.

## What is actually broken (measured, LiveKit logs 2026-09-09..14)

Root cause is students' mobile connectivity — the server is idle, teachers do
not drop, 2 students produce ~46% of incidents. But there are two different
failures and only one of them is about bitrate:

| Failure | Evidence | Lowering bitrate helps? |
|---|---|---|
| **Slow channel** | channel estimate on weak students: median 279 kbps, as low as 38–90 kbps; screen share lowest layer was ~1.75 Mbps | yes |
| **Connection lost for seconds** | 2026-09-14 lessons were audio-only (~50–110 kbps) and still had 18 full drops + ~50 resumes, each on an individual phone | no |

What turns a short network blip into a long silence is **our client**:

- after a full disconnect there is no automatic reconnect — the page shows
  "Qayta urinish" and waits. Gap from `PEER_CONNECTION_DISCONNECTED` to the
  student being back in the room: **median 34 s, p75 78 s, p90 189 s** (n=216);
- after rejoining, the microphone comes back **off**. Of 460 rejoins by
  participants who had published a mic before, 128 (28%) had not turned it
  back on 10 minutes later (some may have been muted on purpose — the server
  cannot tell mute from silence);
- phones leave and rejoin ~3 extra times per lesson per student (679 extra
  `CLIENT_REQUEST_LEAVE` in 5 days; Windows ~1). The cause is not visible
  server-side.

Checked and ruled out: turning a camera off does not cut audio (36 camera-off
events, 26 with no audio change at all; the leave rate after camera-off equals
the rate after camera-on, 17%).

## Phase 0 — written, waiting for deploy

Branch `fix/yozuv-xona-qayta-ochilishi` (uncommitted):

- recording: restart the room-audio mixer when the room reopens mid-lesson;
- screen share: layers 360p/3 fps/150 kbps, 720p/5 fps/500 kbps,
  1080p/15 fps/1.5 Mbps (was `original` 7 Mbps on Retina screens).

Deploy window: after 09:00 Tashkent, before the first lesson (composition runs
00:00–09:00, deploy restarts containers).

## Phase 1 — automatic reconnect and state restore (highest value)

File: `frontend/src/features/live-room/model/useLiveKitRoom.ts` (+ banner text
in `LiveRoomPage.vue`).

1. `onDisconnected` classifies the reason with a pure function:
   - **retry**: unknown reason, `SIGNAL_CLOSE`,
     `CONNECTION_TIMEOUT`, `MEDIA_FAILURE`, `STATE_MISMATCH`, `JOIN_FAILURE`,
     `SERVER_SHUTDOWN`;
   - **stop** (show message, no retry): `DUPLICATE_IDENTITY`,
     `PARTICIPANT_REMOVED`, `ROOM_DELETED`, `ROOM_CLOSED`, user pressed "Chiqish".
2. Retry loop with backoff 1 → 2 → 4 → 8 → 10 s (cap), a fresh token each
   attempt (`fetchLiveKitJoin` already runs inside `connect()`); stop when the
   session is over (join endpoint refuses) or the page is disposed.
3. Retry immediately on `window` `online` and on `visibilitychange` → visible
   while disconnected.
4. Remember the user's own intent (`wantMic`, `wantCamera`) — set only by the
   user's toggles, cleared by teacher moderation — and re-apply it after a
   successful reconnect. Screen share and book board are NOT restored
   automatically (they need a user gesture / picker).
5. UI: status `reconnecting` with "Qayta ulanmoqda…" instead of the red error;
   the "Qayta urinish" button appears only after ~30 s of failed attempts.

Acceptance:
- phone in a lesson, airplane mode for 20 s, then off → back in the room
  within ~5 s of the network returning, **mic in the same state as before**;
- teacher mutes a student, student's network drops and returns → student stays
  muted;
- "Chiqish" and "Siz boshqa oynada kirdingiz" never trigger a reconnect.

Post-deploy metric (LiveKit logs): drop → rejoin gap (baseline median 34 s),
share of rejoins with the mic re-published (baseline 72%).

## Phase 2 — lighter student camera, audio first

Same file.

1. Student camera: one layer, ~426×240 @ 15 fps, `maxBitrate` 150 kbps,
   `simulcast: false` (today 360p + 180p ≈ 610 kbps against a 279 kbps median
   channel). Teacher camera unchanged.
2. Encoding priority: audio `high`, student video `low`, so the browser's
   bandwidth estimator cuts video first.
3. Teacher audio 48 → 32 kbps (Opus 32 kbps mono still carries music
   acceptably); saves ~30 kbps with RED on every student's downlink.
   Student audio stays 24 kbps.
4. Keep RED and DTX on — RED roughly doubles audio bytes but is what survives
   packet loss on mobile; removing it would trade continuity for bandwidth.

Acceptance: Chrome DevTools throttled to 300 kbps up / 300 kbps down, student
turns the camera on and talks for 2 minutes → teacher hears continuous speech;
video may freeze.

## Phase 3 — find out why phones leave mid-lesson

1. Client events: `pagehide`, `visibilitychange`, `online`/`offline`,
   LiveKit `Disconnected` reason, reconnect attempts and outcome,
   `navigator.connection.effectiveType` when available, Telegram WebView flag.
2. Transport: `POST /api/v1/live-sessions/{id}/client-events`, authenticated,
   only for participants of that session (RBAC test), writes a structured
   Serilog line — no table, no migration; read with `docker logs` like the
   rest of the diagnostics.
3. After ~3 lesson days: rank causes, then fix the top one (candidates: Telegram
   WebView closing on app switch → "open in browser" hint; screen lock →
   Wake Lock API; our own code path).

## Order and checks

Deploy Phase 0 → Phase 1 → Phase 2 → Phase 3, each deployed and measured
separately so the metrics show which change helped.

Every phase: `eslint --max-warnings 0`, `vue-tsc`, `npm run build` (Docker
recipe), a real phone test on mobile data; Phase 3 also backend tests
(`dotnet test`) including the RBAC case. There is no frontend unit
test runner in this project — reconnect policy is kept as a pure function so
one can be added without reshaping the code.

---

## Measured after Phases 0–3 (night of 2026-09-15, 7 lessons, 82 users)

Phases 0–3 reached production at ~09:55 Tashkent on 2026-09-15, so every
lesson that day ran on them. Sources: LiveKit container log and the Phase 3
client events (2 463 of them — the telemetry works).

| Metric | Baseline | Measured | |
|---|---|---|---|
| drop → back in room, median | 34 s | **4.3 s** | ✅ |
| back within 5 s | — | 54% (139/256) | ✅ |
| Screen Wake Lock | absent | 584 acquired, 1 error | ✅ |
| student camera | 360p+180p ≈ 610 kbps | 426×240, 150 kbps, no simulcast | ✅ |
| mic re-published after a gap | 72% | **73%** | ❌ unchanged |
| connects per participant per lesson | ~4 (3 extra) | **3.2** (max 21) | ❌ unchanged |

Phase 1 and 2 did what they were written to do. The two metrics that did not
move are the ones this section exists for.

### What the telemetry showed that server logs could not

1. **The page is rebuilt, not reconnected.** 305 `page-closed` events, only
   79 preceded by a real "Chiqish". The repeating shape is
   `connected → wake-lock → page-closed (mic=true) → connected (mic=false)`.
   `wantMic` lived in the composable, so a remount erased it — which is why
   the mic-restored rate did not move: `restoreMedia()` was never reached.
   In the evening window 204 of those were in-SPA navigations (the student
   home page loaded 305 times) against 86 deliberate exits, on a route where
   Telegram shows the back button and Android maps the system back gesture
   to it.
2. **Background reconnects cannot work.** 44 of 53 attempts were made with
   the page hidden; 43 failed. Visible attempts: 7 of 9 succeeded. The failed
   hidden attempts still drove the backoff to its 10 s cap and tripped the
   30-second red-error banner, so the student returned to an error screen.
3. **A network blip logged students out.** `performRefresh` ended the session
   on any non-ok response, and `requestRaw` ended it whenever the refresh
   threw — including a plain network error. With a 15-minute access token
   each participant refreshes ~6 times per lesson (334 refreshes that
   evening, 8 genuine expiries), and each unlucky one produced exactly the
   remount above.
4. **Cost of the recording indicator.** `recording-status` was polled
   20 476 times in four hours — 75% of all API traffic — including while the
   page was hidden.

Everything else was healthy: zero unhandled server errors (the 500s in the
HTTP log are a Serilog artifact — `UseSerilogRequestLogging` sits inside
`ExceptionHandlingMiddleware`, so it records 500 for an exception the handler
then maps to 409/401), and the night composition ran 14/14 in 2 h 43 min of a
9-hour window.

## Phase 4 — stop losing the page (implemented 2026-09-16)

1. `liveMediaIntent.ts` — mic/camera intent in `localStorage`, per session,
   10-minute validity, restored on a first connect as well as a reconnect.
2. `http.ts` — the session ends only on 401/403 from the refresh endpoint.
3. `useLiveKitRoom.ts` — no reconnect attempts while `document.hidden`
   (re-check every 20 s, attempt counter untouched); returning to the page
   resets the backoff and the red-error window.
4. `LiveRoomPage.vue` — leaving a running lesson by navigation asks for
   confirmation; `page-closed` now carries the reason that started it.
5. `useRecordingIndicator.ts` — no background polling; refetch on focus.

Post-deploy metrics to read from the next night's log:
- share of rejoins with the mic re-published (baseline 73%);
- connects per participant per lesson (baseline 3.2);
- the `reason` distribution on `page-closed` — if `unknown` still dominates,
  the remount is not navigation and the cause is elsewhere;
- `recording-status` request count (baseline 20 476 per four hours).
