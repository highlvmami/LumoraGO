# LumoraGo

Alt+Space ile açılan, her özelliği bir eklenti olan Windows launcher'ı (Raycast / PowerToys Run tarzı).

> Durum: **Faz 2 – MVP** (sürüm 0.4.0). Yazdıkça Başlat menüsü ve Store uygulamaları bulanık aramayla listelenir ("vsc" → Visual Studio Code), "2+3*4" gibi hesaplar anında sonuçlanır.

## Çalıştırma

Gerekenler: Windows 10/11 ve [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project src/LumoraGo.App
```

## Kurulum ve güncelleme

[Releases](https://github.com/highlvmami/LumoraGO/releases) sayfasındaki `LumoraGo-win-Setup.exe` ile kur. Kurulan LumoraGo her açılışta yeni sürüm arar, bulursa arka planda indirir ve launcher gizlenince kurup sessizce yeniden başlar. `/v` yazınca çalışan sürümü, güncelleme durumunu ve sürüm notlarını gösterir.

`dotnet run` ile veya Actions'tan indirilen zip ile çalışan kopyalar kendini güncellemez.

### Yeni sürüm çıkarmak

1. `Directory.Build.props` içindeki `<Version>` değerini artır (örn. `0.3.0`).
2. `CHANGELOG.md` dosyasının başına `## 0.3.0` başlığı ve altına `- ` ile başlayan maddeler ekle.
3. Birleştirdikten sonra GitHub'da **Actions → Release → Run workflow** ile başlat (ya da `v0.3.0` etiketini gönder).

Release iş akışı testleri çalıştırır, kurulum dosyasını ve güncelleme paketlerini üretip GitHub Releases'a yükler. Etiket ile `<Version>` uyuşmazsa veya CHANGELOG'da o sürüm yoksa durur.

| Tuş | Ne yapar |
|---|---|
| Alt+Space | Aç / gizle |
| ↑ / ↓ | Sonuçlarda gez |
| Enter | Seçili sonucu çalıştır |
| Tab | Seçili sonucu kutuya tamamla (soluk öneriyi sağ ok da kabul eder) |
| Ctrl+Enter | Uygulamanın klasörünü aç |
| Shift+Enter | Yönetici olarak çalıştır |
| Esc | Gizle (odak kaybında da gizlenir) |

`/v` yazınca sürüm ve sürüm notları görünür. `/yt kedi` YouTube'da, `/gg kedi` Google'da arar. `/git` GitHub profilini, `/linkedin` LinkedIn'i açar. Windows kısayolları: `/kilit`, `/uyku`, `/kapat`, `/yeniden`, `/oturum`, `/cop`, `/ayarlar`, `/gorev`, `/cmd`, `/ip`. Hepsini görmek için `/liste`.

`/f rapor` kullanıcı klasörlerinde dosyayı adıyla anında bulur. `/ds şifre` tüm bilgisayarda dosya adlarında ve metin dosyalarının içinde arar (deepsearch). Windows, Program Files, AppData ve node_modules gibi klasörler, resim/video/arşiv gibi ikili dosyalar ve 5 MB'tan büyük dosyaların içi atlanır. Ayarlar: tepsi simgesine sağ tıkla → Ayarlar, ya da launcher'da `ayar` yaz. Kısayol, tema, pano geçmişi ve arama klasörleri oradan değişir.

`/c` pano geçmişini açar (Enter yapıştırır). Seçili sonuçta sağ ok veya Ctrl+K ek eylemleri gösterir.

`5 km mil`, `100 usd tl` gibi yazınca birim ve döviz çevirir. `/not ekmek al` not ekler, `/not` notları listeler. `/e kalp` emoji arar. `#1e90ff` gibi bir renk kodu yazınca HEX, RGB ve HSL karşılıkları çıkar. `/w chrome` açık pencereye geçer. `/t 10dk çay` zamanlayıcı kurar.

Arama tamamen bilgisayarında yapılır. İnternete yalnızca güncelleme kontrolü ve döviz kurları için çıkılır (open.er-api.com); notlar, ayarlar ve (istenirse) pano geçmişi `%LOCALAPPDATA%\LumoraGo` altında durur.

`/liste` yazınca yazabileceğin tüm komutlar A'dan Z'ye listelenir; `/liste çık` gibi devam edersen liste süzülür.

Tepsi simgesine sağ tıklayınca: Göster, Windows açılışında başlat, Günlük klasörünü aç, Çıkış.

Günlükler: `%LocalAppData%\LumoraGo\logs`. Her açılışta "Kısayoldan görünmeye: X ms" satırı yazılır; hedef 50 ms altı.

## Yapı

```
src/
  LumoraGo.Plugin.Abstractions   Eklenti API'si (IPlugin, Query, Result). Küçük ve kararlı kalır.
  LumoraGo.Core                  Sorgu ayrıştırma, eklenti yönetimi, paralel arama. UI'dan bağımsız.
  LumoraGo.App                   WPF arayüzü: pencere, kısayol, tepsi, Windows entegrasyonu.
tests/
  LumoraGo.Core.Tests            xUnit testleri
```

Arayüz sadece çekirdekle konuşur; çekirdek eklentileri yalnızca `IPlugin` üzerinden tanır. Arama her tuşta öncekini iptal eder, her eklenti paralel çalışır, 200 ms'yi aşan eklenti atlanır, hata veren eklenti launcher'ı çökertmez.

## Testler

```powershell
dotnet test
```

Core ve testler Linux/macOS'ta da çalışır. Uygulama projesi `EnableWindowsTargeting` sayesinde her yerde derlenir ama sadece Windows'ta çalışır.

## Yol haritası

Fazlar:

- [x] Faz 0: Hazırlık (depo, solution, CI)
- [x] Faz 1: İskelet (kısayol, tepsi, göster/gizle, sonuç listesi)
- [x] Faz 2: Uygulama başlatma ve bulanık arama → MVP ← burada
- [ ] Faz 3: Eklenti sistemi
- [ ] Faz 4: Yerleşik eklentiler
- [ ] Faz 5: Kısayollar, tema, ayarlar
- [ ] Faz 6: Performans ve yayın → 1.0
