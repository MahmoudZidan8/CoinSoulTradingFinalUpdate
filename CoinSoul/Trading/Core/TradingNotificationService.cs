using CoinSoul.Components.Common;
using CoinSoul.Trading.Core;
using MudBlazor;

namespace CoinSoul.Infrastructure.Notifications;

public class TradingNotificationService : INotificationService
{
    private readonly IDialogService _dialog;

    public TradingNotificationService(IDialogService dialog)
    {
        _dialog = dialog;
    }

    public Task Success(string message)
        => Show(message, Severity.Success);

    public Task Error(string message)
        => Show(message, Severity.Error);

    public Task Warning(string message)
        => Show(message, Severity.Warning);

    public Task Info(string message)
        => Show(message, Severity.Info);

    private Task Show(string message, Severity severity)
    {
        var parameters = new DialogParameters
        {
            ["Message"] = message,
            ["Severity"] = severity
        };

        _dialog.Show<TradingNotificationDialog>(
            "Notification",
            parameters,
            new DialogOptions
            {
                CloseOnEscapeKey = true,
                MaxWidth = MaxWidth.Small,
                FullWidth = true,
                DisableBackdropClick = false
            });

        return Task.CompletedTask;
    }
}
