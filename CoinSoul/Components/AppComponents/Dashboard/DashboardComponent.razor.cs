using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot;
using CoinSole;
using CoinSoul.BinanceService.API;
using CoinSoul.BinanceService.AutoServices.AccountDataService;
using CoinSoul.BinanceService.AutoServices.SpotTradeService;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine;
using CryptoExchange.Net.CommonObjects;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace CoinSoul.Components.AppComponents.Dashboard
{
    public class DashboardComponentBase : CoinSoleComponentBase
    {
        [Inject] IAutoSpotTradeService AutoSpotTradeService { get; set; }

        [Inject] IAutoAccountDataService AutoAccountDataService { get; set; }

        [Inject] ISnackbar Snackbar { get; set; }
        [Inject] ITradingEngine Engine { get; set; } = default!;

        //await Engine.EnqueueAsync(new StartBotCommand());
        //await Engine.EnqueueAsync(new StopBotCommand());

        protected int Index = -1; //default value cannot be 0 -> first selectedindex is 0.

        protected ChartOptions Options = new();

        protected List<ChartSeries> Series =
        [
            new ChartSeries() { Name = "Revenue", Data = [90, 79, 72, 69, 62, 62, 55, 65, 70] },
            new ChartSeries() { Name = "Loss", Data = [10, 41, 35, 51, 49, 62, 69, 91, 148] },
        ];

        double TotalRevenue, TotalLoss;

        protected double[]? PieData { get; set; }
        protected string[] PieLabels { get; set; } = ["Revenue", "Loss"];
        //protected List<ChartData> ChartsData =
        //[
        //  new() { Revenue = 90, Loss = 10 },
        //  new() { Revenue = 79, Loss = 41 },
        //  new() { Revenue = 72, Loss = 35 },
        //  new() { Revenue = 69, Loss = 51 },
        //  new() { Revenue = 62, Loss = 49 },
        //  new() { Revenue = 55, Loss = 62 },
        //  new() { Revenue = 70, Loss = 69 }
        //];

        public string[] XAxisLabels = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep" };

        CancellationTokenSource CancellationTokenSource = new(50000);

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                //   var orderbookOrders = await AutoAccountDataService.GetOrdersAsync("PEPEUSDT");

                //  CancellationToken CancellationToken = CancellationTokenSource.Token;

                //    var balance = await AutoAccountDataService.GetBalancesAsync("FDUSD");

                //   var getExchangeInformation1 = await AutoSpotTradeService.GetOrderBookAsync("ETHFIUSDT", 10);

                //      var x = await AutoAccountDataService.GetBalancesAsync(APINames.AccountId, CancellationToken);

                //  Task buyTask = Task.Run(BuyTokens);

                //  Task sellTask = Task.Run(SellTokens);

                //   await Task.WhenAll(buyTask, sellTask);

                // await BuyTokens();
                // await SellTokens();

                // Options.ChartPalette = [Colors.Green.Darken1, Colors.Red.Darken1];

                /* TotalRevenue = ChartsData.Sum(x => x.Revenue);
                 TotalLoss = ChartsData.Sum(x => x.Loss);

                 PieData = [TotalRevenue, TotalLoss];*/

                //  StateHasChanged();
            }
        }

        //    protected List<OrderId> FilledOrders { get; set; } = [];
        protected List<ChartData> BuySellPrice { get; set; } = [];

        protected Dictionary<long, ChartData> OrdersFilled { get; set; } = [];

        protected bool IsTrading { get; set; } = false;
        protected bool IsSelling { get; set; } = false;

        protected MudDataGrid<KeyValuePair<long, ChartData>> MudGrid { get; set; }

        //decimal LimitBuy = 40.00M;
        protected async Task BuyTokens()
        {
            while (IsTrading)
            {
                try
                {
                    var balances = await AutoAccountDataService.GetBalancesAsync("USDT");

                    var usdtAmount = balances.ElementAt(0);

                    if (usdtAmount.Available > 11)
                    {
                        var orderbookOrders = await AutoSpotTradeService.GetOrderBookAsync("PEPEUSDT", 2);

                        if (orderbookOrders.Bids.Any() && orderbookOrders.Bids.Count() >= 2)
                        {
                            var order1 = orderbookOrders.Bids.ElementAt(0);
                            //   var order2 = orderbookOrders.Asks.ElementAt(1);

                            //decimal orderRate = 0;
                            /* if (order1.Quantity > 0 && order2.Quantity > 0)
                             {
                                 orderRate = (order1.Quantity * 100) / order2.Quantity;
                             }*/

                            var quantity = Convert.ToInt32(11 / order1.Price);

                            var order = await AutoAccountDataService.PlaceOrderAsync("PEPEUSDT", OrderSide.Buy,
                                 SpotOrderType.Limit, quantity, null, null, order1.Price, TimeInForce.GoodTillCanceled);

                            BuySellPrice.Add(new() { OrderId = order.Id, Buy = order1.Price, Sell = 0 });

                            ShowSnackBar("Buying", Severity.Success);

                            //if (orderRate < 30)
                            //{
                            //    var order = await AutoAccountDataService.PlaceOrderAsync("PEPEUSDT", OrderSide.Buy,
                            //        SpotOrderType.StopLossLimit, 2, null, null, order2.Price, null, (order2.Price - 0.05M));

                            //    BuySellPrice.Add(new() { OrderId = order.Id, Buy = order2.Price, Sell = 0 });

                            //    ShowSnackBar("Buying", Severity.Success);

                            //    IsOrderFilled = true;
                            //    FinalPrice = order2.Price;
                            //    completedOrder = order;
                            //}
                            //else
                            //{
                            //    var order = await AutoAccountDataService.PlaceOrderAsync("PEPEUSDT", OrderSide.Buy,
                            //      SpotOrderType.StopLossLimit, 2, null, null, order1.Price, null, (order1.Price - 0.05M));

                            //    BuySellPrice.Add(new() { Buy = order1.Price, Sell = 0, OrderId = order.Id });

                            //    ShowSnackBar("Buying", Severity.Info);

                            //    IsOrderFilled = true;
                            //    FinalPrice = order1.Price;
                            //    completedOrder = order;
                            //}

                            /*     if (IsOrderFilled == true && FinalPrice > 0 && completedOrder != null)
                                 {
                                     //  LimitBuy -= 4 * FinalPrice;
                                     var sellTokens = SellTokens(FinalPrice, completedOrder);
                                     sellTokens.Wait();
                                 }*/
                        }
                    }
                }
                catch (Exception)
                {
                    // Wait for 3 seconds before the next iteration
                    await Task.Delay(3000);
                    continue;
                }

                // Wait for 3 seconds before the next iteration
                await Task.Delay(3000);
            }
        }

        protected async Task TrackBuyOrders()
        {
            while (IsTrading)
            {
                try
                {
                    if (BuySellPrice.Count == 0)
                    {
                        // Wait for 3 seconds before the next iteration
                        await Task.Delay(3000);
                        continue;
                    }

                    var buyOrder = BuySellPrice[0];

                    var order = await AutoAccountDataService.GetOrderAsync("PEPEUSDT", buyOrder.OrderId);

                    if (order != null)
                    {
                        if (order.Status == OrderStatus.Filled)
                        {
                            //   OrdersFilled.Add(buyOrder.OrderId, new() { OrderId = buyOrder.OrderId, Buy = buyOrder.Buy, Sell = 0 });
                            await SellTokens(order);
                        }
                    }
                }
                catch (Exception)
                {
                    // Wait for 3 seconds before the next iteration
                    await Task.Delay(3000);
                    continue;
                }

                // Wait for 3 seconds before the next iteration
                await Task.Delay(3000);
            }

        }

        protected async Task SellTokens(BinanceOrder order)
        {
            var balances = await AutoAccountDataService.GetBalancesAsync("PEPE");

            var pepeAmount = balances.ElementAt(0);

            var sellPrice = order.Price + 0.00000001M;
            var sellOrder = await AutoAccountDataService.PlaceOrderAsync("PEPEUSDT", OrderSide.Sell,
                 SpotOrderType.Limit, pepeAmount.Available, null, null, sellPrice, TimeInForce.GoodTillCanceled);

            BuySellPrice.RemoveAt(0);

            //    OrdersFilled[order.Id].Sell = sellPrice;

            ShowSnackBar("Selling", Severity.Success);

            //while (IsTrading)
            //{
            //    try
            //    {
            /*  OrdersFilled
              IsPerformingSell = true;
              var balance = await AutoAccountDataService.GetBalancesAsync("ETHFI");

              if (balance.Any())
              {
                  var usdtBalance = balance.First();

                  if (usdtBalance.Available >= 4)
                  {
                      var orderbookOrders = await AutoSpotTradeService.GetOrderBookAsync("ETHFIFDUSD", 2);

                      if (orderbookOrders.Bids.Any() && orderbookOrders.Bids.Count() >= 2)
                      {
                          var order1 = orderbookOrders.Bids.ElementAt(0);
                          var order2 = orderbookOrders.Bids.ElementAt(1);

                          var orderRate = (order1.Quantity * 100) / order2.Quantity;

                          var newPrice = currentPrice + (decimal)0.02;

                          decimal finalPrice = 0;
                          if (orderRate < 30)
                          {
                              finalPrice = newPrice > order2.Price ? newPrice : order2.Price;

                              Console.WriteLine($"Order Price: {order2.Price}");
                              Console.WriteLine($"Final Price: {finalPrice}");

                              var sellOrder = AutoAccountDataService.PlaceOrderAsync("ETHFIFDUSD", CommonOrderSide.Sell,
                                  CommonOrderType.Limit, 4, finalPrice, APINames.AccountId, null);

                              var priceIndex = BuySellPrice.FindIndex(x => x.OrderId == currentOrder.Id);
                              if (priceIndex != -1)
                              {
                                  BuySellPrice[priceIndex].Sell = (decimal)finalPrice;
                              }
                              ShowSnackBar("Selling", Severity.Info);
                          }
                          else
                          {
                              finalPrice = newPrice > order1.Price ? newPrice : order1.Price;

                              Console.WriteLine($"Order Price: {order2.Price}");
                              Console.WriteLine($"Final Price: {finalPrice}");

                              var sellOrder = AutoAccountDataService.PlaceOrderAsync("ETHFIFDUSD", CommonOrderSide.Sell,
                                  CommonOrderType.Limit, 4, finalPrice, APINames.AccountId, null);

                              var priceIndex = BuySellPrice.FindIndex(x => x.OrderId == currentOrder.Id);
                              if (priceIndex != -1)
                              {
                                  BuySellPrice[priceIndex].Sell = (decimal)finalPrice;
                              }
                              ShowSnackBar("Selling", Severity.Info);
                          }
                      }
                  }
              }

              IsPerformingSell = false;
              // Update UI state
              StateHasChanged();*/
            //  }
            //    catch (Exception)
            //    {
            //        // Wait for 3 seconds before the next iteration
            //        await Task.Delay(3000);
            //        return;
            //    }

            //    // Wait for 3 seconds before the next iteration
            //    await Task.Delay(3000);
            //}
        }

        protected async Task TrackSellOrders()
        {
            while (true)
            {
                try
                {
                    if (BuySellPrice.Count == 0)
                    {
                        continue;
                    }

                    var buyOrder = BuySellPrice[0];

                    var order = await AutoAccountDataService.GetOrderAsync("PEPEUSDT", buyOrder.OrderId);

                    if (order != null)
                    {
                        if (order.Status == OrderStatus.Filled)
                        {
                            await SellTokens(order);
                        }
                    }


                }
                catch (Exception)
                {
                    // Wait for 3 seconds before the next iteration
                    await Task.Delay(3000);
                    continue;
                }

                // Wait for 3 seconds before the next iteration
                await Task.Delay(3000);
            }
        }

        protected void ShowSnackBar(string message, Severity severity)
        {
            Snackbar.Add(message, severity);
        }

        protected async Task ProcesssBuying()
        {
            IsTrading = !IsTrading;

            if (IsTrading == true)
            {
                await BuyTokens();
            }
        }

        protected async Task ProcesssSelling()
        {
            IsSelling = !IsSelling;

            if (IsSelling == true)
            {
                await TrackBuyOrders();
            }
        }

        public class ChartData()
        {
            public long OrderId { get; set; }
            public decimal Buy { get; set; }
            public decimal Sell { get; set; }
        }
    }
}
