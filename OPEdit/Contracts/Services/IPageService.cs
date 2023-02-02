using System.Windows.Controls;

namespace OPEdit.Contracts.Services;

public interface IPageService
{
    Type GetPageType(string key);

    Page GetPage(string key);
}
