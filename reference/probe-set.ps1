<#
  probe-set.ps1 — SET-проба защиты заряда (cmd 0x10). ПИШЕТ В КОНТРОЛЛЕР БАТАРЕИ.
  Безопасно и ОБРАТИМО: сохраняет исходное состояние и восстанавливает его в конце.
  Отвечает на вопросы:
    - хватает ли обычного admin для SET (без SYSTEM);
    - на какой % встаёт защита при включении;
    - ПИШЕТСЯ ли под-функция 0x01 (= произвольный лимит заряда?).

  Запускать в PowerShell ОТ АДМИНИСТРАТОРА:
    pwsh -ExecutionPolicy Bypass -File .\probe-set.ps1

  Ничего критичного не делает: только меняет порог заряда и вкл/выкл защиты,
  затем возвращает как было. Прервать можно в любой момент (Ctrl+C) —
  тогда просто выставь заново нужный лимит через настройки Xiaomi, если что.
#>

$ErrorActionPreference = 'Stop'
$inst = Get-CimInstance -Namespace root/wmi -ClassName MiCommonInterface

function Mifs {
    param([byte]$Op, [byte]$Cmd, [byte]$Arg = 0, [byte]$Val = 0)
    $in = [byte[]]::new(32)
    $in[1] = $Op            # 0xFA=GET, 0xFB=SET
    $in[3] = $Cmd
    $in[4] = $Arg
    $in[6] = $Val
    (Invoke-CimMethod -InputObject $inst -MethodName MiInterface -Arguments @{ InData = [byte[]]$in }).OutData
}
function Get10([byte]$sub) { (Mifs 0xFA 0x10 $sub)[6] }              # чтение под-функции заряда → OUT[6]
function Set10([byte]$sub, [byte]$val) { [void](Mifs 0xFB 0x10 $sub $val) }  # запись
function Line($t, $v) { "{0,-40} = {1} (0x{2:X2})" -f $t, $v, $v }

Write-Host "===== SET-проба защиты заряда (0x10) =====`n"
$adm = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
"Admin: $adm"; if (-not $adm) { Write-Warning "Нужен админ."; return }

# --- Сохранить исходное состояние ---
$origLimit  = Get10 0x01
$origEnable = Get10 0x02
Write-Host "`n--- Исходное состояние ---"
Line "Лимит (sub 0x01)"  $origLimit
Line "Защита вкл (sub 0x02)" $origEnable

# --- Тест 1: включить защиту, посмотреть, меняется ли лимит на 70 ---
Write-Host "`n--- Тест 1: включаем защиту (0x02: 0 -> 1) ---"
Set10 0x02 0
Start-Sleep -Milliseconds 100
Set10 0x02 1
Start-Sleep -Milliseconds 150
Line "Защита вкл после включения" (Get10 0x02)
Line "Лимит после включения"      (Get10 0x01)   # ← если стал 0x46 (70) — порог фиксированный 70

# --- Тест 2: пробуем записать ПРОИЗВОЛЬНЫЙ лимит 80% в под-функцию 0x01 ---
Write-Host "`n--- Тест 2: пишем лимит = 80 в sub 0x01 ---"
Set10 0x01 80
Start-Sleep -Milliseconds 150
$after = Get10 0x01
Line "Лимит после записи 80" $after
if ($after -eq 80) {
    Write-Host "  >>> УСПЕХ: под-функция 0x01 ПИШЕТСЯ — произвольный % ВОЗМОЖЕН через WMI!" -ForegroundColor Green
} elseif ($after -eq 70 -or $after -eq 0x46) {
    Write-Host "  >>> Лимит зафиксировался на 70 — похоже, порог фиксированный (только 70/100)." -ForegroundColor Yellow
} else {
    Write-Host "  >>> Неожиданное значение — записать в лог, разобрать." -ForegroundColor Yellow
}

# --- Восстановление исходного состояния ---
Write-Host "`n--- Восстановление исходного состояния ---"
Set10 0x01 $origLimit
Start-Sleep -Milliseconds 100
Set10 0x02 $origEnable
Start-Sleep -Milliseconds 150
Line "Лимит восстановлен"  (Get10 0x01)
Line "Защита восстановлена" (Get10 0x02)

Write-Host "`n===== Готово. Скопируй весь вывод. ====="
