# Sürüm notları

Her sürüm bir `## X.Y.Z` başlığı ve altında `- ` ile başlayan maddelerden oluşur. Launcher'da `/v` yazınca çalışan sürümün maddeleri görünür; GitHub sürüm sayfasındaki notlar da buradan alınır.

## 0.29.0
- Ayarlar penceresi yenilendi: daha büyük, solda bölümler menüsü (Genel, Arama, Pano, Kısayollar, Kişiler, Google, Spotify, Yapay zekâ, Takvim ve mail) ve kartlar halinde düzen
- Google ve Spotify kurulum adımları ayrı bir kartta, satır satır okunur halde

## 0.28.0
- Yeni: ekrandan metin. /ocr (ya da /yazı) yazıp Enter'a bas, ekranda bir bölge seç; içindeki yazı panoya kopyalanır. Windows'un kendi metin tanıması kullanılır, internete bir şey gitmez
- Yeni: yapay zekâ. "? soru" ya da "/ai soru" yazıp Enter'a basınca Claude yanıtlar, yanıt listede görünür (Enter kopyalar, Ctrl+Enter yapıştırır, Shift+Boşluk tamamını gösterir)
- Panoda metin varken /ai özetle, /ai çevir (Türkçe ya da İngilizce), /ai düzelt ve /ai resmi çalışır
- Yapay zekâ için Ayarlar → Yapay zekâ'ya console.anthropic.com'dan alınan API anahtarı girilir. İstek yalnızca Enter'a basınca gider

## 0.27.0
- Yeni: /spotify (ya da /sp, /müzik). Kurulum gerekmeden /spotify tarkan aramayı Spotify uygulamasında açar; /spotify sonraki, önceki, duraklat şarkıyı kontrol eder
- Ayarlar → Spotify'dan bir kez bağlanınca şarkı, liste, albüm ve sanatçılar launcher'da listelenir ve Enter çalar (Premium gerekir; değilse uygulamada açılır). Boş /spotify şu an çalanı ve listelerini gösterir

## 0.26.0
- Yeni: /ezan (ya da /namaz, /vakit) bugünün namaz vakitlerini ve sıradaki vakte kalan süreyi gösterir ("Akşama 1 sa 22 dk"). /ezan ankara başka şehir, /ezan yarın yarının vakitleri. Vakitler Diyanet hesabıyla aladhan.com'dan gelir
- Yeni: /eczane nöbetçi eczaneleri Google Haritalar'da ve e-Devlet'in resmi listesinde açar; /eczane kadıköy gibi ilçe de yazılabilir. İstanbul için Eczacı Odası listesi de çıkar
- Ayarlar'daki şehir artık /hava, /ezan ve /eczane için ortak

## 0.25.0
- Yeni: pencere yerleştirme. Bir pencere öndeyken launcher'ı açıp "sol yarı", "sağ yarı", "sağ üst", "sol üçte bir", "ortala", "tam ekran", "küçült" ya da "diğer ekran" yaz, Enter'a bas. /pencere hepsini listeler
- Yeni: hazır metinler. ";adres" ya da ";iban" yazıp Enter'a basınca metin önceki pencereye yapıştırılır, Ctrl+Enter panoya kopyalar. /metin hepsini gösterir ve düzenleme ekranını açar
- Düzenleme ekranında ad, kısa ad, kategori ve çok satırlı içerik var; {tarih}, {saat}, {gün}, {yarın}, {pano} ve {imleç} gibi yer tutucular yapıştırırken doldurulur, canlı önizleme görünür. Arama, çoğaltma, silme, içe ve dışa aktarma da var

## 0.24.2
- /takvim artık bu haftayı ve önümüzdeki bir ayı gösterir; her etkinliğin başında "Bugün", "Yarın" ya da "Cum 16 Eki" yazar. Yalnızca bugün için /takvim bugün

## 0.24.1
- /takvim artık Google Takvim'de işaretli tüm takvimlerini gösterir (Aile, Doğum Günleri, Türkiye'deki Tatiller…); ana takvim dışındakilerin altında hangi takvimden geldiği yazar
- Bunun için Ayarlar'dan "Google ile bağlan"a bir kez daha bas; basmazsan yalnızca ana takvim gelmeye devam eder

## 0.24.0
- Yeni: Google Takvim. /takvim etkinlikleri doğrudan Google'dan gösterir (takvim adresi gerekmez); "etkinlik yarın 15:00 dişçi" yazıp Enter'a basınca Google Takvim'e hemen eklenir
- Yeni: Google Görevler. /görev (ya da /yapılacak, /todo) tüm listelerdeki yapılacakları gösterir; Enter tamamlar, yazınca "Görev ekle" çıkar ("/görev yarın fatura öde"). Her yerde "görev süt al" da çalışır
- Yeni: /drive ile Google Drive'da dosya adına göre arama; boşken son açtıkların. Enter tarayıcıda açar
- Yeni: /youtube ile abone olduğun kanallar, oynatma listelerin ve beğendiğin videolar içinde arama; en altta YouTube'da arama satırı. "/youtube yenile" listeyi hemen yeniler
- Kurulum, bir kerelik: Google Cloud'da Google Calendar API, Google Tasks API, Google Drive API ve YouTube Data API v3'ü etkinleştir, sonra Ayarlar'dan "Google ile bağlan"a yeniden bas ve tüm kutucukları işaretle

## 0.23.1
- Yeni: /güncelle yazınca LumoraGo yeni sürümü beklemeden kontrol eder, varsa indirip hemen kurar ve yeniden açılır

## 0.23.0
- Yeni: /foto ile fotoğraflar. Google Fotoğraflar'dan seçtiğin fotoğraflar ve bilgisayardaki Resimler klasörü en yenisi başta listelenir, yanda önizlemesi açılır
- /foto'da en üstteki satır Google Fotoğraflar'ın seçim sayfasını açar; seçip "Bitti" deyince fotoğraflar launcher'a iner. Google tüm kütüphanenin okunmasına izin vermediği için yalnızca seçtiklerin gelir
- Ada, klasöre ya da tarihe göre arayabilirsin ("/foto tatil", "/foto mart 2024"); sağ ok menüsünde klasörde göster ve listeden kaldır var
- Google fotoğrafları için bir kerelik: Google Cloud'da "Google Photos Picker API"yi etkinleştir ve Ayarlar → Kişiler'den "Google ile bağlan"a yeniden bas

## 0.22.2
- Google Rehber: adında emoji ya da bozuk karakter olan bir kişi eşitlemeyi durduruyor ve Ayarlar "okunuyor…" yazısında takılı kalıyordu; düzeldi
- Eşitleme beklenmedik bir hatayla durursa artık Ayarlar'da hatanın kendisi yazar

## 0.22.1
- Google Rehber: Google girişinde kişileri görme kutucuğu işaretsiz bırakılırsa artık "0 kişi" yerine bunu açıkça söyler ve kutucuğu işaretleyip yeniden bağlanmanı ister
- "Bağlantıyı kes" artık istemci kimliğini ve gizli anahtarı silmez; yeniden bağlanırken tekrar girmen gerekmez
- Google bir hata döndürdüğünde kendi açıklaması da Ayarlar'da ve kayıtta görünür

## 0.22.0
- Google Rehber: Ayarlar → Kişiler → "Google ile bağlan" ile Google Kişiler'deki herkes (ad, telefonlar, mailler, şirket) LumoraGo'ya gelir ve 3 saatte bir kendiliğinden yenilenir. Google'da eklediğin, değiştirdiğin ya da sildiğin kişi burada da güncellenir
- Yalnızca okuma izni istenir, Google Rehber'inde hiçbir şey değişmez. Bağlantı bilgisi Windows hesabına bağlı şifreli dosyada tutulur; "Bağlantıyı kes" Google'dan gelen kişileri LumoraGo'dan siler
- Google ilk bağlantı için bir kerelik Google Cloud kurulumu istiyor (adımlar Ayarlar'da yazıyor). Dosyadan aktarılan kişiler de kalır; Google'da aynı adla olan kişi iki kez görünmez

## 0.21.2
- LumoraGo'nun kendi logosu: uygulama, görev çubuğu, tepsi, Ayarlar penceresi ve kurulum dosyası artık mor "L" ve yıldız simgesini kullanır
- "/rehber" (ya da "/kisi", "/kişi") boş yazılınca artık tüm kişiler listelenir; önceden ilk 20 kişide kalıyordu
- Kişi araması yazdığın metnin geçtiği kişileri getirir: "/kişi ali" adında, e-postasında ya da şirketinde "ali" geçenleri gösterir (adı "ali" ile başlayanlar önce). Önceden harfleri sırayla içeren alakasız kişiler de çıkıyordu. Genel aramada yalnızca adı ya da soyadı yazılanla başlayan kişiler çıkar

## 0.21.1
- "/mac" düzeldi: "/mac fenerbahçe", "/mac fb", "/mac galatasaray", "/mac arsenal" artık takımın maçlarını getiriyor. En üstte son oynanan maç büyük kart olarak, altında sıradaki maçlar, en altta önceki maçlar. Sorun, maç kaynağının (ESPN) tarih aralığıyla yapılan isteği reddetmesiydi; artık takımın fikstürü doğrudan okunuyor
- "/lig": Süper Lig puan tablosu (sıra, puan, maç, galibiyet/beraberlik/mağlubiyet, averaj). "/lig premier", "/lig la liga", "/lig serie a", "/lig bundesliga", "/lig ligue 1" diğer ligler
- Takım adları Türkçe harfleriyle görünür (Fenerbahçe, Beşiktaş, İstanbul Başakşehir…)

## 0.21.0
- Sürükle-bırak: bir sonucu fareyle tutup başka bir pencereye bırak. Dosyalar (/f, /ds, /son, betikler) gerçek dosya olarak gider: Gezgin'e kopyalanır, WhatsApp, Teams ya da Gmail'e ek olur. Yer imleri ve toplantılar bağlantı olarak, pano kayıtları, notlar, hesap sonucu, emoji, sözlük maddesi ve kişi numarası düz metin olarak gider. Uygulamayı masaüstüne bırakınca kısayolu kopyalanır
- Sürüklerken launcher kapanmaz, bırakınca kapanır; Esc sürüklemeyi iptal eder
- Dış eklentiler de sonuçlarına DragData (dosya, adres ya da metin) koyabilir

## 0.20.0
- Kutudan doğrudan eylem: düz Türkçe yaz, LumoraGo ne anladığını satırda gösterir, Enter onaylar
- "hatırlat yarın 9 fatura", "hatırlat 20 dk sonra çay", "hatırlat cuma akşam annemi ara": vakti gelince sesli bildirim. LumoraGo kapalıyken kaçan hatırlatıcılar açılınca gösterilir. "/hatırlatıcılar" bekleyenleri listeler: Enter tamamlandı, Ctrl+Enter 10 dakika ertele
- "mesaj ahmet geliyorum": rehberdeki kişiyi (ya da yazılan numarayı) bulur, WhatsApp'ı mesaj yazılmış olarak açar; göndermeyi sen yaparsın
- "mail ayşe toplantı | yarın 10'da müsait misin": varsayılan mail uygulamasında konusu ve metni dolu yeni mail
- "etkinlik cuma 14:00 dişçi": Google Takvim'de dolu bir yeni etkinlik sayfası açar, kaydetmek sende
- "not süt al": /not listesine hızlı ekleme
- Anlaşılan tarihler: bugün, yarın, öbür gün, haftaya, pazartesi…pazar, "cuma 14:30", "3 kasım", "2 saat sonra", "20 dk sonra", sabah, öğlen, akşam, gece. Saat yazılmazsa 09:00; bugün geçmiş bir saat yazılırsa ertesi güne kurulur ve satırda yazar

## 0.19.0
- Kişiler: Ayarlar → Kişiler'den Google Kişiler, iPhone (iCloud) ya da Outlook'tan aldığın .vcf veya .csv dosyasını seç. "/kisi ahmet" ya da doğrudan "ahmet" yazınca kişi çıkar (Türkçe harfsiz de bulur, numaranın bir parçasını yazınca da). Enter numarayı kopyalar, Ctrl+Enter WhatsApp sohbetini, Shift+Enter yeni maili açar; sağ ok menüsünde telefonla arama (Telefon Bağlantısı) ve diğer numaralar var. 0532…, +90 532… ve 532… aynı numara sayılır
- Kişiler şifreli (Windows hesabına bağlı) saklanır, hiçbir yere gönderilmez. "Dosya değişince yeniden oku" seçilirse dosya güncellenince rehber de güncellenir

## 0.18.1
- "/mac fenerbahçe" artık takımın maçlarını buluyor: önceden yalnızca içinde bulunulan haftaya bakılıyordu, takımın o hafta maçı yoksa hiçbir şey çıkmıyordu. Şimdi son 3 hafta ve önümüzdeki 4 hafta taranır; önce canlı maç, sonra son oynanan, sonra sıradaki maç gelir. "fb", "gs", "bjk", "ts" kısaltmaları da çalışır
- Launcher'daki LumoraGo yazısının altında küçük harflerle çalışan sürüm görünür

## 0.18.0
- Betikler: %LOCALAPPDATA%\LumoraGo\scripts klasörüne konan .ps1, .bat, .cmd ve .py dosyaları adıyla aranır ve çalıştırılır ("yedekle" ya da "/calistir yedekle"). Addan sonra yazılanlar betiğe argüman olarak gider ("yedekle D:\Yedek"). Enter arka planda çalıştırır ve bitince çıkış kodunu ve son satırları bildirir, Ctrl+Enter terminalde açar, Shift+Enter yönetici olarak çalıştırır. "betik" yazınca klasör açılır; ilk açılışta örnek "merhaba.ps1" konur
- Eylem zincirleri: Ayarlar'da birkaç adımı tek isimde topla (uygulama aç, site aç, dosya/klasör aç, betik çalıştır, sesi ayarla, bekle, panoya yaz, bildirim göster) ve launcher'da adını yazıp çalıştır. Bir adım hata verirse zincir durur ve hangi adımda durduğunu bildirir

## 0.17.0
- Sözlük: "/tanım kalem" (ya da "/tanim") TDK'dan anlamları, örnek cümleleri, atasözü ve deyimleri gösterir; "/tanım en serendipity" İngilizce anlam, okunuş ve eş anlamlılar. Enter anlamı kopyalar, Shift+Boşluk maddenin tamamını önizler
- Bilgi kartları: sonuçlar büyük kart olarak görünür
- "/hava", "/hava izmir", "/hava yarın", "/hava hafta": hava durumu, hissedilen sıcaklık, yağış olasılığı ve sonraki 12 saatin eğrisi (Open-Meteo). Varsayılan şehir Ayarlar'dan; boşsa bağlantının konumu
- "/hisse THYAO", "/hisse AAPL", "/hisse btc eth": fiyat, günlük değişim ve gün içi eğri (Yahoo Finance, 15 dk gecikmeli; kripto CoinGecko)
- "/mac fenerbahçe": takımın canlı, biten ve sıradaki maçları; "/mac" Süper Lig haftası (ESPN)
- "TK1980" gibi bir uçuş kodu yazınca Flightradar24'te takip satırı çıkar (Ctrl+Enter FlightAware)

## 0.16.0
- Hızlı önizleme: bir sonuç seçiliyken Shift+Boşluk sağda önizleme panelini açar. Resimler, metin ve kod dosyaları (ilk 300 satır), klasör içeriği, video ve ses küçük resmi gösterilir; PDF, Word, Excel ve PowerPoint için Windows Gezgini'nin önizleyicisi kullanılır. Pano kayıtları, notlar ve yer imleri de önizlenir. ↑↓ ile gezince önizleme de değişir, Esc önce paneli kapatır
- Ayarlar'da "Önizlemeyi her zaman açık tut" seçeneği

## 0.15.0
- Kullanıma göre sıralama: LumoraGo hangi yazıda hangi sonucu seçtiğini öğrenir. "ch" yazıp hep Chrome'u seçiyorsan bir süre sonra "c" ve "ch" yazınca Chrome en üstte çıkar. Uygulamalar, dosyalar, yer imleri, ayarlar ve komutların hepsinde çalışır; eski seçimlerin etkisi yaklaşık 30 günde yarıya iner. Ayarlar'da "Öğrenilen sıralamayı sıfırla" düğmesi var

## 0.14.1
- "/ds" için yeni ayar: "Sistem klasörlerini de ara" açılırsa C:\ içinde Windows, Program Files, AppData, node_modules ve .git dahil her yer taranır (daha uzun sürer). Varsayılan değişmedi

## 0.14.0
- Takvim: "/takvim" bugünün kalan toplantılarını, "/takvim yarın" ve "/takvim hafta" ilerisini gösterir; Enter toplantı bağlantısını (Meet, Teams, Zoom) açar. Google Takvim ve Outlook.com için takvimin gizli ICS adresi Ayarlar'a yapıştırılır, uygulama kaydı gerekmez. Tekrarlayan toplantılar ve saat dilimleri desteklenir
- Mail: "/mail" okunmamış mailleri (gönderen, konu, zaman) listeler, yazınca süzer; Enter gelen kutusunu açar. Gmail, Yahoo, Yandex, iCloud ve diğer IMAP sunucuları çalışır; Gmail için uygulama şifresi gerekir. Mailler okundu işaretlenmez ve gövdeleri indirilmez. Outlook/Hotmail/Microsoft 365 şifreyle IMAP'e kapalı olduğu için desteklenmiyor
- Takvim adresleri ve mail şifresi Windows hesabına bağlı (DPAPI) şifreli dosyada tutulur, settings.json'a yazılmaz

## 0.13.0
- Ağ araçları: "/ping google.com" dört kez ping atıp ortalama ve kayıp gösterir, "/dns google.com" (veya "/dns google.com mx") DNS kayıtlarını, "/whois google.com" kayıt kuruluşu ve tarihleri gösterir; IP yazınca ters DNS ve ağ sahibi gelir. Sorgular Enter ile başlar, sonuçlar geldikçe liste dolar
- "/hiz": indirme, yükleme ve gecikme ölçen internet hız testi (Cloudflare uçları, yaklaşık 33 MB veri kullanır)
- "/disip": dış IP adresin, yaklaşık konumun ve servis sağlayıcın; Enter kopyalar

## 0.12.0
- Yer imleri: "/b jira" Chrome, Edge, Brave, Vivaldi ve Opera yer imlerinde ad, adres ve klasöre göre arar; Enter açar
- Windows ayarları: "bluetooth", "ekran", "varsayılan uygulamalar", "duvar kağıdı" gibi yazınca ilgili Ayarlar sayfası çıkar
- Son dosyalar: "/son" Windows'ta son açtığın dosya ve klasörleri gösterir, yazınca süzülür
- Dünya saati: "/saat tokyo" şehirdeki saati ve seninle farkını gösterir; "/saat" tüm şehirleri listeler

## 0.11.0
- Özel kısayollar: Ayarlar'da "kelime | hedef" yazarak kendi kısayollarını ekle (site, dosya, klasör veya uygulama). Hedefte {0} varsa kelimeden sonra yazılan metinle arar
- Yeni web aramaları: "/tr" Türkçeye çevirir, "/wiki" Vikipedi, "/harita" Google Haritalar, "/gh" GitHub
- Araçlar: "/uuid", "/sifre 24" güçlü şifre, "/b64" Base64, "/url" URL kodlama, "/hash" SHA-256/SHA-1/MD5, "/say" karakter ve kelime sayısı; Enter kopyalar
- "/kill chrome": donan uygulamayı zorla kapatır (aynı addaki tüm işlemler)
- "/ses": "/ses 40" sesi %40 yapar; oynat/duraklat, sonraki, önceki, sessiz, artır, azalt

## 0.10.0
- Eklenti klasörü: kendi yazdığın veya indirdiğin eklentiler `%LOCALAPPDATA%\LumoraGo\extensions` altına klasörüyle kopyalanınca LumoraGo açılışta yükler. Klasörü açmak için launcher'da "eklenti" yaz
- Örnek eklenti ve nasıl yazılacağı README'de; hatalı eklenti atlanır, launcher çökmez

## 0.9.0
- Pencere geçişi: "/w chrome" açık pencereler arasında başlık veya uygulama adıyla arar; Enter öne getirir, sağ ok ile pencereyi kapat
- Zamanlayıcı: "/t 10dk çay", "/t 1sa 30dk", "/t 90sn", "/t 14:30 ilaç"; süre dolunca ses ve bildirim gelir. "/t" çalışanları ve kalan süreyi gösterir, Enter iptal eder
- Kalıcı pano geçmişi: Ayarlar'da "Kapatınca da hatırla" açılırsa son 200 kopya diske yazılır ve LumoraGo yeniden açılınca geri gelir. Kapatınca dosya silinir

## 0.8.0
- Birim ve döviz çevirme: "5 km mil", "100 usd tl", "30 c f", "2 gb mb" yazınca sonuç en üstte çıkar; Enter sayıyı kopyalar. Kurlar günde birkaç kez internetten alınır ve önbellekte tutulur
- Notlar: "/not ekmek al" not ekler, "/not" listeler; Enter tamamlandı işaretler, sağ ok ile kopyala, sil, tamamlananları temizle
- Emoji arama: "/e kalp" veya "/e heart"; Enter önceki pencereye yapıştırır, Ctrl+Enter kopyalar
- Renk kodları: "#1e90ff", "rgb(30, 144, 255)" veya "hsl(210, 100%, 56%)" yazınca renk örneği ve HEX, RGB, HSL karşılıkları çıkar; Enter kopyalar

## 0.7.0
- Ayarlar penceresi: tepsi simgesine sağ tık → Ayarlar, veya launcher'da "ayar" yaz
- Kısayol değiştirilebilir (örn. Ctrl+Shift+Space); başka uygulama kullanıyorsa eskisi kalır
- Tema seçimi: koyu, açık veya Windows'u takip et
- Pano geçmişi açılıp kapatılabilir
- Derin aramanın klasörleri ve atlanacak klasör adları, dosya aramanın ek klasörleri seçilebilir

## 0.6.0
- Pano geçmişi: "/c" son kopyaladıklarını listeler, yazarak süzülür; Enter önceki pencereye yapıştırır, Ctrl+Enter sadece kopyalar. Geçmiş yalnızca bellekte tutulur, şifre yöneticilerinin gizli işaretlediği kopyalar alınmaz
- Eylem menüsü: seçili sonuçta sağ ok (veya Ctrl+K) ek eylemleri açar; uygulamalarda yönetici olarak çalıştır, dosya konumunu aç, yolu kopyala; dosyalarda klasöründe göster, yolu kopyala. Esc veya sol ok geri döner

## 0.5.0
- Derin arama: "/ds şifre" tüm bilgisayarda dosya ve klasör adlarında, metin dosyalarının içinde arar; sonuçlar bulundukça listeye eklenir, Enter ile durdurulur
- Dosya arama: "/f rapor" masaüstü, belgeler, indirilenler, resimler, videolar, müzik ve OneDrive'daki dosyaları adıyla anında bulur
- Dosya sonuçlarında Enter açar, Ctrl+Enter klasöründe gösterir

## 0.4.1
- Güncelleme kontrolü "kontrol ediliyor"da takılı kalmıyor; 30 saniyede yanıt gelmezse tekrar denenir
- Launcher açık kalsa da 15 dakikada bir yeni sürüm arar, bulunca indirip pencere gizlenince kurar
- /v son kontrol saatini gösterir

## 0.4.0
- Daha koyu yeni tema: derin siyah arka plan, mor vurgu rengi; Windows açık temada olsa da koyu kalır
- Tab ile tamamlama: seçili sonucun adı kutuya yazılır, hesap sonucunda sayı yazılır
- Yazdıkça soluk renkte öneri: "vis" yazınca "ual Studio Code" görünür, Tab veya sağ ok ile kabul edilir
- "/" ile başlayınca komut önerileri: "/li" yazınca "/liste" çıkar
- "/yt ..." YouTube'da, "/gg ..." Google'da arar (varsayılan tarayıcıda açılır)
- "/git" GitHub profilini, "/linkedin" (veya "/linkedln") LinkedIn ana sayfasını açar
- Sürüm bilgisi artık "/v" ile açılır ("/version" yerine)
- Windows kısayolları: "/kilit", "/uyku", "/kapat", "/yeniden", "/oturum", "/cop" (geri dönüşüm kutusu), "/ayarlar", "/gorev" (Görev Yöneticisi), "/cmd", "/ip" (IP adresini kopyalar)

## 0.3.0
- Uygulama arama: Başlat menüsü ve Microsoft Store uygulamaları, simgeleriyle birlikte listelenir
- Bulanık eşleşme: "vsc" yazınca Visual Studio Code, "chr" yazınca Chrome bulunur; Türkçe harfler fark etmez
- Sık ve yeni kullanılan uygulamalar üste çıkar
- Ctrl+Enter uygulamanın klasörünü açar, Shift+Enter yönetici olarak çalıştırır
- Hesap makinesi: "2+3*4" yazınca sonuç en üstte, Enter ile panoya kopyalanır
- Windows 11 görünümü: akrilik arka plan, yuvarlak köşeler, açılış animasyonu, seçim çubuğu ve alt bilgi çubuğu
- Açık/koyu tema Windows ayarını kendiliğinden takip eder

## 0.2.0
- Açılışta otomatik güncelleme: yeni sürüm arka planda indirilir, launcher gizlenince kurulup sessizce yeniden başlar
- /version: çalışan sürümü, güncelleme durumunu ve sürüm notlarını gösterir
- /liste: yazılabilecek tüm komutları A'dan Z'ye listeler
- Arama kutusunun sağında şeffaf LumoraGo yazısı

## 0.1.0
- Alt+Space ile açılan arama penceresi, Esc ile gizleme
- Sistem tepsisi simgesi, tek örnek çalışma, Windows açılışında başlatma
- Sonuç listesinde ↑/↓ ile gezinme, Enter ile çalıştırma
