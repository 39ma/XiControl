<#
  find-mibutton.ps1 — живой слушатель HID_EVENT20: печатает код КАЖДОГО события сразу.
  Задача: нажать ТОЛЬКО Mi-кнопку и увидеть её код (EventDetail[1]).

  PowerShell ОТ АДМИНИСТРАТОРА:
    pwsh -ExecutionPolicy Bypass -File .\find-mibutton.ps1
  Затем жми ТОЛЬКО Mi-кнопку (несколько раз). Выход — Ctrl+C.
#>

$ErrorActionPreference = 'Stop'

Register-CimIndicationEvent -Namespace root/wmi -Query "SELECT * FROM HID_EVENT20" -SourceIdentifier mikey -Action {
    $d = $Event.SourceEventArgs.NewEvent.EventDetail
    if ($d -and $d.Count -gt 2) {
        $hex = ($d[0..7] | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
        Write-Host ("{0}   code=0x{1:X2}  value=0x{2:X2}    [{3}]" -f (Get-Date -Format HH:mm:ss), $d[1], $d[2], $hex) -ForegroundColor Cyan
    }
} | Out-Null

Write-Host "Слушаю HID_EVENT20. Жми ТОЛЬКО Mi-кнопку (несколько раз)." -ForegroundColor Yellow
Write-Host "Если при нажатии Mi-кнопки ничего не печатается — значит она НЕ шлёт HID_EVENT20." -ForegroundColor DarkGray
Write-Host "Выход: Ctrl+C`n"

try { while ($true) { Start-Sleep -Seconds 1 } }
finally {
    Unregister-Event -SourceIdentifier mikey -ErrorAction SilentlyContinue
    Get-Event -SourceIdentifier mikey -ErrorAction SilentlyContinue | Remove-Event -ErrorAction SilentlyContinue
    Write-Host "`nСлушатель остановлен."
}
