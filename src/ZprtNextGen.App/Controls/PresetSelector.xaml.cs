#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZprtNextGen.App.ViewModels;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.App.Controls;

/// <summary>Сгруппированный селектор пресетов с выпадающим списком.</summary>
public partial class PresetSelector : UserControl
{
    public static readonly DependencyProperty GroupsProperty = DependencyProperty.Register(
        nameof(Groups), typeof(IEnumerable<PresetGroupViewModel>), typeof(PresetSelector),
        new PropertyMetadata(Array.Empty<PresetGroupViewModel>()));

    public static readonly DependencyProperty SelectedPresetProperty = DependencyProperty.Register(
        nameof(SelectedPreset), typeof(Preset), typeof(PresetSelector), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(
        nameof(SelectCommand), typeof(ICommand), typeof(PresetSelector), new PropertyMetadata(null));

    public IEnumerable<PresetGroupViewModel> Groups
    {
        get => (IEnumerable<PresetGroupViewModel>)GetValue(GroupsProperty);
        set => SetValue(GroupsProperty, value);
    }

    public Preset? SelectedPreset
    {
        get => (Preset?)GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    private readonly List<Preset> _visiblePresets = new(); // плоский список видимых пресетов (для стрелок)
    private readonly List<Button> _itemButtons = new();    // кнопки пресетов в том же порядке
    private readonly List<Button> _focusableButtons = new(); // все фокусируемые кнопки popup'а (заголовки + пресеты)
    private int _focusedIndex = -1;

    public PresetSelector() => InitializeComponent();

    private void OnPopupOpened(object sender, EventArgs e)
    {
        RebuildVisibleLists();

        if (_itemButtons.Count > 0)
        {
            _focusedIndex = SelectedPreset is null ? -1 : _visiblePresets.IndexOf(SelectedPreset);
            if (_focusedIndex < 0)
                _focusedIndex = 0;
            _itemButtons[_focusedIndex].Focus();
        }
        else
        {
            _focusedIndex = -1;
        }
    }

    private void RebuildVisibleLists()
    {
        _visiblePresets.Clear();
        _itemButtons.Clear();
        _focusableButtons.Clear();

        if (Groups is not null)
        {
            foreach (var group in Groups.OfType<PresetGroupViewModel>())
            {
                if (!group.IsExpanded)
                    continue;

                foreach (var preset in group.Presets)
                    _visiblePresets.Add(preset);
            }
        }

        if (PopupBorder.Child is not null)
        {
            foreach (var element in GetDescendants(PopupBorder.Child))
            {
                if (element is not Button button)
                    continue;

                if (button.DataContext is PresetGroupViewModel)
                {
                    _focusableButtons.Add(button);
                }
                else if (button.DataContext is Preset)
                {
                    _focusableButtons.Add(button);
                    _itemButtons.Add(button);
                }
            }
        }
    }

    private void OnScrollKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                MoveFocus(-1);
                e.Handled = true;
                break;
            case Key.Down:
                MoveFocus(+1);
                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteFocused();
                e.Handled = true;
                break;
            case Key.Escape:
                ClosePopup();
                e.Handled = true;
                break;
            case Key.Tab:
                CycleFocus((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
                e.Handled = true;
                break;
        }
    }

    private void OnRootKeyDown(object sender, KeyEventArgs e)
    {
        if (!PresetsPopup.IsOpen)
            return;

        if (IsFocusInPopup())
            return;

        if (e.Key == Key.Escape)
        {
            ClosePopup();
            e.Handled = true;
        }
        else if (e.Key == Key.Down &&
                 ReferenceEquals(FocusManager.GetFocusedElement(this), FieldToggle) &&
                 _itemButtons.Count > 0)
        {
            _focusedIndex = 0;
            _itemButtons[0].Focus();
            e.Handled = true;
        }
    }

    private void OnGroupHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not PresetGroupViewModel group)
            return;

        group.IsExpanded = !group.IsExpanded;

        if (PresetsPopup.IsOpen)
            RebuildVisibleLists();
    }

    private void OnPresetItemClick(object sender, RoutedEventArgs e) => ClosePopup();

    private void MoveFocus(int delta)
    {
        if (_itemButtons.Count == 0)
            return;

        _focusedIndex = Math.Clamp(_focusedIndex + delta, 0, _itemButtons.Count - 1);
        _itemButtons[_focusedIndex].Focus();
    }

    private void ExecuteFocused()
    {
        if (_focusedIndex < 0 || _focusedIndex >= _visiblePresets.Count)
            return;

        SelectCommand?.Execute(_visiblePresets[_focusedIndex]);
        ClosePopup();
    }

    private void CycleFocus(bool backward)
    {
        if (_focusableButtons.Count == 0)
            return;

        var focused = FocusManager.GetFocusedElement(this) as Button;
        var index = focused is null ? -1 : _focusableButtons.IndexOf(focused);
        index = backward ? index - 1 : index + 1;

        if (index < 0)
            index = _focusableButtons.Count - 1;
        if (index >= _focusableButtons.Count)
            index = 0;

        _focusableButtons[index].Focus();
    }

    private void ClosePopup() => PresetsPopup.IsOpen = false;

    private bool IsFocusInPopup()
    {
        var element = FocusManager.GetFocusedElement(this) as DependencyObject;
        while (element is not null)
        {
            if (ReferenceEquals(element, PopupBorder))
                return true;
            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private static IEnumerable<DependencyObject> GetDescendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        foreach (var child in GetChildren(root).Reverse())
            stack.Push(child);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in GetChildren(node).Reverse())
                stack.Push(child);
        }
    }

    private static IEnumerable<DependencyObject> GetChildren(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            yield return VisualTreeHelper.GetChild(node, i);
    }
}
