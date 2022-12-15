namespace GeoblockingMiddleware

open System
open System.Net.Http
open Newtonsoft.Json
open System.Collections.Concurrent

type IpApiModel = { status: string; countryCode: string }

/// Supported services
/// Todo: Add more.
type GeoService =
    /// http://ip-api.com/
    | Ip_Api
    /// https://ipinfo.io/
    | IpInfo of Token: String
    /// Disable using geoblocking for now, return
    | Disabled

type GeoblockStatus =
    { IsBlocked: bool
      LastAccessTime: DateTime }

type GeoPermission =
    | AllowedItems of Whitelist: string list
    | BlockedItems of Blacklist: string list

type GeoConfig =
    {
        /// Countries allowed or denied for the endpoint
        Countries: GeoPermission
        /// URL path for protected or denied endpoint.
        ApiPaths: GeoPermission
        /// Use a concurrent dictionary to cache the results
        UseCache: bool
        /// Clean up cache in X hours. Zero = don't clean up. If you setup this, then recommended value is DNS TTL, e.g.: 36.
        CacheCleanUpHours: float
        /// If not identified, should we let user in or not
        BlockOnError: bool
        /// Timeout after which we detect we couldn't identify the country
        TimeoutMs: int
        /// Used external service provider
        Service: GeoService
    }

module Common =
    let defaultConfig =
        { //Example:
          Countries = AllowedItems [ "GB" ]
          ApiPaths = BlockedItems [ "/mobile-api"; "/oauth" ]
          UseCache = true
          CacheCleanUpHours = 0.
          BlockOnError = true
          TimeoutMs = 5000
          Service = Ip_Api }

    /// Keep in memory a list of the latest ip addresses with their blocking status.
    /// This is public so it can be cleaned with scheduled task outside of this library.
    let lastIpAddressesCache = ConcurrentDictionary<string, GeoblockStatus>()

    let serviceCallIpApi (ipAddress: string) timeout =
        task {
            use httpClient = new HttpClient(Timeout = TimeSpan.FromMilliseconds timeout)

            let! json = httpClient.GetStringAsync($"http://ip-api.com/json/{ipAddress}?fields=status,countryCode")

            let response = JsonConvert.DeserializeObject<IpApiModel>(json)

            if response.status <> "success" then
                // The geo-service returned an error
                return None
            else
                return Some(response.countryCode.ToUpper())
        }

    let serviceCallIpInfo (token: string) (ipAddress: string) timeout =
        task {
            use httpClient = new HttpClient(Timeout = TimeSpan.FromMilliseconds timeout)

            httpClient.DefaultRequestHeaders.Accept.Add(
                System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json")
            )

            try
                let! response = httpClient.GetStringAsync($"https://ipinfo.io/{ipAddress}/country?token={token}")
                return Some response
            with _ ->
                return None
        }

    let shouldBlock config ipAddress (requestEndpointPath: string) =
        task {
            try
                // Check if the endpoint requires geo-blocking based on IP address
                let shouldGeoblock =

                    if String.IsNullOrEmpty requestEndpointPath then
                        match config.ApiPaths with
                        | AllowedItems _ -> true
                        | BlockedItems _ -> false
                    else
                        let pathToCheck = requestEndpointPath.ToUpper()

                        match config.ApiPaths with
                        | BlockedItems bpaths -> bpaths |> Seq.exists (fun path -> pathToCheck.Contains(path.ToUpper()))
                        | AllowedItems apaths ->
                            apaths |> Seq.exists (fun path -> pathToCheck.Contains(path.ToUpper())) |> not

                if not shouldGeoblock then
                    // The endpoint doesn't require geoblocking
                    return false
                else

                if
                    String.IsNullOrEmpty ipAddress
                then
                    // No IP
                    return config.BlockOnError
                else

                    // If we already know this address, we can directly apply the same blocking status
                    match lastIpAddressesCache.TryGetValue(ipAddress) with
                    | true, status when config.UseCache ->
                        let st =
                            lastIpAddressesCache.AddOrUpdate(
                                ipAddress,
                                status,
                                fun _ s -> { s with LastAccessTime = DateTime.UtcNow }
                            )

                        return st.IsBlocked

                    | _ ->
                        // Ask 3rd party to geo-localise the ip address to check if it's in UK or not
                        let! getCountryCode =
                            match config.Service with
                            | Ip_Api -> serviceCallIpApi ipAddress config.TimeoutMs
                            | IpInfo token -> serviceCallIpInfo token ipAddress config.TimeoutMs
                            | Disabled -> // Mostly for simulation use
                                task {
                                    //do! System.Threading.Tasks.Task.Delay 1000
                                    return
                                        if String.IsNullOrEmpty ipAddress then
                                            None
                                        else
                                            Some String.Empty
                                }

                        match getCountryCode with
                        | None ->
                            // The geo-service returned an error
                            return config.BlockOnError
                        | Some code ->
                            let countryCode = code.ToUpper()

                            let countryIsBad =
                                match config.Countries with
                                | BlockedItems countries ->
                                    countries |> List.exists (fun c -> c.ToUpper() = countryCode)
                                | AllowedItems countries ->
                                    countries |> List.exists (fun c -> c.ToUpper() = countryCode) |> not

                            let status =
                                let newStatus =
                                    { IsBlocked = countryIsBad
                                      LastAccessTime = DateTime.UtcNow }

                                if not config.UseCache then
                                    newStatus
                                else
                                    lastIpAddressesCache.AddOrUpdate(ipAddress, newStatus, (fun _ s -> newStatus))

                            return status.IsBlocked
            with _ ->
                // Could not reliably determine if the request is legit. Let it through.
                return config.BlockOnError
        }

    let mutable hasSetupCacellation: System.Threading.CancellationTokenSource option =
        None

    let setupCacheCleanup config =
        // Cancel previous cacheCleanup tasks from memory
        match hasSetupCacellation with
        | Some token ->
            token.Cancel()
            token.Dispose()
        | None -> ()

        // Setup a new task for cleaning the cache
        let newToken = new System.Threading.CancellationTokenSource()
        hasSetupCacellation <- Some newToken

        if (not config.UseCache) || config.CacheCleanUpHours <= 0 then
            ()
        else
            let checkTime, validityTime =
                (1000. * 3600. * config.CacheCleanUpHours) |> int, config.CacheCleanUpHours

            Async.StartAsTask(
                async {
                    // Sleep in the background, waiting for IRQ to wake up
                    do! checkTime |> Async.Sleep
                    let cleanTime = DateTime.UtcNow.AddHours(-1. * validityTime)
                    // Make a copy, to not hit cache-modification during iteration
                    let cachedItems = lastIpAddressesCache |> Seq.toArray

                    cachedItems
                    |> Array.filter (fun c -> c.Value.LastAccessTime < cleanTime)
                    |> Array.iter (fun i ->
                        let _ = lastIpAddressesCache.TryRemove i.Key
                        ())
                },
                cancellationToken = newToken.Token
            )
            |> ignore
