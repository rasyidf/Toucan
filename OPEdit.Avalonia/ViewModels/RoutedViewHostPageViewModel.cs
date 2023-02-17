using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;

namespace OPEdit.Avalonia.ViewModels
{
        internal class RoutedViewHostPageViewModel : ReactiveObject, IScreen
        {
            public RoutedViewHostPageViewModel()
            {
                Start = new(this);
                Editor = new(this);
                Router.Navigate.Execute(Start);
            }

            public RoutingState Router { get; } = new();
            public StartViewModel Start { get; }
            public EditorViewModel Editor { get; }

            public void ShowStart() => Router.Navigate.Execute(Start);
            public void ShowEditor() => Router.Navigate.Execute(Editor);
        }
    }