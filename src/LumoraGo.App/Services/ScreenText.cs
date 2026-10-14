using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace LumoraGo.App.Services;

/// <summary>
/// Ekrandan metin: Windows'un ekran alıntısı aracıyla (Win+Shift+S'teki) bölge seçtirir, resmi Windows'un kendi OCR'ı ile
/// çevrimdışı okur. İnternete hiçbir şey gönderilmez.
/// </summary>
internal static class ScreenText
{
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    /// <summary>Bölge seçtirir ve okunan metni döndürür; kullanıcı vazgeçerse ya da süre dolarsa null.</summary>
    public static async Task<string?> CaptureAsync(Action<string> open, TimeSpan timeout)
    {
        var before = GetClipboardSequenceNumber();
        open("ms-screenclip:");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            if (GetClipboardSequenceNumber() == before)
            {
                continue;
            }

            if (TryGetClipboardImage() is { } image)
            {
                return await RecognizeAsync(image);
            }

            before = GetClipboardSequenceNumber();
        }

        return null;
    }

    public static BitmapSource? TryGetClipboardImage()
    {
        try
        {
            return System.Windows.Clipboard.ContainsImage() ? System.Windows.Clipboard.GetImage() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Resmi Windows OCR'ı ile okur. Satırlar korunur.</summary>
    /// <exception cref="InvalidOperationException">Bu Windows'ta OCR dili yüklü değilse.</exception>
    public static async Task<string> RecognizeAsync(BitmapSource image)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? (OcrEngine.IsLanguageSupported(new Language("tr")) ? OcrEngine.TryCreateFromLanguage(new Language("tr")) : null)
            ?? (OcrEngine.IsLanguageSupported(new Language("en-US")) ? OcrEngine.TryCreateFromLanguage(new Language("en-US")) : null)
            ?? throw new InvalidOperationException("Windows'ta metin tanıma dili yüklü değil. Ayarlar → Saat ve dil → Dil'den Türkçe ya da İngilizce dil paketini ekle.");

        using var memory = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(Scale(image, OcrEngine.MaxImageDimension)));
        encoder.Save(memory);
        memory.Position = 0;

        using var stream = memory.AsRandomAccessStream();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
    }

    /// <summary>Küçük yazılar daha iyi okunsun diye küçük resimleri büyütür; OCR sınırını aşanları küçültür.</summary>
    private static BitmapSource Scale(BitmapSource image, uint max)
    {
        var longest = Math.Max(image.PixelWidth, image.PixelHeight);
        var factor = longest < 800 ? 2.0 : longest > max ? (double)max / longest : 1.0;
        return factor == 1.0 ? image : new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(factor, factor));
    }
}
