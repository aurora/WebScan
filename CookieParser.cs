// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Harald Lapp

using Microsoft.Playwright;

namespace WebScan;

/// <summary>
/// Parses Netscape/Mozilla cookie files (cookies.txt format).
/// Line format: domain\tincludeSubdomains\tpath\tsecure\texpiry\tname\tvalue
/// Lines prefixed with #HttpOnly_ are recognized as HttpOnly cookies (curl/Cookie-Editor convention).
/// </summary>
public static class CookieParser
{
    public static IReadOnlyList<Cookie> ParseNetscapeCookieFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Cookie file not found: {filePath}");

        var cookies = new List<Cookie>();
        var lines = File.ReadAllLines(filePath);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Recognize #HttpOnly_ prefix (Cookie-Editor/curl marks HttpOnly cookies this way)
            var isHttpOnly = false;
            if (trimmed.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase))
            {
                isHttpOnly = true;
                trimmed = trimmed.Substring("#HttpOnly_".Length);
            }
            else if (trimmed.StartsWith('#'))
            {
                // Skip actual comment lines
                continue;
            }

            var parts = trimmed.Split('\t');
            if (parts.Length < 7)
            {
                Console.WriteLine($"[WARN] Skipping cookie line (too few fields): {trimmed}");
                continue;
            }

            var domain = parts[0];
            // includeSubdomains = parts[1] (TRUE/FALSE)
            var path = parts[2];
            var secure = parts[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            var expiryStr = parts[4];
            var name = parts[5];
            var value = parts[6];

            var cookie = new Cookie
            {
                Name = name,
                Value = value,
                Domain = domain,
                Path = path,
                Secure = secure,
                HttpOnly = isHttpOnly
            };

            // Set expiry (0 = session cookie)
            if (long.TryParse(expiryStr, out var expiry) && expiry > 0)
            {
                cookie.Expires = expiry;
            }

            cookies.Add(cookie);
        }

        Console.WriteLine($"[INFO] Loaded {cookies.Count} cookies from '{filePath}'.");
        return cookies;
    }
}
