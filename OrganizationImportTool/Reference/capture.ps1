# DPI-aware, occlusion-proof window capture via PrintWindow.
# Usage: capture.ps1 -exeArgs "--ui-main" -out "shot.png" [-wait 6]
param([string]$exeArgs = "--ui-main", [string]$out = "shot.png", [int]$wait = 6)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Native {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[Native]::SetProcessDPIAware() | Out-Null

$exe = Join-Path $PSScriptRoot "..\bin\Debug\net7.0-windows\CargoSync.exe"
$p = Start-Process -FilePath $exe -ArgumentList $exeArgs -PassThru
Start-Sleep -Seconds $wait
if ($p.HasExited) { Write-Output "PROCESS EXITED early (code $($p.ExitCode))"; exit 1 }

$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Output "No main window"; Stop-Process -Id $p.Id -Force; exit 1 }

$r = New-Object Native+RECT
[Native]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $hgt = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap($w, $hgt)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
# 2 = PW_RENDERFULLCONTENT: captures the window itself, even if covered by other windows
[Native]::PrintWindow($h, $dc, 2) | Out-Null
$g.ReleaseHdc($dc)
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Stop-Process -Id $p.Id -Force
Write-Output "Saved $out ($w x $hgt)"
