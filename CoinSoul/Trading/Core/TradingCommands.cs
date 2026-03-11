namespace CoinSoul.Trading.Core;

public interface ITradingCommand { }

public sealed record StartBotCommand() : ITradingCommand;
public sealed record StopBotCommand() : ITradingCommand;
public sealed record EmergencyStopCommand(string Reason) : ITradingCommand;
public sealed record UpdateBotSettingsCommand(BotSettings Settings) : ITradingCommand;
