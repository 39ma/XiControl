<#
  probe-events.ps1 — проверка WMI-событий (горячие Fn-клавиши, Mi-кнопка).
  Отвечает на Вопрос 3: ловятся ли HID_EVENT из пользовательской сессии
  (→ хватит трей-резидента) или нет (→ нужна служба).

  PowerShell ОТ АДМИНИСТРАТОРА:
    pwsh -ExecutionPolicy Bypass -File .\probe-events.ps1

  Во время 25-секундного окна ЖМИ по очереди:
    Fn+K (режим), Fn+F4 (микрофон), Fn+S (частота), Fn+J (тачпад), Mi/AI-кнопку,
    клавишу яркости подсветки, режим "в самолёте".
#>

$ErrorActionPreference = 'Continue'

# Пробуем несколько HID_EVENT классов (по умолчанию MIControl слушает HID_EVENT20).
$classes = @('HID_EVENT20','HID_EVENT10','HID_EVENT00','HID_EVENT30')
$subs = @()
foreach ($c in $classes) {
    try {
        Register-CimIndicationEvent -Namespace root/wmi -Query "SELECT * FROM $c" -SourceIdentifier $c -ErrorAction Stop
        $subs += $c
        Write-Host "Подписка OK: $c"
    } catch {
        Write-Host "  (нет класса $c — пропуск)"
    }
}
if (-not $subs) { Write-Warning "Ни один HID_EVENT класс не подписался."; return }

Write-Host "`n>>> ЖМИ Fn-клавиши и Mi-кнопку сейчас (25 сек): Fn+K, Fn+F4, Fn+S, Fn+J, Mi, яркость... <<<`n"
Start-Sleep -Seconds 25

Write-Host "--- Пойманные события ---"
$any = $false
foreach ($c in $subs) {
    $evs = Get-Event -SourceIdentifier $c -ErrorAction SilentlyContinue
    foreach ($e in $evs) {
        $any = $true
        $ne = $e.SourceEventArgs.NewEvent
        $det = $ne.EventDetail
        $hex = if ($det) { ($det | ForEach-Object {'{0:X2}' -f $_}) -join ' ' } else { '(нет EventDetail)' }
        "[{0}]  EventDetail = [ {1} ]" -f $c, $hex
    }
    Remove-Event -SourceIdentifier $c -ErrorAction SilentlyContinue
    Unregister-Event -SourceIdentifier $c -ErrorAction SilentlyContinue
}
if (-not $any) { Write-Host "  Событий НЕ поймано (возможно, класс другой или нужна служба)." }

Write-Host "`n===== Готово. Скопируй вывод. ====="
