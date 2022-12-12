namespace Geoblocking.Core

open System
open System.Net
open Microsoft.AspNetCore.Http

type GeoblockingMiddleware(next: RequestDelegate) =

    /// GeoBlocking configuration that can be overriden
    let mutable Config = Geoblocking.Common.defaultConfig

    member this.Invoke(context: HttpContext) =

        let denyRequest (context: HttpContext) =
            task {
                context.Response.StatusCode <- int HttpStatusCode.Forbidden
                do! context.Response.WriteAsync("Geo location not allowed")
            }

        let acceptRequest (next: RequestDelegate, context: HttpContext) =
            next.Invoke(context)

        task {
            let ipAddress = context.Request.HttpContext.Connection.RemoteIpAddress.ToString()
            let path = if context.Request.Path.HasValue then context.Request.Path.Value else ""
            let! isBlocked = Geoblocking.Common.shouldBlock Config ipAddress path
            if isBlocked then
                do! denyRequest context
            else 
                do! acceptRequest(next, context)
        }
