using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Models;
using MediaBrowser.Common.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Controllers;

/// <summary>
/// Server-side bridge from Jellyfin to Jellyseerr.
/// </summary>
[ApiController]
[Authorize]
[Route("JellyseerrBridge")]
public sealed class JellyseerrBridgeController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JellyseerrBridgeController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrBridgeController"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's shared HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public JellyseerrBridgeController(IHttpClientFactory httpClientFactory, ILogger<JellyseerrBridgeController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets bridge status for signed-in users. It says whether the bridge is set up, but
    /// not where Jellyseerr lives, since this server may be reachable from the internet.
    /// </summary>
    /// <returns>Bridge status.</returns>
    [HttpGet("Status")]
    public ActionResult<BridgeStatus> Status()
    {
        var config = GetConfig();
        return new BridgeStatus
        {
            IsConfigured = TryCreateBaseUri(config) is not null && !string.IsNullOrWhiteSpace(config.JellyseerrApiKey),
            RequestSubmissionEnabled = config.EnableRequestSubmission
        };
    }

    /// <summary>
    /// Checks that Jellyseerr can be reached and that the API key works. Used by the
    /// Test Connection button on the settings page, so it's limited to administrators.
    /// </summary>
    /// <param name="request">The URL and key to test. Blank values fall back to the saved settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the connection worked, with a message to show the admin.</returns>
    [HttpPost("TestConnection")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<ActionResult<ConnectionTestResult>> TestConnection([FromBody] ConnectionTestRequest request, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? config.JellyseerrBaseUrl : request.BaseUrl.Trim();
        var apiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? config.JellyseerrApiKey : request.ApiKey.Trim();

        var baseUri = TryCreateBaseUri(baseUrl);
        if (baseUri is null)
        {
            return TestFailed("Enter a full URL starting with http:// or https://, for example http://jellyseerr:5055.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return TestFailed("Enter the Jellyseerr API key. You can find it in Jellyseerr under Settings, General.");
        }

        using var client = CreateClient(baseUri, apiKey);

        string? version;
        try
        {
            using var statusResponse = await client.GetAsync("/api/v1/status", cancellationToken).ConfigureAwait(false);
            if (!statusResponse.IsSuccessStatusCode)
            {
                return TestFailed($"Something answered at {baseUri.Authority}, but not like Jellyseerr does (HTTP {(int)statusResponse.StatusCode}). Check the URL and port.");
            }

            version = await ReadVersionAsync(statusResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return TestFailed(UnreachableMessage(baseUri, ex));
        }

        var jellyseerrName = version is null ? "Jellyseerr" : $"Jellyseerr {version}";
        try
        {
            using var meResponse = await client.GetAsync("/api/v1/auth/me", cancellationToken).ConfigureAwait(false);
            if (meResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return TestFailed($"Reached {jellyseerrName}, but it rejected the API key. Copy the key again from Jellyseerr under Settings, General.");
            }

            if (!meResponse.IsSuccessStatusCode)
            {
                return TestFailed($"Reached {jellyseerrName}, but checking the API key failed with HTTP {(int)meResponse.StatusCode}.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return TestFailed($"Reached {jellyseerrName}, but the connection dropped while checking the API key: {ex.Message}");
        }

        return new ConnectionTestResult
        {
            Success = true,
            Message = $"Connected to {jellyseerrName} and the API key works. Remember to save."
        };
    }

    private static ConnectionTestResult TestFailed(string message)
    {
        return new ConnectionTestResult { Success = false, Message = message };
    }

    private static string UnreachableMessage(Uri baseUri, Exception ex)
    {
        var message = $"Couldn't reach Jellyseerr at {baseUri.Authority}: {ex.Message}";
        if (baseUri.IsLoopback)
        {
            // The mistake that's easiest to make: in Docker, localhost is the Jellyfin container itself.
            message += " If Jellyfin runs in Docker, localhost means the Jellyfin container itself, not your server. Use the Jellyseerr container name instead, for example http://jellyseerr:5055.";
        }

        return message;
    }

    private static async Task<string?> ReadVersionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ReadString(document.RootElement, "version") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Serves the standalone request page.
    /// </summary>
    /// <returns>HTML page.</returns>
    [HttpGet("Page")]
    [AllowAnonymous]
    public IActionResult Page()
    {
        return EmbeddedFile("Web.request-page.html", "text/html; charset=utf-8");
    }

    /// <summary>
    /// Serves plugin web assets.
    /// </summary>
    /// <param name="assetName">Asset name.</param>
    /// <returns>Asset response.</returns>
    [HttpGet("Assets/{assetName}")]
    [AllowAnonymous]
    public IActionResult Asset(string assetName)
    {
        var resourceName = assetName switch
        {
            "request-page.js" => "Web.request-page.js",
            "request-entry.js" => "Web.request-entry.js",
            _ => null
        };

        return resourceName is null
            ? NotFound()
            : EmbeddedFile(resourceName, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Proxies a Jellyseerr search request.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="page">Page number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpGet("Search")]
    public Task<IActionResult> Search([FromQuery] string query, [FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IActionResult>(BadRequest(new { message = "Query is required." }));
        }

        return ForwardGetAsync("/api/v1/search", new Dictionary<string, string?>
        {
            ["query"] = query,
            ["page"] = Math.Max(page, 1).ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    /// <summary>
    /// Proxies an allow-listed Jellyseerr discover category.
    /// </summary>
    /// <param name="category">Category ID.</param>
    /// <param name="page">Page number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpGet("Discover/{category}")]
    public Task<IActionResult> Discover(string category, [FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        var endpoint = category.ToLowerInvariant() switch
        {
            "trending" => "/api/v1/discover/trending",
            "movies" => "/api/v1/discover/movies",
            "upcoming-movies" => "/api/v1/discover/movies/upcoming",
            "tv" or "shows" => "/api/v1/discover/tv",
            "upcoming-tv" or "upcoming-shows" => "/api/v1/discover/tv/upcoming",
            _ => null
        };

        if (endpoint is null)
        {
            return Task.FromResult<IActionResult>(BadRequest(new { message = "Unknown discover category." }));
        }

        var query = new Dictionary<string, string?>
        {
            ["page"] = Math.Max(page, 1).ToString(CultureInfo.InvariantCulture)
        };

        if (!endpoint.Equals("/api/v1/discover/trending", StringComparison.Ordinal))
        {
            AddAllowedDiscoverFilters(query);
        }

        return ForwardGetAsync(endpoint, query, cancellationToken);
    }

    private void AddAllowedDiscoverFilters(IDictionary<string, string?> query)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sortBy",
            "primaryReleaseDateGte",
            "primaryReleaseDateLte",
            "firstAirDateGte",
            "firstAirDateLte",
            "voteAverageGte",
            "voteAverageLte",
            "watchRegion",
            "watchProviders"
        };

        foreach (var pair in ControllerContext.HttpContext.Request.Query)
        {
            if (!allowed.Contains(pair.Key))
            {
                continue;
            }

            var value = pair.Value.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                query[pair.Key] = value;
            }
        }
    }

    /// <summary>
    /// Proxies Jellyseerr movie details.
    /// </summary>
    /// <param name="tmdbId">TMDB movie ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpGet("Movie/{tmdbId:int}")]
    public Task<IActionResult> Movie(int tmdbId, CancellationToken cancellationToken)
    {
        return ForwardGetAsync($"/api/v1/movie/{tmdbId}", new Dictionary<string, string?>(), cancellationToken);
    }

    /// <summary>
    /// Proxies Jellyseerr TV details.
    /// </summary>
    /// <param name="tmdbId">TMDB TV ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpGet("Tv/{tmdbId:int}")]
    public Task<IActionResult> Tv(int tmdbId, CancellationToken cancellationToken)
    {
        return ForwardGetAsync($"/api/v1/tv/{tmdbId}", new Dictionary<string, string?>(), cancellationToken);
    }

    /// <summary>
    /// Proxies Rotten Tomatoes and IMDb ratings for a movie.
    /// </summary>
    /// <param name="tmdbId">TMDB movie ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpGet("Movie/{tmdbId:int}/Ratings")]
    public async Task<IActionResult> MovieRatings(int tmdbId, CancellationToken cancellationToken)
    {
        // The combined endpoint has both Rotten Tomatoes and IMDb. Older Jellyseerr versions
        // don't have it, so fall back to the Rotten Tomatoes only one.
        var combined = await ForwardGetAsync($"/api/v1/movie/{tmdbId}/ratingscombined", new Dictionary<string, string?>(), cancellationToken).ConfigureAwait(false);
        if (combined is ContentResult { StatusCode: StatusCodes.Status404NotFound })
        {
            return await ForwardGetAsync($"/api/v1/movie/{tmdbId}/ratings", new Dictionary<string, string?>(), cancellationToken).ConfigureAwait(false);
        }

        return combined;
    }

    /// <summary>
    /// Proxies Rotten Tomatoes ratings for a show.
    /// </summary>
    /// <param name="tmdbId">TMDB TV ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpGet("Tv/{tmdbId:int}/Ratings")]
    public Task<IActionResult> TvRatings(int tmdbId, CancellationToken cancellationToken)
    {
        return ForwardGetAsync($"/api/v1/tv/{tmdbId}/ratings", new Dictionary<string, string?>(), cancellationToken);
    }

    /// <summary>
    /// Proxies titles similar to a movie.
    /// </summary>
    /// <param name="tmdbId">TMDB movie ID.</param>
    /// <param name="page">Page number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpGet("Movie/{tmdbId:int}/Recommendations")]
    public Task<IActionResult> MovieRecommendations(int tmdbId, [FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        return ForwardGetAsync($"/api/v1/movie/{tmdbId}/recommendations", PageQuery(page), cancellationToken);
    }

    /// <summary>
    /// Proxies titles similar to a show.
    /// </summary>
    /// <param name="tmdbId">TMDB TV ID.</param>
    /// <param name="page">Page number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpGet("Tv/{tmdbId:int}/Recommendations")]
    public Task<IActionResult> TvRecommendations(int tmdbId, [FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        return ForwardGetAsync($"/api/v1/tv/{tmdbId}/recommendations", PageQuery(page), cancellationToken);
    }

    /// <summary>
    /// Proxies a movie collection, such as every film in a series, with each film's status.
    /// </summary>
    /// <param name="collectionId">TMDB collection ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpGet("Collection/{collectionId:int}")]
    public Task<IActionResult> Collection(int collectionId, CancellationToken cancellationToken)
    {
        return ForwardGetAsync($"/api/v1/collection/{collectionId}", new Dictionary<string, string?>(), cancellationToken);
    }

    private static Dictionary<string, string?> PageQuery(int page)
    {
        return new Dictionary<string, string?>
        {
            ["page"] = Math.Max(page, 1).ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Submits a sanitized request to Jellyseerr.
    /// </summary>
    /// <param name="request">Request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Jellyseerr response.</returns>
    [HttpPost("Request")]
    public async Task<IActionResult> SubmitRequest([FromBody] BridgeRequest request, CancellationToken cancellationToken)
    {
        var config = GetConfig();
        if (!config.EnableRequestSubmission)
        {
            return StatusCode(403, new { message = "Request submission is disabled by the Jellyfin administrator." });
        }

        if (!IsUserAllowed(config))
        {
            return Forbid();
        }

        var validation = ValidateRequest(request, config);
        if (validation is not null)
        {
            return validation;
        }

        var payload = await BuildJellyseerrRequestPayloadAsync(request, cancellationToken).ConfigureAwait(false);
        return await ForwardPostAsync("/api/v1/request", payload, cancellationToken).ConfigureAwait(false);
    }

    private static PluginConfiguration GetConfig()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private static Uri? TryCreateBaseUri(PluginConfiguration config)
    {
        return TryCreateBaseUri(config.JellyseerrBaseUrl);
    }

    private static Uri? TryCreateBaseUri(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return uri;
    }

    private HttpClient CreateClient()
    {
        var config = GetConfig();
        var baseUri = TryCreateBaseUri(config) ?? throw new InvalidOperationException("Jellyseerr base URL is not configured.");
        if (string.IsNullOrWhiteSpace(config.JellyseerrApiKey))
        {
            throw new InvalidOperationException("Jellyseerr API key is not configured.");
        }

        return CreateClient(baseUri, config.JellyseerrApiKey);
    }

    private HttpClient CreateClient(Uri baseUri, string apiKey)
    {
        // The factory hands out a fresh HttpClient each time but reuses the network
        // connections underneath, so making one per call no longer wastes sockets.
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        client.BaseAddress = baseUri;
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<IActionResult> ForwardGetAsync(string path, IReadOnlyDictionary<string, string?> query, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var url = BuildRelativeUrl(path, query);
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            return await ProxyResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }

    private async Task<IActionResult> ForwardPostAsync(string path, JsonElement payload, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            using var content = new StringContent(payload.GetRawText(), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(path, content, cancellationToken).ConfigureAwait(false);
            return await ProxyResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }

    private static async Task<IActionResult> ProxyResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            Content = body,
            ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
        };
    }

    private static string BuildRelativeUrl(string path, IReadOnlyDictionary<string, string?> query)
    {
        if (query.Count == 0)
        {
            return path;
        }

        var queryString = string.Join(
            "&",
            query.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        return string.IsNullOrEmpty(queryString) ? path : $"{path}?{queryString}";
    }

    private IActionResult? ValidateRequest(BridgeRequest request, PluginConfiguration config)
    {
        var mediaType = request.MediaType.ToLowerInvariant();
        if (mediaType is not ("movie" or "tv"))
        {
            return BadRequest(new { message = "mediaType must be movie or tv." });
        }

        if (request.MediaId <= 0)
        {
            return BadRequest(new { message = "mediaId must be a positive TMDB ID." });
        }

        if (request.Is4K && !config.Enable4KRequests)
        {
            return StatusCode(403, new { message = "4K requests are disabled." });
        }

        if (mediaType == "movie")
        {
            return null;
        }

        if (request.Seasons is null)
        {
            return null;
        }

        var seasons = request.Seasons.Value;
        if (seasons.ValueKind == JsonValueKind.String && seasons.GetString() == "all")
        {
            return null;
        }

        if (seasons.ValueKind == JsonValueKind.Array && seasons.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var season) && season >= 0))
        {
            return null;
        }

        return BadRequest(new { message = "TV seasons must be omitted, \"all\", or an array of non-negative season numbers." });
    }

    private bool IsUserAllowed(PluginConfiguration config)
    {
        if (config.AllowedUserIds.Length == 0)
        {
            return true;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("jellyfin:userId")
            ?? User.FindFirstValue("Jellyfin-UserId");
        return userId is not null && config.AllowedUserIds.Contains(userId, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<JsonElement> BuildJellyseerrRequestPayloadAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        var mediaType = request.MediaType.ToLowerInvariant() == "tv" ? "tv" : "movie";
        var payload = new Dictionary<string, object?>
        {
            ["mediaType"] = mediaType,
            ["mediaId"] = request.MediaId,
            ["is4k"] = request.Is4K
        };

        if (mediaType == "tv")
        {
            payload["seasons"] = request.Seasons is null ? "all" : JsonSerializer.Deserialize<object>(request.Seasons.Value.GetRawText(), JsonOptions);
        }

        var defaults = await GetServiceDefaultsAsync(mediaType, request.Is4K, cancellationToken).ConfigureAwait(false);
        if (defaults is not null)
        {
            if (defaults.ServerId is not null)
            {
                payload["serverId"] = defaults.ServerId;
            }

            if (defaults.ProfileId is not null)
            {
                payload["profileId"] = defaults.ProfileId;
            }

            if (!string.IsNullOrWhiteSpace(defaults.RootFolder))
            {
                payload["rootFolder"] = defaults.RootFolder;
            }

            if (defaults.Tags is not null)
            {
                payload["tags"] = JsonSerializer.Deserialize<object>(defaults.Tags.Value.GetRawText(), JsonOptions);
            }

            if (mediaType == "tv" && defaults.LanguageProfileId is not null)
            {
                payload["languageProfileId"] = defaults.LanguageProfileId;
            }
        }

        return JsonSerializer.SerializeToElement(payload, JsonOptions);
    }

    private async Task<ServiceDefaults?> GetServiceDefaultsAsync(string mediaType, bool is4K, CancellationToken cancellationToken)
    {
        // These defaults are a nice-to-have. If Jellyseerr's answer is missing or in a shape we
        // don't expect, send the request without them and let Jellyseerr use its own defaults.
        try
        {
            return await ReadServiceDefaultsAsync(mediaType, is4K, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Couldn't read the default {MediaType} server from Jellyseerr; sending the request without it", mediaType);
            return null;
        }
    }

    private async Task<ServiceDefaults?> ReadServiceDefaultsAsync(string mediaType, bool is4K, CancellationToken cancellationToken)
    {
        var endpoint = mediaType == "movie" ? "/api/v1/settings/radarr" : "/api/v1/settings/sonarr";
        using var client = CreateClient();
        using var response = await client.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var server in document.RootElement.EnumerateArray())
        {
            if (server.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var serverIs4K = ReadBool(server, "is4k");
            var isDefault = ReadBool(server, "isDefault");
            if (serverIs4K != is4K || !isDefault)
            {
                continue;
            }

            return new ServiceDefaults
            {
                ServerId = ReadInt(server, "id"),
                ProfileId = ReadInt(server, "activeProfileId"),
                RootFolder = ReadString(server, "activeDirectory"),
                Tags = server.TryGetProperty("tags", out var tags) ? tags.Clone() : null,
                LanguageProfileId = ReadInt(server, "activeLanguageProfileId")
            };
        }

        return null;
    }

    private static bool ReadBool(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var result) ? result : null;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private IActionResult EmbeddedFile(string resourceSuffix, string contentType)
    {
        Response.Headers.CacheControl = "no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        var assembly = typeof(JellyseerrBridgeController).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.Ordinal));
        if (resource is null)
        {
            return NotFound();
        }

        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        return Content(reader.ReadToEnd(), contentType);
    }
}
