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

## Deploy to the home server

Run `.\deploy.ps1`. It publishes for ARM64 on this computer, uploads the result and your `.env` to `~/buywindow` on the Pi, and runs it in Docker at <http://192.168.1.137:8085>. It's only reachable on the home network. `.\deploy.ps1 -Rollback` returns to the previous image.

## Email alerts

Add `SMTP_USER` and `SMTP_PASSWORD` to `.env`, then deploy again. For Gmail, the password must be an [app password](https://myaccount.google.com/apppasswords). The home page has a button to send a test email.

You get one email per ticker per month when:

- **A buy is ready:** sent after the 8:05 PM Eastern refresh, so a price correction can't undo it.
- **A deadline buy is blocked:** the buy is due but the data is out of date.

Each email includes the ticker's verdict. Tickers that trigger at the same check share one email. If sending fails, the app tries again every 15 minutes and shows the problem on the home page.

## How it decides

Each ticker answers two questions, shown in one line at the top of its page and on the watchlist:

1. **Own it?** Should you keep buying this at all? From Finviz and SEC filings.
2. **When?** The buy score and buy rule below, which pick the day this month.

**Own it?** Five rules. Any failure shows **Review** with the reasons. **Own it** needs every answered rule to pass and at least 3 answered, so ETFs show **Not checked**.

| Rule | Passes when | From |
|---|---|---|
| Forward P/E | Above 0 and under 25 | Finviz |
| PEG | Above 0 and under 2 | Finviz, else SEC |
| Debt/Equity | Under 0.35 | Finviz, else SEC |
| EPS growth | Above 0% | SEC trailing 12 months, else Finviz "EPS this Y" |
| Short float | Under 10% | Finviz |

It doesn't affect the score, the buy rule or alerts. Finviz only has today's numbers, so it can't be backtested.

**Score vs Lynch.** The ticker page's "Own it? details" compares every past day's score with the Lynch checks as the filings stood that day. On Nov 13, 2026 the correlation was between -0.06 and +0.06 for BE, LLY, MU, SNDK and TSLA, so the two are unrelated.

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

- The first close with a score of 70 or higher means buy at the next trading day's open. A month's chances start with the previous month's last close, which buys at the first open.
- If that hasn't happened by the second-to-last trading day's close, buy at the last trading day's open.

**Backtest and verdict.** The backtest applies the same rule to past months, using next-day opening prices. It compares the price paid with the first trading day's open and shows how both sit above the month's lowest price. The verdict comes from the 95% confidence range of the monthly difference from the first day:

- **Signal better:** the whole range shows the signal paying less.
- **First day better:** the whole range shows it paying more.
- **No clear winner:** the range includes zero.

**Moving averages.** The top of the ticker page shows the 5-, 20-, 50-, 100- and 200-day averages and the year-to-date average, as readings and on a chart. The year-to-date average covers closes since the year's first trading day and restarts each January. They're for information only and don't affect the score or the buy rule.

**Best day to buy.** The watchlist names the day of the month with the lowest average price, measured against each month's average. If a 5-day stretch clearly beats that average, the day comes from that stretch. Otherwise it's the lowest day overall, marked "Could be chance". Tickers with under 5 years of data use the finished months they have and are also marked "Could be chance". It's for information only and doesn't affect the buy rule.

**History.** Up to the last 10 years, using only data after the most recent trading gap of more than 30 days. Under 5 years is labeled low confidence and the full day-of-month pattern is hidden, though the best day is still shown.

**Stale data.** If a ticker or SPY isn't current, the app shows a warning and no Buy status, including a deadline buy that's due.

Prices refresh at 6:00 PM Eastern on trading days and again at 8:05 PM, after Tiingo's corrections finish.

**Latest price.** The top of the ticker page shows the latest price, green if it's above the previous close and red if it's below. While the market is open, and for half an hour after the close, the 15-minute check fetches the watchlist's prices from Tiingo's IEX feed in one request. At other times it shows the last daily close. Live prices only count trades on IEX, are kept in memory, and are fetched again after a restart. They don't affect the score or alerts.

**Earnings release.** Under the price, the ticker page links the company's latest earnings release on SEC EDGAR: the newest 8-K reporting results (item 2.02). It also links the earnings call webcast, using the address the press release gives nearest its mention of the webcast or call. That's often the company's investor site rather than the call itself, where the webcast and its archive are listed. The SEC asks apps to name a contact email, so this sends `SEC_CONTACT_EMAIL` to the SEC, or `ALERT_EMAIL` or `SMTP_USER` when that's blank. With none set, the link is hidden. ETFs and most foreign companies don't file these releases, so their pages say none was found.

**Peter Lynch checks.** The top of the ticker page checks the stock against six stock-picking rules attributed to Peter Lynch, using the company's 10-K and 10-Q filings on SEC EDGAR and the latest price:

| Rule | Passes when |
|---|---|
| Trailing P/E | Price over the last 12 months' diluted earnings per share is under 25 |
| Forward P/E | Never checked: it needs analysts' estimates, which the SEC doesn't have |
| Debt/Equity | Total debt, including the part due this year and short-term borrowings, is under 35% of shareholders' equity |
| EPS growth | The last 12 months' earnings per share are over 15% above the 12 months before |
| PEG | Trailing P/E over EPS growth in percent is under 2 |
| Market value | Price times the latest quarter's diluted share count is over $5 billion |

The last 12 months come from the latest 10-Q's year so far, plus the last full year, minus the same months of that year. A loss fails the P/E, growth and PEG rules, and equity below zero fails debt. A rule the filings can't answer, such as debt filed under a tag the app doesn't read, is shown as not checked and left out of the count. It uses the same SEC contact email as the earnings release, and the filings are fetched again after 6 hours. ETFs and most foreign companies don't file these numbers, so their pages say none were found. It's for information only and doesn't affect the score or the buy rule.

**Finviz.** The ticker page shows the stock's P/E, forward P/E and short interest from its [Finviz](https://finviz.com/) quote page, the page's full table of readings, its latest 10 headlines, and a link to the page. The [Obscura](https://github.com/h4ckf0r0day/obscura) browser fetches the page, without running its scripts. The Docker images include Obscura; to run without Docker, put `obscura` on your PATH or set `Finviz:ObscuraPath`. Each ticker is fetched at most once an hour, one at a time. Finviz's terms don't allow automated access, so keep this personal and light. It's for information only and doesn't affect the score or the buy rule.

## Limits

- Tiingo's free plan allows 50 requests an hour, 1,000 a day and 500 symbols a month. Each ticker and SPY uses one request per refresh. Live prices add one request per 15 minutes during market hours, about 28 a day.
- Tiingo's free license is for personal use only, so don't share the app or its data. The container only listens on this computer.
- Tiingo's search has no country field, so results can include non-US listings. Check the match before adding it.
- The trading calendar covers regular US market holidays, not one-off closures, in the current month.
- There are no earnings warnings, because Tiingo doesn't document future earnings dates.

## Decision log

| Date | Decision |
|---|---|
| 2026-10-30 | The buy threshold stays at 70. On BE, NBIS, MU and HOOD it triggered in about 13-29% of months, so it passes the "almost never triggers" check. It should not be tuned to improve backtest results. |
| 2026-10-31 | A signal on a month's last close now buys at the next month's first open. It used to count for neither month, so SNDK's 75 on Oct 30 sent no alert. This was made to stop missing alerts, not to improve backtest results. |
| 2026-11-02 | The live score stays at five indicators. Adding the ticker's 200-day average showed no clear difference on BE, MU, NBIS, SNDK or TSLA, checked on the home server on Nov 2, 2026. The test's direction and 70 trigger were set before seeing results and should not be tuned to improve them. |
| 2026-11-13 | Fundamentals stay out of the score, as a separate "Own it?" check. Finviz numbers can't be backtested, and the Lynch checks showed no correlation with the score. The 200-day test was removed from the page. |
| 2026-11-02 | Rule of 40 stays out of the score. It changes quarterly, so it can't pick a day within a month, and it doesn't apply to ETFs. |
