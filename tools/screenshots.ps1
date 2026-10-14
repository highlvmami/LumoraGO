# README ekran görüntülerini temiz bir Windows'ta çeker: LumoraGo'yu açar, sorguları yazar, pencereyi PNG'ye kaydeder.
# Kullanım: pwsh tools/screenshots.ps1 -Exe publish/LumoraGo.exe -Out docs/screenshots
param([string]$Exe, [string]$Out = "docs/screenshots")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  // Aynı başlıklı gizli yardımcı pencereler de var; görünür olanı ara.
  public static IntPtr FindVisible(string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, p) => {
      var sb = new System.Text.StringBuilder(256);
      GetWindowText(h, sb, 256);
      if (sb.ToString() == title && IsWindowVisible(h)) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
}
"@

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$env:LUMORAGO_KEEP_OPEN = "1"
$exePath = (Resolve-Path $Exe).Path

function Window([string]$title) {
  for ($i = 0; $i -lt 50; $i++) {
    $h = [Win]::FindVisible($title)
    if ($h -ne [IntPtr]::Zero -and [Win]::IsWindowVisible($h)) { return $h }
    Start-Sleep -Milliseconds 200
  }
  $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
  [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen(0, 0, 0, 0, $bmp.Size)
  $bmp.Save((Join-Path $Out "_hata-ekran.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  Get-Process LumoraGo -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "işlem: $($_.Id) '$($_.MainWindowTitle)' $($_.MainWindowHandle)" }
  throw "$title penceresi bulunamadı"
}

function Capture([IntPtr]$h, [string]$name, [int]$pad = 24) {
  $r = New-Object Win+RECT
  if ([Win]::DwmGetWindowAttribute($h, 9, [ref]$r, 16) -ne 0) { [Win]::GetWindowRect($h, [ref]$r) | Out-Null }
  $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $left = [Math]::Max(0, $r.Left - $pad); $top = [Math]::Max(0, $r.Top - $pad)
  $right = [Math]::Min($screen.Width, $r.Right + $pad); $bottom = [Math]::Min($screen.Height, $r.Bottom + $pad)
  $bmp = New-Object System.Drawing.Bitmap ($right - $left), ($bottom - $top)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($left, $top, 0, 0, $bmp.Size)
  $bmp.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Host "kaydedildi: $name.png ($($right - $left)x$($bottom - $top))"
}

# Kısa sorgular: sorgu, dosya adı, beklenecek saniye (internetten gelenler daha uzun).
$shots = @(
  @("not", "uygulama-arama", 2),
  @("2+3*4", "hesap-makinesi", 2),
  @("100 usd tl", "doviz", 6),
  @("/hava istanbul", "hava", 8),
  @("/hisse THYAO", "hisse", 8),
  @("/sistem", "sistem", 5),
  @("/temizle", "temizle", 8),
  @("/haber", "haber", 10),
  @("/çevir good morning, how are you?", "ceviri", 6),
  @("/ezan istanbul", "ezan", 8),
  @("/liste", "komut-listesi", 3)
)

# Masaüstündeki diğer pencereler arkada görünmesin.
(New-Object -ComObject Shell.Application).MinimizeAll()
Start-Sleep -Seconds 1
Start-Process $exePath
Start-Sleep -Seconds 8
$h = Window "LumoraGo"

foreach ($shot in $shots) {
  (New-Object -ComObject Shell.Application).MinimizeAll()
  Start-Sleep -Milliseconds 500
  Start-Process $exePath   # ikinci kopya çalışan launcher'a "göster" der
  Start-Sleep -Milliseconds 800
  $h = Window "LumoraGo"
  [Win]::SetForegroundWindow($h) | Out-Null
  Set-Clipboard -Value $shot[0]
  [System.Windows.Forms.SendKeys]::SendWait("^a")
  [System.Windows.Forms.SendKeys]::SendWait("^v")
  Start-Sleep -Seconds $shot[2]
  # Arka planda yüklenen sonuçlar (döviz kurları gibi) ilk yazışta "yükleniyor" der; bir kez daha yaz.
  [System.Windows.Forms.SendKeys]::SendWait("^a")
  [System.Windows.Forms.SendKeys]::SendWait("^v")
  Start-Sleep -Seconds 2
  Capture $h $shot[1]
}

# Ayarlar penceresi
Start-Process $exePath
Start-Sleep -Milliseconds 800
$h = Window "LumoraGo"
[Win]::SetForegroundWindow($h) | Out-Null
Set-Clipboard -Value "lumorago ayarları"
[System.Windows.Forms.SendKeys]::SendWait("^a")
[System.Windows.Forms.SendKeys]::SendWait("^v")
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
Capture (Window "LumoraGo ayarları") "ayarlar" 0
