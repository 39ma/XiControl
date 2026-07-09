<#
  charge.ps1 — управление лимитом заряда (cmd 0x10) для эксперимента "в бою".
  Ставит лимит и ОСТАВЛЯЕТ его активным (в отличие от probe-set.ps1, который восстанавливал).

  PowerShell ОТ АДМИНИСТРАТОРА:
    .\charge.ps1 -Limit 60     # выставить лимит 60% и включить защиту
    .\charge.ps1 -Status       # показать текущий лимит, статус защиты и % батареи
    .\charge.ps1 -Off          # выключить защиту и вернуть лимит 100%

  Эксперимент:
    1) на батарее (сейчас ~57%):  .\charge.ps1 -Limit 60
    2) подключить адаптер
    3) периодически:              .\charge.ps1 -Status   — до скольки дозарядится
    4) после:                     .\charge.ps1 -Off
#>

[CmdletBinding(DefaultParameterSetName='Status')]
param(
    [Parameter(ParameterSetName='Set', Mandatory)]   [ValidateRange(30,100)] [byte]$Limit,
    [Parameter(ParameterSetName='Off', Mandatory)]   [switch]$Off,
    [Parameter(ParameterSetName='Rearm', Mandatory)] [switch]$Rearm,
    [Parameter(ParameterSetName='Status')]           [switch]$Status
)

$ErrorActionPreference = 'Stop'
$inst = Get-CimInstance -Namespace root/wmi -ClassName MiCommonInterface

function Mifs { param([byte]$Op,[byte]$Cmd,[byte]$Arg=0,[byte]$Val=0)
    $in=[byte[]]::new(32); $in[1]=$Op; $in[3]=$Cmd; $in[4]=$Arg; $in[6]=$Val
    (Invoke-CimMethod -InputObject $inst -MethodName MiInterface -Arguments @{ InData=[byte[]]$in }).OutData }
function Get10([byte]$s){ (Mifs 0xFA 0x10 $s)[6] }
function Set10([byte]$s,[byte]$v){ [void](Mifs 0xFB 0x10 $s $v) }

function Show-Status {
    $b = Get-CimInstance Win32_Battery
    $ac = switch ($b.BatteryStatus) { 1 {'на батарее'} 2 {'от сети (AC)'} default {"код $($b.BatteryStatus)"} }
    "  Заряд батареи (Windows) : {0}%" -f $b.EstimatedChargeRemaining
    "  Питание                 : {0}" -f $ac
    "  Лимит MIFS  (sub 0x01)  : {0}%" -f (Get10 0x01)
    "  Защита вкл  (sub 0x02)  : {0}" -f (Get10 0x02)
    "  Флаг        (sub 0x03)  : {0}" -f (Get10 0x03)
}

$adm = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $adm) { Write-Warning "Нужен PowerShell от администратора."; return }

switch ($PSCmdlet.ParameterSetName) {
    'Set' {
        Write-Host "=== Ставлю лимит $Limit% и включаю защиту ===`n"
        Write-Host "ДО:"; Show-Status
        Set10 0x01 $Limit                       # записать порог
        Start-Sleep -Milliseconds 100
        Set10 0x02 0; Start-Sleep -Milliseconds 100; Set10 0x02 1   # ре-арм (off->on) сбрасывает стейт EC
        Start-Sleep -Milliseconds 200
        Write-Host "`nПОСЛЕ:"; Show-Status
        Write-Host "`nТеперь подключи адаптер и periodically запускай:  .\charge.ps1 -Status"
    }
    'Rearm' {
        Write-Host "=== Переарм защиты (0x02: off->on) на ТЕКУЩЕМ питании ===`n"
        Write-Host "ДО:"; Show-Status
        Set10 0x02 0; Start-Sleep -Milliseconds 150; Set10 0x02 1
        Start-Sleep -Milliseconds 200
        Write-Host "`nПОСЛЕ:"; Show-Status
        Write-Host "`nСмотри, остановится ли заряд теперь:  .\charge.ps1 -Status"
    }
    'Off' {
        Write-Host "=== Выключаю защиту, возвращаю лимит 100% ===`n"
        Set10 0x02 0; Start-Sleep -Milliseconds 100
        Set10 0x01 100; Start-Sleep -Milliseconds 150
        Show-Status
    }
    default {
        Write-Host "=== Статус ===`n"; Show-Status
    }
}
