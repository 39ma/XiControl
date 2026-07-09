<#
  map-keys.ps1 — картограф Fn/спец-клавиш. Печатает КОД + предполагаемое назначение
  (гипотезы из карты MIControl; коды на нашей модели могут отличаться — проверяем).

  PowerShell ОТ АДМИНИСТРАТОРА:
    pwsh -ExecutionPolicy Bypass -File .\map-keys.ps1

  Затем жми ПО ОДНОЙ спец-клавише и записывай: какая клавиша → какой code.
  Список для обхода (что есть на клавиатуре):
    микрофон, подсветка клавиатуры (+/-), «настройки» (2 тумблера / Mi), «проекция»,
    скриншот, тачпад, частота экрана, Fn-Lock, Caps Lock, камера, стрелки/громкость.
  Выход: Ctrl+C.
#>

$ErrorActionPreference = 'Stop'

$guess = @{
    0x01 = 'Проекция (Win+P) — или "слабый зарядник" при value=2'
    0x02 = 'Скриншот (Ножницы)'
    0x05 = 'Подсветка клавиатуры (уровень = value; 0x80=Авто)'
    0x07 = 'Fn-Lock'
    0x08 = 'Смена режима (общий канал)'
    0x09 = 'Caps Lock'
    0x0A = 'Микрофон (общий канал)'
    0x10 = 'Тачпад вкл/выкл'
    0x13 = 'Частота обновления экрана'
    0x16 = 'Режим (value: 1=Баланс 2=Тихий 3=Турбо 4=Полный 9=Авто)'
    0x1A = 'Частота обновления экрана'
    0x1B = 'Открыть Настройки Windows'
    0x21 = 'Микрофон mute (value 0/1)'
    0x23 = '? пара с 0x24'
    0x24 = '? пара с 0x23'
    0x25 = 'Mi-кнопка (нажатие)'
    0x26 = 'Mi-кнопка (отпускание)'
    0xA0 = 'Камера/приватность'
}

Register-CimIndicationEvent -Namespace root/wmi -Query "SELECT * FROM HID_EVENT20" -SourceIdentifier mapkeys -MessageData $guess -Action {
    $d = $Event.SourceEventArgs.NewEvent.EventDetail
    if ($d -and $d.Count -gt 2) {
        $code = [int]$d[1]
        $val = [int]$d[2]
        $label = $Event.MessageData[$code]
        if (-not $label) { $label = '??? НЕИЗВЕСТНО — новый код' }
        $hex = ($d[0..7] | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
        Write-Host ("{0}  code=0x{1:X2} value=0x{2:X2}  |  {3}   [{4}]" -f (Get-Date -Format HH:mm:ss), $code, $val, $label, $hex) -ForegroundColor Cyan
    }
} | Out-Null

Write-Host "Картограф запущен. Жми ПО ОДНОЙ спец-клавише и смотри код + гипотезу." -ForegroundColor Yellow
Write-Host "Выход: Ctrl+C`n"

try { while ($true) { Start-Sleep -Seconds 1 } }
finally {
    Unregister-Event -SourceIdentifier mapkeys -ErrorAction SilentlyContinue
    Get-Event -SourceIdentifier mapkeys -ErrorAction SilentlyContinue | Remove-Event -ErrorAction SilentlyContinue
    Write-Host "`nКартограф остановлен."
}
