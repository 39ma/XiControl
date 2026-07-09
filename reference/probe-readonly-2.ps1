<#
  probe-readonly-2.ps1 — свип sub-аргументов (ТОЛЬКО GET, безопасно).
  Цель:
    1) понять неизвестную команду 0x0C;
    2) проверить, оживает ли «неподдерживаемая» телеметрия с правильным sub-arg
       (как у заряда, где нужен arg=0x02).
  В железо НЕ пишет (operation=0xFA). Запускать в PowerShell ОТ АДМИНИСТРАТОРА.

    pwsh -ExecutionPolicy Bypass -File .\probe-readonly-2.ps1
#>

$ErrorActionPreference = 'Stop'

$inst = Get-CimInstance -Namespace root/wmi -ClassName MiCommonInterface

function Invoke-Mifs {
    param($Instance, [byte]$Cmd, [byte]$Arg = 0x00, [byte]$Arg2 = 0x00)
    $in = [byte[]]::new(32)
    $in[1] = 0xFA          # GET
    $in[3] = $Cmd
    $in[4] = $Arg
    $in[6] = $Arg2
    $res = Invoke-CimMethod -InputObject $Instance -MethodName MiInterface -Arguments @{ InData = [byte[]]$in }
    [PSCustomObject]@{
        Status = ('0x{0:X2}' -f $res.OutData[1])   # 0x80 ok / 0xE0 unsupported
        OutHex = (($res.OutData[0..11] | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
    }
}

Write-Host "===== Свип sub-аргументов (GET) =====`n"
"Instance: $($inst.InstanceName)`n"

# Команды, которые интересно прощупать разными sub-arg
$cmds = @(0x0C, 0x08, 0x0A, 0x10, 0x0D, 0x12, 0x13, 0x16, 0x09, 0x0B, 0x0E, 0x0F)

foreach ($cmd in $cmds) {
    "### cmd 0x{0:X2}" -f $cmd
    foreach ($arg in 0x00..0x08) {
        $r = Invoke-Mifs $inst -Cmd ([byte]$cmd) -Arg ([byte]$arg)
        $mark = if ($r.Status -eq '0x80') { ' <== OK' } else { '' }
        "  arg=0x{0:X2}  st={1}  [ {2} ]{3}" -f $arg, $r.Status, $r.OutHex, $mark
    }
    ""
}

Write-Host "===== Готово. Скопируй вывод (особенно строки с '<== OK'). ====="
