module Geoblocking.Owin

open System
open System.Net
open Microsoft.Owin
open FSharp.Control.TaskBuilder

type GeoblockingMiddleware(next: OwinMiddleware) =
    inherit OwinMiddleware(next)

    /// GeoBlocking configuration that can be overriden
    let mutable Config = Geoblocking.Common.defaultConfig

    let denyRequest (context: IOwinContext) =
        task {
            context.Response.StatusCode <- int HttpStatusCode.Forbidden
            do! context.Response.WriteAsync("Geo location not allowed")
        }

    let acceptRequest (next: OwinMiddleware, context: IOwinContext) =
        next.Invoke(context)

    override this.Invoke(context: IOwinContext) =

        task {
            let ipAddress = context.Request.RemoteIpAddress.ToString()
            let path = context.Request.Uri.AbsolutePath
            let! isBlocked = Geoblocking.Common.shouldBlock Config ipAddress path
            if isBlocked then
                do! denyRequest context
            else
                do! acceptRequest(next, context)
        }

open System.Runtime.CompilerServices
open Owin

[<Extension>]
type GeoblockingExtensions =

    [<Extension>]
    static member UseGeoblocking(app:IAppBuilder, settings:Geoblocking.Common.GeoConfig) =
        app.Use(fun context next ->
            
            task {
                let ipAddress = context.Request.RemoteIpAddress.ToString()
                let path = context.Request.Uri.AbsolutePath
                let! isBlocked = Geoblocking.Common.shouldBlock settings ipAddress path
                if isBlocked then
                    context.Response.StatusCode <- int HttpStatusCode.Forbidden
                    do! context.Response.WriteAsync("Geo location not allowed")
                else
                    return! next.Invoke()
            }
        )
