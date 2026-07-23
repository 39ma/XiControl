namespace XiControl.Localization;

/// <summary>
/// Локализация как зависимость (DI) — шов над статическим Loc. Существующие вызовы
/// Loc.T в UI мигрируют на инжектированный ILocalizer по мере декомпозиции форм
/// (Фаза 2); новый код берёт интерфейс сразу.
/// </summary>
public interface ILocalizer
{
    /// <summary>Текущий язык интерфейса.</summary>
    Lang Current { get; set; }

    /// <summary>Строка по ключу; неизвестный ключ возвращается как есть.</summary>
    string T(string key);

    /// <summary>Строка по ключу + string.Format по аргументам.</summary>
    string T(string key, params object[] args);
}

/// <summary>Прод-реализация поверх статического словаря Loc.</summary>
public sealed class Localizer : ILocalizer
{
    public Lang Current { get => Loc.Current; set => Loc.Current = value; }
    public string T(string key) => Loc.T(key);
    public string T(string key, params object[] args) => Loc.T(key, args);
}
