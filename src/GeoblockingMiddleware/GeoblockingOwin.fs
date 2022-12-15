namespace GeoblockingMiddleware

open GeoblockingMiddleware
open System.Net
open Microsoft.Owin
open FSharp.Control.TaskBuilder

type GeoblockingMiddleware(next: OwinMiddleware, settings:GeoConfig) =
    inherit OwinMiddleware(next)

    /// GeoBlocking configuration that can be overriden
    let mutable Config = settings
    do Common.setupCacheCleanup settings

    let denyRequest (context: IOwinContext) =
        task {
            context.Response.StatusCode <- int HttpStatusCode.Forbidden
            do! context.Response.WriteAsync("Geo location not allowed")
        }

    let acceptRequest (next: OwinMiddleware, context: IOwinContext) = next.Invoke context
    
    new(next) = GeoblockingMiddleware(next, Common.defaultConfig)

    override this.Invoke(context: IOwinContext) =

        task {
            let ipAddress = context.Request.RemoteIpAddress.ToString()

            let path = context.Request.Uri.AbsolutePath
            let! isBlocked = Common.shouldBlock Config ipAddress path

            if isBlocked then
                do! denyRequest context
            else
                do! acceptRequest (next, context)
        }

    member this.ReInitCacheCleaning() =
        Common.setupCacheCleanup Config

open System.Runtime.CompilerServices
open Owin

[<Extension>]
type GeoblockingExtensions =

    [<Extension>]
    static member UseGeoblocking(app: IAppBuilder, settings: GeoConfig) =
        Common.setupCacheCleanup settings

        app.Use(fun context next ->

            task {
                let ipAddress = context.Request.RemoteIpAddress.ToString()

                let path = context.Request.Uri.AbsolutePath
                let! isBlocked = Common.shouldBlock settings ipAddress path

                if isBlocked then
                    context.Response.StatusCode <- int HttpStatusCode.Forbidden
                    do! context.Response.WriteAsync("Geo location not allowed")
                else
                    return! next.Invoke()
            })
