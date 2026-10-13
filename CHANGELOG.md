# Sürüm notları

Her sürüm bir `## X.Y.Z` başlığı ve altında `- ` ile başlayan maddelerden oluşur. Launcher'da `/v` yazınca çalışan sürümün maddeleri görünür; GitHub sürüm sayfasındaki notlar da buradan alınır.

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
