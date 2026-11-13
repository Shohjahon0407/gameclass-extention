# Yangi versiyani chiqarish (Manajoy GameClass)

## 1. Versiyani ko'tarish
`src/Manajoy.GameClass.Bridge/Manajoy.GameClass.Bridge.csproj` → `<Version>`.

## 2. Yig'ish
```bash
bash field-kit/build-field-kit.sh
```

Inno Setup 6 Wine ichida kerak. Bir marta o'rnatiladi:
```bash
wine innosetup-6.x.exe /VERYSILENT /CURRENTUSER /DIR="C:\InnoSetup6"
```

Natija `field-kit/out/` papkasiga tushadi:

| Fayl | Nima |
|---|---|
| `ManajoyGameClass-Setup-<v>.exe` | Windows o'rnatuvchisi — saytdagi asosiy fayl |
| `ManajoyGameClass-<v>-portable.exe` | o'rnatuvchisiz bitta fayl (Telegram uchun zaxira, < 50 MB) |
| `ManajoyGameClassKit-<v>.zip` | texnik uchun to'plam |
| `site/gameclass/` | yuklab olish sahifasi; versiya, hajm va SHA-256 to'ldirilgan |

## 3. Lab'da tekshirish (Windows VM)
1. **O'rnatish.** O'rnatuvchini GUI orqali o'rnating (uz/ru/en):
   - shartlar sahifasi chiqadi;
   - birinchi o'rnatishda papka tanlanadi, yangilashda papka sahifasi chiqmaydi;
   - yakunida dastur oynasi ochiladi.
2. **Yangilash.** O'rnatilgan bridge ustidan o'rnating. Xizmat to'xtab, yana ishga tushishi kerak; Manajoy ulanishi saqlanadi:
   ```
   http://127.0.0.1:8787/health → version, push.last_success_at
   ```
3. **O'chirish.**
   ```
   unins000.exe /VERYSILENT
   ```
   Natijada quyidagilar o'chishi kerak: xizmat, papka, `*.secret` fayllar, `C:\ProgramData\Manajoy\GameClassBridge`, reestr kaliti va yorliqlar.
4. **Defender tekshiruvi.**
   ```
   MpCmdRun -Scan -ScanType 3 -File <installer>
   ```
   Natija: «found no threats».

## 4. Microsoft'ga yuborish (har versiyada)
Imzosiz fayl bo'lgani uchun antivirus va SmartScreen ishonchini oshirish shu yo'l bilan bo'ladi.

1. https://www.microsoft.com/en-us/wdsi/filesubmission ga kiring.
2. «Software developer» ni tanlang va `ManajoyGameClass-Setup-<v>.exe` faylini yuklang.
3. Turini **Incorrectly detected as malware/malicious** qilib belgilang. Izohga yozing: «Manajoy GameClass installer (Inno Setup), read-only club status bridge».
4. Javob odatda 1–3 kunda keladi. Yangi versiya chiqqanda buni takrorlang.

## 5. Saytga joylash
- `out/site/gameclass/` papkasini biznes sahifasining `/gameclass/` manziliga yuklang (statik fayllar).
- `.exe` faylni nginx `application/octet-stream` turi bilan bersin.
- `latest.json` keyinchalik dasturning o'zi «yangi versiya bor» deb aytishi uchun ishlatiladi.

## 6. Sertifikat (keyin)
Code signing sertifikati olinganda `build-field-kit.sh` ga ikkita imzolash qadami qo'shiladi:
- installer'ga kirgan `Manajoy.GameClass.Bridge.exe` ni imzolash;
- `ManajoyGameClass-Setup-<v>.exe` ni imzolash.

Buning uchun Inno'da `SignTool=` sozlanadi; Linux'da `osslsigncode` ishlatiladi. Shundan keyin «Windows protected your PC» oynasi yo'qoladi.
