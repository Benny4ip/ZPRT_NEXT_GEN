#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Группа пресетов для отображения в UI.</summary>
public class PresetGroupViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<Preset> Presets { get; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
                _onExpandedChanged?.Invoke(Id, value);
        }
    }

    private readonly Action<string, bool>? _onExpandedChanged;

    /// <summary>Создать группу пресетов.</summary>
    /// <param name="id">Идентификатор группы.</param>
    /// <param name="name">Отображаемое имя группы.</param>
    /// <param name="presets">Пресеты группы.</param>
    /// <param name="collapsedGroups">Идентификаторы свёрнутых групп (из сохранённых настроек).</param>
    /// <param name="onExpandedChanged">Обратный вызов при изменении <see cref="IsExpanded"/> (хост может сохранить свёрнутые группы).</param>
    public PresetGroupViewModel(
        string id,
        string name,
        IEnumerable<Preset> presets,
        IEnumerable<string>? collapsedGroups = null,
        Action<string, bool>? onExpandedChanged = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Presets = presets?.ToList() ?? throw new ArgumentNullException(nameof(presets));
        _onExpandedChanged = onExpandedChanged;

        // Начальное состояние задаётся напрямую в поле, чтобы событие не пробрасывалось при инициализации.
        _isExpanded = !(collapsedGroups?.Contains(Id) ?? false);
    }

    /// <summary>Изменить поле с уведомлением об изменении свойства.</summary>
    protected virtual bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
