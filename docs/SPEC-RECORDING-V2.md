# SPEC — Lesson recording pipeline v2 (track capture + night composition)

> **Status:** contract. Agents implementing this cannot see the conversation that
> produced it. Everything needed is in this file. If something here contradicts
> the code, the code wins — stop and report, do not guess.
>
> **Scope note:** this document does **not** replace `docs/SPEC.md` (the
> project-wide contract, referenced from code comments as "SPEC 5-bo'lim",
> "SPEC 8-bo'lim", …). That file stays untouched. This file is additive and
> owns exactly one subsystem: lesson recording.

---

## 0. Ground rules that override assumptions

Read this section before anything else. Four of these were wrong in the task
briefing that produced this SPEC, and each one would have produced working code
that solves the wrong problem.

| Claim you may have been given | Reality in this repo |
|---|---|
| Frontend is React | **Vue 3.5 + TypeScript + Vite 6**, FSD layout (`app/`, `entities/`, `features/`, `widgets/`, `pages/`, `shared/`). No React anywhere. |
| Migrations are Alembic | **EF Core migrations**, `backend/src/Zinnur.Infrastructure/Persistence/Migrations/`. Procedure is `docs/MIGRATIONS.md` (runs through Docker — there is no local .NET SDK on the dev machine). |
| RBAC is `module:verb` permissions | There is **no permission registry**. Authorisation is role-based: `[Authorize(Roles = "Teacher,Assistant,Academic,Admin")]` plus per-object checks inside the service layer. Do not invent a permission system. |
| Storage config comes from env | In production the `Storage__*` env values are **empty**. Storage is read at runtime from the `AppSettings` table via `IRuntimeOptions<StorageOptions>` (`storage.service_url`, `storage.bucket`, `storage.access_key`, `storage.secret_key`). Never read `IConfiguration` for storage. |

**Layering** (enforced by project references, do not break):
`Zinnur.WebApi` → `Zinnur.Infrastructure` → `Zinnur.Application` → `Zinnur.Domain`.
Domain has no external dependencies. Application defines ports (interfaces),
Infrastructure implements them. HTTP, Twirp, S3 signing and ffmpeg process
handling are **Infrastructure only**.

**Reference implementation.** The closest existing module, and the one whose
shape every new file must copy:

- Entity + state machine → `backend/src/Zinnur.Domain/Entities/SessionRecording.cs`
- Port → `backend/src/Zinnur.Application/Recordings/Services/ILiveKitEgress.cs`
- Adapter → `backend/src/Zinnur.Infrastructure/Services/LiveKitEgressClient.cs`
- Background job → `backend/src/Zinnur.Application/Recordings/Jobs/RecordingWatchdogJob.cs`
- EF config → `backend/src/Zinnur.Infrastructure/Persistence/Configurations/SessionRecordingConfiguration.cs`
- Webhook → `backend/src/Zinnur.Application/Recordings/Services/RecordingWebhookHandler.cs`

**Language.** Identifiers, file names, this SPEC and commit messages in English.
User-facing strings (anything that reaches `SessionRecording.Error`, the admin
UI, or a toast) in **Uzbek** — that is what the existing code does and those
strings are read by staff. See §10 item 7 about code comments.

**Additive rule (from `CLAUDE.md`).** Do not rewrite `RecordingService`,
`RecordingWatchdogJob`, `AutoRecordingScheduler`, `LiveKitEgressClient` or
`R2RecordingStorage`. Add alongside. There are exactly **three** permitted edits
to existing behaviour, each listed explicitly in §5.9; anything beyond those is
out of scope.

---

## 1. Goal

Lesson recording today uses LiveKit `RoomCompositeEgress`: a headless Chrome +
Xvfb + real-time x264 encode per lesson, measured at 0.93–1.63 CPU cores
(peak 2.14) and 1.5–2.3 GB RAM. The production box is 4 vCPU / 8 GB, and the
egress worker is capped at `EGRESS_CPUS=2.0` with `room_composite_cpu_cost: 1.5`,
so **exactly one** recording fits. The real schedule peaks at **6** concurrent
recording-enabled groups; overlapping lessons fail silently with
"Yozuv xizmati javob bermadi (timeout)".

This SPEC replaces the capture step with two cheap live captures and one heavy
offline one:

- **Video** — LiveKit **`TrackEgress`** per host video track (camera, screen
  share). Passthrough remux, no Chrome, no transcoding, ~0.05 core per track.
- **Audio** — **one** audio-only `RoomCompositeEgress` per lesson, producing a
  single continuous mixed Opus file containing *everyone* in the room: teacher,
  screen-share audio and students. Audio-only room composite runs through the
  egress **SDK source**, not Chrome (§3.4).
- **Encoding** — a nightly composition job between 00:00 and 09:00
  Asia/Tashkent muxes those files into one mp4 at the recording's existing
  `ObjectKey`.

Because the night job has no real-time deadline it encodes at full source
resolution with a proper rate-controlled x264 pass, so the output is **better**
than today's 720p grid, not worse — and because the mixed audio file is one
continuous stream for the whole lesson, it is also the timeline spine that every
video segment aligns to (§9.1).

Audience: the academic department (who review every lesson for quality — the
existing `SessionReview` / R29 feature) and students catching up on a missed
lesson.

---

## 2. Data model

All times are `DateTimeOffset` in **UTC**. Money is not involved. There is no
soft delete in this project — deletes are real. Enum values are stored as `int`
and **new values are only ever appended**; existing numbers never change
(the rule is written on `RecordingStatus` and it is load-bearing: the numbers
are in production rows).

### 2.1 New enum — `RecordingPipeline`

`backend/src/Zinnur.Domain/Enums/RecordingPipeline.cs`

```
RoomComposite    = 0   // the existing LiveKit RoomCompositeEgress path
TrackComposition = 1   // TrackEgress capture + nightly ffmpeg composition
```

Used on both `SessionRecording.Pipeline` (which pipeline produced this row) and
`Group.RecordingPipeline` (which pipeline this group's lessons use — see 2.5).

### 2.2 New enum — `RecordingCompositionStatus`

`backend/src/Zinnur.Domain/Enums/RecordingCompositionStatus.cs`

```
Collecting = 0   // lesson is running (or just ended); track egresses still open
Queued     = 1   // every track reached a terminal state; waiting for the night window
Running    = 2   // a compositor holds the lease; ffmpeg is working
Completed  = 3   // final mp4 uploaded and verified; SessionRecording.Status = Completed
Failed     = 4   // gave up; SessionRecording.Status = Failed, reason in Error
```

`NULL` on rows with `Pipeline = RoomComposite`. A non-null value on a
`RoomComposite` row is a bug.

### 2.3 New enum — `RecordingTrackKind`

`backend/src/Zinnur.Domain/Enums/RecordingTrackKind.cs`

```
CameraVideo = 0   // LiveKit TrackSource CAMERA          — TrackEgress
ScreenVideo = 1   // LiveKit TrackSource SCREEN_SHARE    — TrackEgress
MicAudio    = 2   // LiveKit TrackSource MICROPHONE      — TrackEgress, fallback mode only
ScreenAudio = 3   // LiveKit TrackSource SCREEN_SHARE_AUDIO — TrackEgress, fallback mode only
RoomAudio   = 4   // whole-room mixed audio — audio-only RoomCompositeEgress, NOT a LiveKit track
```

Any other source (`UNKNOWN`, future values) is **ignored** — no row is created.

`RoomAudio` is the odd one out and the difference matters: it is not produced by
a LiveKit *track*, it is one audio-only room-composite egress covering the whole
room for the whole lesson. It therefore has no `TrackSid` and no participant of
its own — see §2.4 for the sentinel values.

Which audio kinds appear is decided by the `recordings.audio_capture_mode`
setting (§2.7) and the two modes are **mutually exclusive**:

| `audio_capture_mode` | Audio rows created | When |
|---|---|---|
| `RoomComposite` (default) | exactly one `RoomAudio` | normal operation — captures students |
| `TeacherTrack` | `MicAudio` + `ScreenAudio` (0–n each) | fail-fast fallback (§3.4b) — teacher only, no students |

🔴 **Never both.** Mixing a room-audio file (which already contains the teacher)
with a separate teacher-mic file would play the teacher's voice twice, slightly
offset — comb filtering that sounds like a broken microphone, not like an echo,
and which no one would diagnose quickly.

### 2.4 New table — `RecordingTracks`

Entity `backend/src/Zinnur.Domain/Entities/RecordingTrack.cs`, inherits
`BaseEntity` (`Id: long`, `CreatedAt`, `UpdatedAt?`).
EF config `backend/src/Zinnur.Infrastructure/Persistence/Configurations/RecordingTrackConfiguration.cs`.

One row = one raw object. Normally: 1 `RoomAudio` row + 1–2 video rows; a lesson
where the teacher reconnects twice and toggles screen share three times can have
10+ video rows and still exactly **one** `RoomAudio` row. **Multiple video
segments per lesson are the normal case, not an edge case; multiple audio rows
are not possible in the default mode.**

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `Id` | `bigint` PK identity | no | — | `BaseEntity` |
| `RecordingId` | `bigint` | no | — | FK → `SessionRecordings.Id`, **cascade delete** |
| `TrackSid` | `varchar(64)` | no | — | LiveKit `TR_…`, or the sentinel `"ROOM"` for the `RoomAudio` row |
| `ParticipantIdentity` | `varchar(64)` | **yes** | `NULL` | LiveKit identity = `User.Id` as invariant string (`LiveSessionService.CreateJoinTokenAsync`). **`NULL` for `RoomAudio`** — a whole-room mix belongs to no participant, and writing `""` there would be a lie that some future `WHERE` clause believes |
| `Kind` | `int` | no | — | `RecordingTrackKind` |
| `MimeType` | `varchar(64)` | yes | `NULL` | e.g. `video/vp8`, `audio/opus`; from the `track_published` payload |
| `ObjectKey` | `varchar(500)` | no | — | raw object key, see 2.6. Length matches `AssignmentConfiguration.ObjectKeyMaxLength = 500` |
| `EgressId` | `varchar(100)` | yes | `NULL` | LiveKit egress id for **this track** |
| `Status` | `int` | no | `0` | reuses `RecordingStatus` (`Requested/Starting/Active/Completed/Failed`) — same five meanings, same webhook transitions, so no second enum |
| `StartedAt` | `timestamptz` | yes | `NULL` | from `egress_started` / `egress_ended` (`started_at`, nanoseconds) |
| `EndedAt` | `timestamptz` | yes | `NULL` | from `egress_ended` (`ended_at`, nanoseconds) |
| `SizeBytes` | `bigint` | yes | `NULL` | from `file.size` |
| `DurationSeconds` | `int` | yes | `NULL` | from `file.duration` (ns) or `EndedAt - StartedAt` |
| `ProbedDurationMs` | `int` | yes | `NULL` | measured by `ffprobe` at composition time; drift check, see §9.1 |
| `Attempts` | `int` | no | `0` | egress start attempts |
| `LastAttemptAt` | `timestamptz` | yes | `NULL` | |
| `StopRequestedAt` | `timestamptz` | yes | `NULL` | same purpose as on `SessionRecording`: don't re-send `StopEgress` |
| `Error` | `varchar(500)` | yes | `NULL` | Uzbek, staff-facing |

`RecordingTrack` exposes `public const string RoomAudioSid = "ROOM";`. LiveKit
track sids always begin with `TR_`, so the sentinel can never collide with a real
one, and the existing unique index does double duty: it guarantees **at most one
`RoomAudio` row per recording**, which is what stops a re-delivered
`room_started` webhook from starting a second mixer.

Indexes:

- `UX_RecordingTracks_RecordingId_TrackSid` — **unique** on `(RecordingId, TrackSid)`. This is the idempotency guard: LiveKit re-delivers `track_published`, and it is also the single-mixer guard described above.
- `UX_RecordingTracks_EgressId` — **unique** on `EgressId`. Postgres allows many `NULL`s in a unique index, so unstarted rows are fine. The webhook finds the row by this column.
- `IX_RecordingTracks_RecordingId_Kind_StartedAt` — the composition job's read path.
- `IX_RecordingTracks_Status_LastAttemptAt` — the starter/reconciler job's read path.

Relationship: `SessionRecording.Tracks` → `ICollection<RecordingTrack>`,
`RecordingTrack.Recording` → `SessionRecording?`.
`DeleteBehavior.Cascade` (a recording row without its tracks is meaningless).

`IApplicationDbContext` gains `DbSet<RecordingTrack> RecordingTracks { get; }`.

### 2.5 Changed table — `SessionRecordings` (additive columns only)

No existing column changes type, nullability or meaning. No existing row's data
is rewritten except the one-time repair in 2.8.

| New column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `Pipeline` | `int` | no | `0` | `RecordingPipeline`. `0` = existing behaviour, so every existing row is correct without a data migration |
| `CompositionStatus` | `int` | yes | `NULL` | `RecordingCompositionStatus`; `NULL` for `RoomComposite` |
| `CompositionAttempts` | `int` | no | `0` | counts **real failures** (ffmpeg non-zero exit, probe failure, upload failure). Max 3 |
| `CompositionInterruptions` | `int` | no | `0` | counts **09:00 window cut-offs** only. Max 10 |
| `CompositionStartedAt` | `timestamptz` | yes | `NULL` | when the current/last `Running` began |
| `CompositionFinishedAt` | `timestamptz` | yes | `NULL` | terminal timestamp |
| `CompositionLeaseUntil` | `timestamptz` | yes | `NULL` | worker lease; see §4.4 |
| `CompositionError` | `varchar(500)` | yes | `NULL` | last composition failure/interruption reason, Uzbek |
| `RawPurgedAt` | `timestamptz` | yes | `NULL` | when the raw objects were deleted |

New index:

- `UX_SessionRecordings_SessionId_Pipeline_Active` — **unique** on
  `(SessionId, Pipeline)` with filter `"Status" < 3`
  (`Completed = 3`, `Failed = 4`, so `< 3` means "not terminal").
  This makes "at most one live recording attempt per session per pipeline" a
  database invariant instead of a service-layer convention, which matters now
  that two pipelines write rows for the same session.

**Why counters and a lease on the row instead of a `RecordingCompositionJobs`
table.** The codebase's own precedent points the other way — `SessionRecording`
exists as a separate table precisely so that each *attempt* has a row. That
argument does not transfer here: one `SessionRecording` produces exactly one
final artefact at exactly one `ObjectKey`, so composition state is a property of
that artefact, not a stream of independent attempts. A separate table would need
its own unique constraint to stop two workers claiming the same recording, which
is exactly what a lease column gives for free, and it would produce orphan rows
whenever a recording is deleted. Per-attempt history has no consumer: the admin
page shows recordings, not encoder runs.

**Why `CompositionAttempts` and `CompositionInterruptions` are two counters.**
An interruption is not a failure — it means the queue was longer than the night.
Conflating them would mark a perfectly healthy recording `Failed` after five busy
nights. Keeping them apart means a job that genuinely crashes dies after 3 tries,
while a job that keeps losing the race dies only after 10 nights (by which point
something is wrong with the schedule, not the job).

### 2.6 Changed table — `Groups` (one additive column)

| New column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `RecordingPipeline` | `int` | no | `0` | `RecordingPipeline`. `0` = `RoomComposite` = today's behaviour for all 33 existing groups |

This column is **independent of `Group.RecordEnabled`**. `RecordEnabled` answers
"is this group recorded at all"; `RecordingPipeline` answers "by which
mechanism". A group with `RecordEnabled = false` is never recorded regardless of
pipeline. Put the new property directly under `RecordingPipeline`'s natural
neighbours in `Group.cs` (`RecordEnabled`, `RecordingsVisibleToStudents`).

**Parallel A/B mode is not a third enum value.** During Phase 3 group 7 (`ATF-97`)
runs *both* pipelines. That is expressed by a separate global setting
(`recordings.track_pipeline_shadow_groups`, 2.7) rather than a
`RecordingPipeline.Both` value, because "run both" is a temporary rollout state,
not a property of a group, and baking it into a persisted enum would leave a
permanent value that means nothing after Phase 4.

### 2.7 New settings (`SettingsRegistry`)

Add to `backend/src/Zinnur.Application/Settings/SettingsRegistry.cs` in the
existing "DARS YOZUVLARI (R5)" block, and add matching constants to
`SettingsRegistry.Keys`. All are `Source = SettingSource.Database` (editable from
the admin panel, no deploy needed). `SettingsRegistry` validates uniqueness of
`Key`/`StorageKey` at startup — a duplicate crashes the app, which is the
intended behaviour.

| Key | Kind | Default | Group | Meaning |
|---|---|---|---|---|
| `recordings.track_pipeline_enabled` | `Toggle` | `false` | `Content` | Global kill switch. If `false`, **no** `TrackComposition` recording is created and no composition job claims work, whatever the group column says. Emergency brake. |
| `recordings.track_pipeline_shadow_groups` | `Text` | `""` | `Content` | Comma-separated group ids that run **both** pipelines in parallel for A/B comparison. Phase 3 value: `7`. Empty = nobody. Max 100 chars. Invalid/unknown ids are ignored with a warning log, never an exception. |
| `recordings.compose_window_start` | `Text` | `"00:00"` | `Content` | Night window start, `HH:mm`, **Asia/Tashkent** (via `IScheduleTimeZoneProvider`). |
| `recordings.compose_window_end` | `Text` | `"09:00"` | `Content` | Night window end, `HH:mm`, Asia/Tashkent. Hard stop. |
| `recordings.compose_preset` | `Choice` | `"medium"` | `Content` | x264 preset. Choices: `veryfast`, `faster`, `fast`, `medium`, `slow`. See §4.6 and Decision 2 (§10) for why the default is not `slow`. |
| `recordings.compose_crf` | `Number` | `"21"` | `Content` | x264 CRF, min 16, max 28. |
| `recordings.audio_capture_mode` | `Choice` | `"RoomComposite"` | `Content` | `RoomComposite` = one mixed audio-only room composite, **includes students**. `TeacherTrack` = teacher mic + screen audio via `TrackEgress`, **no students**. This is the fail-fast fallback of §3.4b and the reason it is a setting: flipping it takes effect on the next lesson with no deploy. |
| `recordings.compose_audio_offset_ms` | `Number` | `"0"` | `Content` | Calibration constant added to the audio delay at composition time. Min `-2000`, max `2000`. Exists because a *constant* A/V offset is a fixed pipeline latency that one number fixes, whereas accumulating drift is a design fault — §9.1 tells them apart. Leave at `0` until §9.1 measures otherwise. |

"Strictest wins" is the established pattern in this codebase (see
`IRecordingService.SetVisibilityAsync`). It applies here too.

**Which rows `AutoRecordingScheduler.EnqueueAsync` creates — the complete truth
table.** No other combination exists; implement it exactly, do not simplify.

| `Group.RecordEnabled` | `track_pipeline_enabled` | group in shadow list | `Group.RecordingPipeline` | Rows created |
|---|---|---|---|---|
| `false` | — | — | — | **none** (unchanged behaviour) |
| `true` | `false` | — | — | 1 × `RoomComposite` |
| `true` | `true` | **yes** | either | **2 rows**: 1 × `RoomComposite` + 1 × `TrackComposition` |
| `true` | `true` | no | `RoomComposite` | 1 × `RoomComposite` |
| `true` | `true` | no | `TrackComposition` | 1 × `TrackComposition` |

The shadow list beats the group column, and it is the **only** way to get two
rows. A `TrackComposition` row is created with
`CompositionStatus = Collecting`, `EgressId = NULL`, and
`ObjectKey = storage.BuildObjectKey(session.Id)`.

Both rows are created in the same `SaveChangesAsync` as today (the scheduler is
called from `LiveSessionService.StartAsync` and must stay off the egress path —
it only writes rows, it never talks to LiveKit).

### 2.8 Object key layout

Final output — **unchanged**, still produced by
`IRecordingStorage.BuildObjectKey(sessionId)`:

```
recordings/{yyyy-MM}/{sessionId}/{16 hex chars}.mp4
```

Raw track objects — new, a **separate root prefix** so no existing tooling,
lifecycle rule or admin listing sees them:

```
raw/{sessionId}/{recordingId}/{trackSid}.{ext}
```

- `recordingId` in the path (not just `sessionId`) is what keeps the two
  pipelines from colliding during shadow mode and keeps a retried recording from
  overwriting an earlier one's raw files.
- For the `RoomAudio` row, `{trackSid}` is the sentinel, so the key is literally
  `raw/{sessionId}/{recordingId}/ROOM.ogg`. Its extension is **not** predicted —
  we choose `EncodedFileType: OGG` in the request (§3.4), so `.ogg` is a fact,
  not a guess.
- For track rows, `{ext}` is predicted from the `track_published` `mime_type` at
  insert time:

  | mime_type | ext | note |
  |---|---|---|
  | `video/vp8` | `.webm` | **the expected case** — the frontend does not set `videoCodec`, so livekit-client publishes VP8 |
  | `video/h264` | `.mp4` | |
  | `video/vp9` | `.webm` | |
  | `audio/opus` | `.ogg` | only reachable in `TeacherTrack` mode |
  | absent / anything else | `.webm` for video, `.ogg` for audio | fallback |

- **The prediction is not trusted.** `egress_ended` returns the real
  `file.filename`; if it differs from `ObjectKey`, overwrite `ObjectKey` with the
  returned value (this is exactly what `SessionRecording.MarkCompleted` already
  does for the composite path). Log at `Warning` when they differ, so the mapping
  table above can be corrected from production evidence after the first lesson.
  Add `IRecordingStorage.BuildRawObjectKey(long sessionId, long recordingId, string trackSid, string extension)`
  next to `BuildObjectKey`; key layout is the storage adapter's business, per the
  reasoning already written on `IRecordingStorage.BuildObjectKey`.

Raw objects are deleted after a successful composition (§4.5). They are **never**
served to a user and never get a view link.

### 2.9 Migration

**One** EF Core migration, named `AddTrackCompositionPipeline`, in
`backend/src/Zinnur.Infrastructure/Persistence/Migrations/`. Generate it through
Docker exactly as `docs/MIGRATIONS.md` describes (there is no local .NET SDK).

`Up`, in this order:

1. **Data repair, before the new unique index.** The index
   `UX_SessionRecordings_SessionId_Pipeline_Active` would fail on legacy rows if
   any session ever ended up with two non-terminal recordings. Run first:

   ```sql
   UPDATE "SessionRecordings" r
   SET "Status" = 4,
       "Error"  = 'Eski, yakunlanmagan yozuv urinishi — yangi indeks talabi bilan yopildi.',
       "UpdatedAt" = now()
   WHERE r."Status" < 3
     AND EXISTS (
       SELECT 1 FROM "SessionRecordings" o
       WHERE o."SessionId" = r."SessionId"
         AND o."Status" < 3
         AND o."Id" > r."Id");
   ```

   (keeps the newest non-terminal row per session, fails the older ones).
   Before running this in production, take the mandatory backup —
   `./infra/scripts/backup-db.sh` — and record the affected row count.
   Expected count: **0**, because `AutoRecordingScheduler` already prevents this.
   A non-zero count is worth reporting, not worth stopping for.
2. `AddColumn` × 9 on `SessionRecordings` (2.5), each with the stated default.
3. `AddColumn` × 1 on `Groups` (2.6), default `0`.
4. `CreateTable RecordingTracks` (2.4) + its 4 indexes.
5. `CreateIndex UX_SessionRecordings_SessionId_Pipeline_Active` with
   `.HasFilter("\"Status\" < 3")`.

`Down` reverses 5 → 2. It does **not** undo step 1 (the repair is not
reversible and the rows are correctly `Failed`).

⚠️ Per `docs/MIGRATIONS.md`, the previous migration
(`20260828120000_AddEnrollmentApplications`) was **hand-written**, including its
`Designer.cs` and the model snapshot. Before generating this one, run the
"Tekshiruv" check described there: add a throwaway migration and confirm its
`Up`/`Down` are empty. If they are not, fix the snapshot first — otherwise this
migration will contain spurious drops. After autogeneration, **read the
migration file** and delete anything that is not in the list above.

---

## 3. Capture — what happens during the lesson

Two capture mechanisms run side by side during a lesson. Neither transcodes
video and neither launches Chrome.

### 3.1 What is captured

**Video — `TrackEgress`, host only.** Only tracks published by the **host**,
i.e. where the LiveKit participant `identity` equals `LiveSession.HostId`
rendered as an invariant string. `TrackEgress` is documented as passthrough:
"tracks are exported as is, without transcoding".

| LiveKit `track.source` | `RecordingTrackKind` | Typical | Cost |
|---|---|---|---|
| `CAMERA` | `CameraVideo` | 720p VP8 (frontend sets `videoCaptureDefaults: VideoPresets.h720`) | ~0.05 core |
| `SCREEN_SHARE` | `ScreenVideo` | up to 1080p15 VP8 | ~0.05 core |

**Audio — one audio-only `RoomCompositeEgress` per lesson.** One row
(`RecordingTrackKind.RoomAudio`), one continuous Opus/OGG file, covering the
whole room for the whole lesson: teacher mic, screen-share audio, and every
student who speaks. It is a *mix*, so it decodes and re-encodes audio — but that
is one stereo Opus stream, not video, and it runs on the SDK source with no
browser (§3.4).

In the default `RoomComposite` audio mode, `MICROPHONE` and `SCREEN_SHARE_AUDIO`
track publications create **no** rows — the mix already contains them, and
capturing them twice is the comb-filter bug described in §2.3.

### 3.2 Student audio — captured, via one mixed room track

**Decision (owner, 2026-09-05): student audio must be captured.** Today's
`RoomCompositeEgress` records mixed room audio, so student voices are in current
recordings; losing them would have been a real regression, and the owner ruled
that unacceptable. The mechanism is **not** one egress per student.

#### Rejected alternative: one `TrackEgress` per participant

This is the arithmetic that ruled it out, and it still stands:

1. **Memory, not CPU, is the wall.** LiveKit egress runs each job in its own
   handler process. A student's mic track is published the first time they
   unmute and then stays published (livekit-client mutes rather than unpublishes;
   `stopMicTrackOnMute` is not set), so the egress runs for the rest of the
   lesson. At the measured peak — 6 concurrent lessons, attendance up to 25 —
   ten speakers per lesson is 60 extra handler processes at roughly 50–150 MB
   each. That is 3–9 GB on an 8 GB box that already budgets 3 GB to the API and
   3 GB to egress. The failure mode is OOM-killing the SFU during a live lesson.
2. **Muted tracks produce holed files.** LiveKit stops sending packets while
   muted, and track egress writes only what it receives; it does not pad silence.
   Reconstructing ten students' mute/unmute timelines to sample accuracy is a
   far harder sync problem than anything else in this design.

#### Chosen mechanism: one audio-only room composite

One egress per **lesson**, not per participant. LiveKit's mixer does the summing
inside the SFU process, so:

- **1 process per lesson instead of ~12.** Memory is bounded and known.
- **1 continuous file with no holes.** Mute/unmute changes what is *in* the mix,
  not whether the file is being written. Silence is real silence, sample-aligned,
  for the entire lesson.
- **It is the sync spine.** A single unbroken audio timeline is exactly what the
  composition step wants; video segments are placed against it (§9.1). This is
  strictly better than the teacher-mic-with-holes design it replaces.

#### The premise this rests on, and how it is proved

An earlier draft rejected this option on the grounds that audio-only room
composite "still launches Chrome (~0.3–0.5 core, ~500 MB)". **That premise is
wrong for the audio-only case.** Verified directly against the image running in
production, `livekit/egress:v1.14`:

```
$ docker run --rm --entrypoint sh livekit/egress:v1.14 \
    -c "grep -aoE '[a-z_]*cpu_cost' /usr/bin/egress | sort -u"
audio_room_composite_cpu_cost
audio_web_cpu_cost
participant_cpu_cost
room_composite_cpu_cost
sdk_audio_room_composite_cpu_cost      <-- distinct SDK cost for this exact case
track_composite_cpu_cost
track_cpu_cost
web_cpu_cost

$ ... -c "grep -aoE 'EGRESS_SOURCE_TYPE_[A-Z]+' /usr/bin/egress | sort -u"
EGRESS_SOURCE_TYPE_SDK
EGRESS_SOURCE_TYPE_WEB

$ ... -c "grep -aoE 'config\.[A-Za-z]*Source[A-Za-z]*' /usr/bin/egress | sort -u"
config.IsSDKSourceRequest
config.ShouldUseSDKSource                <-- there is a runtime decision
config.SDKSourceParams
config.WebSourceParams
```

A separate `sdk_audio_room_composite_cpu_cost` key, a `ShouldUseSDKSource`
predicate and an `EGRESS_SOURCE_TYPE_SDK` value only coexist if audio-only room
composite can run **without Chrome**.

🔴 **This is still an inference from binary strings, not a measurement.** The
SPEC therefore treats "audio-only room composite uses the SDK source" as an
**assumption with a fail-fast gate** (§3.4) and a fallback that needs no deploy:
`recordings.audio_capture_mode = "TeacherTrack"` reverts to teacher-mic
`TrackEgress` — no student audio, but a working, cheap recording. Log the
outcome in `docs/ASSUMPTIONS.md` either way.

### 3.3 Track discovery — webhooks

**Prerequisite (being handled separately, do not re-spec):** LiveKit webhooks are
currently disabled in `infra/livekit/livekit.yaml` and must be enabled. The
watchdog bug where recordings are marked `Failed` after 10 minutes because
`egress_started` never arrives is being fixed in parallel. **This design does not
work without webhooks** — track discovery has no other real-time source.

Infrastructure work required (§5.8): a production-only
`infra/livekit/livekit.prod.yaml` mounted by `docker-compose.prod.yml`, because
LiveKit runs `network_mode: host` in production and cannot resolve the `api`
service name. The webhook URL must be `http://127.0.0.1:5080/api/v1/livekit/webhook`
and `webhook.api_key` must byte-for-byte equal `.env`'s `LIVEKIT_API_KEY`.
The dev file keeps `http://api:8080/api/v1/livekit/webhook`.

🔴 The base file mounts `./infra/livekit/livekit.yaml:/etc/livekit/livekit.yaml:ro`
and Compose **merges** volume lists rather than replacing them. Two mounts on the
same container path is a startup failure. The prod overlay must therefore use
`volumes: !override` and restate **both** entries (the config file and
`livekit-data:/data`) — exactly the trap already documented for `ports:` in
`docker-compose.prod.yml`. `command:` stays `["--config", "/etc/livekit/livekit.yaml"]`;
only the host-side file changes.

⚠️ `webhook.api_key` must name a key that exists in `LIVEKIT_KEYS`. LiveKit does
**not** expand `${...}` in this file, so the key *name* is written literally and
only the secret comes from the environment. Getting this wrong produces no error
— webhooks are simply never signed and never arrive.

The receiving endpoint and signature verification already exist and need no
changes: `LiveKitWebhookController` (`POST /api/v1/livekit/webhook`,
`[AllowAnonymous]`, 64 KB body cap, always returns 200) and
`LiveKitWebhookVerifier` (HS256 JWT + body SHA-256 claim).

**New events handled.** Extend `LiveKitWebhookParser` and add a *new* handler —
do not rewrite `RecordingWebhookHandler`, which today returns `Ignored` for any
event without an `egress_id` and must keep doing so.

| Event | Action |
|---|---|
| `room_started` | resolve room → session → active `TrackComposition` recording; **ensure the `RoomAudio` row** (§3.4) and start the audio-only room composite inline |
| `track_published` | resolve room → session → active `TrackComposition` recording; **first, ensure the `RoomAudio` row** (idempotent — this is the belt to `room_started`'s braces); then: if `participant.identity != HostId` → ignore; map `track.source`; in `RoomComposite` audio mode ignore `MICROPHONE`/`SCREEN_SHARE_AUDIO`; **insert `RecordingTrack`** (`Status = Requested`) with the predicted `ObjectKey`; **start the track egress inline** (below) |
| `track_unpublished` | find the row by `(RecordingId, TrackSid)`; if `Starting`/`Active` and `EgressId` is set and `StopRequestedAt` is null → `StopEgress`, set `StopRequestedAt`. A `false` result is normal and logs at `Warning`, never `Error`. **Never** matches the `RoomAudio` row — it has no track |
| `participant_left` | if it is the host → treat every non-terminal **video** track of that recording as `track_unpublished`. The `RoomAudio` mixer keeps running: students may still be talking, and the teacher usually reconnects |
| `room_finished` | stop the `RoomAudio` egress if it is still non-terminal (`StopEgress` once, guarded by `StopRequestedAt`); mark the recording's capture phase closed; the reconcile job (§4.1) takes it from there |
| `egress_started` / `egress_updated` / `egress_ended` | if `egress_id` matches a `RecordingTrack` → apply to that row (this covers the `RoomAudio` row too — it is an egress like any other); otherwise fall through to the existing `RecordingWebhookHandler` unchanged |

**Why `room_started` *and* `track_published` both ensure the audio row.** In the
normal flow the `SessionRecording` row already exists when `room_started` fires,
because `LiveSessionService.StartAsync` writes it before the teacher's browser
connects. But if the two race, or if `room_started` is lost, the first
`track_published` recovers within milliseconds instead of waiting up to 60 s for
the reconcile job — and 60 s of missing audio at the start of a lesson is the
part everyone notices. The unique index on `(RecordingId, "ROOM")` makes the
double attempt free.

**Idempotency.** Every handled event goes through the existing
`ILiveKitWebhookLog.TryBeginAsync(eventId)` (table `RecordingWebhookEvents`,
primary key `EventId`, 200 chars). Events that are *ignored* (non-host tracks,
irrelevant sources, unknown rooms) must **not** be written to that table — a
25-participant lesson generates a lot of noise and the table has no retention
job. Keep the existing ordering: cheap filters first, `TryBeginAsync` only when
the event is about to change state.

**Why the egress start is inline in the webhook, unlike the composite path.**
`IRecordingService`'s doc comment forbids calling egress from
`LiveSessionService.StartAsync`, because that is the most latency-critical action
on the platform and a slow LiveKit must never delay a lesson. A webhook is not on
that path: nothing is waiting for it, the controller already swallows every
exception and answers 200, and the Twirp client has a 10 s timeout. The
alternative — a polling job — is worse here: `JobSchedulerWorker` ticks at
`Jobs:TickSeconds` (default **30 s**), which is the *effective floor* for any
`IScheduledJob` interval no matter what `Interval` the job declares. Thirty
seconds of missing footage at the start of every screen share is a real quality
loss. So: start inline, and keep a reconciliation job (§4.1) as the safety net.

### 3.4 Starting a track egress

New port method on `ILiveKitEgress` (additive, existing methods untouched):

```csharp
Task<EgressStartResult> StartTrackRecordingAsync(
    TrackEgressStartRequest request, CancellationToken ct = default);
```

with `public sealed record TrackEgressStartRequest(string RoomName, string TrackId, string ObjectKey);`
in `Recordings/Dtos/RecordingDtos.cs`.

Implementation in `LiveKitEgressClient`: Twirp
`POST {base}/twirp/livekit.Egress/StartTrackEgress`, body written with
`Utf8JsonWriter` in **snake_case** exactly like the existing
`BuildStartPayload` (the global camelCase `JsonSerializerOptions` must not touch
it), same `roomRecord` JWT, same storage credentials read once per call from
`IRuntimeOptions<StorageOptions>`:

```json
{
  "room_name": "...",
  "track_id": "TR_...",
  "file": {
    "filepath": "raw/<sessionId>/<recordingId>/<trackSid>.<ext>",
    "disable_manifest": true,
    "s3": {
      "access_key": "...", "secret": "...", "region": "...",
      "bucket": "...", "endpoint": "<Storage:ServiceUrl>",
      "force_path_style": true
    }
  }
}
```

Response handling, error wrapping and Uzbek error strings: identical to
`StartRoomRecordingAsync`. A failure never throws; it returns
`EgressStartResult.Fail(...)` and the row records the reason.

### 3.4b Starting the audio-only room composite — and forcing the SDK source

Second new port method (additive; **do not** modify `StartRoomRecordingAsync` or
`BuildStartPayload` — that is the live production path for the old pipeline):

```csharp
Task<EgressStartResult> StartRoomAudioRecordingAsync(
    RoomAudioEgressStartRequest request, CancellationToken ct = default);
```

with `public sealed record RoomAudioEgressStartRequest(string RoomName, string ObjectKey);`.

Twirp `POST {base}/twirp/livekit.Egress/StartRoomCompositeEgress` — the same
method as the old pipeline, a deliberately different body:

```json
{
  "room_name": "...",
  "audio_only": true,
  "file_outputs": [{
    "file_type": "OGG",
    "filepath": "raw/<sessionId>/<recordingId>/ROOM.ogg",
    "disable_manifest": true,
    "s3": {
      "access_key": "...", "secret": "...", "region": "...",
      "bucket": "...", "endpoint": "<Storage:ServiceUrl>",
      "force_path_style": true
    }
  }]
}
```

🔴 **Three fields decide whether Chrome starts. Get them wrong and the design
silently costs 6× what it is budgeted for.**

| Field | Value | Why |
|---|---|---|
| `audio_only` | `true` | the precondition for `config.ShouldUseSDKSource` |
| `custom_base_url` | **omitted entirely** | a custom template URL can only be rendered by a browser, so any value here forces the web source. Do not send it as `""` either — omit the key |
| `layout` | **omitted entirely** | the existing `BuildStartPayload` sends `"grid"`. A layout is a property of a *rendered page*; sending one on an audio-only request is at best meaningless and at worst the thing that tips the source selection. Audio has no layout |

`file_type: "OGG"` (Opus) rather than MP4/AAC: Opus is what the SFU already
carries, it is the documented audio-only choice, `OGG` is present in the v1.14
binary's strings, and ffmpeg reads it without complaint. The mix still costs one
decode + one encode of a single stereo stream — cheap, but not free, which is why
§3.5 budgets it explicitly rather than calling it zero.

Everything else — the `roomRecord` JWT, reading storage credentials once per call
from `IRuntimeOptions<StorageOptions>`, snake_case `Utf8JsonWriter` output, error
wrapping — is identical to the existing method.

#### The fail-fast gate

The SDK-source premise is an inference (§3.2), so it must be **proved on the
first real lesson, not assumed**. Egress logs a `request validated` line
containing `sourceType` (both strings are present in the v1.14 binary). After the
first P3 lesson:

```bash
docker compose logs livekit-egress | grep -m5 'request validated'
```

| Observed | Meaning | Action |
|---|---|---|
| `"sourceType": "EGRESS_SOURCE_TYPE_SDK"` | no Chrome — the design holds | continue P3 |
| `"sourceType": "EGRESS_SOURCE_TYPE_WEB"` | Chrome is running per lesson | 🔴 **fail fast** |

**Fail-fast action, in order:**

1. Set `recordings.audio_capture_mode = "TeacherTrack"` in the admin panel. Takes
   effect on the next lesson; no deploy, no restart, no migration.
2. Recording continues with teacher-mic + screen-share `TrackEgress` — the
   original design: cheap, correct, no student audio.
3. Record the outcome in `docs/ASSUMPTIONS.md` and re-open the decision with the
   owner, since the reason for their 2026-09-05 ruling no longer holds.

Do **not** "make it work" by raising `EGRESS_CPUS` to fit six Chrome instances.
That directly violates the hard constraint that peak-hour CPU must not increase,
and the first symptom would be a degraded live lesson, not a bad recording.

Belt and braces: even in the default mode the worker refuses to start the mixer
if `audio_capture_mode` is not `RoomComposite`, so a half-applied setting cannot
produce both audio sources at once.

### 3.5 Egress worker capacity — the trap that must not be repeated

LiveKit's egress worker refuses a job whose declared CPU cost exceeds what is
available; **it refuses silently**, the caller just times out. This already
happened once on this project (2026-09-01) and the whole explanation is written
in `docker-compose.prod.yml` next to `room_composite_cpu_cost: 1.5`.

`track_cpu_cost` **defaults to 1.0**, and the audio-composite costs default to
roughly the same order. With today's `EGRESS_CPUS=2.0` and `max_cpu_utilization`
0.8 the worker has ~1.6 available, so it would accept **one** track egress and
silently drop everything else — including the audio mixer, i.e. a recording with
video and no sound at all.

Required config change in `docker-compose.prod.yml`'s `EGRESS_CONFIG_BODY` (keep
the existing `room_composite_cpu_cost` line — the old pipeline must keep working):

```yaml
cpu_cost:
  room_composite_cpu_cost: 1.5            # unchanged: the old pipeline
  track_cpu_cost: 0.05                    # measured ~0.05 core per passthrough track
  sdk_audio_room_composite_cpu_cost: 0.15 # the mixer, SDK source (expected path)
  audio_room_composite_cpu_cost: 0.5      # the mixer if it falls back to Chrome
```

**Both** audio costs are set, and deliberately at different values. If the SDK
assumption holds, the 0.15 line applies. If it does not, the 0.5 line applies and
capacity accounting stays *honest* — the worker starts refusing jobs, which is
loud, instead of quietly overloading a 4 vCPU box, which is not. Neither value is
a way to "make it fit": 0.5 is a realistic Chrome-backed cost and six of them
would legitimately not fit, which is exactly the fail-fast signal §3.4b wants.

Raise `EGRESS_CPUS` from `2.0` to `3.0` in `.env` (documented in `.env.example`,
whose comment already says `EGRESS_CPUS >= cost / 0.8`). Available capacity
becomes 3.0 × 0.8 = **2.4**.

**Declared-cost budget**

| Scenario | Declared | vs 2.4 available |
|---|---|---|
| 1 lesson: camera + screen + mixer | 0.05 + 0.05 + 0.15 = **0.25** | fits |
| **6 concurrent lessons** (measured peak) | 6 × 0.25 = **1.50** | fits, 0.9 spare |
| P3 shadow: 1 track lesson + its `RoomComposite` twin | 0.25 + 1.5 = **1.75** | fits |
| Worst case: 6 track lessons + 1 `RoomComposite` | 1.50 + 1.5 = **3.00** | ❌ does **not** fit — never widen the shadow list past one group |
| If SDK premise fails: 6 lessons at 0.5 mixer | 6 × 0.60 = **3.60** | ❌ refused — this is the fail-fast working as designed |

**Real CPU budget — the constraint that actually matters**

Today: **1.0–1.6 cores** for **one** lesson (the other five fail).

| Component | Per lesson | × 6 |
|---|---|---|
| camera `TrackEgress` (passthrough) | ~0.05 | 0.30 |
| screen `TrackEgress` (passthrough) | ~0.05 | 0.30 |
| audio mixer (SDK source, 1 stereo Opus decode+mix+encode) | **~0.15 (estimated)** | 0.90 |
| **total** | **~0.25** | **~1.50** |

So six lessons cost about what **one** lesson costs today: ~1.5 cores against
1.0–1.6. Per lesson the new pipeline is **4–6× cheaper**. The peak-hour constraint
is met, and it is met while recording six lessons instead of one.

🔴 **Stated plainly, as required: the mixer's ~0.15 is an estimate, not a
measurement, and it is 60% of the per-lesson cost.** The entire cost claim rests
on one unmeasured number. It is therefore a P3 gate with explicit numbers, not a
footnote:

- measured audio-mixer CPU **≤ 0.20 core** for one lesson → proceed;
- **0.20–0.35** → proceed to P4 only after re-running this table with the real
  figure and confirming 6 × total ≤ 1.6 cores;
- **> 0.35** → treat as the §3.4b fail-fast condition even if the log says
  `EGRESS_SOURCE_TYPE_SDK`. The log proves *no Chrome*; it does not prove *cheap*.

P3 has one lesson at a time (group 7 is alone in the schedule), so the six-lesson
figure is an extrapolation from one measurement. Say so in the P3 report; do not
present it as measured.

Memory: 3 handler processes per lesson without Chrome, 18 at peak.
`EGRESS_MEM=3G` is unchanged but **must be watched during P3** (DoD item).

⚠️ Confirm all four `cpu_cost` keys are recognised by `livekit/egress:v1.14`
before deploying — LiveKit parses config strictly and an unknown key stops the
container. The keys were verified present in the binary's strings:

```bash
docker run --rm --entrypoint sh livekit/egress:v1.14 \
  -c "grep -aoE '[a-z_]*cpu_cost' /usr/bin/egress | sort -u"
```

and after deploy the startup log line `cpu available: X  max cost: Y` must show
`available ≈ 2.4`.

### 3.6 Reconnects, screen share toggles, restarts

- **Screen share starts mid-lesson** → `track_published` (`SCREEN_SHARE`) → new
  `RecordingTrack` → new egress → new file. Its `StartedAt` is minutes after the
  camera's; the composition places it on the timeline at that offset.
- **Screen share stops** → `track_unpublished` → `StopEgress` → the file is
  finalised, `egress_ended` fills `EndedAt`/`SizeBytes`. Restarting screen share
  produces a **different `TrackSid`** and therefore another row and another file.
- **Teacher reconnects** → `participant_left` closes the open **video** tracks,
  the rejoin publishes new `TrackSid`s → new rows, new files, a gap on the video
  timeline. The gap is rendered as black video. **The audio does not gap**: the
  room mixer never stopped, so students talking through the teacher's reconnect
  are recorded, and the reconnect is a visual cut rather than a resync point.
  This is the clearest single benefit of the mixed-audio design.
- **API restarts mid-lesson** → webhooks fired while it was down are lost.
  Recovery is the reconciliation job (§4.1), not the webhook. The already-running
  room mixer is unaffected — it lives in the egress container, not ours.

---

## 4. Night composition

### 4.1 `RecordingTrackReconcileJob` (API container, cheap)

`backend/src/Zinnur.Application/Recordings/Jobs/RecordingTrackReconcileJob.cs`,
`IScheduledJob`, `Name = "recording-track-reconcile"`, `Interval = 60 s`
(effective floor is `Jobs:TickSeconds` = 30 s, which is fine here).
Registered in `Program.cs` next to `RecordingWatchdogJob`, `AddScoped`.

Per run, batch ≤ 100:

1. `RecordingTrack` rows stuck in `Requested` for > 60 s and under
   `MaxAttempts = 5` → retry. `RoomAudio` rows retry via
   `StartRoomAudioRecordingAsync`, everything else via
   `StartTrackRecordingAsync`. Over the limit → `Failed`.
2. **Missing `RoomAudio` row.** For each `TrackComposition` recording whose
   session is still `Live`, in `RoomComposite` audio mode, with no `RoomAudio`
   row → create and start one. This is the recovery path for a lost
   `room_started` *and* a lost first `track_published`. 🔴 It is the highest-value
   step in this job: a missing video segment costs a few minutes of picture, a
   missing mixer costs **the entire lesson's sound**.
3. For each `TrackComposition` recording whose session is still `Live`, call
   LiveKit `POST /twirp/livekit.RoomService/ListParticipants` (`{"room": "<name>"}`)
   and insert rows for any host **video** track we do not know about. This is the
   restart-recovery path. It needs a token with `video: { roomAdmin: true, room: <name> }`
   — a **different grant** from the existing `roomRecord` egress token, so add a
   second small token builder in `LiveKitEgressClient` (or a sibling
   `LiveKitRoomServiceClient`; either is fine, do not extend `LiveKitTokenService`,
   whose contract is browser join tokens).
   Cross-check the `RoomAudio` row against
   `POST /twirp/livekit.Egress/ListEgress` (`{"room_name": "<name>", "active": true}`):
   if our row says `Active` but LiveKit has no such egress, the mixer died — mark
   the row `Failed` and, if the session is still `Live`, start a **replacement**
   mixer. A replacement produces a second audio file, so the row's `TrackSid`
   sentinel becomes `ROOM` / `ROOM2` / … (`RecordingTrack.RoomAudioSid` plus an
   ordinal). §4.6 concatenates them in `StartedAt` order; the gap between them is
   silence. This is the one case where more than one `RoomAudio` row exists.
4. `RoomAudio` rows whose `StartedAt` is older than
   `RecordingWatchdogSettings.MaxDuration` (4 h) → `StopEgress` + `MarkStopRequested`.
   A forgotten room must not run a mixer forever; this mirrors the guard the old
   pipeline already has.
5. `RecordingTrack` rows in `Starting`/`Active` whose session has been
   `Ended`/`Cancelled` for more than `FinalizeGrace = 10 min` → `HeadAsync` the
   raw key; present → `Completed` with the returned size, absent → `Failed`
   ("Trek fayli omborga tushmadi." / for `RoomAudio`:
   "Dars ovozi omborga tushmadi."). Same "storage is the source of truth"
   philosophy as `RecordingWatchdogJob.FinalizeAsync`.
6. Recordings in `CompositionStatus = Collecting` whose session is terminal and
   **all** rows (video and audio) are terminal → `Queued`.
   If zero rows reached `Completed` → `CompositionStatus = Failed` and
   `SessionRecording.MarkFailed("Darsdan yozib olingan trek topilmadi.")`.

⚠️ **Audio-only is a success, video-only is a success, neither is a failure.**
A lesson where the teacher never turned the camera on still has a full audio
recording and must be `Completed` (§4.6 renders a black canvas). Do not add a
"video is required" check — the academic department reviews explanation quality,
which is carried by the audio.

### 4.2 Where the composition runs

**A new container.** Not the API container, for two reasons that are both fatal:

1. `JobRunner.RunAllAsync` executes due jobs **sequentially** and awaits each. A
   90-minute ffmpeg run inside an `IScheduledJob` would block
   `SessionAutoCloseJob`, `MonthlyBillingJob`, `PenaltyScanJob` and
   `ChatRetentionJob` for 90 minutes. Sessions would stop auto-closing.
2. ffmpeg is not installed in the API image, and the API image is the one exposed
   to the internet.

Add to `backend/Dockerfile` a **new final stage**, after the existing `runtime`:

```dockerfile
FROM runtime AS runtime-media
USER root
RUN apk add --no-cache ffmpeg
USER zinnur
```

🔴 **`docker-compose.yml`'s `api` service currently has no `build.target`,** so it
builds the last stage. Adding a stage after `runtime` would silently move the API
onto the ffmpeg image. Adding `target: runtime` to the `api` build block is
therefore **mandatory and part of the same commit**, not a follow-up.

New service in `docker-compose.yml` (and resource limits in
`docker-compose.prod.yml`, matching the file's existing style):

```yaml
compositor:
  build: { context: ./backend, dockerfile: Dockerfile, target: runtime-media }
  image: zinnur/compositor:dev
  container_name: zinnur-v2-compositor
  restart: unless-stopped
  env_file: [ .env ]
  environment:
    ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}
    Jobs__Enabled: "false"              # no shared scheduler here
    Notifications__Enabled: "false"     # outbox stays in the API container only
    Composition__Enabled: "true"
    Composition__ScratchPath: /var/lib/zinnur/compose
  volumes:
    - compositor-scratch:/var/lib/zinnur/compose
  # no `ports:` — nothing external talks to this container
```

Production limits: `cpus: "${COMPOSITOR_CPUS:-3.5}"`,
`memory: ${COMPOSITOR_MEM:-2G}`, `reservations.memory: 512M`, plus the file's
`logging: *default-logging` anchor. Add `COMPOSITOR_CPUS`/`COMPOSITOR_MEM` to
`.env.example` with the reasoning.

The API container gets `Composition__Enabled: "false"` explicitly (do not rely on
a default — an unset flag on both containers is how you get two encoders).
`Notifications__Enabled=false` on the compositor is belt-and-braces: the outbox
dispatcher already leases rows, so duplicate sends were not possible, but a
second idle dispatcher is 300 MB of nothing.

### 4.3 `RecordingCompositionWorker`

`backend/src/Zinnur.WebApi/Workers/RecordingCompositionWorker.cs`, a
`BackgroundService` (like `OutboxWorker`), registered only when
`Composition:Enabled` is true, via a new
`backend/src/Zinnur.WebApi/Workers/CompositionSetup.cs` +
`CompositionOptions.cs` pair that mirrors `JobsSetup`/`JobsOptions` exactly
(same `Read(IConfiguration)`, `Number`, `Flag` helpers and clamping).

Loop:

```
every 60 s:
  if !settings.track_pipeline_enabled          -> sleep
  if now (Asia/Tashkent) outside [start, end)  -> sleep
  if now >= end - 30 min                       -> sleep    // don't start what we can't finish
  claim one recording (4.4); none -> sleep
  compose it (4.5) with a CancellationToken that fires at `end`
```

**Parallelism: exactly one composition at a time.** Justification:

- x264 already parallelises across all cores inside one process
  (`-threads 0`), so two jobs on 4 vCPU finish the same total work in the same
  wall time — they only double peak RAM and double the number of half-finished
  jobs at 09:00.
- The hard stop then discards at most **one** job's work, not N.
- 4 vCPU is the whole box. The API, Postgres, Redis, LiveKit and the nightly
  `pg_dump` all still need to run. One encoder at `cpus: 3.5` leaves a margin;
  two do not.

Budget against the worst designed-for night: 6 lessons × 90 min = 9 h of source.
At the shipped default (`preset medium`, §4.6) the measured rate on this class of
box is 1.5–2× real time, so ~4.5–6 h of encoding inside a 9 h window. It fits
with ~3 h of margin. That margin is the whole reason the default is not `slow`
(Decision D2, §10).

### 4.4 Claiming — the lease

Mutual exclusion is the **database row**, not `IJobLock`. The Postgres advisory
lock holds a dedicated connection for the whole job; `JobRunner` already logs a
`LockLost` warning for exactly this failure mode, and holding one for 90 minutes
across a network blip is asking for two encoders writing the same key.

Claim in a single statement, so two workers cannot both win:

```sql
UPDATE "SessionRecordings"
SET "CompositionStatus" = 2,            -- Running
    "CompositionStartedAt" = @now,
    "CompositionLeaseUntil" = @now + interval '5 minutes',
    "UpdatedAt" = @now
WHERE "Id" = (
  SELECT "Id" FROM "SessionRecordings"
  WHERE "Pipeline" = 1
    AND ( "CompositionStatus" = 1                                    -- Queued
       OR ("CompositionStatus" = 2 AND "CompositionLeaseUntil" < @now) )  -- expired lease
  ORDER BY "CreatedAt" ASC, "Id" ASC                                 -- oldest first
  LIMIT 1
  FOR UPDATE SKIP LOCKED)
RETURNING "Id";
```

- **Oldest first** is required by the "unfinished work is picked up the next
  night" rule.
- The lease is renewed to `now + 5 min` every **60 s** while ffmpeg runs.
- A claim that finds an **expired** lease is a crash recovery: increment
  `CompositionAttempts`, delete any local scratch directory and any object at the
  final `ObjectKey`, and start from scratch. **Never resume a partial ffmpeg
  run** — a partially written mp4 has no usable moov atom and appending to it
  produces a file that plays for 3 seconds and then stops, which is worse than no
  file.

### 4.5 Composing one recording

Run inside `backend/src/Zinnur.Infrastructure/Services/FfmpegRecordingComposer.cs`,
behind an Application port
`backend/src/Zinnur.Application/Recordings/Services/IRecordingComposer.cs`
(so the Application layer never sees `Process`, ffmpeg arguments or the
filesystem). Signature:

```csharp
Task<CompositionResult> ComposeAsync(CompositionPlan plan, CancellationToken ct);
```

`CompositionPlan` is built in the Application layer from the DB rows (track
list with offsets and durations, target key, preset, crf); `CompositionResult`
carries `SizeBytes`, `DurationSeconds`, and a failure reason.

Steps:

1. **Scratch.** `{ScratchPath}/{recordingId}/`. Deleted in a `finally`, always,
   including on cancellation. On startup the worker deletes every directory under
   `ScratchPath` older than 24 h (crash leftovers).
2. **Download** every `Completed` raw object to scratch via a new
   `IRecordingStorage.OpenReadAsync(objectKey, ct)` (mirror
   `IMediaStorage.OpenReadAsync`; signed GET against `Storage:ServiceUrl` with
   the existing `S3SigV4`). **Do not feed ffmpeg presigned URLs**: the view-link
   TTL is 15 minutes and the encode runs for hours, ffmpeg's HTTP seeking against
   R2 is slow and retries badly, and Cloudflare R2 egress to our server is free
   so there is no cost argument for streaming.
3. **Probe** each file with `ffprobe -v error -show_entries format=duration -of json`.
   Store the result in `RecordingTrack.ProbedDurationMs`. If
   `|probed - (EndedAt - StartedAt)| > 2 s`, log a `Warning` with both numbers —
   this is the drift signal that §9.1 is about. Do not abort on drift.
4. **Build the timeline, anchored on audio.** `T0 = MIN(StartedAt)` over all
   `Completed` rows — audio and video alike, so nothing that was captured is ever
   clipped. Every row occupies `[StartedAt - T0, EndedAt - T0]`. Total output
   duration is `MAX(EndedAt) - T0`.

   The **audio is the spine**: it is one continuous row covering essentially the
   whole span, so it is placed once with a single delay of
   `(audio.StartedAt - T0) * 1000 + recordings.compose_audio_offset_ms`
   milliseconds and then never cut, split or mixed. Video segments are placed
   against that same `T0`. There is no `amix` and no per-segment audio
   resynchronisation, which is what removes the entire class of drift-accumulation
   bugs the earlier teacher-mic design had.

   In the rare multi-`RoomAudio` case (§4.1 step 3), concatenate the audio rows in
   `StartedAt` order with each one delayed by its own offset — still no overlap,
   still no mixing, and the gap between them is genuine silence.
5. **Encode** (§4.6) into `{scratch}/out.mp4`.
6. **Verify.** `ffprobe` the output: it must have exactly one video and one audio
   stream, `duration > 0`, and duration within **±2 s** of the timeline length.
   Failure here is a composition failure, not a success with a bad file.
7. **Upload** to `SessionRecording.ObjectKey` (the key the row already holds —
   the composition writes to the *existing* key, it does not invent one) via a
   new `IRecordingStorage.PutAsync(objectKey, stream, length, contentType, ct)`.
   A single `PUT` (R2's single-object limit is 5 GiB; outputs are 1–2 GB), so the
   final key is either absent or complete — there is no partial state visible to
   students. Use `StorageOptions.LargeUploadTimeoutSeconds` (1800), not
   `TimeoutSeconds`.
   ⚠️ `RuntimeStorageOptions.Compose` currently does not carry
   `LargeUploadTimeoutSeconds` from the seed; it must (one-line addition,
   listed in §5.9).
8. **Commit.** In one `SaveChangesAsync`:
   `CompositionStatus = Completed`, `CompositionFinishedAt = now`, and
   `SessionRecording.MarkCompleted(objectKey: null, sizeBytes, durationSeconds, endedAt, now)`
   — the existing domain method already does exactly the right thing and must be
   reused, not reimplemented.
9. **Purge raw.** Delete every raw object via a new
   `IRecordingStorage.DeleteAsync(objectKey, ct)`, set `RawPurgedAt = now`.
   A delete failure is **not** a composition failure: log at `Warning`, leave
   `RawPurgedAt` null, and let the next night's worker retry the purge for any
   `Completed` recording with `RawPurgedAt IS NULL`. Orphan raw objects cost
   money; a rolled-back good recording costs a lesson.

Failure handling:

| What happened | Effect |
|---|---|
| ffmpeg exit ≠ 0, probe failure, upload failure | `CompositionAttempts++`; if < 3 → back to `Queued`; if ≥ 3 → `CompositionStatus = Failed` + `MarkFailed(reason)` |
| Cancellation token fired (09:00, or container stop) | `CompositionInterruptions++`; if < 10 → back to `Queued`, `CompositionError = "Tungi oyna tugadi — keyingi kechada davom etadi."`, `CompositionAttempts` **unchanged**; if ≥ 10 → `Failed` |
| Zero `Completed` tracks | `Failed` immediately, `"Darsdan yozib olingan trek topilmadi."` |

Process control: `SIGTERM` on cancellation, wait 10 s, then `SIGKILL`. Never
leave an orphan ffmpeg — the container's stop timeout must be ≥ 30 s.

### 4.6 Encoding parameters

Canvas **1920×1080 @ 30 fps**, `yuv420p`.

Layout rules, evaluated per time interval:

- Screen share active → screen share fills the frame
  (`scale=1920:1080:force_original_aspect_ratio=decrease` + `pad` to centre),
  teacher camera as a **480×270 inset in the bottom-right, 24 px margin**.
- No screen share → teacher camera fills the frame (same scale/pad).
- Neither → black.

Because a lesson has multiple segments, this is **one** `filter_complex` with all
inputs, each shifted by `-itsoffset <seconds>` and gated with
`enable='between(t,<start>,<end>)'`. The `enable` expressions are computed in
.NET from the timeline in step 4, not hand-written. Shape:

```
[0:v] scale=1920:1080:force_original_aspect_ratio=decrease,
      pad=1920:1080:(ow-iw)/2:(oh-ih)/2 [scr]
[1:v] split=2 [camA][camB]
[camA] scale=1920:1080:force_original_aspect_ratio=decrease,
       pad=1920:1080:(ow-iw)/2:(oh-ih)/2 [camFull]
[camB] scale=480:-2 [camPip]
color=c=black:s=1920x1080:r=30 [bg]
[bg][camFull] overlay=0:0:enable='<intervals with camera and no screen>' [v1]
[v1][scr]     overlay=0:0:enable='<intervals with screen>'               [v2]
[v2][camPip]  overlay=W-w-24:H-h-24:enable='<intervals with screen and camera>' [v]
```

**Audio — one input, one filter, no mixing.** The `RoomAudio` file already
contains the summed room, so the whole audio chain is:

```
[N:a] adelay=<ms>|<ms>,aresample=async=1:first_pta=0 [a]
```

where `<ms> = (audio.StartedAt - T0) * 1000 + recordings.compose_audio_offset_ms`.

- **No `amix`.** In the default mode there is exactly one audio input. `amix`
  appears only in the `TeacherTrack` fallback mode (mic + screen audio), where it
  must carry `normalize=0` — the default divides by the input count and would
  halve the teacher's volume whenever screen audio exists.
- **`aresample=async=1`** absorbs small timestamp discontinuities inside the
  Opus stream by inserting or dropping samples rather than shifting everything
  after them. It is the difference between a 40 ms glitch and 40 ms of permanent
  lip-sync error for the rest of the lesson.

Degenerate cases, all of which must produce a playable file with **both**
streams, because players and the existing `<video>` element behave badly
otherwise:

- audio but no video (camera never on) → black canvas for the full duration,
  encodes to almost nothing. **This is a success, not a failure** (§4.1 step 6);
- video but no audio (mixer failed) → `anullsrc=r=48000:cl=stereo` silent track,
  and `CompositionError` records `"Dars ovozi yozib olinmadi."` so staff know why
  the file is silent before they open it;
- neither → the recording is `Failed`, not composed.

Output:

```
-c:v libx264 -preset <recordings.compose_preset> -crf <recordings.compose_crf>
-pix_fmt yuv420p -profile:v high -level 4.1 -g 60 -threads 0
-c:a aac -b:a 128k -ac 2 -ar 48000
-max_muxing_queue_size 1024
-movflags +faststart
```

`+faststart` rewrites the moov atom to the front so the browser can start playing
before the whole file downloads; it needs a second local pass, which is why the
encode targets local scratch and not a network sink.

**Why this is better than today, concretely:** today is a 720p grid encoded in
real time by LiveKit's default preset with CBR rate control, where every
participant tile — including 12 muted student thumbnails — steals bitrate from
the thing that matters, and frames are dropped when the CPU limit is hit. The new
output is 1080p, the screen share is full-frame at source resolution, no student
thumbnail consumes a pixel, the audio is the same full room mix as today, rate
control is CRF (constant quality) instead of
CBR, and nothing is dropped because nothing is real time. The x264 preset is the
*smallest* of those factors, which is the whole argument in Decision D2 (§10).

Expected size at `crf 21`: roughly 1.5–3 Mbps → **1.0–2.0 GB** per 90-minute
lesson, i.e. the same order as today's 92 MB – 1.75 GB range but at 1080p.

### 4.7 Disk budget

Scratch, per job: raw inputs ~1.5–2.5 GB + output ~1–2 GB + faststart temp
~1–2 GB ≈ **6 GB peak**. One job at a time, cleaned in `finally`. Reserve
**20 GB** on the `compositor-scratch` volume. The server has 139 GB free.

R2: raw objects live from the lesson until the following night ≈ up to 33 h. Worst
designed-for day: 6 lessons × ~2 GB = 12 GB of transient raw storage.

---

## 5. Modules and phases

Each unit lists the files it touches. **Shared files are listed separately in
§5.8 and are strictly sequential** — two agents editing `Program.cs`,
`docker-compose.yml` or the migrations folder at the same time will produce a
merge conflict or, worse, a silently lost registration.

### 5.1 M1 — Domain

**Files:**
`Zinnur.Domain/Enums/RecordingPipeline.cs`,
`Zinnur.Domain/Enums/RecordingCompositionStatus.cs`,
`Zinnur.Domain/Enums/RecordingTrackKind.cs`,
`Zinnur.Domain/Entities/RecordingTrack.cs`,
`Zinnur.Domain/Entities/SessionRecording.cs` (**additive only**),
`Zinnur.Domain/Entities/Group.cs` (one property).

New methods on `SessionRecording` — the state machine lives in the entity, as it
already does for the egress states:

```
void   BeginComposition(DateTimeOffset now)                       // Collecting (set at creation for TrackComposition)
void   MarkRawCollected(DateTimeOffset now)                       // Collecting -> Queued
bool   TryClaimComposition(DateTimeOffset now, TimeSpan lease)    // Queued|expired Running -> Running
void   RenewCompositionLease(DateTimeOffset now, TimeSpan lease)
void   ReleaseCompositionForRetry(string reason, DateTimeOffset now)   // failure   -> Queued, Attempts++
void   InterruptComposition(DateTimeOffset now)                        // 09:00 cut -> Queued, Interruptions++
void   MarkCompositionFailed(string reason, DateTimeOffset now)        // -> Failed + MarkFailed
void   MarkCompositionCompleted(long? sizeBytes, int? durationSeconds,
                                DateTimeOffset endedAt, DateTimeOffset now)  // -> Completed + MarkCompleted
void   MarkRawPurged(DateTimeOffset now)
```

All idempotent and all no-ops once `IsFinished`, matching the existing methods.

`RecordingTrack` gets its own small state machine: `BeginAttempt`, `MarkStarting`,
`MarkActive`, `MarkCompleted`, `MarkFailed`, `MarkStopRequested`, `CanRetry` —
copy the shapes from `SessionRecording`, do not invent new ones.

**Dependencies:** none. **Parallelisable:** yes — nothing else can start without
it, so it is the critical path; do it first and alone.

### 5.2 M2 — Persistence

**Files:**
`Zinnur.Infrastructure/Persistence/Configurations/RecordingTrackConfiguration.cs`,
`Zinnur.Infrastructure/Persistence/Configurations/SessionRecordingConfiguration.cs` (additive),
`Zinnur.Infrastructure/Persistence/Configurations/GroupConfiguration.cs` (one line),
`Zinnur.Infrastructure/Persistence/ApplicationDbContext.cs` (one `DbSet`),
`Zinnur.Application/Common/Interfaces/IApplicationDbContext.cs` (one `DbSet`),
plus the migration + its `Designer.cs` + `ApplicationDbContextModelSnapshot.cs`.

**Dependencies:** M1. **Parallelisable:** no — it owns the migration head, which
is a shared file (§5.8).

### 5.3 M3 — Egress adapter (track start + room service)

**Files:**
`Zinnur.Application/Recordings/Services/ILiveKitEgress.cs` (**two** methods:
`StartTrackRecordingAsync`, `StartRoomAudioRecordingAsync`),
`Zinnur.Application/Recordings/Dtos/RecordingDtos.cs` (`TrackEgressStartRequest`,
`RoomAudioEgressStartRequest`, `LiveKitTrackEventDto`),
`Zinnur.Infrastructure/Services/LiveKitEgressClient.cs` (additive:
`StartTrackRecordingAsync` + `BuildTrackStartPayload`,
`StartRoomAudioRecordingAsync` + `BuildRoomAudioStartPayload`, room-admin token,
`ListParticipantsAsync`, `ListEgressAsync`).

🔴 `BuildStartPayload` (the old pipeline's body builder) is **not** touched, and
`BuildRoomAudioStartPayload` is a separate method rather than a flag on it. They
call the same Twirp endpoint with deliberately different bodies, and the
difference — `layout` and `custom_base_url` omitted — is exactly what decides
whether Chrome starts (§3.4b). A shared builder with an `audioOnly` boolean would
put that decision one careless edit away from being reverted.

New log EventIds continue the file's existing range: **6606–6619**.

**Dependencies:** M1. **Parallelisable:** yes, alongside M4 and M5.

### 5.4 M4 — Storage adapter

**Files:**
`Zinnur.Application/Recordings/Services/IRecordingStorage.cs` (adds
`BuildRawObjectKey`, `OpenReadAsync`, `PutAsync`, `DeleteAsync`),
`Zinnur.Infrastructure/Services/R2RecordingStorage.cs` (implementations),
`Zinnur.Infrastructure/Options/RuntimeStorageOptions.cs` (carry
`LargeUploadTimeoutSeconds`).

Reuse `S3SigV4` for signing; reuse the `R2SubmissionStorage.HttpClientName`
named client as `HeadAsync` already does. New log EventIds: **6621–6629**.

**Dependencies:** M1. **Parallelisable:** yes.

### 5.5 M5 — Webhook: track events

**Files:**
`Zinnur.Application/Recordings/Services/LiveKitWebhookParser.cs` (additive —
parse `room`, `participant`, `track` objects; keep every existing field and the
snake_case/camelCase dual lookup),
`Zinnur.Application/Recordings/Services/ITrackRecordingWebhookHandler.cs` (new port),
`Zinnur.Application/Recordings/Services/TrackRecordingWebhookHandler.cs` (new),
`Zinnur.WebApi/Controllers/LiveKitWebhookController.cs` (**one** change: try the
track handler first, fall through to the existing `IRecordingWebhookHandler`
when it returns `Ignored`).

`RecordingWebhookHandler` itself is **not modified**.

**Dependencies:** M1, M2, M3. **Parallelisable:** no — it is the integration
point of three other units.

### 5.6 M6 — Jobs and worker

**Files:**
`Zinnur.Application/Recordings/Jobs/RecordingTrackReconcileJob.cs` (new),
`Zinnur.Application/Recordings/Services/IRecordingComposer.cs` (new port),
`Zinnur.Application/Recordings/Services/RecordingCompositionPlanner.cs` (new —
builds `CompositionPlan` from rows; **pure, no I/O, unit-testable**),
`Zinnur.Infrastructure/Services/FfmpegRecordingComposer.cs` (new),
`Zinnur.WebApi/Workers/RecordingCompositionWorker.cs` (new),
`Zinnur.WebApi/Workers/CompositionSetup.cs` (new),
`Zinnur.WebApi/Workers/CompositionOptions.cs` (new),
`Zinnur.Application/Recordings/RecordingLog.cs` (additive log methods).

The filter-graph string is produced by `RecordingCompositionPlanner`, **not** by
`FfmpegRecordingComposer`. That is what makes the hardest part of this design
testable without a process, a network or a file (§8, `RecordingCompositionPlannerTests`).

**Dependencies:** M1, M2, M3, M4. **Parallelisable:** the planner and the ffmpeg
adapter can be written in parallel by two agents once the `CompositionPlan`
record is agreed; the worker cannot start until both exist.

### 5.7 M7 — Selection, settings, DTO and UI

**Files:**
`Zinnur.Application/Settings/SettingsRegistry.cs` (**all 8** definitions of §2.7
+ 8 matching `Keys` constants),
`Zinnur.Application/Recordings/Services/AutoRecordingScheduler.cs` (**changed**, see §5.9),
`Zinnur.Application/Recordings/Dtos/RecordingDtos.cs` (`RecordingDto` gains
`Pipeline: string` and `CompositionStatus: string?`),
`Zinnur.Application/Recordings/Services/RecordingService.cs` (populate the two new
DTO fields — projection only, no logic change),
`Zinnur.Application/Groups/Dtos/GroupDtos.cs` (`RecordingPipeline` on the read
DTO and both write DTOs, defaulting to `RoomComposite`),
`Zinnur.Application/Groups/Services/GroupService.cs` (read/write the new field),
`frontend/src/shared/types/api.ts` (`RecordingPipelineName`, two new
`RecordingDto` fields, `recordingPipeline` on the group DTOs),
`frontend/src/entities/recording/model/types.ts` (Uzbek labels/tones for the new
values),
`frontend/src/entities/recording/ui/RecordingCard.vue` (pipeline badge),
`frontend/src/widgets/recording-board/ui/RecordingBoard.vue` (show composition
state in the staff list),
`frontend/src/features/group-form/model/group-sections.ts` +
`frontend/src/features/group-form/ui/GroupEditDrawer.vue` (pipeline selector next
to `recordEnabled`, with the existing change-summary label pattern).

**Dependencies:** M1, M2. **Parallelisable:** backend and frontend halves in
parallel **only after** the DTO field names in `RecordingDtos.cs` are committed —
`frontend/src/shared/types/api.ts` is hand-written to mirror the C# records, so
the names must exist first.

### 5.8 Shared files — SEQUENTIAL, one agent at a time

| File | Why it is shared | Who touches it |
|---|---|---|
| `backend/src/Zinnur.WebApi/Program.cs` | job + worker registration | M6 only, last |
| `backend/src/Zinnur.Infrastructure/DependencyInjection.cs` (`AddRecordings`) | new port registrations | M3, M4, M6 — **serialise** |
| `backend/src/Zinnur.Application/DependencyInjection.cs` | `ITrackRecordingWebhookHandler`, planner | M5, M6 — serialise |
| `.../Persistence/Migrations/*` + `ApplicationDbContextModelSnapshot.cs` | one migration head | M2 only |
| `IApplicationDbContext.cs` / `ApplicationDbContext.cs` | one `DbSet` | M2 only |
| `docker-compose.yml`, `docker-compose.prod.yml` | `api` target, `compositor` service, egress `cpu_cost` | infra step, after M6 |
| `backend/Dockerfile` | `runtime-media` stage | infra step |
| `.env`, `.env.example` | `EGRESS_CPUS`, `COMPOSITOR_*` | infra step (never commit `.env`) |
| `infra/livekit/livekit.yaml` + new `livekit.prod.yaml` | webhook enable | infra step |
| `frontend/src/shared/types/api.ts` | every DTO mirror | M7 only |
| `SettingsRegistry.cs` | duplicate key = startup crash | M7 only |

### 5.9 The only permitted changes to existing behaviour

Exactly three. Anything else is out of scope and must be reported, not done.

1. **`RecordingWatchdogJob` must ignore the new pipeline.** Add
   `&& r.Pipeline == RecordingPipeline.RoomComposite` to the `Where` at
   `RecordingWatchdogJob.cs:101`.
   🔴 Without this the watchdog `HEAD`s a final `ObjectKey` that does not exist
   yet, waits `FinalizeGrace` and marks every track-pipeline recording `Failed`
   ten minutes after the lesson ends. This is the single highest-severity
   interaction in the whole design.

   ⚠️ **The merged 2026-09-04 watchdog fix does not cover this.** That fix added
   `if (!sessionOver && recording.StopRequestedAt is null) return false;`, which
   stops the watchdog giving up *while the lesson is still running*. A
   track-pipeline recording's final object legitimately does not exist until the
   next morning, so once the session ends the old path resumes and still fails
   the row. Verified against the merged code: the query at
   `RecordingWatchdogJob.cs:98–106` filters only on `Status`, not `Pipeline`.
   The one-line `Where` addition is still required work.
2. **`AutoRecordingScheduler.EnqueueAsync` becomes pipeline-aware.** Today it
   refuses if *any* non-terminal row exists for the session; it must refuse only
   if a non-terminal row exists **for the same pipeline**, and it must create one
   or two rows according to §2.7. Track-pipeline rows are created with
   `Pipeline = TrackComposition`, `CompositionStatus = Collecting`, and
   `ObjectKey = storage.BuildObjectKey(session.Id)` — the same final-key scheme,
   whose 8 random bytes already guarantee the two rows cannot collide.
3. **`RuntimeStorageOptions.Compose` must carry `LargeUploadTimeoutSeconds`**
   from `Seed`. It currently drops it, so every runtime-composed
   `StorageOptions` silently falls back to the 60 s default — harmless today,
   fatal for a 2 GB upload.

Explicitly **unchanged**: `RecordingService`, `IRecordingService`, the
`/api/v1/recordings` routes and their authorisation, `RecordingWebhookHandler`,
`Group.RecordingsVisibleToStudents`, `SessionRecording.ShowToStudents` /
`HideFromStudents`, and the `SessionRecordings` rows already in production.

### 5.10 Rollout phases

| Phase | Content | Exit condition |
|---|---|---|
| **P0** | Prerequisite (owned elsewhere): enable LiveKit webhooks, `livekit.prod.yaml`, watchdog `egress_started` fix | `egress_started` observed in the API log for a real lesson |
| **P1** | M1 + M2 + migration applied to dev and prod. Nothing behaves differently: `Pipeline = 0` everywhere, `recordings.track_pipeline_enabled = false` | `upgrade head` succeeds; existing recordings still complete |
| **P2** | M3–M6 + infra (Dockerfile stage, `compositor` service, `track_cpu_cost`, `EGRESS_CPUS=3.0`). Still globally off | compositor container healthy and idle; `docker compose config` shows no `minio` in prod |
| **P3** | **Shadow on one group.** `recordings.track_pipeline_enabled = true`, `recordings.track_pipeline_shadow_groups = "7"`. Group 7 = `ATF-97`, Mon/Thu 10:00, alone in the schedule. Both pipelines run on the same lesson; two `SessionRecording` rows, two different `ObjectKey`s, neither visible to students (`IsVisibleToStudents` defaults to `false`) | **(a)** `EGRESS_SOURCE_TYPE_SDK` observed (§3.4b); **(b)** mixer CPU ≤ 0.20 core (§3.5); **(c)** §9.1 A/V verification passes on 2 consecutive lessons; **(d)** student speech audible in the composed file; **(e)** peak CPU during the lesson ≤ today's. **(a) and (b) gate everything else — check them on the first lesson before spending two more.** |
| **P4** | Widen: set `Group.RecordingPipeline = TrackComposition` on a handful of groups, clear the shadow list for group 7 and set its column instead. Re-run the §3.5 capacity arithmetic first | one full week with ≥ 3 concurrent recorded lessons and no failures |
| **P5** | All 33 groups. `RoomComposite` code stays in place and selectable — it is the rollback | — |

Rollback at any phase: set `recordings.track_pipeline_enabled = false` in the
admin panel. No deploy, no migration, no restart. Groups fall back to
`RoomComposite` on the next lesson.

---

## 6. API contract

**No new endpoints and no new routes.** This is deliberate: recording is fully
automatic (`IRecordingService`'s manual start/stop was removed on 2026-09-01 by
owner decision) and adding an operator trigger would recreate the "two sources of
truth" problem that removal fixed. Everything is driven by the group flag, the
settings and the schedule.

Changes to existing responses (additive fields only — no field is removed,
renamed or retyped):

**`RecordingDto`** — returned by
`GET /api/v1/recordings?from&to`,
`GET /api/v1/live-sessions/{sessionId}/recordings`,
`PATCH /api/v1/recordings/{id}/visibility`:

```
+ pipeline:          "RoomComposite" | "TrackComposition"
+ compositionStatus: "Collecting" | "Queued" | "Running" | "Completed" | "Failed" | null
```

Serialised as **enum names**, matching the existing `status` field. `null`
`compositionStatus` means the old pipeline.

**Group read DTO and both group write DTOs** (`GroupDtos.cs`):

```
+ recordingPipeline: "RoomComposite" | "TrackComposition"     // default "RoomComposite"
```

Authorisation is unchanged and is **not** re-derived: writing group settings
already goes through `GroupService`, which is reachable only from the
staff-restricted group endpoints. Reading recordings already filters by role
inside `RecordingService`. Do not add a second authorisation check — the
codebase's own comment on `IRecordingService.ListAsync` explains why duplicated
permission logic drifts apart.

Error cases: unchanged. `403` still means permission (foreign group, or the
academic department hid the recording), `409` still means state (revealing a
recording that is not `Completed`), `503` still means storage is unconfigured.

Pagination and filtering: unchanged. `GET /api/v1/recordings` still **requires**
both `from` and `to` (omitting them returns 500 today — a known live-verified
behaviour documented in `frontend/src/entities/recording/api/recording-api.ts`)
and still caps the range at 92 days.

Webhook contract: `POST /api/v1/livekit/webhook` keeps its current shape —
`[AllowAnonymous]`, HS256 signature + body-hash verification, 64 KB cap,
**always 200** (a non-2xx makes LiveKit retry, and the endpoint is now on the
critical path for track discovery, so a retry storm during a lesson is the last
thing wanted). It simply understands more event names.

---

## 7. UI

Nothing new for students. Two additions for staff, both read-only signals plus
one control.

### 7.1 `ManageRecordingsPage` / `RecordingBoard.vue` (staff)

- Each row gains a small **pipeline badge**: `Tungi montaj` for
  `TrackComposition`, nothing for `RoomComposite` (the default must stay
  visually quiet — 33 groups will show it).
- When `compositionStatus` is not null and not `Completed`, the status cell shows
  the composition state instead of the raw recording status, because "Yozilmoqda"
  is misleading for a lesson that ended six hours ago:

  | `compositionStatus` | Uzbek label | Tone (existing `RecordingTone`) |
  |---|---|---|
  | `Collecting` | `Yozilmoqda` | `live` |
  | `Queued` | `Tungi montaj navbatida` | `accent` |
  | `Running` | `Montaj qilinmoqda` | `accent` |
  | `Completed` | `Tayyor` | `success` |
  | `Failed` | `Xato` | `danger` |

- During P3 a group-7 lesson shows **two** rows. That is the intended comparison
  surface; the badge is what tells them apart.

### 7.2 `GroupEditDrawer.vue` (Academic / Admin only — the drawer already is)

A select in the same basic section as `recordEnabled`:

- label `Yozib olish usuli`
- options `Standart (jonli montaj)` = `RoomComposite`, `Tungi montaj (sifatliroq)` = `TrackComposition`
- **disabled** when `recordEnabled` is false, with helper text
  `Avval "Darslarni yozib olish"ni yoqing.` — a pipeline choice on a group that
  is not recorded is a confusing no-op.
- included in the existing change-summary list in `group-sections.ts`
  (`if (next.recordingPipeline !== prev.recordingPipeline) labels.push('Yozib olish usuli')`).

Permission hiding: none beyond what already exists. The drawer, the recordings
board and the settings page are all already staff-only; students never receive
`pipeline` or `compositionStatus` in a way that matters because their list is
filtered to `Completed` rows.

### 7.3 Live room

**No change.** `RecordingIndicator.vue` reads
`GET /api/v1/live-sessions/{id}/recording-status`, which lights up for any
non-terminal `SessionRecording` row. A `TrackComposition` row is non-terminal
during the lesson, so the indicator behaves identically. This matters: the
"participants can see they are being recorded" indicator is a **consent
requirement**, written as a conditional part of the automatic-recording decision
in `IRecordingService`. Verify it still lights up during P3 — it is a DoD item.

🔴 **The consent argument is stronger under this design, not weaker.** The mixed
room audio records **students' voices**, which the earlier teacher-only draft did
not. The indicator is the only thing a student sees telling them so. If the
indicator is broken, the correct response is to stop recording, not to ship and
fix it later.

---

## 8. Definition of Done

Machine-checked (`CLAUDE.md`: "before saying done, the machine must confirm it"):

- [ ] `dotnet test` green — both `Zinnur.UnitTests` and `Zinnur.IntegrationTests`.
- [ ] `npm run build` green in `frontend/` (runs `vue-tsc --noEmit` first; note the machine has no local node — build through Docker as `MEMORY.md` describes, with the three `VITE_*` variables).
- [ ] `TreatWarningsAsErrors=true` is on (`Directory.Build.props`) — the build fails on a single warning; do not suppress, fix.
- [ ] Migration exists, `dotnet ef migrations script --idempotent` reviewed by a human, and `upgrade head` succeeds on a **fresh empty database** (`docker compose down -v` in dev only) and on a **copy of the production dump**.
- [ ] The throwaway-migration snapshot check from `docs/MIGRATIONS.md` produces an empty `Up`/`Down` **after** this migration is added.

New tests required:

- [ ] `Zinnur.UnitTests/Recordings/RecordingTrackTests.cs` — the track state machine, including "a late `egress_ended` never resurrects a `Failed` row".
- [ ] `Zinnur.UnitTests/Recordings/SessionRecordingCompositionTests.cs` — every transition in §4.5's table, plus: `TryClaimComposition` returns `false` on a live lease and `true` on an expired one; an interruption does not increment `CompositionAttempts`.
- [ ] `Zinnur.UnitTests/Recordings/RecordingCompositionPlannerTests.cs` — **the most important test file in this SPEC**. Golden-string tests for the filter graph over: single camera; camera + one screen-share interval; camera + two screen-share intervals; a reconnect gap; audio-only (no video at all); video-only (mixer failed → `anullsrc`); a video track whose `StartedAt` is the timeline anchor and one that is not; **audio starting before the first video** and **audio starting after it**; a non-zero `recordings.compose_audio_offset_ms`; the multi-`RoomAudio` concat case. Assert there is **no `amix`** in `RoomComposite` mode and **exactly one** `adelay`.
- [ ] `Zinnur.IntegrationTests/Recordings/TrackWebhookTests.cs` — `track_published` for the host creates a row; for a student creates nothing; a duplicate `track_published` (same `EventId`) creates nothing; an unknown room is ignored; `track_unpublished` requests a stop exactly once; **`room_started` creates exactly one `RoomAudio` row and a following `track_published` does not create a second**; **in `RoomComposite` mode a host `MICROPHONE` publication creates no row**; **in `TeacherTrack` mode it does, and no `RoomAudio` row exists**.
- [ ] `Zinnur.IntegrationTests/Recordings/CompositionQueueTests.cs` — two workers racing the claim statement produce one winner; the claim is refused outside the night window; the claim is refused inside `end - 30 min`; oldest-first ordering holds.
- [ ] An RBAC test for every touched endpoint (`CLAUDE.md`: mandatory). Since no new endpoints are added, this means: the group update endpoint rejects `Student`/`Teacher` writing `recordingPipeline`, and a student's `GET /api/v1/recordings` never returns a non-`Completed` row.

⚠️ A separate agent owns `backend/tests/` (it added `RecordingWatchdogTests.cs`
there). Coordinate before writing into that project — add new files, do not edit
or move existing ones.

Manual, on production during P3 (each needs a recorded result, not a "looks
fine"):

- [ ] 🔴 **SDK source proved (§3.4b)** — `docker compose logs livekit-egress | grep 'request validated'`
      shows `"sourceType": "EGRESS_SOURCE_TYPE_SDK"` for the audio-only room
      composite. `EGRESS_SOURCE_TYPE_WEB` is a **stop condition**, not a note.
- [ ] 🔴 **Audio mixer CPU ≤ 0.20 core** for one lesson (§3.5 gate). Record the
      number; 0.20–0.35 requires re-running the §3.5 budget table before P4,
      > 0.35 is a stop condition even if the source type says SDK.
- [ ] **CPU during a live lesson** — `docker stats` sampled every 10 s across a
      full group-7 lesson. Track-egress total ≤ 0.3 core. Combined
      egress-container CPU during the shadow lesson must not exceed today's
      measured 2.14 peak.
- [ ] **Egress container memory** with 2 track jobs + 1 audio mixer + 1 room
      composite ≤ 3 GB.
- [ ] **The recording indicator** lights up for a student in the live room
      (consent — §7.3; students' voices are now in the file).
- [ ] **Student audio is actually present** — a student speaks at a noted
      timestamp; that speech is audible in the composed mp4 at the same
      timestamp. This is the entire point of the owner's 2026-09-05 decision and
      it is not proved by any automated check.
- [ ] **A/V sync (§9.1)** verified on 2 consecutive lessons, including the
      constant-vs-accumulating shape analysis.
- [ ] **Night budget** — the composition worker's log shows total encode wall
      time for the night and it is < 6 h; nothing is left `Queued` at 09:00 on a
      normal (≤ 4 lesson) day.
- [ ] **Raw purge** — `RawPurgedAt` is set and the `raw/` prefix is empty for
      every `Completed` recording older than 48 h.
- [ ] **The old pipeline still works** — a `RoomComposite` group records,
      completes, and its file plays. This is the rollback path; if it is broken,
      nothing else matters.
- [ ] The `api` image does **not** contain ffmpeg
      (`docker run --rm --entrypoint sh zinnur/api:… -c 'command -v ffmpeg'`
      returns nothing) — proof that `target: runtime` was not forgotten.

---

## 9. Open risks

### 9.1 A/V sync across separate files — still the biggest risk, now much smaller

The composite pipeline muxes one clock. This pipeline reassembles independently
recorded files from timestamps. If that reassembly is off, every recording is
subtly wrong and nobody notices until a student says the teacher's lips do not
match.

**The mixed-audio decision (§3.2) materially reduced this risk.** The earlier
design had N audio files with mute-holes plus M video files, so *every* audio
segment boundary was a potential resync point and errors accumulated. Now:

- **one** continuous audio file spans the whole lesson;
- it is placed with **one** delay and never cut or mixed;
- video segments are the only things being positioned, and a video segment that
  is 200 ms out is far less perceptible than audio that is 200 ms out, because
  lip-sync is judged *against* the audio;
- a teacher reconnect is a visual cut, not an audio splice.

The failure mode therefore collapses from "accumulating drift across many
splices" to "a possibly-constant offset between two clocks", and a constant
offset is fixable with one number: `recordings.compose_audio_offset_ms` (§2.7).

What makes it tractable at all: **all offsets come from one clock.** LiveKit's
`started_at` / `ended_at` (nanoseconds, in the `egress_ended` payload) are stamped
by the single LiveKit server process, so they are mutually consistent even though
each file's internal timestamps start at zero. Composition uses those offsets
(`-itsoffset`), never file duration.

What can still go wrong: packet loss and jitter make a file's *media* duration
drift from its *wall-clock* duration over 90 minutes — now concentrated in the
one audio file, where it matters most.

**Verification — three layers, all required before P4:**

1. **Automated, every job.** `ffprobe` each raw file; compare media duration to
   `EndedAt - StartedAt` and store it in `RecordingTrack.ProbedDurationMs`. Log a
   `Warning` above 2 s. The **`RoomAudio` row's number is the one that matters**:
   it is the spine, so its drift is the recording's drift. After 10 lessons, read
   those numbers — they are the empirical drift rate. Above ~0.1% (5 s over
   90 min) on the audio row, this SPEC needs revising, not tuning.
2. **Automated, every job.** `ffprobe` the output: duration within ±2 s of the
   timeline length, exactly one video and one audio stream. Failure blocks the
   upload.
3. **Human, during P3 — this is why the shadow rollout exists.** The
   `RoomComposite` output of the *same* lesson is a ground-truth A/V-synced
   reference. Play both files side by side and measure lip-sync offset at 0%,
   50% and 100% of the lesson. **Accept if |offset| ≤ 120 ms throughout, with
   audio never leading video by more than 45 ms** (ITU-R BT.1359-1 detectability
   thresholds; the asymmetry is real — audio arriving early is far more
   noticeable than late).

   🔴 **Read the shape of the three measurements, not just their size** — this is
   the whole reason for sampling at three points:

   | Pattern | Diagnosis | Action |
   |---|---|---|
   | roughly equal at 0 / 50 / 100%, e.g. −80, −85, −78 ms | **constant** offset = fixed pipeline latency | set `recordings.compose_audio_offset_ms` to the negated mean, re-compose one lesson, re-measure. Not a design fault |
   | growing, e.g. −20, −300, −600 ms | **accumulating drift** | design fault. Do not tune the offset — it cannot fix a slope. Stop the rollout |
   | jumps at a screen-share toggle or reconnect | timeline/`enable` bug in the planner | fix the planner; add the case to `RecordingCompositionPlannerTests` |

   Repeat on a second lesson containing at least one screen-share toggle, one
   reconnect and **at least one student speaking** — the last is new and
   non-negotiable, since student audio is the reason this mechanism exists and it
   is only present in the mix. Record all measured numbers in
   `docs/ASSUMPTIONS.md`.

If layer 3 shows accumulating drift, do **not** widen the rollout. Fall back to
the composite pipeline (one settings toggle) and revisit.

### 9.2 Second-order risks

| Risk | Why it bites | Mitigation in this SPEC |
|---|---|---|
| **Audio-only room composite launches Chrome after all** | The SDK-source premise is inferred from binary strings, not measured. If wrong, per-lesson cost goes from ~0.25 to ~0.6 core and 6 concurrent lessons roughly double today's peak CPU | §3.4b fail-fast: assert `EGRESS_SOURCE_TYPE_SDK` in the `request validated` log on the first P3 lesson, plus a hard ≤ 0.20 core measurement gate in §3.5. Fallback is `recordings.audio_capture_mode = "TeacherTrack"`, a settings change with no deploy |
| **The room mixer dies mid-lesson and nobody notices** | Video keeps recording, so everything looks healthy; the lesson ends up silent | §4.1 step 3 cross-checks against `ListEgress` and starts a replacement mixer; §4.6 writes `"Dars ovozi yozib olinmadi."` into `CompositionError` so staff see it in the list before opening the file |
| **Both audio sources active at once** | A room mix plus a teacher-mic track plays the teacher twice, slightly offset — comb filtering that sounds like broken hardware, not like an echo | §2.3 makes the two modes mutually exclusive; §3.4b has the worker refuse to start a mixer unless the mode says so; in `RoomComposite` mode `MICROPHONE` publications create no rows at all |
| `track_cpu_cost` silently rejects jobs | LiveKit refuses without an error; the caller sees a timeout. Already happened once here | §3.5 sets all four costs and raises `EGRESS_CPUS`; the DoD verifies the container's startup `cpu available / max cost` log line |
| Adding a Dockerfile stage moves the API image | `api` has no `build.target` today, so it builds the last stage | §4.2 makes `target: runtime` part of the same commit; the DoD asserts ffmpeg is absent from the API image |
| `RecordingWatchdogJob` fails every new recording | It `HEAD`s a key that will not exist until tomorrow morning | §5.9 item 1 — highest-severity item in the SPEC |
| VP8 raw files are `.ivf`, not `.webm` | Older LiveKit docs say `.ivf`; current docs say WebM. The mapping table is a prediction | §2.8 treats the webhook's returned `filename` as authoritative and logs mismatches. Confirm on the first P3 lesson |
| The night window is exceeded on a busy day | 6 lessons is designed-for, not a ceiling | Oldest-first queue + `CompositionInterruptions`; a spill-over is a *normal* state, not an error. Watched as a DoD item |
| Webhooks are lost while the API restarts | Track discovery has no other real-time source | §4.1 step 3 reconciles from `ListParticipants` — but only while the session is still `Live`. A restart spanning the whole lesson loses that lesson's recording. Accepted; the composite pipeline has the same exposure |
| `RecordingWebhookEvents` grows | Track events add rows and the table has no retention job | Only *handled* events are logged (~50 rows per lesson ≈ 14k/month). Not a problem at this scale; if the table passes 5M rows, add a retention job modelled on `ChatRetentionJob` |
| Storage settings are blank at 02:00 | `IRuntimeOptions<StorageOptions>` reads the DB; a misconfiguration means the compositor cannot download or upload | The worker checks `IRecordingStorage.IsConfigured` before claiming and sleeps rather than burning attempts |

---

## 10. Decisions and open questions

Items 1–2 are **settled**; they are recorded here rather than deleted so nobody
relitigates them in six months. Items 3–7 are open and each carries a recommended
decision: **if nobody answers, the recommendation stands** — implement it and log
it in `docs/ASSUMPTIONS.md`.

### Decided

**D1. Student audio — CAPTURED. (owner, 2026-09-05)**

An earlier draft of this SPEC recommended shipping without student audio. **The
owner decided against that recommendation: student audio must be captured.**

- **Mechanism:** one audio-only `RoomCompositeEgress` per lesson producing a
  single continuous mixed file (§3.2, §3.4b). Not one egress per student — that
  arithmetic was not overturned and is preserved in §3.2 as the rejected
  alternative.
- **Premise it rests on:** audio-only room composite runs on the egress **SDK
  source**, i.e. without Chrome. Evidenced from the `livekit/egress:v1.14` binary
  (`sdk_audio_room_composite_cpu_cost`, `config.ShouldUseSDKSource`,
  `EGRESS_SOURCE_TYPE_SDK`), **not yet measured**.
- **Fail-fast:** if the first P3 lesson logs `EGRESS_SOURCE_TYPE_WEB`, or the
  mixer measures above 0.35 core, set
  `recordings.audio_capture_mode = "TeacherTrack"` — a settings change, no
  deploy — and re-open the decision with the owner, because the basis for it
  no longer holds.
- **Side effect, recorded deliberately:** students' voices are now in the
  recording. That raises the stakes on the consent indicator (§7.3), which is
  therefore a hard DoD item rather than a nicety.
- **Side benefit:** the continuous mixed file is a better sync spine than the
  teacher-mic-with-mute-holes design it replaces, which measurably lowers the
  §9.1 risk.

**D2. x264 preset — `medium` / `crf 21`, preset exposed as a setting. (owner,
accepted 2026-09-05)**

The brief asked for a *slow* preset. The arithmetic says it does not fit: if
`medium` runs at 1.5–2× real time on this box, `slow` runs at roughly 1.0–1.1×,
so 9 hours of source needs ~8.6 hours inside a 9-hour window — zero margin, on a
box that also runs the nightly `pg_dump`. The dominant quality wins are
resolution (1080p vs 720p), no student thumbnails stealing bitrate, CRF instead
of CBR, and no dropped frames; the preset is worth maybe 5–8% bitrate at equal
quality on top of that. After P3 measures the actual encode rate, the owner can
raise `recordings.compose_preset` to `slow` from the admin panel with no deploy;
the oldest-first queue already handles the resulting multi-night spill-over
correctly.

### Still open

3. **Output frame rate.** 30 fps costs ~20% more CPU than 25.
   → **Recommendation: 30 fps.** The camera is 30 fps; 25 introduces visible
   judder on the only content a human watches closely (the teacher). Screen
   share at 15 fps is frame-duplicated either way and costs nothing extra under
   CRF.

4. **Where the camera inset sits.** Bottom-right is conventional but can cover
   content in a slide's lower-right corner.
   → **Recommendation: bottom-right, 480×270, 24 px margin.** Revisit after P3
   with real slides; moving it is a one-line change in the planner and a golden
   test update.

5. **A/B rows visible to staff during P3.** Two rows per lesson for group 7 will
   look like a bug to anyone who has not read this file.
   → **Recommendation: keep both rows and ship the pipeline badge (§7.1).** The
   comparison is the entire point of P3, and hiding one row would mean staff
   cannot open the new file to review it.

6. **Where composition state lives (§2.5).** Columns on `SessionRecording`
   contradict the codebase's own "one attempt = one row" precedent.
   → **Recommendation: columns.** Reasoning is written in §2.5 so the next reader
   does not relitigate it. If per-attempt encoder history is ever needed, it is a
   new append-only table, not a restructure.

7. **Code comment language.** `CLAUDE.md` and the task brief both say comments in
   English. Every existing file in `Recordings/` has long, deliberate Uzbek
   doc-comments that explain *why*, and they are the best documentation in this
   repo.
   → **Recommendation: follow `CLAUDE.md` — English comments in new files,
   Uzbek for user-facing strings.** Flagging it because a mixed-language module
   is worse than either choice consistently applied, and the owner may well
   prefer repo consistency. One word from the owner flips this; nothing else in
   the SPEC depends on it.
