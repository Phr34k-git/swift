using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Client.ViewModels;

/// <summary>
/// Base type for view models that need property-change notifications.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// Fires when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Updates a backing field and raises a property-change notification when needed.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Raises a property-change notification.
    /// </summary>
    protected void OnPropertyChanged(string? propertyName)
    {
        if (propertyName is not null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
