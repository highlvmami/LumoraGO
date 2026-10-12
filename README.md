# LumoraGo

Alt+Space ile açılan, her özelliği bir eklenti olan Windows launcher'ı (Raycast / PowerToys Run tarzı).

> Durum: **Faz 1 – İskelet** (sürüm 0.2.0). Pencere, kısayol, tepsi simgesi ve sonuç listesi çalışıyor. Uygulama arama (Faz 2) henüz yok; şimdilik sadece LumoraGo'nun kendi komutları listelenir ("çık" yazmayı dene).

## Çalıştırma

Gerekenler: Windows 10/11 ve [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project src/LumoraGo.App
```

## Kurulum ve güncelleme

[Releases](https://github.com/highlvmami/LumoraGO/releases) sayfasındaki `LumoraGo-win-Setup.exe` ile kur. Kurulan LumoraGo her açılışta yeni sürüm arar, bulursa arka planda indirir ve launcher gizlenince kurup sessizce yeniden başlar. `/version` yazınca çalışan sürümü, güncelleme durumunu ve sürüm notlarını gösterir.

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
| Esc | Gizle (odak kaybında da gizlenir) |

`/version` yazınca sürüm ve sürüm notları görünür.

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
- [ ] Faz 1: İskelet (kısayol, tepsi, göster/gizle, sonuç listesi) ← burada
- [ ] Faz 2: Uygulama başlatma ve bulanık arama → MVP
- [ ] Faz 3: Eklenti sistemi
- [ ] Faz 4: Yerleşik eklentiler
- [ ] Faz 5: Kısayollar, tema, ayarlar
- [ ] Faz 6: Performans ve yayın → 1.0
