using Microsoft.Win32;
using XiControl.Wmi;

namespace XiControl.SystemIntegration;

/// <summary>
/// Переустанавливает лимит заряда после сна и смены питания (AC↔батарея).
/// EC сбрасывает защиту на этих событиях (проверено вживую), поэтому её нужно
/// переармливать, пока пользователь хочет «беречь батарею».
/// Дополнительно ре-арм на входе в сон/гибернацию и при завершении сеанса — иначе EC
/// остаётся без защиты на весь период «выключено» и батарея заряжается до 100%
/// («Выключение» в Windows 11 с быстрым запуском — это тоже гибернация). Служба
/// оригинального MIControl делает так же (suspend-колбэк срабатывает и на входе).
/// </summary>
public sealed class ChargeGuard : IDisposable
{
    private readonly MifsClient _mifs;
    private readonly Func<bool> _careWanted;   // желаемое состояние (из настроек/UI)
    private readonly System.Windows.Forms.Timer _debounce;

    public ChargeGuard(MifsClient mifs, Func<bool> careWanted)
    {
        _mifs = mifs;
        _careWanted = careWanted;

        // события StatusChange могут сыпаться пачкой — гасим дребезг
        _debounce = new System.Windows.Forms.Timer { Interval = 1500 };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reapply(); };

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Resume — выход из сна; StatusChange — смена питания AC↔батарея
        if (e.Mode is PowerModes.Resume or PowerModes.StatusChange)
        {
            _debounce.Stop();
            _debounce.Start();
        }
        // Suspend — вход в сон/гибернацию/«выключение» с быстрым запуском: ре-арм сразу,
        // без дебаунса — после этого события наш код уже не выполнится
        else if (e.Mode == PowerModes.Suspend)
        {
            _debounce.Stop();
            Reapply();
        }
    }

    // Завершение сеанса (shutdown/restart/logoff) — последняя возможность заармить EC
    // перед периодом «выключено»; заодно закрывает окно, когда дебаунс (1.5 с) не успел
    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => Reapply();

    /// <summary>Применить желаемое состояние заряда прямо сейчас (напр. при старте).</summary>
    public void Reapply()
    {
        try
        {
            if (_careWanted())
                _mifs.SetChargeCare(true);   // ре-арм off→on внутри
        }
        catch (Exception ex) { Log.Ex("ChargeGuard.Reapply", ex); /* железо могло быть недоступно */ }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _debounce.Dispose();
    }
}
