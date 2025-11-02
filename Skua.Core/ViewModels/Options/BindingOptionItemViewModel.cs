using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using System.ComponentModel;
using System.Windows;

namespace Skua.Core.ViewModels;

public class BindingOptionItemViewModel<TDisplay, TOptionBindingTarget> : CommandOptionItemViewModel, IDisposable
    where TOptionBindingTarget : class, IOptionDictionary, INotifyPropertyChanged
{
    private readonly string _binding;
    private readonly TOptionBindingTarget _options;
    private bool _disposed;

    public BindingOptionItemViewModel(string content, string binding, TOptionBindingTarget options, IRelayCommand command) : base(content, command, typeof(TDisplay))
    {
        _binding = binding;
        _options = options;
        Value = _options.OptionDictionary[_binding].Invoke();
        PropertyChangedEventManager.AddHandler(_options, Option_PropertyChanged, string.Empty);
    }

    public BindingOptionItemViewModel(string content, string tag, string binding, TOptionBindingTarget options, IRelayCommand command) : base(content, tag, command, typeof(TDisplay))
    {
        _binding = binding;
        _options = options;
        Value = _options.OptionDictionary[_binding].Invoke();
        PropertyChangedEventManager.AddHandler(_options, Option_PropertyChanged, string.Empty);
    }

    public BindingOptionItemViewModel(string content, string description, string tag, string binding, TOptionBindingTarget options, IRelayCommand command) : base(content, description, tag, command, typeof(TDisplay))
    {
        _binding = binding;
        _options = options;
        Value = _options.OptionDictionary[_binding].Invoke();
        PropertyChangedEventManager.AddHandler(_options, Option_PropertyChanged, string.Empty);
    }

    private void Option_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == _binding)
            Value = _options?.OptionDictionary[_binding].Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        PropertyChangedEventManager.RemoveHandler(_options, Option_PropertyChanged, string.Empty);
        GC.SuppressFinalize(this);
    }

    ~BindingOptionItemViewModel()
    {
        Dispose();
    }
}