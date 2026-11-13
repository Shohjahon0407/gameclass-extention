; *** Inno Setup version 6.5.0+ Uzbek (Latin) messages — Manajoy translation ***
;
; To download user-contributed translations of this file, go to:
;   https://jrsoftware.org/files/istrans/
;
; Note: When translating this text, do not add periods (.) to the end of
; messages that didn't have them already, because on those messages Inno
; Setup adds the periods automatically (appending a period would result in
; two periods being displayed).

[LangOptions]
; The following three entries are very important. Be sure to read and
; understand the '[LangOptions] section' topic in the help file.
LanguageName=O'zbekcha
LanguageID=$0443
; LanguageCodePage should always be set if possible, even if this file is Unicode
; For English it's set to zero anyway because English only uses ASCII characters
LanguageCodePage=1254
; If the language you are translating to requires special font faces or
; sizes, uncomment any of the following entries and change them accordingly.
;DialogFontName=
;DialogFontSize=9
;DialogFontBaseScaleWidth=7
;DialogFontBaseScaleHeight=15
;WelcomeFontName=Segoe UI
;WelcomeFontSize=14

[Messages]

; *** Application titles
SetupAppTitle=O'rnatish
SetupWindowTitle=O'rnatish - %1
UninstallAppTitle=O'chirish
UninstallAppFullTitle=%1 dasturini o'chirish

; *** Misc. common
InformationTitle=Ma'lumot
ConfirmTitle=Tasdiqlash
ErrorTitle=Xato

; *** SetupLdr messages
SetupLdrStartupMessage=%1 o'rnatiladi. Davom etasizmi?
LdrCannotCreateTemp=Vaqtinchalik fayl yaratib bo'lmadi. O'rnatish to'xtatildi
LdrCannotExecTemp=Vaqtinchalik papkadagi faylni ishga tushirib bo'lmadi. O'rnatish to'xtatildi
HelpTextNote=

; *** Startup error messages
LastErrorMessage=%1.%n%nXato %2: %3
SetupFileMissing=O'rnatish papkasida %1 fayli yo'q. Muammoni tuzating yoki dasturni qayta yuklab oling.
SetupFileCorrupt=O'rnatish fayllari buzilgan. Dasturni qayta yuklab oling.
SetupFileCorruptOrWrongVer=O'rnatish fayllari buzilgan yoki bu versiyaga mos emas. Dasturni qayta yuklab oling.
InvalidParameter=Buyruq qatorida noto'g'ri parametr berildi:%n%n%1
SetupAlreadyRunning=O'rnatish dasturi allaqachon ishlayapti.
WindowsVersionNotSupported=Dastur kompyuteringizdagi Windows versiyasini qo'llab-quvvatlamaydi.
WindowsServicePackRequired=Dastur uchun %1 Service Pack %2 yoki yangisi kerak.
NotOnThisPlatform=Dastur %1 da ishlamaydi.
OnlyOnThisPlatform=Dastur faqat %1 da ishlaydi.
OnlyOnTheseArchitectures=Dasturni faqat quyidagi protsessorlar uchun mo'ljallangan Windows'ga o'rnatish mumkin:%n%n%1
WinVersionTooLowError=Dastur uchun %1 %2 versiyasi yoki yangisi kerak.
WinVersionTooHighError=Dasturni %1 %2 versiyasi yoki yangisiga o'rnatib bo'lmaydi.
AdminPrivilegesRequired=Dasturni o'rnatish uchun administrator sifatida kirgan bo'lishingiz kerak.
PowerUserPrivilegesRequired=Dasturni o'rnatish uchun administrator yoki Power Users guruhi a'zosi bo'lishingiz kerak.
SetupAppRunningError=%1 hozir ishlayapti.%n%nUning barcha oynalarini yoping va davom etish uchun OK ni, chiqish uchun Bekor qilish ni bosing.
UninstallAppRunningError=%1 hozir ishlayapti.%n%nUning barcha oynalarini yoping va davom etish uchun OK ni, chiqish uchun Bekor qilish ni bosing.

; *** Startup questions
PrivilegesRequiredOverrideTitle=O'rnatish rejimini tanlang
PrivilegesRequiredOverrideInstruction=O'rnatish rejimini tanlang
PrivilegesRequiredOverrideText1=%1 ni barcha foydalanuvchilar uchun (administrator huquqi kerak) yoki faqat siz uchun o'rnatish mumkin.
PrivilegesRequiredOverrideText2=%1 ni faqat siz uchun yoki barcha foydalanuvchilar uchun (administrator huquqi kerak) o'rnatish mumkin.
PrivilegesRequiredOverrideAllUsers=&Barcha foydalanuvchilar uchun
PrivilegesRequiredOverrideAllUsersRecommended=&Barcha foydalanuvchilar uchun (tavsiya etiladi)
PrivilegesRequiredOverrideCurrentUser=&Faqat men uchun
PrivilegesRequiredOverrideCurrentUserRecommended=&Faqat men uchun (tavsiya etiladi)

; *** Misc. errors
ErrorCreatingDir="%1" papkasini yaratib bo'lmadi
ErrorTooManyFilesInDir="%1" papkasida fayllar juda ko'p, yangi fayl yaratib bo'lmadi

; *** Setup common messages
ExitSetupTitle=O'rnatishdan chiqish
ExitSetupMessage=O'rnatish tugamadi. Hozir chiqsangiz, dastur o'rnatilmaydi.%n%nO'rnatishni keyinroq qayta ishga tushirishingiz mumkin.%n%nChiqasizmi?
AboutSetupMenuItem=O'rnatish &haqida...
AboutSetupTitle=O'rnatish haqida
AboutSetupMessage=%1, versiya %2%n%3%n%n%1 sayti:%n%4
AboutSetupNote=
TranslatorNote=

; *** Buttons
ButtonBack=< &Orqaga
ButtonNext=&Keyingi >
ButtonInstall=&O'rnatish
ButtonOK=OK
ButtonCancel=Bekor qilish
ButtonYes=&Ha
ButtonYesToAll=Hammasiga h&a
ButtonNo=&Yo'q
ButtonNoToAll=Hammasiga y&o'q
ButtonFinish=&Tayyor
ButtonBrowse=&Tanlash...
ButtonWizardBrowse=T&anlash...
ButtonNewFolder=&Yangi papka

; *** "Select Language" dialog messages
SelectLanguageTitle=O'rnatish tilini tanlang
SelectLanguageLabel=O'rnatish davomida ishlatiladigan tilni tanlang.

; *** Common wizard text
ClickNext=Davom etish uchun Keyingi ni, chiqish uchun Bekor qilish ni bosing.
BeveledLabel=
BrowseDialogTitle=Papkani tanlash
BrowseDialogLabel=Ro'yxatdan papkani tanlang va OK ni bosing.
NewFolderName=Yangi papka

; *** "Welcome" wizard page
WelcomeLabel1=[name] o'rnatish ustasiga xush kelibsiz
WelcomeLabel2=Kompyuteringizga [name/ver] o'rnatiladi.%n%nDavom etishdan oldin boshqa dasturlarni yopish tavsiya etiladi.

; *** "Password" wizard page
WizardPassword=Parol
PasswordLabel1=Bu o'rnatish parol bilan himoyalangan.
PasswordLabel3=Parolni kiriting va Keyingi ni bosing. Katta-kichik harflar farqlanadi.
PasswordEditLabel=&Parol:
IncorrectPassword=Parol noto'g'ri. Qayta urinib ko'ring.

; *** "License Agreement" wizard page
WizardLicense=Shartlar va maxfiylik
LicenseLabel=Davom etishdan oldin quyidagi muhim ma'lumotni o'qing.
LicenseLabel3=Quyidagi foydalanish shartlari va maxfiylik siyosatini o'qing. O'rnatishni davom ettirish uchun ularni qabul qilishingiz kerak.
LicenseAccepted=Shartlarni &qabul qilaman
LicenseNotAccepted=Shartlarni qabul &qilmayman

; *** "Information" wizard pages
WizardInfoBefore=Ma'lumot
InfoBeforeLabel=Davom etishdan oldin quyidagi muhim ma'lumotni o'qing.
InfoBeforeClickLabel=Tayyor bo'lsangiz, Keyingi ni bosing.
WizardInfoAfter=Ma'lumot
InfoAfterLabel=Davom etishdan oldin quyidagi muhim ma'lumotni o'qing.
InfoAfterClickLabel=Tayyor bo'lsangiz, Keyingi ni bosing.

; *** "User Information" wizard page
WizardUserInfo=Foydalanuvchi ma'lumotlari
UserInfoDesc=Ma'lumotlaringizni kiriting.
UserInfoName=&Foydalanuvchi nomi:
UserInfoOrg=&Tashkilot:
UserInfoSerial=&Seriya raqami:
UserInfoNameRequired=Ismni kiritishingiz kerak.

; *** "Select Destination Location" wizard page
WizardSelectDir=O'rnatish papkasini tanlang
SelectDirDesc=[name] qayerga o'rnatilsin?
SelectDirLabel3=[name] quyidagi papkaga o'rnatiladi.
SelectDirBrowseLabel=Davom etish uchun Keyingi ni bosing. Boshqa papka tanlash uchun Tanlash ni bosing.
DiskSpaceGBLabel=Kamida [gb] GB bo'sh joy kerak.
DiskSpaceMBLabel=Kamida [mb] MB bo'sh joy kerak.
CannotInstallToNetworkDrive=Tarmoq diskiga o'rnatib bo'lmaydi.
CannotInstallToUNCPath=UNC yo'liga o'rnatib bo'lmaydi.
InvalidPath=Disk harfi bilan to'liq yo'l kiriting, masalan:%n%nC:\APP%n%nyoki UNC yo'l:%n%n\\server\share
InvalidDrive=Tanlangan disk yoki UNC papka mavjud emas yoki unga kirib bo'lmaydi. Boshqasini tanlang.
DiskSpaceWarningTitle=Diskda joy yetarli emas
DiskSpaceWarning=O'rnatish uchun kamida %1 KB bo'sh joy kerak, tanlangan diskda esa %2 KB bor.%n%nBaribir davom etasizmi?
DirNameTooLong=Papka nomi yoki yo'li juda uzun.
InvalidDirName=Papka nomi noto'g'ri.
BadDirName32=Papka nomida quyidagi belgilar bo'lmasligi kerak:%n%n%1
DirExistsTitle=Papka mavjud
DirExists=Papka:%n%n%1%n%nallaqachon mavjud. Baribir shu papkaga o'rnatilsinmi?
DirDoesntExistTitle=Papka mavjud emas
DirDoesntExist=Papka:%n%n%1%n%nmavjud emas. Yaratilsinmi?

; *** "Select Components" wizard page
WizardSelectComponents=Qismlarni tanlang
SelectComponentsDesc=Qaysi qismlar o'rnatilsin?
SelectComponentsLabel2=O'rnatiladigan qismlarni belgilang, keraksizlaridan belgini olib tashlang. Tayyor bo'lsangiz, Keyingi ni bosing.
FullInstallation=To'liq o'rnatish
; if possible don't translate 'Compact' as 'Minimal' (I mean 'Minimal' in your language)
CompactInstallation=Ixcham o'rnatish
CustomInstallation=Tanlab o'rnatish
NoUninstallWarningTitle=Qismlar mavjud
NoUninstallWarning=Quyidagi qismlar kompyuteringizda allaqachon o'rnatilgan:%n%n%1%n%nBelgini olib tashlash ularni o'chirmaydi.%n%nBaribir davom etasizmi?
ComponentSize1=%1 KB
ComponentSize2=%1 MB
ComponentsDiskSpaceGBLabel=Tanlangan qismlar uchun kamida [gb] GB joy kerak.
ComponentsDiskSpaceMBLabel=Tanlangan qismlar uchun kamida [mb] MB joy kerak.

; *** "Select Additional Tasks" wizard page
WizardSelectTasks=Qo'shimcha vazifalar
SelectTasksDesc=Qanday qo'shimcha vazifalar bajarilsin?
SelectTasksLabel2=[name] o'rnatilayotganda bajariladigan qo'shimcha vazifalarni tanlang va Keyingi ni bosing.

; *** "Select Start Menu Folder" wizard page
WizardSelectProgramGroup=Start menyusidagi papka
SelectStartMenuFolderDesc=Dastur yorliqlari qayerga qo'yilsin?
SelectStartMenuFolderLabel3=Dastur yorliqlari Start menyusining quyidagi papkasida yaratiladi.
SelectStartMenuFolderBrowseLabel=Davom etish uchun Keyingi ni bosing. Boshqa papka tanlash uchun Tanlash ni bosing.
MustEnterGroupName=Papka nomini kiritishingiz kerak.
GroupNameTooLong=Papka nomi yoki yo'li juda uzun.
InvalidGroupName=Papka nomi noto'g'ri.
BadGroupName=Papka nomida quyidagi belgilar bo'lmasligi kerak:%n%n%1
NoProgramGroupCheck2=Start menyusida papka &yaratilmasin

; *** "Ready to Install" wizard page
WizardReady=O'rnatishga tayyor
ReadyLabel1=[name] kompyuteringizga o'rnatishga tayyor.
ReadyLabel2a=O'rnatish uchun O'rnatish ni bosing yoki sozlamalarni ko'rib chiqish va o'zgartirish uchun Orqaga ni bosing.
ReadyLabel2b=O'rnatish uchun O'rnatish ni bosing.
ReadyMemoUserInfo=Foydalanuvchi:
ReadyMemoDir=O'rnatish papkasi:
ReadyMemoType=O'rnatish turi:
ReadyMemoComponents=Tanlangan qismlar:
ReadyMemoGroup=Start menyusidagi papka:
ReadyMemoTasks=Qo'shimcha vazifalar:

; *** TDownloadWizardPage wizard page and DownloadTemporaryFile
DownloadingLabel2=Fayllar yuklab olinmoqda...
ButtonStopDownload=Yuklashni &to'xtatish
StopDownload=Yuklab olishni to'xtatasizmi?
ErrorDownloadAborted=Yuklab olish to'xtatildi
ErrorDownloadFailed=Yuklab olib bo'lmadi: %1 %2
ErrorDownloadSizeFailed=Hajmni aniqlab bo'lmadi: %1 %2
ErrorProgress=Noto'g'ri jarayon: %1 / %2
ErrorFileSize=Fayl hajmi noto'g'ri: kutilgan %1, topilgan %2

; *** TExtractionWizardPage wizard page and ExtractArchive
ExtractingLabel=Fayllar ochilmoqda...
ButtonStopExtraction=Ochishni &to'xtatish
StopExtraction=Ochishni to'xtatasizmi?
ErrorExtractionAborted=Ochish to'xtatildi
ErrorExtractionFailed=Ochib bo'lmadi: %1

; *** Archive extraction failure details
ArchiveIncorrectPassword=Parol noto'g'ri
ArchiveIsCorrupted=Arxiv buzilgan
ArchiveUnsupportedFormat=Arxiv formati qo'llab-quvvatlanmaydi

; *** "Preparing to Install" wizard page
WizardPreparing=O'rnatishga tayyorlanmoqda
PreparingDesc=[name] ni o'rnatishga tayyorlanmoqda.
PreviousInstallNotCompleted=Avvalgi dasturni o'rnatish yoki o'chirish tugallanmagan. Uni tugatish uchun kompyuterni qayta yoqing.%n%nQayta yoqqandan keyin [name] o'rnatishini yana ishga tushiring.
CannotContinue=O'rnatishni davom ettirib bo'lmaydi. Chiqish uchun Bekor qilish ni bosing.
ApplicationsFound=Quyidagi dasturlar yangilanishi kerak bo'lgan fayllardan foydalanmoqda. Ularni avtomatik yopishga ruxsat berish tavsiya etiladi.
ApplicationsFound2=Quyidagi dasturlar yangilanishi kerak bo'lgan fayllardan foydalanmoqda. Ularni avtomatik yopishga ruxsat berish tavsiya etiladi. O'rnatish tugagach, ular qayta ishga tushiriladi.
CloseApplications=Dasturlarni &avtomatik yopish
DontCloseApplications=Dasturlarni &yopmaslik
ErrorCloseApplications=Barcha dasturlarni avtomatik yopib bo'lmadi. Davom etishdan oldin yangilanadigan fayllardan foydalanayotgan dasturlarni yoping.
PrepareToInstallNeedsRestart=Kompyuterni qayta yoqish kerak. Qayta yoqqandan keyin [name] o'rnatishini yana ishga tushiring.%n%nHozir qayta yoqilsinmi?

; *** "Installing" wizard page
WizardInstalling=O'rnatilmoqda
InstallingLabel=[name] kompyuteringizga o'rnatilmoqda, kuting.

; *** "Setup Completed" wizard page
FinishedHeadingLabel=[name] o'rnatildi
FinishedLabelNoIcons=[name] kompyuteringizga o'rnatildi.
FinishedLabel=[name] kompyuteringizga o'rnatildi. Dasturni yaratilgan yorliqlar orqali ochishingiz mumkin.
ClickFinish=Chiqish uchun Tayyor ni bosing.
FinishedRestartLabel=[name] o'rnatishini tugatish uchun kompyuterni qayta yoqish kerak. Hozir qayta yoqilsinmi?
FinishedRestartMessage=[name] o'rnatishini tugatish uchun kompyuterni qayta yoqish kerak.%n%nHozir qayta yoqilsinmi?
ShowReadmeCheck=Ha, README faylini ko'rmoqchiman
YesRadio=&Ha, kompyuter hozir qayta yoqilsin
NoRadio=&Yo'q, keyinroq o'zim qayta yoqaman
; used for example as 'Run MyProg.exe'
RunEntryExec=%1 ni ochish
; used for example as 'View Readme.txt'
RunEntryShellExec=%1 ni ko'rish

; *** "Setup Needs the Next Disk" stuff
ChangeDiskTitle=Keyingi disk kerak
SelectDiskLabel2=%1-diskni qo'ying va OK ni bosing.%n%nAgar fayllar boshqa papkada bo'lsa, to'g'ri yo'lni kiriting yoki Tanlash ni bosing.
PathLabel=&Yo'l:
FileNotInDir2="%1" fayli "%2" da topilmadi. To'g'ri diskni qo'ying yoki boshqa papkani tanlang.
SelectDirectoryLabel=Keyingi disk joylashuvini ko'rsating.

; *** Installation phase messages
SetupAborted=O'rnatish tugallanmadi.%n%nMuammoni tuzating va o'rnatishni qayta ishga tushiring.
AbortRetryIgnoreSelectAction=Amalni tanlang
AbortRetryIgnoreRetry=&Qayta urinish
AbortRetryIgnoreIgnore=&Xatoni e'tiborsiz qoldirib davom etish
AbortRetryIgnoreCancel=O'rnatishni bekor qilish
RetryCancelSelectAction=Amalni tanlang
RetryCancelRetry=&Qayta urinish
RetryCancelCancel=Bekor qilish

; *** Installation status messages
StatusClosingApplications=Dasturlar yopilmoqda...
StatusCreateDirs=Papkalar yaratilmoqda...
StatusExtractFiles=Fayllar ochilmoqda...
StatusDownloadFiles=Fayllar yuklab olinmoqda...
StatusCreateIcons=Yorliqlar yaratilmoqda...
StatusCreateIniEntries=INI yozuvlari yaratilmoqda...
StatusCreateRegistryEntries=Reestr yozuvlari yaratilmoqda...
StatusRegisterFiles=Fayllar ro'yxatdan o'tkazilmoqda...
StatusSavingUninstall=O'chirish ma'lumotlari saqlanmoqda...
StatusRunProgram=O'rnatish yakunlanmoqda...
StatusRestartingApplications=Dasturlar qayta ishga tushirilmoqda...
StatusRollback=O'zgarishlar bekor qilinmoqda...

; *** Misc. errors
ErrorInternal2=Ichki xato: %1
ErrorFunctionFailedNoCode=%1 bajarilmadi
ErrorFunctionFailed=%1 bajarilmadi; kod %2
ErrorFunctionFailedWithMessage=%1 bajarilmadi; kod %2.%n%3
ErrorExecutingProgram=Faylni ishga tushirib bo'lmadi:%n%1

; *** Registry errors
ErrorRegOpenKey=Reestr kalitini ochib bo'lmadi:%n%1\%2
ErrorRegCreateKey=Reestr kalitini yaratib bo'lmadi:%n%1\%2
ErrorRegWriteKey=Reestr kalitiga yozib bo'lmadi:%n%1\%2

; *** INI errors
ErrorIniEntry="%1" faylida INI yozuvini yaratib bo'lmadi.

; *** File copying errors
FileAbortRetryIgnoreSkipNotRecommended=Bu faylni &o'tkazib yuborish (tavsiya etilmaydi)
FileAbortRetryIgnoreIgnoreNotRecommended=&Xatoni e'tiborsiz qoldirib davom etish (tavsiya etilmaydi)
SourceIsCorrupted=Manba fayl buzilgan
SourceDoesntExist="%1" manba fayli mavjud emas
SourceVerificationFailed=Manba faylni tekshirib bo'lmadi: %1
VerificationSignatureDoesntExist="%1" imzo fayli mavjud emas
VerificationSignatureInvalid="%1" imzo fayli noto'g'ri
VerificationKeyNotFound="%1" imzo fayli noma'lum kalitdan foydalanadi
VerificationFileNameIncorrect=Fayl nomi noto'g'ri
VerificationFileTagIncorrect=Fayl tegi noto'g'ri
VerificationFileSizeIncorrect=Fayl hajmi noto'g'ri
VerificationFileHashIncorrect=Fayl xeshi noto'g'ri
ExistingFileReadOnly2=Mavjud fayl faqat o'qish uchun belgilangani sababli almashtirib bo'lmadi.
ExistingFileReadOnlyRetry=&Faqat o'qish belgisini olib tashlab qayta urinish
ExistingFileReadOnlyKeepExisting=Mavjud faylni &qoldirish
ErrorReadingExistingDest=Mavjud faylni o'qishda xato yuz berdi:
FileExistsSelectAction=Amalni tanlang
FileExists2=Fayl allaqachon mavjud.
FileExistsOverwriteExisting=Mavjud faylni &almashtirish
FileExistsKeepExisting=Mavjud faylni &qoldirish
FileExistsOverwriteOrKeepAll=Keyingi holatlarda ham &shunday qilish
ExistingFileNewerSelectAction=Amalni tanlang
ExistingFileNewer2=Mavjud fayl o'rnatilayotganidan yangiroq.
ExistingFileNewerOverwriteExisting=Mavjud faylni &almashtirish
ExistingFileNewerKeepExisting=Mavjud faylni &qoldirish (tavsiya etiladi)
ExistingFileNewerOverwriteOrKeepAll=Keyingi holatlarda ham &shunday qilish
ErrorChangingAttr=Mavjud fayl atributlarini o'zgartirishda xato yuz berdi:
ErrorCreatingTemp=O'rnatish papkasida fayl yaratishda xato yuz berdi:
ErrorReadingSource=Manba faylni o'qishda xato yuz berdi:
ErrorCopying=Faylni nusxalashda xato yuz berdi:
ErrorDownloading=Faylni yuklab olishda xato yuz berdi:
ErrorExtracting=Arxivni ochishda xato yuz berdi:
ErrorReplacingExistingFile=Mavjud faylni almashtirishda xato yuz berdi:
ErrorRestartReplace=RestartReplace bajarilmadi:
ErrorRenamingTemp=O'rnatish papkasidagi fayl nomini o'zgartirishda xato yuz berdi:
ErrorRegisterServer=DLL/OCX ni ro'yxatdan o'tkazib bo'lmadi: %1
ErrorRegSvr32Failed=RegSvr32 %1 kodi bilan tugadi
ErrorRegisterTypeLib=Tur kutubxonasini ro'yxatdan o'tkazib bo'lmadi: %1

; *** Uninstall display name markings
; used for example as 'My Program (32-bit)'
UninstallDisplayNameMark=%1 (%2)
; used for example as 'My Program (32-bit, All users)'
UninstallDisplayNameMarks=%1 (%2, %3)
UninstallDisplayNameMark32Bit=32-bit
UninstallDisplayNameMark64Bit=64-bit
UninstallDisplayNameMarkAllUsers=Barcha foydalanuvchilar
UninstallDisplayNameMarkCurrentUser=Joriy foydalanuvchi

; *** Post-installation errors
ErrorOpeningReadme=README faylini ochishda xato yuz berdi.
ErrorRestartingComputer=Kompyuterni qayta yoqib bo'lmadi. Buni o'zingiz qiling.

; *** Uninstaller messages
UninstallNotFound="%1" fayli mavjud emas. O'chirib bo'lmaydi.
UninstallOpenError="%1" faylini ochib bo'lmadi. O'chirib bo'lmaydi
UninstallUnsupportedVer="%1" o'chirish jurnali formati bu versiyaga tanish emas. O'chirib bo'lmaydi
UninstallUnknownEntry=O'chirish jurnalida noma'lum yozuv (%1) uchradi
ConfirmUninstall=%1 va uning barcha qismlarini butunlay o'chirasizmi?
UninstallOnlyOnWin64=Bu dasturni faqat 64-bitli Windows'da o'chirish mumkin.
OnlyAdminCanUninstall=Bu dasturni faqat administrator huquqiga ega foydalanuvchi o'chira oladi.
UninstallStatusLabel=%1 kompyuteringizdan o'chirilmoqda, kuting.
UninstalledAll=%1 kompyuteringizdan o'chirildi.
UninstalledMost=%1 o'chirildi.%n%nBa'zi qismlarni o'chirib bo'lmadi, ularni qo'lda o'chirishingiz mumkin.
UninstalledAndNeedsRestart=%1 ni o'chirishni tugatish uchun kompyuterni qayta yoqish kerak.%n%nHozir qayta yoqilsinmi?
UninstallDataCorrupted="%1" fayli buzilgan. O'chirib bo'lmaydi

; *** Uninstallation phase messages
ConfirmDeleteSharedFileTitle=Umumiy fayl o'chirilsinmi?
ConfirmDeleteSharedFile2=Quyidagi umumiy fayldan boshqa dasturlar foydalanmayotganga o'xshaydi. O'chirilsinmi?%n%nAgar biror dastur undan foydalanayotgan bo'lsa, o'chirilgach u to'g'ri ishlamasligi mumkin. Ishonchingiz komil bo'lmasa, Yo'q ni tanlang. Faylni qoldirish zarar keltirmaydi.
SharedFileNameLabel=Fayl nomi:
SharedFileLocationLabel=Joylashuvi:
WizardUninstalling=O'chirish holati
StatusUninstalling=%1 o'chirilmoqda...

; *** Shutdown block reasons
ShutdownBlockReasonInstallingApp=%1 o'rnatilmoqda.
ShutdownBlockReasonUninstallingApp=%1 o'chirilmoqda.

; The custom messages below aren't used by Setup itself, but if you make
; use of them in your scripts, you'll want to translate them.

[CustomMessages]

NameAndVersion=%1, versiya %2
AdditionalIcons=Qo'shimcha yorliqlar:
CreateDesktopIcon=&Ish stolida yorliq yaratish
CreateQuickLaunchIcon=&Tez ishga tushirish panelida yorliq yaratish
ProgramOnTheWeb=%1 saytda
UninstallProgram=%1 ni o'chirish
LaunchProgram=%1 ni ochish
AssocFileExtension=%1 ni %2 fayl kengaytmasi bilan &bog'lash
AssocingFileExtension=%1 %2 fayl kengaytmasi bilan bog'lanmoqda...
AutoStartProgramGroupDescription=Avtomatik ishga tushirish:
AutoStartProgram=%1 ni avtomatik ishga tushirish
AddonHostProgramNotFound=%1 tanlangan papkada topilmadi.%n%nBaribir davom etasizmi?
