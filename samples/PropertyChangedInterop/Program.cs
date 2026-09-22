using System.ComponentModel;
using System.Runtime.CompilerServices;
using AsyncEventBridge;

[assembly: GenerateAsyncEventsFor(typeof(INotifyPropertyChanged))]

INotifyPropertyChanged model = new PersonViewModel();

var legacyNotifications = 0;
PropertyChangedEventHandler legacyHandler = (_, eventArgs) =>
{
    if (eventArgs.PropertyName == nameof(PersonViewModel.Name))
    {
        legacyNotifications++;
    }
};

model.PropertyChanged += legacyHandler;

var nextNameChange = model.PropertyChangedAsync(
    eventArgs => eventArgs.PropertyName == nameof(PersonViewModel.Name));

((PersonViewModel)model).Name = "Ready";

PropertyChangedEventArgs change = await nextNameChange;

model.PropertyChanged -= legacyHandler;

Console.WriteLine($"Awaited property: {change.PropertyName}");
Console.WriteLine($"Legacy event subscribers still work: {legacyNotifications == 1}");

if (change.PropertyName != nameof(PersonViewModel.Name) || legacyNotifications != 1)
{
    throw new InvalidOperationException("The INotifyPropertyChanged interoperability sample did not observe the expected change.");
}

public sealed class PersonViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
}
