// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Harald Lapp

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebScan;

/// <summary>
/// Crawls a website using Playwright (JavaScript rendering), saves HTML and images.
/// Restricts crawling to URLs at the same level or deeper than the start URL.
/// </summary>
public class WebCrawler
{
    private readonly string _startUrl;
    private readonly string _outputDir;
    private readonly IReadOnlyList<Cookie>? _cookies;
    private readonly int? _limit;
    private readonly CrawlMode _mode;
    private readonly CleanMode _cleanMode;
    private readonly int _maxWait;
    private readonly HashSet<string> _visitedUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _urlQueue = new();
    private readonly string _basePath;
    private readonly Uri _startUri;
    private readonly Random _random = new();
    private readonly List<string> _includePaths = new();
    private readonly List<string> _excludePaths = new();
    private int _pagesDownloaded;

    // Realistic user agents (current desktop browsers)
    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Safari/605.1.15",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0"
    ];

    public WebCrawler(string startUrl, string outputDir, IReadOnlyList<Cookie>? cookies, int? limit,
        CrawlMode mode, CleanMode cleanMode, int maxWait,
        IReadOnlyList<string>? includePaths = null, IReadOnlyList<string>? excludePaths = null)
    {
        _startUrl = startUrl.TrimEnd('/');
        _outputDir = outputDir;
        _cookies = cookies;
        _limit = limit;
        _mode = mode;
        _cleanMode = cleanMode;
        _maxWait = maxWait;
        _startUri = new Uri(_startUrl);

        // Determine base path
        _basePath = _startUri.AbsolutePath.TrimEnd('/');

        // Resolve include/exclude paths relative to the start URL
        var baseForResolve = _startUrl.EndsWith('/') ? _startUrl : _startUrl + "/";
        if (includePaths != null)
        {
            foreach (var p in includePaths)
            {
                if (Uri.TryCreate(new Uri(baseForResolve), p, out var resolved))
                {
                    _includePaths.Add(resolved.AbsolutePath.TrimEnd('/'));
                }
            }
        }

        if (excludePaths != null)
        {
            foreach (var p in excludePaths)
            {
                if (Uri.TryCreate(new Uri(baseForResolve), p, out var resolved))
                {
                    _excludePaths.Add(resolved.AbsolutePath.TrimEnd('/'));
                }
            }
        }

        // Always include the base path itself
        if (_includePaths.Count > 0 && !_includePaths.Any(ip =>
                _basePath.Equals(ip, StringComparison.OrdinalIgnoreCase) ||
                _basePath.StartsWith(ip + "/", StringComparison.OrdinalIgnoreCase)))
        {
            _includePaths.Insert(0, _basePath);
        }

        Console.WriteLine($"[INFO] Start URL: {_startUrl}");
        Console.WriteLine($"[INFO] Base path: {_basePath}");
        if (_includePaths.Count > 0)
            Console.WriteLine($"[INFO] Include paths: {string.Join(", ", _includePaths)}");
        if (_excludePaths.Count > 0)
            Console.WriteLine($"[INFO] Exclude paths: {string.Join(", ", _excludePaths)}");
        Console.WriteLine($"[INFO] Output: {_outputDir}");
        Console.WriteLine($"[INFO] Mode: {_mode}");
        Console.WriteLine($"[INFO] HTML cleanup: {_cleanMode}");
        if (_limit.HasValue)
            Console.WriteLine($"[INFO] Limit: {_limit.Value} pages");
    }

    /// <summary>
    /// Playwright only ships browser builds for specific distro releases. Newer Ubuntu releases
    /// (e.g. in a Distrobox) are rejected, so fall back to the newest supported build.
    /// </summary>
    private static void ApplyLinuxPlatformOverride()
    {
        if (!OperatingSystem.IsLinux()
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLAYWRIGHT_HOST_PLATFORM_OVERRIDE")))
            return;

        try
        {
            var values = File.ReadAllLines("/etc/os-release")
                .Select(l => l.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1].Trim('"'));

            if (values.GetValueOrDefault("ID") == "ubuntu"
                && Version.TryParse(values.GetValueOrDefault("VERSION_ID"), out var version)
                && version > new Version(24, 4))
            {
                Environment.SetEnvironmentVariable("PLAYWRIGHT_HOST_PLATFORM_OVERRIDE", "ubuntu24.04-x64");
            }
        }
        catch (IOException)
        {
        }
    }

    public async Task CrawlAsync()
    {
        var userAgent = UserAgents[_random.Next(UserAgents.Length)];

        ApplyLinuxPlatformOverride();

        using var playwright = await Playwright.CreateAsync();
        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args =
            [
                "--disable-blink-features=AutomationControlled",
                "--disable-features=IsolateOrigins,site-per-process",
                "--no-sandbox"
            ]
        };

        IBrowser browser;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(launchOptions);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist"))
        {
            // Browser not downloaded yet: install it via Playwright's built-in installer
            // (works on Windows and Linux without PowerShell).
            Console.WriteLine("[INFO] Chromium not found, installing (one-time download)...");
            var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"Playwright browser installation failed (exit code {exitCode}).", ex);
            browser = await playwright.Chromium.LaunchAsync(launchOptions);
        }
        await using var _ = browser;

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = userAgent,
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            Locale = "en-US",
            TimezoneId = "America/New_York",
            JavaScriptEnabled = true
        });

        // Stealth: hide navigator.webdriver
        await context.AddInitScriptAsync(@"
            Object.defineProperty(navigator, 'webdriver', { get: () => false });
            Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
            Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en', 'de-DE', 'de'] });
            window.chrome = { runtime: {} };
        ");

        // Set cookies if provided
        if (_cookies != null && _cookies.Count > 0)
        {
            await context.AddCookiesAsync(_cookies);
            Console.WriteLine($"[INFO] {_cookies.Count} cookies set in browser context.");

            // Debug: print cookie details
            var domains = _cookies.Select(c => c.Domain).Distinct().ToList();
            Console.WriteLine($"[DEBUG] Cookie domains: {string.Join(", ", domains)}");
            foreach (var cookie in _cookies)
            {
                var expInfo = cookie.Expires > 0
                    ? DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires).ToString("yyyy-MM-dd HH:mm:ss")
                    : "Session";
                Console.WriteLine($"[DEBUG]   {cookie.Name} = {cookie.Value[..Math.Min(20, cookie.Value.Length)]}... (Domain: {cookie.Domain}, Path: {cookie.Path}, Secure: {cookie.Secure}, Expires: {expInfo})");
            }
        }

        Console.WriteLine($"[INFO] User-Agent: {userAgent}");

        // Add start URL to queue
        _urlQueue.Add(_startUrl);
        _visitedUrls.Add(NormalizeUrl(_startUrl));

        while (_urlQueue.Count > 0)
        {
            // Check limit
            if (_limit.HasValue && _pagesDownloaded >= _limit.Value)
            {
                Console.WriteLine($"\n[INFO] Limit of {_limit.Value} pages reached. Stopping.");
                break;
            }

            // Select URL: random in gentle mode, first in fast mode
            string url;
            if (_mode == CrawlMode.Gentle && _urlQueue.Count > 1)
            {
                var index = _random.Next(_urlQueue.Count);
                url = _urlQueue[index];
                _urlQueue.RemoveAt(index);
            }
            else
            {
                url = _urlQueue[0];
                _urlQueue.RemoveAt(0);
            }

            // Delay in gentle mode
            if (_mode == CrawlMode.Gentle && _pagesDownloaded > 0)
            {
                var delay = GetGentleDelay();
                Console.WriteLine($"[WAIT] {delay}ms delay...");
                await Task.Delay(delay);
            }

            var shouldContinue = await ProcessPageAsync(context, url);
            if (!shouldContinue)
            {
                Console.WriteLine("\n[ABORT] Crawling aborted.");
                break;
            }
        }

        Console.WriteLine($"\n[DONE] Crawling complete. {_pagesDownloaded} pages downloaded.");
    }

    /// <summary>
    /// Generates a human-like random delay.
    /// Sometimes short (quick clicking), sometimes longer (reading).
    /// Bounded by --max-wait.
    /// </summary>
    private int GetGentleDelay()
    {
        // Minimum is 200ms, maximum is _maxWait
        var min = Math.Min(200, _maxWait);
        var mid = (int)(_maxWait * 0.5);

        // Weighted random distribution:
        // 30% chance: short delay - quick clicking
        // 50% chance: medium delay - normal browsing
        // 20% chance: longer delay - reading the page
        var roll = _random.NextDouble();

        if (roll < 0.30)
            return _random.Next(min, Math.Max(min + 1, mid));
        if (roll < 0.80)
            return _random.Next(mid, Math.Max(mid + 1, (int)(_maxWait * 0.8)));
        return _random.Next((int)(_maxWait * 0.7), _maxWait);
    }

    private async Task<bool> ProcessPageAsync(IBrowserContext context, string url)
    {
        Console.WriteLine($"\n[CRAWL] {url}");

        var page = await context.NewPageAsync();

        try
        {
            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });

            if (response == null || !response.Ok)
            {
                Console.WriteLine($"[WARN] Failed to load page: {url} (Status: {response?.Status})");
                return true; // Continue crawling
            }

            // Redirect detection: check if final URL is still in scope
            var finalUrl = page.Url;
            if (!finalUrl.Equals(url, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[REDIRECT] {url} → {finalUrl}");

                // On the start page: redirect likely means login redirect
                if (_pagesDownloaded == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine();
                    Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
                    Console.WriteLine("║  ABORT: The start page was redirected!                       ║");
                    Console.WriteLine("║                                                              ║");
                    Console.WriteLine("║  This indicates that the cookies are invalid or the           ║");
                    Console.WriteLine("║  session has expired.                                         ║");
                    Console.WriteLine("║                                                              ║");
                    Console.WriteLine("║  Please export fresh cookies and try again.                   ║");
                    Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
                    Console.WriteLine();
                    Console.WriteLine($"  Requested: {url}");
                    Console.WriteLine($"  Redirected to: {finalUrl}");
                    Console.ResetColor();
                    return false; // Abort crawling
                }

                // On sub-pages: redirect out of scope = skip
                if (!IsInScope(finalUrl))
                {
                    Console.WriteLine($"[SKIP] Redirect target is out of scope.");
                    return true; // Continue crawling
                }
            }

            // Wait for content to render
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Additional wait for JS rendering
            var jsWait = _mode == CrawlMode.Gentle ? _random.Next(2000, 4000) : 2000;
            await page.WaitForTimeoutAsync(jsWait);

            // Get HTML content (after JS rendering)
            var htmlContent = await page.ContentAsync();

            // Extract links and add to queue
            var links = await ExtractLinksAsync(page, url);
            foreach (var link in links)
            {
                var normalized = NormalizeUrl(link);
                if (!_visitedUrls.Contains(normalized))
                {
                    _visitedUrls.Add(normalized);
                    _urlQueue.Add(link);
                }
            }

            // Download images and rewrite paths in HTML
            htmlContent = await DownloadImagesAndRewriteHtml(page, htmlContent, url);

            // Rewrite links to local file paths
            htmlContent = RewriteLinksToLocal(htmlContent, url);

            // Clean HTML
            htmlContent = HtmlCleaner.Clean(htmlContent, _cleanMode);

            // Save HTML
            SaveHtml(url, htmlContent);
            _pagesDownloaded++;

            Console.WriteLine($"[OK] Page saved ({_pagesDownloaded}/{(_limit.HasValue ? _limit.Value.ToString() : "∞")})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Error at {url}: {ex.Message}");
        }
        finally
        {
            await page.CloseAsync();
        }

        return true; // Continue crawling
    }

    private async Task<List<string>> ExtractLinksAsync(IPage page, string currentUrl)
    {
        var links = new List<string>();

        var hrefs = await page.EvalOnSelectorAllAsync<string[]>(
            "a[href]",
            "elements => elements.map(e => e.href)");

        foreach (var href in hrefs)
        {
            try
            {
                var absoluteUrl = ResolveUrl(href, currentUrl);
                if (absoluteUrl != null && IsInScope(absoluteUrl))
                {
                    links.Add(absoluteUrl);
                }
            }
            catch
            {
                // Skip invalid URLs
            }
        }

        Console.WriteLine($"[INFO] {links.Count} in-scope links found.");
        return links;
    }

    private string? ResolveUrl(string href, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        // Skip fragment-only links
        if (href.StartsWith('#'))
            return null;

        // Skip javascript: links
        if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return null;

        // Skip mailto: links
        if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return null;

        // Skip tel: links
        if (href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            return null;

        if (Uri.TryCreate(new Uri(baseUrl), href, out var resolved))
        {
            // Remove fragment
            var urlWithoutFragment = resolved.GetLeftPart(UriPartial.Query);
            return urlWithoutFragment;
        }

        return null;
    }

    /// <summary>
    /// Checks if a URL is in scope based on domain, include paths, and exclude paths.
    /// If --include is specified, the URL must match one of the include paths.
    /// If --exclude is specified, matching URLs are excluded even if they match an include.
    /// If neither is specified, the default behavior applies (same level or deeper than start URL).
    /// </summary>
    private bool IsInScope(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Check same domain
        if (!uri.Host.Equals(_startUri.Host, StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.AbsolutePath.TrimEnd('/');

        // Check excludes first (excludes always win)
        foreach (var exclude in _excludePaths)
        {
            if (path.Equals(exclude, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(exclude + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // If includes are specified, URL must match one of them
        if (_includePaths.Count > 0)
        {
            return _includePaths.Any(include =>
                path.Equals(include, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(include + "/", StringComparison.OrdinalIgnoreCase));
        }

        // Default: path must start with base path (same level or deeper)
        return path.Equals(_basePath, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(_basePath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> DownloadImagesAndRewriteHtml(IPage page, string html, string pageUrl)
    {
        var imgSrcs = await page.EvalOnSelectorAllAsync<string[]>(
            "img[src]",
            "elements => elements.map(e => e.src)");

        foreach (var src in imgSrcs)
        {
            try
            {
                var absoluteSrc = ResolveUrl(src, pageUrl);
                if (absoluteSrc == null) continue;

                var localPath = await DownloadImageAsync(absoluteSrc, pageUrl);
                if (localPath != null)
                {
                    html = html.Replace(src, localPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to load image: {src} - {ex.Message}");
            }
        }

        // Also handle data-src attributes (lazy loading)
        var dataSrcs = await page.EvalOnSelectorAllAsync<string[]>(
            "img[data-src]",
            "elements => elements.map(e => e.getAttribute('data-src'))");

        foreach (var src in dataSrcs)
        {
            try
            {
                var absoluteSrc = ResolveUrl(src, pageUrl);
                if (absoluteSrc == null) continue;

                var localPath = await DownloadImageAsync(absoluteSrc, pageUrl);
                if (localPath != null)
                {
                    html = html.Replace(src, localPath);
                }
            }
            catch
            {
                // Skip
            }
        }

        // CSS background-image URLs in style attributes
        var bgImages = await page.EvalOnSelectorAllAsync<string[]>(
            "[style*='background']",
            @"elements => {
                const urls = [];
                elements.forEach(el => {
                    const match = el.style.backgroundImage.match(/url\(['""]?(.*?)['""]?\)/);
                    if (match) urls.push(match[1]);
                });
                return urls;
            }");

        foreach (var src in bgImages)
        {
            try
            {
                var absoluteSrc = ResolveUrl(src, pageUrl);
                if (absoluteSrc == null) continue;

                var localPath = await DownloadImageAsync(absoluteSrc, pageUrl);
                if (localPath != null)
                {
                    html = html.Replace(src, localPath);
                }
            }
            catch
            {
                // Skip
            }
        }

        return html;
    }

    /// <summary>
    /// Known tracking/analytics domains whose "images" (tracking pixels) should be skipped.
    /// </summary>
    private static readonly string[] TrackingDomains =
    [
        "bat.bing.com",
        "www.google.com",
        "www.google.de",
        "www.google-analytics.com",
        "googleads.g.doubleclick.net",
        "www.googletagmanager.com",
        "www.googleadservices.com",
        "connect.facebook.net",
        "www.facebook.com",
        "pixel.facebook.com",
        "analytics.twitter.com",
        "t.co",
        "snap.licdn.com",
        "px.ads.linkedin.com",
        "ct.pinterest.com",
        "tr.snapchat.com",
        "ad.doubleclick.net",
        "stats.g.doubleclick.net",
        "cm.g.doubleclick.net"
    ];

    /// <summary>
    /// Checks if a URL is a known tracking pixel / analytics beacon.
    /// </summary>
    private static bool IsTrackingUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return TrackingDomains.Any(d => uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> DownloadImageAsync(string imageUrl, string pageUrl)
    {
        // Skip tracking pixels
        if (IsTrackingUrl(imageUrl))
        {
            Console.WriteLine($"[SKIP] Tracking pixel: {new Uri(imageUrl).Host}");
            return null;
        }

        try
        {
            var uri = new Uri(imageUrl);
            var relativePath = GetRelativeFilePath(imageUrl, isImage: true);
            var fullPath = Path.Combine(_outputDir, relativePath);

            // Create directory
            var dir = Path.GetDirectoryName(fullPath);
            if (dir != null) Directory.CreateDirectory(dir);

            // Skip if file already exists
            if (File.Exists(fullPath))
            {
                return GetRelativePathFromPage(pageUrl, relativePath);
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                UserAgents[_random.Next(UserAgents.Length)]);
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            // Set cookies for HTTP requests
            if (_cookies != null)
            {
                var cookieHeader = string.Join("; ",
                    _cookies
                        .Where(c => c.Domain != null && uri.Host.EndsWith(c.Domain.TrimStart('.')))
                        .Select(c => $"{c.Name}={c.Value}"));

                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);
                }
            }

            var bytes = await httpClient.GetByteArrayAsync(imageUrl);
            await File.WriteAllBytesAsync(fullPath, bytes);

            Console.WriteLine($"[IMG] {Path.GetFileName(fullPath)} ({bytes.Length / 1024}KB)");

            return GetRelativePathFromPage(pageUrl, relativePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Image download failed: {imageUrl} - {ex.Message}");
            return null;
        }
    }

    private void SaveHtml(string url, string htmlContent)
    {
        var relativePath = GetRelativeFilePath(url, isImage: false);
        var fullPath = Path.Combine(_outputDir, relativePath);

        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null) Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, htmlContent);
        Console.WriteLine($"[SAVE] {relativePath}");
    }

    /// <summary>
    /// Creates a relative file path from a URL.
    /// For images: uses a short hash-based filename to avoid path length issues.
    /// </summary>
    private string GetRelativeFilePath(string url, bool isImage)
    {
        var uri = new Uri(url);
        var path = uri.AbsolutePath.TrimStart('/');

        if (string.IsNullOrEmpty(path))
            path = "index";

        if (!isImage)
        {
            // Encode query parameters into filename
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var safeQuery = Regex.Replace(uri.Query, @"[^\w\-]", "_");
                path += safeQuery;
            }

            // HTML files get .html extension
            if (!Path.HasExtension(path) || !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                path = path.TrimEnd('/');
                path += ".html";
            }
        }
        else
        {
            // For images: use hash-based filename to avoid overly long paths
            // Determine file extension from the URL path (before query params)
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (string.IsNullOrEmpty(ext) || ext.Length > 5)
            {
                ext = ".png"; // Fallback
            }

            // Generate a short hash from the full URL (including query params)
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();

            // Try to keep a recognizable part of the original filename
            var originalName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            if (!string.IsNullOrEmpty(originalName) && originalName.Length <= 40)
            {
                // Sanitize the original name
                originalName = Regex.Replace(originalName, @"[^\w\-.]", "_");
                path = Path.Combine("_images", $"{originalName}_{hash}{ext}");
            }
            else
            {
                path = Path.Combine("_images", $"{hash}{ext}");
            }
        }

        // Replace invalid characters
        path = Regex.Replace(path, @"[<>:""|?*]", "_");

        return path;
    }

    /// <summary>
    /// Calculates the relative path from a page to a resource.
    /// </summary>
    private string GetRelativePathFromPage(string pageUrl, string resourceRelativePath)
    {
        var pageRelPath = GetRelativeFilePath(pageUrl, isImage: false);
        var pageDir = Path.GetDirectoryName(pageRelPath) ?? "";
        var resourceFullPath = Path.Combine(_outputDir, resourceRelativePath);
        var pageFullDir = Path.Combine(_outputDir, pageDir);

        try
        {
            var relUri = new Uri(pageFullDir + Path.DirectorySeparatorChar)
                .MakeRelativeUri(new Uri(resourceFullPath));
            return Uri.UnescapeDataString(relUri.ToString()).Replace('\\', '/');
        }
        catch
        {
            return resourceRelativePath.Replace('\\', '/');
        }
    }

    /// <summary>
    /// Rewrites links in HTML to local file paths.
    /// </summary>
    private string RewriteLinksToLocal(string html, string currentPageUrl)
    {
        html = Regex.Replace(html, @"href=""([^""]+)""", match =>
        {
            var href = match.Groups[1].Value;
            try
            {
                var resolved = ResolveUrl(href, currentPageUrl);
                if (resolved != null && IsInScope(resolved))
                {
                    var targetRelPath = GetRelativeFilePath(resolved, isImage: false);
                    var localHref = GetRelativePathFromPage(currentPageUrl, targetRelPath);
                    return $"href=\"{localHref}\"";
                }
            }
            catch
            {
                // Skip
            }

            return match.Value;
        });

        return html;
    }

    private static string NormalizeUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var normalized = uri.GetLeftPart(UriPartial.Query).TrimEnd('/').ToLowerInvariant();
            return normalized;
        }
        return url.TrimEnd('/').ToLowerInvariant();
    }
}
