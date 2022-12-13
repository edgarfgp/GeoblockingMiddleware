namespace GeoblockingMiddleware.Tests

open System
open System.Threading
open System.Threading.Tasks
open GeoblockingMiddleware
open Xunit
open FsUnitTyped

module GeoTestClass =

    let rnd = Random()

    [<Fact>]
    let ``Should be blocked: ApiPaths hit blocked`` () =
        task {
            let config =
                { //Example:
                  Countries = AllowedItems [ "XX" ]
                  ApiPaths = BlockedItems [ "/mypath"; "/test2" ]
                  UseCache = true
                  BlockOnError = true
                  TimeoutMs = 1000
                  Service = Disabled }

            let! actualResult = Common.shouldBlock config "0.0.0.1" "http://myserver/mypath/thing.aspx"
            actualResult |> shouldEqual true
        }
        :> Task

    [<Fact>]
    let ``Should allow: ApiPaths hit allow`` () =
        task {
            let config =
                { //Example:
                  Countries = AllowedItems [ "XX" ]
                  ApiPaths = AllowedItems [ "/mypath"; "/test2" ]
                  UseCache = true
                  BlockOnError = true
                  TimeoutMs = 1000
                  Service = Disabled }

            let! actualResult = Common.shouldBlock config "0.0.0.2" "http://myserver/mypath/thing.aspx"
            actualResult |> shouldEqual false
        }
        :> Task

    [<Fact>]
    let ``Should allow: Country hit allow`` () =
        task {
            let config =
                { //Example:
                  Countries = BlockedItems [ "XX" ]
                  ApiPaths = BlockedItems [ "/mypath"; "/test2" ]
                  UseCache = true
                  BlockOnError = true
                  TimeoutMs = 1000
                  Service = Disabled }

            let! actualResult = Common.shouldBlock config "0.0.0.3" "http://myserver/mypath/thing.aspx"
            actualResult |> shouldEqual false
        }
        :> Task

    [<Fact>]
    let ``Should be blocked: IP failed to get and config block on error`` () =
        task {
            let config =
                { //Example:
                  Countries = AllowedItems [ "XX" ]
                  ApiPaths = AllowedItems [ "/test"; "/test2" ]
                  UseCache = false
                  BlockOnError = true
                  TimeoutMs = 1000
                  Service = Disabled }

            let! actualResult = Common.shouldBlock config null "http://myserver/mypath/thing.aspx"
            actualResult |> shouldEqual true
        }
        :> Task

    [<Fact>]
    let ``Should allow: IP failed to get and loose config`` () =
        task {
            let config =
                { //Example:
                  Countries = AllowedItems [ "XX" ]
                  ApiPaths = AllowedItems [ "/test"; "/test2" ]
                  UseCache = false
                  BlockOnError = false
                  TimeoutMs = 1000
                  Service = Disabled }

            let! actualResult = Common.shouldBlock config null "http://myserver/mypath/thing.aspx"
            actualResult |> shouldEqual false
        }
        :> Task

    [<Fact>]
    let ``Should allow: Country is not blocked`` () =
        task {
            let config =
                { //Example:
                  Countries = BlockedItems [ "XX" ]
                  ApiPaths = AllowedItems [ "/mypath" ]
                  UseCache = true
                  BlockOnError = true
                  TimeoutMs = 1000
                  Service = Disabled }

            let! actualResult = Common.shouldBlock config "::1" "http://myserver/mypath/thing.aspx"

            // Allow by not-blocked-country
            actualResult |> shouldEqual false
        }
        :> Task

    [<Fact>]
    let ``Multiple users`` () =

        let config =
            { //Example:
              Countries = AllowedItems [ "US" ]
              ApiPaths = BlockedItems [ "/test"; "/test2" ]
              UseCache = true
              BlockOnError = true
              TimeoutMs = 1000
              Service = Disabled }

        let _ =
            [ 1u .. 100u ]
            |> Seq.map (fun _ ->
                let rndIp () =
                    (".",
                     Array.unfold
                         (fun x ->
                             if x < 4 then
                                 Some(rnd.Next(0, 255).ToString(), x + 1)
                             else
                                 None)
                         0)
                    |> String.Join

                let t1 =
                    Tasks.Task.Run(fun () ->
                        let _ = Common.shouldBlock config (rndIp ())
                        Console.WriteLine Thread.CurrentThread.ManagedThreadId)

                let t2 =
                    Tasks.Task.Run(fun () ->
                        let ip = rndIp ()
                        let _ = Common.shouldBlock config ip
                        System.Threading.Thread.Sleep 100
                        let _ = Common.shouldBlock config ip
                        Console.WriteLine Thread.CurrentThread.ManagedThreadId)

                Tasks.Task.WhenAll [| t1; t2 |]

            )
            |> Seq.toArray
            |> Tasks.Task.WaitAll

        // Let's just hope we got this far
        shouldEqual true

    [<Fact>]
    let ``Cache should work`` () =
        task {
            let config =
                { //Example:
                  Countries = AllowedItems [ "XX" ]
                  ApiPaths = BlockedItems [ "/mypath" ]
                  UseCache = true
                  BlockOnError = true
                  TimeoutMs = 1000
                  Service = Disabled }

            let ip = "12.34.56.78"
            let! actualResult = Common.shouldBlock config ip "http://myserver/mypath/thing.aspx"
            // This is declined country

            actualResult |> shouldEqual true

            let cachedIp = Common.lastIpAddressesCache.ContainsKey ip
            cachedIp |> shouldEqual true

            Common.lastIpAddressesCache.[ip].IsBlocked |> shouldEqual true

        }
        :> Task
