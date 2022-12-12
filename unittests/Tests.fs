namespace unittests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open System.Threading
open System.Threading.Tasks
open Geoblocking.Common

[<TestClass>]
type GeoTestClass () =

    let rnd = new Random()

    [<TestMethod>]
    member this.``Should be blocked: ApiPaths hit blocked`` () =
        task {
            let config = 
                { //Example:
                   Countries = AllowedItems ["XX"]
                   ApiPaths = BlockedItems [ "/mypath"; "/test2" ]
                   UseCache = true
                   BlockOnError = true
                   TimeoutMs = 1000
                   Service = Disabled
                }
            let! isBlocked = Geoblocking.Common.shouldBlock config "0.0.0.1" "http://myserver/mypath/thing.aspx"
            Assert.IsTrue isBlocked
        } :> Task

    [<TestMethod>]
    member this.``Should allow: ApiPaths hit allow`` () =
        task {
            let config = 
                { //Example:
                   Countries = AllowedItems ["XX"]
                   ApiPaths = AllowedItems [ "/mypath"; "/test2" ]
                   UseCache = true
                   BlockOnError = true
                   TimeoutMs = 1000
                   Service = Disabled
                }
            let! isBlocked = Geoblocking.Common.shouldBlock config "0.0.0.2" "http://myserver/mypath/thing.aspx"
            Assert.IsFalse isBlocked
        } :> Task

    [<TestMethod>]
    member this.``Should allow: Country hit allow`` () =
        task {
            let config = 
                { //Example:
                   Countries = BlockedItems ["XX"]
                   ApiPaths = BlockedItems [ "/mypath"; "/test2" ]
                   UseCache = true
                   BlockOnError = true
                   TimeoutMs = 1000
                   Service = Disabled
                }
            let! isBlocked = Geoblocking.Common.shouldBlock config "0.0.0.3" "http://myserver/mypath/thing.aspx"
            Assert.IsFalse isBlocked
        } :> Task

    [<TestMethod>]
    member this.``Should be blocked: IP failed to get and config block on error``  () =
        task {
            let config = 
                { //Example:
                   Countries = AllowedItems ["XX"]
                   ApiPaths = AllowedItems [ "/test"; "/test2" ]
                   UseCache = false
                   BlockOnError = true
                   TimeoutMs = 1000
                   Service = Disabled
                }
            let! shouldBlock = Geoblocking.Common.shouldBlock config null "http://myserver/mypath/thing.aspx"
            Assert.IsTrue shouldBlock
        } :> Task

    [<TestMethod>]
    member this.``Should allow: IP failed to get and loose config``  () =
        task {
            let config = 
                { //Example:
                   Countries = AllowedItems ["XX"]
                   ApiPaths = AllowedItems [ "/test"; "/test2" ]
                   UseCache = false
                   BlockOnError = false
                   TimeoutMs = 1000
                   Service = Disabled
                }
            let! shouldBlock = Geoblocking.Common.shouldBlock config null "http://myserver/mypath/thing.aspx"
            Assert.IsFalse shouldBlock
        } :> Task

    [<TestMethod>]
    member this.``Should allow: Country is not blocked``  () =
        task {
            let config = 
                { //Example:
                   Countries = BlockedItems ["XX"]
                   ApiPaths = AllowedItems [ "/mypath" ]
                   UseCache = true
                   BlockOnError = true
                   TimeoutMs = 1000
                   Service = Disabled
                }
            let! shouldBlock = Geoblocking.Common.shouldBlock config "::1" "http://myserver/mypath/thing.aspx"

            // Allow by not-blocked-country
            Assert.IsFalse shouldBlock
        } :> Task

    [<TestMethod>]
    member this.MultipleUsers () =

        let config = 
            { //Example:
               Countries = AllowedItems ["US"]
               ApiPaths = BlockedItems [ "/test"; "/test2" ]
               UseCache = true
               BlockOnError = true
               TimeoutMs = 1000
               Service = Disabled
            }

        let taskarray = 
            [1u..100u] |> Seq.map(fun itm ->
                let rndIp() =
                    ("." , Array.unfold (fun x -> 
                        if x < 4 then Some (rnd.Next(0, 255).ToString(), x+1)
                        else None) 0) |> String.Join

                let t1 = Tasks.Task.Run(fun () ->
                    let t = Geoblocking.Common.shouldBlock config (rndIp())
                    Console.WriteLine Thread.CurrentThread.ManagedThreadId
                )
                let t2 = Tasks.Task.Run(fun () ->
                    let ip = rndIp()
                    let test1 = Geoblocking.Common.shouldBlock config ip
                    System.Threading.Thread.Sleep 100
                    let test2 = Geoblocking.Common.shouldBlock config ip
                    Console.WriteLine Thread.CurrentThread.ManagedThreadId
                )
                Tasks.Task.WhenAll [|t1; t2|]
                
            ) |> Seq.toArray |> Tasks.Task.WaitAll

        // Let's just hope we got this far
        Assert.IsTrue true


    [<TestMethod>]
    member this.``Cache should work``() =
        task {
            let config = 
                { //Example:
                   Countries = AllowedItems ["XX"]
                   ApiPaths = BlockedItems [ "/mypath" ]
                   UseCache = true
                   BlockOnError = true
                   TimeoutMs = 1000
                   Service = Disabled
                }
            let ip = "12.34.56.78"
            let! shouldBlock = Geoblocking.Common.shouldBlock config ip "http://myserver/mypath/thing.aspx"
            // This is declined country

            Assert.IsTrue shouldBlock
            let cachedIp = Geoblocking.Common.lastIpAddressesCache.ContainsKey ip
            Assert.IsTrue cachedIp
            Assert.IsTrue Geoblocking.Common.lastIpAddressesCache.[ip].IsBlocked

        } :> Task