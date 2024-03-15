namespace GeoblockingMiddleware.FrameworkTest


open System
open System.Threading
open System.Threading.Tasks
open GeoblockingMiddleware
open Owin
open Xunit
open System
open FsUnitTyped

type MyStartup() =
    member __.Configuration(app:Owin.IAppBuilder) =
        app.UseGeoblocking(Common.defaultConfig) |> ignore
        ()

module GeoTestClass =

    [<Fact>]
    let ``Should be blocked: ApiPaths hit blocked (net48)`` () =
        task {
            let config =
                { GeoblockingMiddleware.Common.defaultConfig with
                    //Example:
                    Countries = AllowedItems [ "XX" ]
                    ApiPaths = BlockedItems [ "/mypath"; "/test2" ]
                    Service = Disabled }

            let! actualResult = Common.shouldBlock config "0.0.0.1" "http://myserver/mypath/thing.aspx"
            actualResult |> shouldEqual true
        }
        :> Task

    [<Fact(Skip = "this does actual http-call")>]
    let ``Should be calling IpApi`` () =
        task {
            let! res = GeoblockingMiddleware.Common.serviceCallIpApi "157.24.0.105" 3000
            match res with
            | None -> failwith "Country not gotten"
            | Some c -> 
                String.IsNullOrEmpty c |> shouldEqual false
        }


