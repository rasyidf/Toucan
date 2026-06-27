namespace Toucan.Core.Contracts;

public interface IMessageService
{
    void ShowMessage(string message, string title = "Info");
    bool ShowConfirmation(string message, string title = "Confirm");
}
