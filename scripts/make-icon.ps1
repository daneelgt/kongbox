<#
  Generates agent/KongBox.Agent/kongbox.ico: a dark rounded square with a white "K" and a green cursor,
  in 256/64/48/32/16 px (PNG frames inside one ICO). Run once; the result is committed.
#>
Add-Type -AssemblyName System.Drawing
$sizes = 256, 64, 48, 32, 16
$pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap -ArgumentList $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.TextRenderingHint = 'AntiAliasGridFit'
  $g.Clear([System.Drawing.Color]::Transparent)

  $r = [Math]::Max(2, [int]($s * 0.22))
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
  $path.AddArc($s - 2 * $r - 1, 0, 2 * $r, 2 * $r, 270, 90)
  $path.AddArc($s - 2 * $r - 1, $s - 2 * $r - 1, 2 * $r, 2 * $r, 0, 90)
  $path.AddArc(0, $s - 2 * $r - 1, 2 * $r, 2 * $r, 90, 90)
  $path.CloseFigure()
  $bg = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 18, 18, 20))
  $g.FillPath($bg, $path)

  $font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', ([single]($s * 0.56)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
  $fmt = New-Object System.Drawing.StringFormat
  $fmt.Alignment = 'Center'
  $fmt.LineAlignment = 'Center'
  $rect = New-Object System.Drawing.RectangleF -ArgumentList ([single]($s * -0.04)), ([single]($s * -0.02)), ([single]$s), ([single]$s)
  $g.DrawString('K', $font, [System.Drawing.Brushes]::White, $rect, $fmt)

  $green = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(255, 48, 209, 88))
  $g.FillRectangle($green, [single]($s * 0.64), [single]($s * 0.70), [single]($s * 0.18), [single][Math]::Max(1, $s * 0.08))

  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  $pngs += , $ms.ToArray()
}

# ICO container: 6-byte header, one 16-byte directory entry per frame, then the PNG data
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter -ArgumentList $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256 in the ICO format
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'agent\KongBox.Agent\kongbox.ico'
[IO.File]::WriteAllBytes($target, $out.ToArray())
'{0} ({1:N1} KB)' -f $target, ((Get-Item $target).Length / 1KB)
