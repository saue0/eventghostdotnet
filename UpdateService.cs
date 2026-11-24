using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using EventGhostDotNet.Core.Updates;

namespace EventGhostDotNet;

internal sealed record UpdateCheckResult(
    UpdateVersion Current,
    UpdateVersion? Latest,
    string? DownloadUrl,
    string? Error)
{
    public bool IsUpdateAvailable => Latest is { } latest && latest.CompareTo(Current) > 0;
}

/// <summary>
/// Vérifie la dernière release publiée sur GitHub. Aucun téléchargement ni remplacement
/// automatique : l'utilisateur ouvre la page de la release et installe la mise à jour.
/// </summary>
internal static class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/saue0/eventghostdotnet/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    public static UpdateVersion CurrentVersion { get; } = GetCurrentVersion();

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "Aucune version publiée n'a été trouvée sur GitHub."
                    : $"Le serveur de mises à jour a répondu {(int)response.StatusCode}.";
                return new UpdateCheckResult(CurrentVersion, null, null, message);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var downloadUrl = root.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (!UpdateVersion.TryParse(tag, out var latest))
            {
                return new UpdateCheckResult(CurrentVersion, null, downloadUrl,
                    $"La dernière version publiée n'est pas reconnue : {tag ?? "(sans étiquette)"}.");
            }

            return new UpdateCheckResult(CurrentVersion, latest, downloadUrl, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or
                                           JsonException or IOException or InvalidOperationException)
        {
            return new UpdateCheckResult(CurrentVersion, null, null,
                "Impossible de contacter le serveur de mises à jour. Vérifiez votre connexion.");
        }
    }

    private static UpdateVersion GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null
            ? new UpdateVersion(0, 0, 0)
            : new UpdateVersion(Math.Max(0, version.Major), Math.Max(0, version.Minor),
                Math.Max(0, version.Build));
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EventGhostDotNet-UpdateCheck/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
