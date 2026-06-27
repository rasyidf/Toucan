using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Toucan.ViewModels;

public class LanguageSummaryItemViewModel : INotifyPropertyChanged
{
    public string Language { get; set; } = string.Empty;
    public double Percentage { get; set; }
    public string Stats { get; set; } = string.Empty;

    public bool IsExpanded
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
