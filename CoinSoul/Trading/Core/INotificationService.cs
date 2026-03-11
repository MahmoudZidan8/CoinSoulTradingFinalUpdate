namespace CoinSoul.Trading.Core;
public interface INotificationService
{
    Task Success(string message);
    Task Error(string message);
    Task Warning(string message);
    Task Info(string message);
}
