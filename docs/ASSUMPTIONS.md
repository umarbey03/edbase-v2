# Assumptions log

One line per decision that was made without asking the project owner:
`- [date] <question> -> <decision> -> <reason>`

- [2026-09-03] Mobile browsers have no `getDisplayMedia` — hide the screen-share button or keep it? -> Keep it visible and explain on tap -> A missing button makes the teacher hunt for it and conclude the app is broken; screen share is the core teaching tool, unlike the fullscreen button which may be hidden silently.
- [2026-09-03] Fullscreen scope: `<main>` (video only) or the whole page? -> The whole page root -> Outside the fullscreen element nothing is painted, so the participants/chat panel was unreachable while presenting. In fullscreen the panel becomes a floating drawer, so the video still gets the full width.
