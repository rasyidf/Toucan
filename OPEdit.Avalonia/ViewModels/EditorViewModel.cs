using ReactiveUI;

namespace OPEdit.Avalonia.ViewModels
{
    public class EditorViewModel : ReactiveObject, IRoutableViewModel
    {
        public EditorViewModel(IScreen screen) => HostScreen = screen;
        public string UrlPathSegment => "editor";
        public IScreen HostScreen { get; }
    }
}