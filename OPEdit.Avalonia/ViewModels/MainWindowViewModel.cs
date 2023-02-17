using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Text;

namespace OPEdit.Avalonia.ViewModels
{
    internal class MainWindowViewModel : ReactiveObject
    {
        public RoutedViewHostPageViewModel RoutedViewHost { get; } = new();
    }
}
