using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class ServerDiscoveryService
    {
        private readonly HttpClient _httpClient;

        public ServerDiscoveryService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IReadOnlyList<ServerLogLocation>> DiscoverAsync(string url)
        {
            var json = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var locations = new List<ServerLogLocation>();

            ExtractDatabasePaths(document.RootElement, locations);
            return locations;
        }

        private static void ExtractDatabasePaths(JsonElement element, ICollection<ServerLogLocation> locations)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    string dbPath = null;
                    string group = null;
                    string installation = null;
                    foreach (var property in element.EnumerateObject())
                    {
                        if (string.Equals(property.Name, "db", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                        {
                            dbPath = property.Value.GetString();
                        }

                        if (string.Equals(property.Name, "group", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                        {
                            group = property.Value.GetString();
                        }

                        if (string.Equals(property.Name, "name", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                        {
                            installation = property.Value.GetString();
                        }

                        ExtractDatabasePaths(property.Value, locations);
                    }

                    if (!string.IsNullOrWhiteSpace(dbPath))
                    {
                        locations.Add(new ServerLogLocation
                        {
                            Installation = installation,
                            Group = group,
                            DatabasePath = dbPath,
                            LogFolderPath = DeriveLogFolder(dbPath)
                        });
                    }
                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        ExtractDatabasePaths(item, locations);
                    }
                    break;
            }
        }

        public static string DeriveLogFolder(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("Database path is required.", nameof(databasePath));
            }

            var trimmed = databasePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.EndsWith("\\DB", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("/DB", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(0, trimmed.Length - 3) + "\\DFS\\Logs";
            }

            return Path.Combine(trimmed, "..", "DFS", "Logs");
        }
    }
}
