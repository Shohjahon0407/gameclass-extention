# Manajoy GameClass: ilova ichidan yangilash

0.11.0 versiyani bir marta `Setup.exe` orqali o'rnating. Keyingi versiyalar
**Sozlamalar → Dastur yangilanishi** orqali yuklanadi va o'rnatiladi.
Panel ochilganda va ochiq tursa har 6 soatda tekshiradi; o'rnatish har doim foydalanuvchi tugmasi bilan boshlanadi.
Internet ishlamasa hozirgi bridge xizmati ishlashda davom etadi.

## Release chiqarish

1. Bridge `.csproj` dagi versiyani oshiring. Chiqarilgan versiya raqamini qayta ishlatmang.
2. `bash field-kit/build-field-kit.sh` bajaring. Installer va sayt bilan birga
   `field-kit/out/site/gameclass/latest.json`, `latest.json.sig` va `download/*.exe` yaratiladi.
3. Shu uchta faylni saytning `public/gameclass/` papkasiga ko'chiring va odatdagi sayt deployini chiqaring.
   Yangi installer avval, manifest va uning imzosi birgalikda oxirida e'lon qilinadi.
   Vercel'da bitta deployment ularni birga chiqaradi.
4. `https://www.manajoy.uz/gameclass/latest.json`, `.json.sig` va manifestdagi download URL
   to'g'ridan-to'g'ri HTTP 200 qaytarishi kerak. Login, HTML sahifa yoki boshqa hostga redirect bo'lmasin.
   `latest.json` va `.sig` uchun `Cache-Control: no-store`; versiyali exe uchun immutable cache ishlatiladi.

Faqat installer tayyor bo'lsa, feedni alohida yaratish:

```bash
python3 field-kit/sign-release.py field-kit/out/site/gameclass 0.11.0 \
  field-kit/out/ManajoyGameClass-Setup-0.11.0.exe
```

## Imzolash kaliti

Public key dasturga `Setup/release-public.pem` resursi sifatida tikilgan.
RSA-3072/SHA-256 imzosi **manifestning aynan byte'lari** uchun tekshiriladi;
manifestdagi SHA-256 va uzunlik esa yuklangan exe uchun tekshiriladi. HTTPS redirectlar qabul qilinmaydi.
Private key saytga, zipga yoki gitga qo'shilmaydi:

```
~/.local/share/manajoy/release-signing/private.pem
```

Uni xavfsiz joyga zaxiralang. Boshqa build kompyuterida shu kalitni tiklang yoki
`MANAJOY_RELEASE_KEY=/xavfsiz/joy/private.pem` belgilang. Yo'qolgan kalit o'rniga
yangi kalit yaratish oldin o'rnatilgan mijozlarning yangilanishini buzadi.
Release skripti private key repo'dagi public key bilan mosligini tekshiradi va mos kelmasa to'xtaydi.

## O'rnatish va tiklash

Fayl `%ProgramData%\Manajoy\GameClassBridge\updates\<attempt>\` ichiga yuklanadi;
Downloads yoki Telegram papkasiga nusxa qo'yilmaydi. Bu papkaga faqat administrator va SYSTEM kira oladi.
Panel yopilgach alohida PowerShell 5.1 yordamchisi:

- xizmatni to'xtatadi, dastur fayllari va sozlamalarni zaxiralaydi;
- uninstall qilmasdan, ayni papkaga Inno Setup bilan yangilaydi;
- autostart holatini saqlaydi, avval ishlagan xizmatni qayta boshlaydi;
- `/health` orqali xizmat yangi versiya ekanini tekshiradi (baza vaqtincha ishlamasa HTTP 503 ham versiyani tekshirishga yaroqli);
- o'rnatish yoki tekshiruv xatosida eski fayllar va installer registry metadata'sini tiklaydi;
- muvaffaqiyatda vaqtinchalik installer va zaxirani o'chiradi, panelni qayta ochadi.

To'xtatilgan xizmat o'z-o'zidan yoqilmaydi. Windows restart talab qilsa majburan restart qilinmaydi;
panelda restart kerakligi ko'rsatiladi. Xato bo'lsa oxirgi backup qo'lda tiklash uchun qoldiriladi.
Natija: `%ProgramData%\Manajoy\GameClassBridge\updates\last-result.json`;
xato diagnostikasi va zaxira o'sha attempt papkasida turadi.
Oldingi Telegram downloadlarini ommaviy qidirib o'chirish bajarilmaydi.

## Tekshiruv

```bash
DOTNET_ROLL_FORWARD=Major dotnet test Manajoy.GameClass.sln
pwsh -NoProfile -File tests/OnlineUpdaterRunner.Tests.ps1
```

PowerShell testi xizmat/installer/registry o'rniga mock ishlatadi: buzilgan fayl va
installer xatosida dastur, SQL kaliti, konfiguratsiya va xarita saqlanishini tekshiradi.
Haqiqiy Windows'da release sinovi: 0.11.0 ni o'rnatib klubga ulash; keyingi **yangi raqamli** release'ni
feedga chiqarish; paneldan yangilash; PIN, ulanish, autostart va xizmat versiyasini tekshirish.
O'chirilgan xizmat, disk joyi yetmasligi, yarim yuklanish va muvaffaqiyatsiz install holatlarini ham sinang.

## 0.11.0 saytga tayyorlash

Bu ishchi kompyuterda sayt repozitori `../user-app-frontend/mana` ichida.
`public/gameclass/` dagi release fayllari va `next.config.ts` dagi cache headerlari
keyingi sayt deployiga qo'shilishi kerak. Lokal nusxaning tayyor bo'lishi production deployment emas.
