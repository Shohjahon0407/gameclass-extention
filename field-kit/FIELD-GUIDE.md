# Manajoy GameClass: klubni ulash yo'riqnomasi

Bu yo'riqnoma klub egasi va klubga o'rnatadigan texnik uchun.

**Manajoy GameClass** GameClass Server kompyuterida ishlaydigan kichik Windows dasturi:
- har 5 soniyada GameClass bazasidan qaysi kompyuter bo'sh, qaysi biri band va qachon bo'shashini **faqat o'qiydi**;
- shuni Manajoy ilovasida mijozlarga ko'rsatadi;
- GameClass'da hech narsani o'zgartirmaydi.

## Klub egasi uchun kafolatlar

- **Faqat o'qish.** Dastur maxsus `gcbridge_ro` login bilan ishlaydi.
  - Bu login faqat o'qiy oladi. Yozishga urinishni SQL Server'ning o'zi rad etadi va sozlash oynasi buni tekshirib ko'rsatadi.
  - GameClass o'zining yozuvchi protseduralarini (sessiya, pul, sozlama) hamma loginlarga ochib qo'ygan. Dastur ularning barchasini shu login uchun yopadi.
- **Bazadagi yagona o'zgarish shu loginni yaratish.** Sessiyalar, pullar, tariflar va sozlamalarga tegilmaydi.
- **Shaxsiy ma'lumot o'qilmaydi.** Mijoz ismlari, telefonlari va parollari olinmaydi.
- **Manajoy'ga nima ketadi.** Jonli holat: har PC uchun raqam, zona, bo'sh/band/o'chiq va qolgan daqiqalar. 0.9.0 dan: har ish kuni uchun klub hisoboti (tushum, PC band vaqti va kun jadvali, bar, xodimlar, smenalar) — ManaJoy Biznes ilovasida egasi va menejer ko'radi. Mijozlarning ism va telefonlari, IP manzillar kompyuterdan chiqmaydi.
- **Hisobot yuborishni o'chirish mumkin.** «Hisobot» → «Hisobot sozlamalari» (PIN bilan) yoki ManaJoy Biznes ilovasi (egasi). O'chirilsa faqat jonli holat ketadi.
- **To'liq olib tashlash mumkin.** Boshqaruv panelidagi «O'chirib tashlash» hammasini, xohlasangiz SQL loginni ham olib tashlaydi.

## Talablar

| Talab | Izoh |
|---|---|
| Windows 10 (1607+), 11, Server 2016+ | Windows 7 / 8.1 da ishlamaydi; tekshiruv qizil ko'rsatadi. |
| 32 yoki 64-bit | Bitta fayl ikkalasida ishlaydi. |
| GameClass SQL bazasi shu kompyuterda | Odatda GameClass Server o'rnatilgan kompyuter. |
| SQL admin paroli | GameClass o'rnatilganda `sa` paroli **bo'sh** bo'ladi. O'zgartirilgan bo'lsa, klub texnigidan so'rang. |
| Internet | Manajoy'ga ulash uchun. |
| ManaJoy Biznes ilovasi | Klub **egasi** akkaunti bilan: ulash kodini shu ilova beradi. |

## Ulash (taxminan 5 daqiqa)

1. **Telefonda.** ManaJoy Biznes ilovasini oching: **Profil → Kompyuterlar holati → GameClass orqali ulash**.
   - Ekranda 8 belgili kod (masalan `ABCD-2345`) va «Telegram'dan yuklab olish» tugmasi chiqadi.
   - Kod 15 daqiqa amal qiladi. Muddati tugasa, «Yangi kod» ni bosing.
2. **Kompyuterda.** GameClass Server kompyuterida Manajoy saytidagi GameClass sahifasidan `ManajoyGameClass-Setup-*.exe` o'rnatuvchisini yuklab oling. Zaxira yo'l — Telegram'dagi `ManajoyGameClass-*-portable.exe` (o'rnatuvchisiz bitta fayl).
3. **O'rnating.** Faylni ikki marta bosing va «Bu ilovaga o'zgartirish kiritishga ruxsat berasizmi?» savoliga **Ha** deng. Keyin o'rnatuvchida:
   - tilni tanlang;
   - shartlar va maxfiylik siyosatini qabul qiling;
   - o'rnatish papkasini tanlang yoki standart papkani qoldiring;
   - «Keyingi» → «O'rnatish» → «Tayyor» ni bosing. Shundan keyin dastur oynasi o'zi ochiladi.
   - Agar «Windows protected your PC» chiqsa: **More info → Run anyway**. Dastur hali raqamli imzolanmagan. Fayl kodini saytdagi SHA-256 bilan solishtirish mumkin.
   - «Kompyuter yoqilganda Manajoy GameClass panelini ochish» belgisi (standart yoqiq): Windows'ga kirilgach 20 soniyada panel o'zi ochiladi, UAC so'ralmaydi. Keyin Sozlamalar → Tizim kartasidagi tugma bilan yoqib/o'chirish mumkin. Bridge xizmati bu belgidan qat'i nazar har doim Windows bilan birga ishga tushadi.
   - Bridge allaqachon o'rnatilgan bo'lsa, o'rnatuvchi uni yangilaydi: xizmat to'xtab, yana ishga tushadi, login va Manajoy ulanishi saqlanadi.
4. **Ochilgan oynada qadamlar bo'yicha yuring:**
   - **Tekshiruv.** Hamma qator yashil yoki sariq bo'lishi kerak. Qizil qator chiqsa, uning tagidagi maslahatni bajaring.
   - **GameClass bazasi.** `sa` parolini kiriting (odatda bo'sh), «Ulash va tekshirish» ni bosing. Hamma qatorlar yashil bo'lishi shart, jumladan «Yozishga urinish rad etildi».
   - **O'rnatish.** «O'rnatish» ni bosing, 10–30 soniya kuting. Oxirida «GameClass'dan N ta kompyuter o'qildi» chiqadi.
   - **Manajoy'ga ulash.** Telefondagi kodni yozing va «Ulash» ni bosing. «… klubiga ulandi» chiqadi.
   - **Tayyor.** Telefondagi ekran ham o'zi «Ulandi!» ga o'tadi.
5. **Tekshirish.** GameClass'da biror kompyuterda sessiya oching. 5–10 soniyada u Manajoy ilovasida «band» bo'lib ko'rinadi.

Shundan keyin oynani yopsangiz ham dastur ishlayveradi; u Windows bilan birga o'zi ishga tushadi. Boshqaruv panelini **Start** menyudan oching: Start → «Manajoy» deb yozing. Uni taskbar'ga qadab qo'yish ham mumkin. Ish stolida ham yorliq bor, lekin ish stoliga o'tish uchun oynalarni yig'sangiz, GameClass soat yonidagi ^ ichiga yashirinadi.

**Tavsiya:** GameClass sozlamalarida **«Не скрывать программу в SysTray»** ni belgilang. Shunda minimallashtirilgan GameClass taskbar'da qoladi va hech qachon «yo'qolib qolmaydi».

## Boshqaruv paneli

Chap paneldagi menyuda uchta bo'lim bor: **Zal**, **Narxlar**, **Sozlamalar**. Ularning ostida **Monitor rejimi** turadi.

- **Zal** — kun bo'yi ochiq turadigan asosiy ekran. Hammasi bitta oynaga sig'adi, sahifani pastga aylantirish shart emas.
  - **Tepada:** klub nomi, holat («Ishlayapti») va so'nggi yangilanish vaqti. O'ng tomonda **Xarita / Ro'yxat**, «Xaritani tahrirlash» va «To'liq ekran» tugmalari.
  - **Holat tugmalari:** `Hammasi 24 · Bo'sh 10 · Band 12 · O'chiq 1 · Ta'mirda 1`. Tugmani bossangiz, faqat shu holatdagi kompyuterlar qoladi. Yana bossangiz, filtr bekor bo'ladi.
  - **Zona tugmalari** (son bilan) va **qidiruv** (PC raqami yoki zona).
  - **Xarita kartochkasi:** katta raqam va uning ostida bitta qator:
    - ⏱ `1:05` — band, 1 soat 5 daqiqa qoldi;
    - 📅 `19:30` — bo'sh, lekin soat 19:30 ga bron qilingan;
    - **P Postpay** — oxirida to'lanadigan seans;
    - Bo'sh / O'chiq / Ta'mirda.

    Band kompyuterning burchagidagi sariq belgi keyingi bron borligini bildiradi. Sichqonchani kartochka ustiga olib borsangiz, to'liq matn chiqadi. Belgilar izohi xaritaning pastki qatorida.
  - **O'ng panel.** Kompyuterni bosing — holati, seansi va **kassa** shu yerda chiqadi. Esc — yopish. Kichik ekranda (1024 px) bu panel faqat kompyuter tanlanganda xarita ustida ochiladi.
  - **Xabarlar** (masalan, «GameClass ikki marta ochilgan») xarita ustida ixcham qatorda chiqadi. Ikkitadan ko'p bo'lsa, «Yana N ta xabar» bosib ochiladi.
- **Narxlar** — har bir zona uchun bitta karta. Unda tarif, hozirgi soatlik narx, hozir amaldagi paketlar va min–max summa bor. Ochiq turganda har daqiqada o'zi yangilanadi.
- **Sozlamalar:**
  - **Manajoy:** qaysi klubga ulangan, mijozlarga ko'rinadimi, nechta PC Manajoy stansiyasiga bog'langan, oxirgi yuborish qachon bo'lgan. Tugmalar: **Qayta ulash** (yangi kod bilan, eski kalit bekor bo'ladi) va **Yuborishni to'xtatish**.
  - **Tizim:** xizmat holati va versiya. Tugmalar: **Qayta ishga tushirish** va **Diagnostika fayli** (ish stolida zip paydo bo'ladi; unda parol yoki kalit yo'q — muammo bo'lsa, shuni Manajoy'ga yuboring). Yangi versiya ochilganda shu yerda **Yangilash** tugmasi chiqadi; login va ulanish saqlanadi.
  - **Klub xaritasi:** sinxron holati va «Xaritani tahrirlash».
  - **Qayta sozlash va o'chirish.**

  Menyudagi «Sozlamalar» yonida sariq nuqta bo'lsa, u yerda e'tibor talab qiladigan narsa bor.
- **Xarita to'liq ekranda.** Sarlavhadagi tugma yoki xarita burchagidagi belgi orqali ochiladi. Holat tugmalari, qidiruv va PC tafsiloti shu yerda ham ishlaydi. Esc — panelga qaytish.
- **Chap panel.** «‹» tugmasi panelni ingichka chiziqqa yig'adi; belgilar ostida bo'lim nomlari qoladi. «›» uni qayta ochadi. Tanlov eslab qolinadi.
- **Monitor rejimi.** Kassadagi ikkinchi monitor yoki televizor uchun. To'liq ekranda katta xarita, hisoblar va soat ko'rinadi, har 3 soniyada yangilanadi. Esc — chiqish.

## Kassa yordamchisi (narx hisoblash)

Xaritada yoki ro'yxatda kompyuterni bosing — o'ng tomonda **«Kassa»** bo'limi ochiladi. Narx GameClass'ning o'z tariflari va qoidalari bo'yicha hisoblanadi: kunduzgi va tungi narx, yaxlitlash, minimal summa, paketlar.

- **Bo'sh kompyuter:**
  - **Vaqt:** 30 daq / 1 / 2 / 3 / 5 soat yoki istalgan daqiqa → qancha to'lanadi va seans qachon tugaydi.
  - **Summa:** mijoz bergan pulga → qancha vaqt va tugash vaqti.
  - **Paket:** hozir sotilayotgan paketlar, narxi va tugash vaqti.
  - **Postpay:** ishonch limiti va taxminiy tugash vaqti.
- **Band kompyuter:** «Vaqt qo'shish» — qo'shimcha summa → yangi tugash vaqti. Hisob aniq bo'lmasa, raqam ko'rsatilmaydi.
- Qo'lda yozilgan daqiqa yoki summa tayyor tugmalardan biriga to'g'ri kelsa, o'sha tugma yonib turadi. Vaqtlar soat:daqiqa ko'rinishida.
- Barcha zonalarning narxlari chap menyudagi **«Narxlar»** bo'limida.

**Muhim:**
- Seansning o'zini **GameClass'da** oching yoki uzaytiring. Bridge GameClass'ga hech narsa yozmaydi.
- Narx chegirmasiz, ya'ni mehmon narxi. Akkaunt chegirmasi GameClass'da qo'llanadi.
- GameClass ba'zan vaqtni yaxlitlaydi. Masalan, 10 daqiqa so'ralsa, minimal summa tufayli 30 daqiqa berishi mumkin. Kassa buni oldindan ko'rsatadi.

## Hisobot (klub egasi uchun)

Chap menyudagi **«Hisobot»** bo'limi GameClass tarixidan klub egasi uchun hisobot tuzadi. 0.9.0 dan xuddi shu hisobot **ManaJoy Biznes** ilovasida ham ko'rinadi (egasi va menejer): bridge har ish kunining yig'indisini 10 daqiqada bir yangilab yuboradi, birinchi ulanganda esa GameClass'dagi butun eski tarixni bosqichma-bosqich (yangisidan eskisiga) yuklaydi. Holati «Hisobot» sahifasining tepasida yozilgan («ManaJoy Biznes ilovasiga yuborilmoqda · oxirgisi … oldin»).

- **PIN-kod.** Birinchi ochilganda egasi 4–8 raqamli PIN o'rnatadi — kassir hisobotni ko'rmaydi. 5 marta xato kiritilsa, 5 daqiqa blok. 15 daqiqa ishlatilmasa, bo'lim o'zi yopiladi.
- **Davr:** bugun, kecha, 7 kun, 30 kun, bu oy, o'tgan oy, bu yil yoki istalgan sanalar (ko'pi bilan 366 kun). Grafiklar soat, kun, hafta yoki oy bo'yicha.
- **Umumiy:** kassa jami, kompyuter vaqti, bar, band soatlar, o'rtacha bandlik, seanslar, o'rtacha chek, eng ko'p band payt — har biri oldingi shunday davr bilan solishtiriladi (▲/▼). Tushum va band soatlar grafigi, eng faol vaqtlar xaritasi (hafta kuni × soat), zonalar, tariflar, bronlar.
- **Kompyuterlar:** har PC necha soat band bo'lgan, bandlik foizi, seanslar, summa, oxirgi ishlatilgani.
- **Kun jadvali:** tanlangan kunda qaysi PC qachondan qachongacha band bo'lgan (mehmon, akkaunt, bron).
- **Bar** va **Xodimlar va smenalar:** mahsulotlar, kassirlar, GameClass'da yopilgan smenalar va qarzlar. Jadvallarni CSV (Excel) qilib yuklab olish mumkin.
- **Ish kuni 06:00 da boshlanadi** (o'zgartirish mumkin: «Hisobot sozlamalari»). Tungi seanslar oldingi kunga yoziladi.

**Hisob qoidasi.** «Kassa jami» GameClass smena hisobotidagi «Итого» bilan bir xil hisoblanadi (lab'da tekshirilgan: 64 226 = 17 226 + 47 000). Seans orqali sotilgan bar «Kompyuter vaqti»dan ayirilib, «Bar»ga o'tkaziladi — ikki marta sanalmaydi. GameClass bronlarni alohida saqlamaydi, shuning uchun bronlar taxminan hisoblanadi.

**PIN unutilsa:** Windows administratori `C:\ProgramData\Manajoy\GameClassBridge\analytics\analytics.json` faylini o'chiradi, keyin «Hisobot»ni ochib yangi PIN o'rnatiladi. GameClass «Clear statistics» qilgan bo'lsa, undan oldingi davr bo'sh chiqadi — sahifada «ma'lumot … dan bor» deb yoziladi.

## Bar ombori (mahsulot qoldig'i)

GameClass barida mahsulot **soni yo'q** — u faqat narx ro'yxati («Услуги») va sotuvlarni saqlaydi. Shuning uchun son bridge'dagi **«Bar»** bo'limida yuritiladi. Bo'lim «Hisobot» bilan bir xil PIN bilan ochiladi, ya'ni faqat egasi kiradi.

- **Boshlash:** mahsulot yonidagi «Kuzatishni boshlash» → javonda va omborda hozir nechta bo'lsa, shuni yozing. Shu daqiqadan keyingi GameClass sotuvlari qoldiqdan o'zi ayriladi.
- **Kirim:** «+ Kirim» → kelgan soni (tez tugmalar +6 / +12 / +24 / +48) va xohlasangiz izoh (yetkazib beruvchi, nakladnoy).
- **⋯ menyusi:**
  - **Sanab to'g'rilash** — javondagi aniq son; farq ko'rsatiladi.
  - **Hisobdan chiqarish** — singan, muddati o'tgan, bepul berilgan.
  - **Kam qoldi chegarasi.**
  - **Tarix** — yozuvlar va kunlik sotuv.
  - **Kuzatishni to'xtatish.**
- **Rangli belgilar:**
  - yashil — yetarli;
  - sariq — chegaradan kam;
  - qizil — tugagan;
  - qizil «Tekshiring» — hisobdagidan ko'p sotilgan. Bu odatda boshlang'ich son noto'g'ri yozilganini yoki tovar sotuvsiz chiqib ketganini bildiradi: sanab, «Sanab to'g'rilash» qiling.
- **Kassir ham ko'radi:** kam qolgan mahsulotlar Zal sahifasida ogohlantirish bo'lib chiqadi (faqat nomi va soni, PIN'siz).
- **ManaJoy Biznes:** qoldiq egasi va menejerga ilovada ko'rinadi, «kam qoldi» bildirishnomasi keladi.
- **Kompyuter almashsa:** ombor yozuvlari Manajoy'da zaxirada turadi. Yangi kompyuterda «Bar» → «Zaxiradan tiklash».
- **GameClass'ga hech narsa yozilmaydi.** Sotuvni GameClass'da bekor qilsangiz, qoldiq o'zi tuzaladi.

## Klub xaritasi

GameClass kompyuterlar qayerda turishini bilmaydi, shuning uchun xaritani bir marta o'zingiz chizasiz (5–10 daqiqa).

1. Boshqaruv panelida **«Xaritani chizish»** ni bosing.
2. **Hududlarni belgilang.** Chapdagi **«Hududlar»** yorlig'ida tayyor andozalar bor: Zal, VIP zal, Bar, Kafe, Konsollar, Dam olish, WC, Chekish joyi, Kassa, Kirish. Birini tanlang va xaritada to'rtburchak torting (yoki bir marta bosing).
   - Nomi va rangini o'ng tomonda o'zgartirasiz.
   - Burchaklaridan tortib kattalashtirasiz.
   - Ctrl+D — nusxa.
3. **Kompyuterlarni joylang.** «Kompyuterlar» yorlig'idan kompyuterlarni hududlarga sudrang. Yoki hududni tanlab **«Joylanmaganlarni shu hududga qo'yish»** ni bosing — bo'sh joylarga o'zi tushadi.
4. Kerak bo'lsa **devor** va **yozuv** qo'shing.
5. **«Saqlash»** ni bosing.

Hududni bir marta bosib tanlang, keyin sudrang — ichidagi kompyuterlar ham birga suriladi. Hudud chizmagan bo'lsangiz, GameClass guruhlari (VIP, General) xaritada o'zi ramka bilan ko'rinadi.

Xarita Manajoy bilan sinxron: mijozlar ilovada aynan shu xaritani ko'radi. ManaJoy Biznes ilovasida o'zgartirsangiz, bridge'da ham o'zgaradi. Ikki joyda bir vaqtda o'zgartirilgan bo'lsa, bridge qaysi birini qoldirishni so'raydi.

- **Internet yo'q bo'lsa.** Xarita shu kompyuterda saqlanadi va keyingi ochilishda yuboriladi.
- **«N ta kompyuter Manajoy stansiyasiga bog'lanmagan».** Bunday kompyuterlar xaritada faqat shu yerda turadi. Ilovada ularni tasdiqlang.

Tugmalar:
- Ctrl+Z — bekor qilish.
- Delete — tanlanganni olib tashlash.
- Strelkalar — siljitish.
- G'ildirak — kattalashtirish.
- Probel + sudrash — xaritani surish.

## Muammolar

| Belgi | Sabab | Nima qilish |
|---|---|---|
| Yuklashda «Virus detected» / Windows Security «Threats found» (Trojan:Win32/Wacatac.B!ml) | 0.4.0 fayli siqilgan edi, Defender'ning sun'iy intellekt modeli uni noto'g'ri «shubhali» deb topgan | **0.4.1** yoki yangisini yuklang. Eski faylni o'chiring. Yangi faylda ham chiqsa, Manajoy'ga xabar bering. Bu holda Defender istisnosi qo'shmang. |
| O'chirish kerak | — | Sozlamalar → «O'chirib tashlash» (SQL loginni ham o'chirish mumkin) yoki Windows «Ilovalar» → Manajoy GameClass → O'chirish. Xizmat, fayllar, shifrlangan kalitlar va jurnallar o'chadi. |
| Fayl bir marta ochilgan, keyin «Ошибка при запуске приложения (0xc000003e)» | Windows faylni diskdan o'qiy olmadi (disk nosozligi) yoki uni antivirus o'zgartirdi | **0.6.1+** birinchi ochilishda o'zining tekshirilgan nusxasini saqlaydi va ish stoliga «Manajoy GameClass» yorlig'ini qo'yadi — **yorliqdan oching**. «Tekshiruv» qadamida «Yuklab olingan fayl» va «Disk holati» qatorlari sababni ko'rsatadi: *o'qib bo'lmadi* → disk, *o'zgargan / topilmadi* → antivirus. Disk xatosi bo'lsa, klub texnigiga ayting (GameClass bazasi uchun ham xavfli). |
| «Windows protected your PC» (SmartScreen) | Fayl hali raqamli imzolanmagan | **More info → Run anyway**. Fayl Manajoy'dan kelganiga ishonch hosil qiling. |
| GameClass «yo'qolib qoldi», qayta ochsam «Невозможно открыть сервер для UDP-порта 3775» | GameClass yopilmagan: minimallashtirilganda u soat yonidagi ^ belgisiga (tray) yashirinadi. Ikkinchi nusxa portni ocha olmaydi | Xato oynasini OK bilan yoping va ikkinchi nusxani yoping. Tray'dagi (^) GameClass belgisini ikki marta bosing. Bridge paneli bu holatni o'zi aytadi. |
| Hamma kompyuter «noma'lum» | GameClass Server'da operator kirmagan yoki dastur yopiq | GameClass Server'ni ochib, **operator** bilan kiring. |
| «SQL administrator paroli noto'g'ri» | `sa` paroli bo'sh emas | Paroldan so'rang yoki «Windows hisobi bilan kirish» ni sinang. |
| «Kod noto'g'ri yoki muddati o'tgan» | Kod 15 daqiqadan eski yoki boshqa kod kiritilgan | Ilovada «Yangi kod» ni bosing. |
| «Klubingizda iCafeCloud ulangan» | Klubda bitta jonli holat manbai bo'lishi mumkin | Ilovada iCafe ulanishini o'chiring. |
| «Manajoy'da jonli holat o'chirilgan» | Ilovada jonli holat o'chirilgan | Kompyuterlar holati → Sozlamalar → «Avtomatik yangilash» ni yoqing. |
| «X ta kompyuter stansiyaga bog'lanmagan» | Manajoy'da shu nomli stansiya topilmadi | Ilovada Kompyuterlar holati → Sozlamalar'da tasdiqlang. |
| GameClass «BOF or EOF» xatosi | Windows formati ruscha emas | `Set-Culture ru-RU` buyrug'ini bering, so'ng chiqib qayta kiring. |
| Rezerv vaqti kelganda GameClass «Could not convert variant of type (Null) into type (Double)» oynalarini to'xtovsiz chiqaradi | GameClass 3.85.6 xatosi (dasturdan qat'i nazar) | GameClass → Manage → System options → Настройки → **Автоактивация брони** ni yoqing. |

## Texnik uchun: PowerShell kit (zaxira yo'l)

`ManajoyGameClassKit-*.zip` ichida o'sha dastur va skriptlar bor: `setup.ps1`, `preflight.ps1`, `create-readonly-login.ps1`, `collect-report.ps1`, `uninstall.ps1`. Oyna ochilmaydigan holatlar uchun (masalan, faqat masofaviy PowerShell bo'lsa):

```powershell
cd C:\ManajoyGameClassKit
powershell -ExecutionPolicy Bypass -File .\setup.ps1
cd bridge\x64      # 32-bit Windows'da: bridge\x86
.\install-service.ps1 -PushUrl <Push URL>      # kalitni support beradi (manage.py gameclass_bridge_key)
```

Olib tashlash: `powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -DropLogin`.
