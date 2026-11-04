namespace ClientApp.UnitTests.Mocks;

public class MockDialogService : IDialogService
{
    public List<string> Messages { get; } = [];

    public Task ShowAlertAsync(string message, string title, string buttonLabel)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}
