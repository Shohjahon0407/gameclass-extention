# Design brief for Stitch: Manajoy GameClass Bridge (v0.5.2)

How to use it: paste **Prompt 0** into Stitch first, then paste the screen prompts one at a time (1–9). UI copy must stay in **Uzbek**, with Russian as the second language. Every screen is a desktop app window measuring **1240×860**, not a phone.

---

## Prompt 0 — project context (paste first)

```
Design a Windows desktop app UI (window 1240×860, Edge app window without browser chrome) called
"Manajoy GameClass" — a small bridge program that a computer-club (internet café / gaming club) owner
installs on the club's main PC where the "GameClass" club-management software already runs.

What the product does:
- Reads (read-only, never writes) the GameClass database: which club PCs are free, busy, offline or
  under repair, session end time, time left, upcoming reservation, zone (Hall / VIP / etc.).
- Sends that live status to Manajoy (a marketplace app), so customers see in the Manajoy app which PC
  is free and when a busy one will be free.
- Lets the owner draw the club floor map (PCs, halls, VIP room, bar, cashier desk, entrance). The same
  map is shown to customers in the Manajoy app.
- Cashier helper: calculates the price of a session (by time, by amount of money, by packet, postpaid,
  or adding time to a running session) exactly the way GameClass would — the cashier then opens the
  session in GameClass itself. The bridge never starts/stops PCs.

Users: club owner (one-time setup, non-technical, ~5 minutes) and cashier/administrator (all day,
needs to see the hall at a glance and quote prices fast). Many users are not technical — use big
clear statuses, short sentences and friendly guidance, and avoid jargon.

Language: Uzbek (Latin) primary, Russian secondary (language switch "O'zbekcha / Русский").
Brand: ManaJoy Biznes. Primary blue #1B54E4 (dark #1230A8, light #E3EAFE), brand gradient
150° #7A2BE2 → #4324D6 → #1B54E4 → #0EA5F0. Background #F5F7F8, cards white, text #15181B,
secondary #4A5560, muted #7B8794, border #E6E9EC. Success #1E9E62, warning #E08A1E, danger #E0463A.
Font Plus Jakarta Sans (Segoe UI for Russian). Radii 10–28 px, soft shadows. Light theme now; also
provide a dark variant for the cashier/monitor screens (clubs are dark rooms).

PC status colors (use consistently everywhere): Bo'sh (free) = green, Band (busy) = blue,
O'chiq (offline) = grey, Ta'mirda (repair) = orange, Noma'lum (unknown) = orange outline.

Layout: a left collapsible rail (logo, steps or navigation, language switch, version) + main area.
Style: modern, calm, trustworthy, like a fintech/POS dashboard, not a gamer neon style.
```

---

## Prompt 1 — Welcome

```
Screen "Welcome" of Manajoy GameClass setup. Left rail: logo, vertical stepper with 5 steps:
Tekshiruv, GameClass bazasi, O'rnatish, Manajoy'ga ulash, Tayyor (none done yet), language switch at
bottom. Main: kicker "GameClass integratsiyasi", title "Klubingiz kompyuterlarini Manajoy'da jonli
ko'rsating", lead text "Mijozlar Manajoy ilovasida qaysi kompyuter bo'sh, qaysi biri band va qachon
bo'shashini ko'radi. Sozlash taxminan 5 daqiqa oladi." Three feature cards with icons:
"Faqat o'qiydi" (does not touch sessions, money, settings), "Avtomatik ishlaydi" (Windows service,
starts with PC), "Faqat holat yuboriladi" (only PC number, zone, free/busy, time left).
Checklist "Sizga kerak bo'ladi": this PC with GameClass Server; ManaJoy Biznes app for the pairing
code. Big primary button "Boshlash". Optional top banner for demo mode (yellow): "Demo rejim: ...".
```

## Prompt 2 — Preflight checks

```
Screen "Kompyuterni tekshiramiz" (step 1 active). A list of ~11 check rows, each with a status icon
(ok green / warning orange / fail red / spinner), name and a short explanation for warnings/failures:
Windows versiyasi, Administrator huquqi, SQL Server xizmati, SQL Server versiyasi, ODBC drayveri,
GameClass Server, GameClass Server dasturi (warn: "Hozir yopiq..."), Til va format, 8787-port,
Diskda bo'sh joy, Manajoy serveri. Summary banner on top: green "Hammasi joyida", orange "Davom etish
mumkin, sariq qatorlarni o'qib chiqing" or red "Qizil qatorlarni tuzatmaguncha o'rnatib bo'lmaydi".
Buttons: "Qayta tekshirish" (secondary), "Davom etish" (primary), "Orqaga".
```

## Prompt 3 — Database connection

```
Screen "GameClass bazasiga ulanamiz" (step 2). Explain: we create a separate read-only login
(gcbridge_ro) and prove it cannot write. Form: "SQL administrator" (default sa), "Parol" with hint
"'sa' paroli odatda bo'sh — bo'sh qoldiring. Parol saqlanmaydi.", checkbox "Windows hisobi bilan
kirish", collapsible "Qo'shimcha sozlamalar" (SQL Server, Baza nomi). Primary "Ulash va tekshirish".
After running: a proof list with green ticks — login yaratildi, faqat o'qish roli, yozish roli yo'q,
baza egasi emas, server administratori emas, yozishga urinish rad etildi — and success text
"Tayyor: login faqat o'qiy oladi". Also show an error state (red card, friendly fix text).
```

## Prompt 4 — Install

```
Screen "Bridge'ni o'rnatamiz" (step 3). Short lead text. A plan list that turns into a progress
timeline: copy files to Program Files, store the password encrypted, register Windows service, start
service, test reading GameClass, create desktop + Start menu shortcut. Each row: pending / running /
done / failed. Primary "O'rnatish", running state "O'rnatilmoqda… (30 soniyagacha)". Success:
"Bridge ishlayapti. GameClass'dan 24 ta kompyuter o'qildi."
```

## Prompt 5 — Pair with Manajoy

```
Screen "Manajoy'ga ulaymiz" (step 4). Left: 3 numbered how-to steps with a small phone illustration
(open ManaJoy Biznes → Profil → Kompyuterlar holati → GameClass orqali ulash → see 8-character code).
Right: big code input "ABCD-2345" style (8 chars, auto dash, monospace, large), primary "Ulash",
link "Keyinroq ulayman". Success state: "«Cyber Arena» klubiga ulandi" + "Birinchi ma'lumot
Manajoy'ga yetib bordi". Error states: wrong/expired code (15 min), iCafeCloud already connected,
too many attempts, no internet.
```

## Prompt 6 — Done

```
Screen "Hammasi tayyor!" (step 5, all steps ticked). Celebratory but calm (brand gradient hero, check
icon). Text: PCs are now visible in Manajoy, the bridge starts with Windows. Tip card: open the panel
from Start → type "Manajoy"; desktop shortcut exists. Primary "Boshqaruv paneliga o'tish".
```

## Prompt 7 — Main dashboard (most important screen)

```
Main "Boshqaruv paneli" for daily use by the club administrator. Left rail (collapsible to icons):
logo, club name, navigation, language, version. Top: status pill (Ishlayapti green / Diqqat talab
orange / Ma'lumot eskirgan / GameClass'ni o'qiy olmayapti red / Xizmat ishlamayapti red), "yangilandi
5 s oldin", buttons "Narxlar", "Monitor rejimi", "Xaritani tahrirlash".
Alert banners (only when relevant): not paired with Manajoy + "Manajoy'ga ulash" button; GameClass
operator not logged in; GameClass hidden in tray; GameClass opened twice; new version available +
"Yangilash".
Summary chips: Bo'sh 12 · Band 9 · O'chiq 2 · Ta'mirda 1.
Center: tabs "Xarita / Ro'yxat" + search "PC raqami yoki zona" + filters by status and zone.
Map view: club floor plan with areas (Zal, VIP zal, Bar, Kassa, Kirish), PC tiles colored by status
showing PC number and "45 daq qoldi" or "Bron 18:30"; zoom in/out/fit, full-screen button.
List view: dense table/grid of PC cards (number, zone, status, time left, next reservation, Postpay tag).
Right side cards: "Manajoy" (ulangan klub, mijozlarga ko'rinadi Ha/Yo'q, stansiyalarga bog'langan
22/24, oxirgi yuborish, buttons Qayta ulash / Yuborishni to'xtatish) and "Tizim" (Windows xizmati,
versiya, ma'lumot manbai, GameClass Server ishlayapti / operator kirmagan; Hisobot olish,
Qayta ishga tushirish, O'chirib tashlash).
Clicking a PC opens a side drawer: zone, GameClass state, current session, next reservation, "on map"
yes/no, buttons "Xaritada ko'rsatish" and the cashier quote panel (Prompt 8).
Also design the empty state: "Klub xaritasini chizing" with illustration and "Xaritani chizish" button.
```

## Prompt 8 — Cashier price quote (PC drawer + Prices modal)

```
Cashier panel "Kassa · narx hisoblash" inside the PC drawer. Header: "Hozir: Kunduzgi · 12 000/soat".
Tabs: Vaqt | Summa | Paket | Postpay | Vaqt qo'shish.
- Vaqt: quick chips 30 daq / 1 soat / 2 soat / 3 soat + custom minutes → big price result.
- Summa: amount input (so'm) → how much time and end time "Tugaydi 21:40".
- Paket: list of currently available packets (name, price, until time).
- Postpay: credit limit and estimated end.
- Vaqt qo'shish: current session line "Kunduzgi · tugaydi 20:15 · to'langan 24 000", amount → "Yangi
  tugash: 21:00 (hozir 20:15)".
Warnings as small orange notes (GameClass rounds up, max amount, reservation conflict at 18:30,
23:59 day cap). Footer note: "Narx GameClass tariflari bo'yicha hisoblandi. Seansni GameClass'da
oching." — no "start session" button (read-only product).
Also a "Narxlar" modal: table by zone → tariff, current price per hour, packets available now,
min–max.
```

## Prompt 9 — Map editor + Monitor mode

```
A. Full-screen map editor "Xaritani chizish". Left panel with tabs "Kompyuterlar" (tray of unplaced
PCs grouped by zone, drag onto canvas; "Avto-joylash" button) and "Hududlar" (presets: Zal, VIP zal,
Bar, Kafe, Konsollar, Dam olish, WC, Chekish joyi, Boshqa hudud). Top toolbar: Tanlash, Devor,
Hudud, Kassa, Kirish, Yozuv; Bekor qilish/Qaytarish; zoom; "Orqaga", primary "Saqlash", unsaved dot.
Canvas: grid, areas as colored rounded rectangles with names, PC tiles, multi-select with
"Bir qatorga tekislash / Bir ustunga tekislash / Xaritadan olish". Right properties panel for the
selected object (nomi, rang, o'lcham, "Ichida 8 ta kompyuter"). Sync badge: "Manajoy bilan sinxron"
/ "Faqat shu kompyuterda" / "Ziddiyat" with a conflict dialog ("Ilovadagini olish" vs "Shu yerdagini
yuborish"). A 3-step hint strip for first-time users.
B. "Monitor rejimi": dark full-screen wall display of the club map for a second monitor — big PC tiles
with status color and time left, summary counters on top, clock, "Chiqish (Esc)".
```

---

## Rules for every screen

- **Real constraint:** the bridge only reads and quotes prices. Do not design "Seans ochish", "PC'ni bloklash" or "O'chirish" buttons for PCs.
- **States:** draw loading, empty and error states for every screen.
- **Text length:** Uzbek and Russian are about 20–30% longer than English, so do not size buttons for short labels.
- **Small windows:** the layout must still work at 1024×700. The rail collapses to icons.
- **Accessibility:** contrast must be AA. Status must never be shown by colour alone; add an icon or text as well.
- **What to export from Stitch:** a screenshot or Figma export of each screen, plus colours, sizes and component names. We then port the design into the existing `SetupUi/` (HTML/CSS/JS, no framework).
