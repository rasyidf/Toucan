using ReactiveUI;

namespace OPEdit.Avalonia.ViewModels
{
    public class StartViewModel : ReactiveObject, IRoutableViewModel
    {
        public StartViewModel(IScreen screen) => HostScreen = screen;
        public string UrlPathSegment => "start";
        public IScreen HostScreen { get; }
    }
}