<#
  probe-perf.ps1 — проба переключения режимов производительности (cmd 0x08).
  Безопасно и обратимо: запоминает текущий режим, перебирает варианты с чтением,
  затем ВОЗВРАЩАЕТ исходный режим.

  PowerShell ОТ АДМИНИСТРАТОРА:
    pwsh -ExecutionPolicy Bypass -File .\probe-perf.ps1

  Режимы (arg): 0x01 Balance, 0x02 Quiet, 0x03 Turbo, 0x04 Full-speed, 0x09 Auto.
  Заметишь по ноуту: кулеры/поведение могут кратко меняться на каждом шаге — это норм.
#>

$ErrorActionPreference = 'Stop'
$inst = Get-CimInstance -Namespace root/wmi -ClassName MiCommonInterface

function Mifs { param([byte]$Op,[byte]$Cmd,[byte]$Arg=0)
    $in=[byte[]]::new(32); $in[1]=$Op; $in[3]=$Cmd; $in[4]=$Arg
    (Invoke-CimMethod -InputObject $inst -MethodName MiInterface -Arguments @{ InData=[byte[]]$in }).OutData }
function GetPerf { (Mifs 0xFA 0x08)[4] }          # текущий режим в OUT[4]
function SetPerf([byte]$m){ [void](Mifs 0xFB 0x08 $m) }

$names = @{ 0x01='Balance'; 0x02='Quiet'; 0x03='Turbo'; 0x04='Full-speed'; 0x09='Auto' }

$adm = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $adm) { Write-Warning "Нужен админ."; return }

Write-Host "===== Проба режимов производительности (0x08) =====`n"
$orig = GetPerf
"Текущий режим: 0x{0:X2} ({1})`n" -f $orig, ($names[[int]$orig])

foreach ($m in 0x01,0x02,0x03,0x04,0x09) {
    SetPerf ([byte]$m)
    Start-Sleep -Milliseconds 300
    $rb = GetPerf
    $ok = if ($rb -eq $m) { 'принят ✓' } else { "НЕ принят (читается 0x{0:X2})" -f $rb }
    "  SET 0x{0:X2} ({1,-11}) -> {2}" -f $m, $names[[int]$m], $ok
}

Write-Host ("`n--- Возврат исходного режима 0x{0:X2} ({1}) ---" -f $orig, $names[[int]$orig])
SetPerf ([byte]$orig)
Start-Sleep -Milliseconds 300
$now = GetPerf
"Сейчас: 0x{0:X2} ({1})" -f $now, $names[[int]$now]

Write-Host "`n===== Готово. Скопируй вывод. ====="
