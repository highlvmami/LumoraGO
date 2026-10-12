# LumoraGo

Alt+Space ile açılan, her özelliği bir eklenti olan Windows launcher'ı (Raycast / PowerToys Run tarzı).

> Durum: **Faz 1 – İskelet.** Pencere, kısayol, tepsi simgesi ve sonuç listesi çalışıyor. Uygulama arama (Faz 2) henüz yok; şimdilik sadece LumoraGo'nun kendi komutları listelenir ("çık" yazmayı dene).

## Çalıştırma

Gerekenler: Windows 10/11 ve [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project src/LumoraGo.App
```

Derlenmiş sürümü denemek için: GitHub'da **Actions** sekmesindeki son başarılı çalışmadan `LumoraGo-win-x64` dosyasını indir, içindeki `LumoraGo.exe`'yi aç (.NET 10 Desktop Runtime gerekir).

| Tuş | Ne yapar |
|---|---|
| Alt+Space | Aç / gizle |
| ↑ / ↓ | Sonuçlarda gez |
| Enter | Seçili sonucu çalıştır |
| Esc | Gizle (odak kaybında da gizlenir) |

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

Fazlar ve ayrıntılar: [LumoraGo Launcher Yol Haritası](https://claude.ai/code/artifact/dc519a2a-1dff-4bcf-bcba-90d536eae0fb)

- [x] Faz 0: Hazırlık (depo, solution, CI)
- [ ] Faz 1: İskelet (kısayol, tepsi, göster/gizle, sonuç listesi) ← burada
- [ ] Faz 2: Uygulama başlatma ve bulanık arama → MVP
- [ ] Faz 3: Eklenti sistemi
- [ ] Faz 4: Yerleşik eklentiler
- [ ] Faz 5: Kısayollar, tema, ayarlar
- [ ] Faz 6: Performans ve yayın → 1.0
