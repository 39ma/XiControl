<#
  probe-readonly.ps1 — БЕЗОПАСНЫЕ read-only пробы MIFS WMI (Xiaomi/Redmi).
  Запускать в PowerShell ОТ АДМИНИСТРАТОРА.
  Только чтение (operation=0xFA GET). В железо ничего НЕ пишет.

  Как запустить:
    1. Win → «PowerShell» → ПКМ → «Запуск от имени администратора»
    2. cd в этот каталог
    3.  powershell -ExecutionPolicy Bypass -File .\probe-readonly.ps1
        (или в pwsh:  pwsh -ExecutionPolicy Bypass -File .\probe-readonly.ps1)
    4. Скопировать весь вывод и прислать.
#>

$ErrorActionPreference = 'Stop'

function Get-MifsInstance {
    Get-CimInstance -Namespace root/wmi -ClassName MiCommonInterface
}

# Собрать 32-байтный буфер и вызвать MiInterface. operation: 0xFA=GET, 0xFB=SET
function Invoke-Mifs {
    param(
        [Parameter(Mandatory)] $Instance,
        [byte]$Operation = 0xFA,
        [byte]$Cmd,
        [byte]$Arg = 0x00,
        [byte]$Arg2 = 0x00
    )
    $in = [byte[]]::new(32)
    $in[1] = $Operation
    $in[3] = $Cmd
    $in[4] = $Arg
    $in[6] = $Arg2
    $res = Invoke-CimMethod -InputObject $Instance -MethodName MiInterface -Arguments @{ InData = [byte[]]$in }
    [PSCustomObject]@{
        Cmd        = ('0x{0:X2}' -f $Cmd)
        ReturnCode = $res.ReturnCode
        OutHex     = (($res.OutData | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
        OutData    = $res.OutData
    }
}

function Show([string]$title, $r) {
    "{0,-26} RC={1}  OUT=[ {2} ]" -f $title, $r.ReturnCode, $r.OutHex
}

Write-Host "===== MIFS read-only probe =====`n"

$cs = Get-CimInstance Win32_ComputerSystem
"Model: $($cs.Manufacturer) $($cs.Model) | SKU: $($cs.SystemSKUNumber)"
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$adm = (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
"Admin: $adm  (должно быть True!)`n"
if (-not $adm) { Write-Warning "Не админ — WMI-доступ не сработает. Перезапусти от администратора."; return }

$inst = Get-MifsInstance
"Instance: $($inst.InstanceName)`n"

Write-Host "--- Функции (GET, читаем текущее состояние) ---"
Show "Perf mode      (0x08)"  (Invoke-Mifs $inst -Cmd 0x08)               # ожидаем режим в OUT[5]
Show "Charge protect (0x10)"  (Invoke-Mifs $inst -Cmd 0x10 -Arg 0x02)     # ожидаем статус в OUT[6]

Write-Host "`n--- Телеметрия (GET) ---"
Show "Fan speeds     (0x0D)"  (Invoke-Mifs $inst -Cmd 0x0D)               # CPU[0..1] GPU[2..3] SYS[6..7] LE
Show "CPU temp       (0x16)"  (Invoke-Mifs $inst -Cmd 0x16)               # °C ?
Show "AC type        (0x13)"  (Invoke-Mifs $inst -Cmd 0x13)               # 1=USB-C 2=DC ?
Show "KB brightness  (0x12)"  (Invoke-Mifs $inst -Cmd 0x12)               # 0..10 / 128=Auto ?
Show "GPU mode       (0x09)"  (Invoke-Mifs $inst -Cmd 0x09)               # 0=Hybrid 1=Discrete 2=UMA ?

Write-Host "`n--- Мягкий перебор неизвестных cmd (ТОЛЬКО GET, безопасно) ---"
foreach ($c in 0x00..0x2A) {
    try {
        $r = Invoke-Mifs $inst -Cmd ([byte]$c)
        # Печатаем только осмысленные (не все нули и RC=0)
        if ($r.ReturnCode -eq 0 -and ($r.OutData -join '') -notmatch '^0*$') {
            Show ("cmd 0x{0:X2}" -f $c) $r
        }
    } catch { }
}

Write-Host "`n===== Готово. Скопируй весь вывод. ====="
