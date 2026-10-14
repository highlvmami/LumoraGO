# LumoraGo Gizlilik Politikası

LumoraGo, Windows için bir başlatıcı (launcher) uygulamasıdır ve tamamen kullanıcının kendi bilgisayarında çalışır.

## Google Kişiler

- "Google ile bağlan" kullanıldığında LumoraGo yalnızca **okuma** izni ister (`contacts.readonly`) ve hesabın e-posta adresini görür.
- Google Kişiler'den alınan ad, telefon, e-posta ve şirket bilgileri yalnızca bu bilgisayarda, Windows hesabına bağlı şifreli bir dosyada (`%LOCALAPPDATA%\LumoraGo\contacts.dat`) saklanır.
- Bu bilgiler hiçbir sunucuya gönderilmez, kimseyle paylaşılmaz, reklam ya da analiz için kullanılmaz. Yalnızca launcher'da kişi aramak için kullanılır.
- Google rehberinde hiçbir değişiklik yapılmaz.
- Bağlantı, Ayarlar → Kişiler → "Bağlantıyı kes" ile ya da https://myaccount.google.com/permissions adresinden istenildiği an kaldırılabilir; kesildiğinde Google'dan gelen kişiler LumoraGo'dan silinir.

## Google Fotoğraflar

- LumoraGo fotoğraf kütüphanesinin tamamını okumaz. Google'ın kendi seçim sayfası açılır ve yalnızca kullanıcının orada seçtiği fotoğraflar alınır (`photospicker.mediaitems.readonly`).
- Seçilen fotoğraflar yalnızca bu bilgisayara (`%LOCALAPPDATA%\LumoraGo\photos`) indirilir ve launcher'da göstermek için kullanılır. Hiçbir sunucuya gönderilmez, kimseyle paylaşılmaz.
- Google Fotoğraflar'da hiçbir değişiklik yapılmaz. Seçilenler /foto listesinden istenildiği an kaldırılabilir.

## Google Takvim, Görevler, Drive ve YouTube

- **Takvim** (`calendar.events`, `calendar.readonly`): Google Takvim'de görünen takvimlerin etkinlikleri launcher'da gösterilir; kullanıcı "etkinlik …" yazıp onaylarsa yeni etkinlik eklenir. Başka bir değişiklik yapılmaz.
- **Görevler** (`tasks`): yapılacaklar listelenir; kullanıcı isterse görev eklenir ya da tamamlandı işaretlenir.
- **Drive** (`drive.metadata.readonly`): yalnızca dosya adları, türleri ve bağlantıları aranır; dosyaların içeriği okunmaz, hiçbir dosya değiştirilmez.
- **YouTube** (`youtube.readonly`): abonelikler, oynatma listeleri ve beğenilen videoların adları ve bağlantıları okunur ve bu bilgisayarda (`%LOCALAPPDATA%\LumoraGo\cache\youtube.json`) saklanır.
- Bu bilgiler hiçbir sunucuya gönderilmez, kimseyle paylaşılmaz; yalnızca launcher'da göstermek için kullanılır.

## Diğer veriler

Ayarlar, notlar, pano geçmişi ve hesap bilgileri yalnızca bilgisayarda (`%LOCALAPPDATA%\LumoraGo`) tutulur. LumoraGo hava durumu, kur, maç gibi bilgiler için ilgili hizmetlere yalnızca yazılan aramayı gönderir; kişisel veri göndermez.

## İletişim

Sorular için: https://github.com/highlvmami/LumoraGO/issues

## Spotify

Ayarlar'dan "Spotify ile bağlan" yapılırsa LumoraGo, kullanıcının kendi Spotify geliştirici uygulamasıyla şu izinleri ister: profil ve abonelik türü (user-read-private), çalma durumu ve kontrolü (user-read-playback-state, user-modify-playback-state, user-read-currently-playing), çalma listeleri (playlist-read-private, playlist-read-collaborative).

- Yenileme anahtarı Windows hesabına bağlı şifreli dosyada tutulur. Arama metinleri yalnızca Spotify'a gönderilir; hiçbir şey başka bir sunucuya gönderilmez.

## Yapay zekâ ve ekrandan metin

- "? soru" ya da "/ai" ile Enter'a basıldığında yazılan soru ya da seçilen işlem için panodaki metin, kullanıcının kendi API anahtarıyla Anthropic'e (Claude API) gönderilir. Enter'a basılmadan hiçbir şey gönderilmez. Anahtar Windows hesabına bağlı şifreli dosyada tutulur.
- Ekrandan metin (/ocr) tamamen bilgisayarda, Windows'un metin tanımasıyla çalışır; resim ya da metin hiçbir yere gönderilmez.
