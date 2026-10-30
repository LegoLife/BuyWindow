# BuyWindow

A personal app for monthly investing. Keep a watchlist of US stocks and ETFs, and for each one see when to buy this month and whether that timing has actually beaten buying on the first trading day.

Timing signals often don't beat that baseline. The verdict on each ticker tells you whether this one does.

## Run it

1. Get a free API key from [Tiingo](https://www.tiingo.com/account/api/token).
2. Copy `.env.example` to `.env` and paste the key in.
3. Run `docker compose up -d --build`.
4. Open <http://127.0.0.1:8085>. Set `BUYWINDOW_PORT` in `.env` to use a different port.

Prices and your watchlist are kept in the `buywindow-data` Docker volume.

To run without Docker, set the key once with `dotnet user-secrets set Tiingo:ApiKey <key> --project src/BuyWindow.Web`, then run `dotnet run --project src/BuyWindow.Web`. Tests run with `dotnet test`.

## How it decides

**Score.** Five indicators are each worth up to 20 points, for a 0-100 score. The rules are the same for every ticker, because tuning them per ticker would fit past noise.

| Indicator | Full points | No points |
|---|---|---|
| RSI (14-day) | 30 or below | 60 or above |
| Bollinger Bands (20-day, 2 deviations) | At or below the lower band | At or above the middle |
| MACD histogram (12/26/9) | Negative and rising two days, or just crossed above zero | Falling or already positive |
| Volume against the prior 20-day average | Down day at 2x or more | 1x or less, or an up day |
| SPY against its 200-day average | At or above | Below |

Between those points the score scales evenly. A negative MACD histogram rising for one day earns 10.

**Buy rule.** Each ticker gets one purchase a month.

- The first close with a score of 70 or higher means buy at the next trading day's open.
- If that hasn't happened by the second-to-last trading day's close, buy at the last trading day's open.

**Backtest and verdict.** The backtest applies the same rule to past months, using next-day opening prices. It compares the price paid with the first trading day's open and shows how both sit above the month's lowest price. The verdict comes from the 95% confidence range of the monthly difference from the first day:

- **Likely edge:** the whole range shows the signal paying less.
- **Likely worse:** the whole range shows it paying more.
- **No clear edge:** the range includes zero.

**History.** Up to the last 10 years, using only data after the most recent trading gap of more than 30 days. Under 5 years is labeled low confidence and the day-of-month pattern is hidden.

**Stale data.** If a ticker or SPY isn't current, the app shows a warning and no Buy status, including a deadline buy that's due.

Prices refresh at 6:00 PM Eastern on trading days and again at 8:05 PM, after Tiingo's corrections finish.

## Limits

- Tiingo's free plan allows 50 requests an hour, 1,000 a day and 500 symbols a month. Each ticker and SPY uses one request per refresh.
- Tiingo's free license is for personal use only, so don't share the app or its data. The container only listens on this computer.
- Tiingo's search has no country field, so results can include non-US listings. Check the match before adding it.
- The trading calendar covers regular US market holidays, not one-off closures, in the current month.
- There are no earnings warnings, because Tiingo doesn't document future earnings dates.

## Decision log

| Date | Decision |
|---|---|
| 2026-10-30 | The buy threshold stays at 70. On BE, NBIS, MU and HOOD it triggered in about 13-29% of months, so it passes the "almost never triggers" check. It should not be tuned to improve backtest results. |
