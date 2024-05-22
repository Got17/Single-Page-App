namespace MySPA

open WebSharper
open WebSharper.JavaScript
open WebSharper.UI
open WebSharper.UI.Client
open WebSharper.UI.Templating
open WebSharper.Sitelets
open WebSharper.Charting


type IndexTemplate = Template<"wwwroot/index.html", ClientLoad.FromDocument>

type EndPoint = 
    | [<EndPoint "/">] MoneyTracking
    | [<EndPoint "/stockPortfolio">] StockPortfolio

[<JavaScript>]
module Chart =
    let createPieChart labels data =
        let chart =
            Chart.Pie (List.zip labels data)
        Renderers.ChartJs.Render(chart, Size = Size(300, 300))

    let createBalanceChart totalIncome totalExpenses =
        let labels = ["Total Income"; "Total Expenses"]
        let data = [totalIncome; totalExpenses]
        let chart = 
            Chart.Pie(List.zip labels data)
        Renderers.ChartJs.Render(chart, Size = Size(300, 300))

[<JavaScript>]
module Pages =
    let emptyAlert() = 
        JS.Alert "Field can not be left empty."

    let categoryIncome = Var.Create ""
    let amountIncome = Var.Create 0.00
    let totalIncome = Var.Create 0.00

    let categoryExpenses = Var.Create ""
    let amountExpenses = Var.Create 0.00    
    let totalExpenses = Var.Create 0.00

    let incomeData = Var.Create ([], [])
    let expenseData = Var.Create ([], [])

    let MoneyPage() =
        let addIncome () =
            totalIncome.Value <- totalIncome.Value + amountIncome.Value
            let (labels, data) = incomeData.Value
            incomeData.Value <- (labels @ [categoryIncome.Value], data @ [amountIncome.Value])
            categoryIncome.Value <- ""
            amountIncome.Value <- 0.00

        let addExpenses () =
            totalExpenses.Value <- totalExpenses.Value + amountExpenses.Value
            let (labels, data) = expenseData.Value
            expenseData.Value <- (labels @ [categoryExpenses.Value], data @ [amountExpenses.Value])
            categoryExpenses.Value <- ""
            amountExpenses.Value <- 0.00


        let balance =
            View.Map2 (fun income expense -> 
                sprintf "%.2f" <| income - expense
            ) totalIncome.View totalExpenses.View

        let totalIncome = 
            View.Map (fun income -> 
                sprintf "%.2f" income
            ) totalIncome.View

        let totalExpenses = 
            View.Map (fun expense -> 
                sprintf "%.2f" expense
            ) totalExpenses.View

        let incomeChart = 
            View.Map2 (fun labels data -> 
                Chart.createPieChart labels data
            ) (View.Map fst incomeData.View) (View.Map snd incomeData.View)

        let expenseChart = 
            View.Map2 (fun labels data -> 
                Chart.createPieChart labels data
            ) (View.Map fst expenseData.View) (View.Map snd expenseData.View)

        let balanceChart = 
            View.Map2 (fun (income: string) (expense:string) -> 
                let floatIncome = float income
                let floatExpense = float expense
                Chart.createBalanceChart  floatIncome floatExpense
            ) totalIncome totalExpenses

        IndexTemplate.MoneyTracking()
            .HeaderSPA("Money Tracking")
            .categoryIncome(categoryIncome)
            .amountIncome(amountIncome)
            .categoryExpenses(categoryExpenses)
            .amountExpenses(amountExpenses)
            .Balance(balance)
            .balanceChart(balanceChart.V)           
            .Expenses(totalExpenses)
            .expensesChart(expenseChart.V)
            .Income(totalIncome)
            .incomeChart(incomeChart.V)
            .addIncome(fun _ -> 
                if categoryIncome.Value <> "" && amountIncome.Value <> 0.00 then
                    addIncome()
                else
                    emptyAlert()
            )
            .addExpense(fun _ -> 
                if categoryExpenses.Value <> "" && amountExpenses.Value <> 0.00 then
                    addExpenses()
                else
                    emptyAlert()
            )
            .Doc()
        
    type Stock = {
        Name: string
        Amount: float
        Price: float
        mutable LastPrice: float
    }

    let stockName = Var.Create ""
    let stockAmount = Var.Create 0.0
    let stockPrice = Var.Create 0.0

    let randomStockLastPrice (price:float) = 
        let random = System.Random()

        20.0 * random.NextDouble() + (price - 10.0)
    

    let stockData = [
        { Name = "Apple"; Amount = 1.1; Price = 189.0; LastPrice = randomStockLastPrice 189.0 }
        { Name = "Alphabet"; Amount = 1.1; Price = 170.0; LastPrice = randomStockLastPrice 170.0 }
        { Name = "Microsoft"; Amount = 1.3; Price = 416.0; LastPrice = randomStockLastPrice 416.0 }
    ]

    let loadStockData() =
        let data = JS.Window.LocalStorage.GetItem("stocks")
        if isNull data then stockData
        else
            try
                let stocks = Json.Deserialize<Stock list>(data)
                // Update LastPrice for each stock
                stocks |> List.map (fun stock ->
                    { stock with LastPrice = randomStockLastPrice stock.Price }
                )
            with
            | _ -> stockData

    let saveStockData (stocks: Stock list) =
        let data = Json.Serialize stocks
        JS.Window.LocalStorage.SetItem("stocks", data)

    let stockModel = 
        let stocks = loadStockData()
        let model = ListModel.Create (fun stock -> stock.Name) stocks
        model.View |> View.Sink (fun stocks -> saveStockData (List.ofSeq stocks))
        model

    let capitalize (str: string) =
        str[0].ToString().ToUpper() + str.Substring(1).ToLower()

    let updateLastPrices() =
        stockModel.Iter(fun stock ->
            let newLastPrice = randomStockLastPrice stock.Price
            stock.LastPrice <- newLastPrice
            stockModel.UpdateBy (fun s -> Some { s with LastPrice = newLastPrice }) stock.Name
        )    

    let startUpdatingLastPrices () =
        async {
            while true do
                do! Async.Sleep 3000 // Update every 3 seconds
                updateLastPrices()
        }
        |> Async.Start

    let StockPage() = 

        startUpdatingLastPrices()

        let totalAsset =
            stockModel.View
            |> View.Map (fun stocks ->
                stocks |> Seq.sumBy (fun stock -> stock.LastPrice * stock.Amount)
            )

        // Event source for live chart updates
        let source = Event<float>()

        // Create a live chart that displays up to 10 points of data
        let liveChart =
            let chart = LiveChart.Line source.Publish 
               
            Renderers.ChartJs.Render(chart, Window = 10, Size = Size(1400, 300))

        let updateChartData () =
            async {
                while true do
                    do! Async.Sleep 3000 // Update every 3 seconds
                    let! totalAssets = totalAsset |> View.GetAsync
                    source.Trigger totalAssets
            }
            |> Async.Start

        updateChartData()

        let totalProfitAndLoss =
            stockModel.View
            |> View.Map (fun stocks ->
                stocks |> Seq.sumBy (fun stock -> (stock.LastPrice * stock.Amount) (* Market value *) - (stock.Price * stock.Amount) (* Cost Basis *))
        )

        IndexTemplate.StockPortfolio()
            .HeaderSPA("Stock Portfolio")
            .stockTableBody(
                stockModel.View.DocSeqCached(fun stock ->
                    let costBasis = stock.Amount * stock.Price
                    let marketValue = stock.LastPrice * stock.Amount
                    let profitAndLoss = marketValue - costBasis
                    let print value = sprintf "%.2f" value

                    
                    IndexTemplate.stockList()
                        .stockName(stock.Name)
                        .stockAmount(print stock.Amount)
                        .stockPrice(print stock.Price)
                        .stockLast(print stock.LastPrice)
                        .color(
                            if profitAndLoss >= 0 then
                                "rgb(0, 210, 0)" // Green color
                            else
                                "red"
                        )
                        .stockProfitAndLoss(print profitAndLoss)
                        .stockCostBasis(print costBasis)
                        .stockMarketValue(print marketValue)
                        .remove(fun _ -> stockModel.RemoveByKey stock.Name)
                        .Doc()
                )
            )
            .stockName(stockName)
            .stockAmount(stockAmount)
            .stockPrice(stockPrice)
            .assetAmount(totalAsset |> View.Map (sprintf "%.2f$"))
            .assetProfitAndLoss(totalProfitAndLoss |> View.Map (sprintf "%.2f$"))
            .assetProfitAndLossPercent(
                let percent = 
                    View.Map2 (fun asset profitAndLoss -> 
                        profitAndLoss * 100.0 / asset
                    ) totalAsset totalProfitAndLoss

                percent |> View.Map (sprintf "%.2f"))
            .colorProfitAndLoss(
                    View.Map ( fun profitAndLoss ->
                        if profitAndLoss >= 0.0 then
                            "rgb(0, 210, 0)" // Green color
                        else
                            "red"
                    ) totalProfitAndLoss
            )
            .add(fun _ ->
                if stockName.Value <> "" && stockAmount.Value > 0.0 && stockPrice.Value > 0.0 then
                    let newStock = { Name = capitalize stockName.Value; Amount = stockAmount.Value; Price = stockPrice.Value; LastPrice = randomStockLastPrice stockPrice.Value }
                    stockModel.Add newStock
                    stockName.Value <- ""
                    stockAmount.Value <- 0.0
                    stockPrice.Value <- 0.0
                else    
                    emptyAlert()
            )
            .assetChart(liveChart)
            .Doc()

[<JavaScript>]
module Client =

    let router = Router.Infer<EndPoint>()
    // Install our client-side router and track the current page
    let currentPage = Router.InstallHash MoneyTracking router


    [<SPAEntryPoint>]
    let Main =
        let renderInnerPage (currentPage: Var<EndPoint>) =
            currentPage.View.Map (fun endpoint ->
                match endpoint with
                | MoneyTracking      -> Pages.MoneyPage()
                | StockPortfolio     -> Pages.StockPage()
            )
            |> Doc.EmbedView

        IndexTemplate()
            .toMoneyPage("/#/")
            .toStockPage("/#/stockPortfolio")
            .PageContent(renderInnerPage currentPage)
            .Bind()
            