# Источники и лицензии

## Референс-проекты (изучены)

### 1. Xiaomi CoreCharge
- Путь: `../../xiaomi_corecharge` (в этом же репозитории)
- Подход: **прямой доступ к EC через WinRing0** (порты 0x62/0x66, регистры `0xA4`/`0xA7`/`0x68`).
- Плюсы: произвольный % заряда (1–100), аккуратный UI/OSD/локализация, много продуманных Win32-деталей.
- Минусы: уязвимый драйвер WinRing0 (блок-лист Microsoft, конфликт с HVCI/Smart App Control), ложные срабатывания AV.
- **Что берём:** идеи UI/OSD, локализация, паттерн автозапуска. Код — можно (лицензия «личное/образовательное»), но переписываем под WMI.

### 2. MI Control Utility (loginsin/ALXR) v1.2.5
- Путь: `../../reserch/micontrol`
- Подход: **штатный WMI-интерфейс прошивки** (`MiCommonInterface`/`MiInterface`) + служба + named pipe + protobuf.
- Плюсы: правильный путь к железу, реверс кодов команд, guard заряда на события питания, обработка Fn-клавиш.
- Минусы: тяжёлая архитектура (служба+GUI+protobuf), UI «на любителя».
- ⚠️ **Лицензия GPLv3.** Копировать `.cpp`/`.h` нельзя без перевода нашего проекта на GPLv3.
  **Факты о протоколе** (коды команд, формат буфера, GUID'ы) — использовать можно свободно, это не объект авторского права.
- **Что берём:** знание протокола (см. `01-wmi-protocol.md`), идею ChargeGuard, идею перехвата Mi-кнопки — **реализуем сами**.

## Драйвер ядра Linux (источник кодов функций)

- `platform/x86: bitland-mifs-wmi` (v10), нацелен в Linux 7.1.
- Патч: https://patchew.org/linux/20260213135107.609958-1-qby140326@gmail.com/
- Новость: https://www.phoronix.com/news/Bitland-Driver-For-Linux-7.1
- Отсюда: GUID'ы, коды функций GPU-mode/fan/kb-brightness/AC-type/CPU-temp, подтверждение operation 250/251.
- ⚠️ Нумерация режимов производительности расходится с Windows-реверсом — доверяем MIControl.

## tongfang-mifs-control-userspace (Python, Linux)

- https://github.com/cubewhy/tongfang-mifs-control-userspace (MIT)
- Тот же транспорт (GUID `B60BFB48…`, буфер 32 байта, op 250/251, ACPI `\_SB.PCI0.WMID.WMAA`),
  но **таблица функций игровой линейки** (Tongfang / Redmi G): GPU-mux, RGB-клавиатура,
  вентиляторы (обороты, макс. режим по типам CPU/SYS), температура, TPLock=12, FnLock=11.
- Подтверждает: у MIFS минимум две таблицы функций по линейкам; наша (Bitland, тонкие) — другая.
- Бонус: в комментариях — заводские диапазоны оборотов кулеров по режимам (Intel/AMD).

## GUID'ы (WMI, Bitland MIFS)

| Назначение | GUID |
|------------|------|
| Метод `MiInterface` | `B60BFB48-3E5B-49E4-A0E9-8CFFE1B3434B` |
| События | `46C93E13-EE9B-4262-8488-563BCA757FEF` |

## Ключевые файлы референса MIControl (для навигации)

| Что | Файл |
|-----|------|
| Транспорт WMI, коды команд | `MIControlSvc/cpp/svc/CMiLowLevelCommand.cpp` + `.h` |
| Защита заряда + guard на питание | `MIControlSvc/cpp/svc/CPowerPowerChargeProtectGuard.cpp` |
| Подписка на события + Fn-клавиши | `MIControlSvc/cpp/svc/SvcMain/CMIService_WMI.cpp`, `CMIService.cpp` |
| Режимы производительности (UI) | `MIControl/cpp/GUI/Dialogs/Pages/CPagePerfomanceDlg.cpp` |
| Заряд (UI, две кнопки 70/100) | `MIControl/cpp/GUI/Dialogs/Pages/CPageChargeProtectDlg.cpp` |
| Микрофон | `MIControl/cpp/GUI/Dialogs/Pages/CPageMicDlg.cpp` |
| Определение модели (TM24) | `CPagePerfomanceDlg.cpp` (`Win32_ComputerSystem.SystemSKUNumber`) |
