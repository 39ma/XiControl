<#
  probe-charge-set.ps1 — перебор СПОСОБОВ ЗАПИСИ порога заряда (cmd 0x10).
  Цель: найти кодировку, при которой GET(0x10, sub 0x01) начинает отдавать 70 вместо 100.
  Все записи — в рамках функции заряда (0x10), значения безопасные (порог %).
  В конце пытается вернуть 100 и выключить защиту.

  PowerShell ОТ АДМИНИСТРАТОРА:
    pwsh -ExecutionPolicy Bypass -File .\probe-charge-set.ps1

  ВАЖНО: запускать лучше НА БАТАРЕЕ (отключи адаптер) — так чище видно setpoint,
  и заряд не убежит. Хотя запись setpoint от питания не зависит.
#>

$ErrorActionPreference = 'Stop'
$inst = Get-CimInstance -Namespace root/wmi -ClassName MiCommonInterface

# Полный контроль над буфером (чтобы пробовать значение в разных offset)
function Raw {
    param([byte[]]$Bytes)
    $in = [byte[]]::new(32)
    for ($i=0; $i -lt $Bytes.Length; $i++) { $in[$i] = $Bytes[$i] }
    (Invoke-CimMethod -InputObject $inst -MethodName MiInterface -Arguments @{ InData=[byte[]]$in }).OutData
}
function Get10([byte]$sub) { (Raw @(0x00,0xFA,0x00,0x10,$sub))[6] }
function Snap($label) {
    "{0,-42}  0x01={1,-4} 0x02={2,-3} 0x04={3,-3} 0x05={4,-3} 0x06={5,-3}" -f `
        $label, (Get10 0x01), (Get10 0x02), (Get10 0x04), (Get10 0x05), (Get10 0x06)
}

$adm = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $adm) { Write-Warning "Нужен админ."; return }

Write-Host "===== Перебор записи порога заряда (цель: 0x01 -> 70) =====`n"
Snap "БАЗА (до всего)"
Write-Host ""

$TARGET = 70   # 0x46

# Кандидаты записи. После каждого — снимок под-функций.
Write-Host "A) выключить защиту (0x02=0), затем setpoint 0x01=70 при ВЫКЛ, offset6"
[void](Raw @(0x00,0xFB,0x00,0x10,0x02,0x00,0x00,0x00)); Start-Sleep -Milliseconds 120
[void](Raw @(0x00,0xFB,0x00,0x10,0x01,0x00,$TARGET,0x00)); Start-Sleep -Milliseconds 150
Snap "  -> A"

Write-Host "`nB) setpoint как значение в offset4:  {FB,10, [70], ...}"
[void](Raw @(0x00,0xFB,0x00,0x10,$TARGET,0x00,0x00,0x00)); Start-Sleep -Milliseconds 150
Snap "  -> B"

Write-Host "`nC) enable c процентом вместо 1:  Put(0x10, 0x02, 70)"
[void](Raw @(0x00,0xFB,0x00,0x10,0x02,0x00,$TARGET,0x00)); Start-Sleep -Milliseconds 150
Snap "  -> C"

Write-Host "`nD) через под-функцию 0x04 = 70"
[void](Raw @(0x00,0xFB,0x00,0x10,0x04,0x00,$TARGET,0x00)); Start-Sleep -Milliseconds 150
Snap "  -> D"

Write-Host "`nE) через под-функцию 0x05 = 70"
[void](Raw @(0x00,0xFB,0x00,0x10,0x05,0x00,$TARGET,0x00)); Start-Sleep -Milliseconds 150
Snap "  -> E"

Write-Host "`nF) через под-функцию 0x06 = 70"
[void](Raw @(0x00,0xFB,0x00,0x10,0x06,0x00,$TARGET,0x00)); Start-Sleep -Milliseconds 150
Snap "  -> F"

Write-Host "`n--- Восстановление: setpoint 100, защита выкл ---"
[void](Raw @(0x00,0xFB,0x00,0x10,0x01,0x00,0x64,0x00)); Start-Sleep -Milliseconds 120
[void](Raw @(0x00,0xFB,0x00,0x10,0x02,0x00,0x00,0x00)); Start-Sleep -Milliseconds 150
Snap "ИТОГ (после восстановления)"

Write-Host "`n===== Готово. Если в каком-то шаге 0x01 стал 70 — способ найден! ====="
