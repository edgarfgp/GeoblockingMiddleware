namespace GeoblockingMiddleware

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open Microsoft.Owin
open Newtonsoft.Json
open FSharp.Control.TaskBuilder

module Geoblocking =
    type IpApiModel =
        { status: string
          countryCode: string }

    type GeoblockStatus =
        { IsBlocked: bool
          LastAccessTime: DateTime }
///
    type GeoblockingMiddleware(next: OwinMiddleware) =
        inherit OwinMiddleware(next)

        let [<Literal>] MAX_IP_IN_CACHE = 100

        // Keep in memory a list of the latest ip addresses with their blocking status
        static let lastIpAddresses = Dictionary<string, GeoblockStatus>()

        override this.Invoke(context: IOwinContext) =
            let updateMemoryCache (ipAddress: string) (status: GeoblockStatus) =
                if lastIpAddresses.ContainsKey(ipAddress) then
                    lastIpAddresses[ipAddress] <- status

                else
                    if lastIpAddresses.Count > MAX_IP_IN_CACHE then
                        let oldestIp = lastIpAddresses |> Seq.sortBy(fun kvp -> kvp.Value.LastAccessTime) |> Seq.head
                        lastIpAddresses.Remove(oldestIp.Key) |> ignore

                    lastIpAddresses.Add(ipAddress, status)

            let denyRequest (context: IOwinContext) =
                task {
                    context.Response.StatusCode <- int HttpStatusCode.Forbidden
                    do! context.Response.WriteAsync("Only requests originating from UK are allowed")
                }

            let acceptRequest (next: OwinMiddleware, context: IOwinContext) =
                next.Invoke(context)

            task {
                try
                    // Check if the endpoint requires geo-blocking based on IP address
                    let shouldGeoblock =
                        let isMobileAPI = context.Request.Uri.AbsolutePath.Contains("/mobileapi/")
                        let isOAuth = context.Request.Uri.AbsolutePath.Contains("/oauth/")
                        isMobileAPI || isOAuth

                    if shouldGeoblock then
                        let ipAddress = context.Request.RemoteIpAddress.ToString()

                        // If we already know this address, we can directly apply the same blocking status
                        match lastIpAddresses.TryGetValue(ipAddress) with
                        | true, status ->
                            updateMemoryCache ipAddress { status with LastAccessTime = DateTime.UtcNow }

                            if status.IsBlocked then
                                do! denyRequest context
                            else
                                do! acceptRequest(next, context)

                        | false, _ ->
                            // Ask 3rd party to geo-localise the ip address to check if it's in UK or not
                            use httpClient = new HttpClient()
                            httpClient.Timeout <- TimeSpan.FromMilliseconds(1000)
                            let! json = httpClient.GetStringAsync($"http://ip-api.com/json/{ipAddress}?fields=status,countryCode")
                            let response = JsonConvert.DeserializeObject<IpApiModel>(json)

                            if response.status <> "success" then
                                // The geo-service returned an error so we allow the request to go through for now
                                do! acceptRequest(next, context)

                            elif response.countryCode <> "GB" then
                                updateMemoryCache ipAddress { IsBlocked = true; LastAccessTime = DateTime.UtcNow }
                                do! denyRequest context

                            else
                                updateMemoryCache ipAddress { IsBlocked = false; LastAccessTime = DateTime.UtcNow }
                                do! acceptRequest(next, context)
                    else
                        // The endpoint doesn't require geoblocking
                        do! acceptRequest(next, context)
                with
                | _ ->
                    // Could not reliably determine if the request is legit. Let it through.
                    do! acceptRequest(next, context)
            }
            

module Say =
    let hello name =
#if ASPNETCORE 
        printfn "ASPNETCORE %s" name
#else
        Console.WriteLine("Hello {0}", name)  
#endif
