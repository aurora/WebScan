// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Harald Lapp

using System.CommandLine;
using WebScan;

var urlOption = new Option<string>(
    name: "--url",
    description: "Start URL to crawl (e.g. https://example.com/help)")
{
    IsRequired = true
};
urlOption.AddAlias("-u");

var cookiesOption = new Option<string?>(
    name: "--cookies",
    description: "Path to Netscape cookie file (cookies.txt format)")
{
    IsRequired = false
};
cookiesOption.AddAlias("-c");

var outputOption = new Option<string>(
    name: "--output",
    description: "Output directory for downloaded pages",
    getDefaultValue: () => "output")
{
    IsRequired = false
};
outputOption.AddAlias("-o");

var limitOption = new Option<int?>(
    name: "--limit",
    description: "Maximum number of pages to crawl (for testing, e.g. --limit 2)")
{
    IsRequired = false
};
limitOption.AddAlias("-l");

var modeOption = new Option<CrawlMode>(
    name: "--mode",
    description: "Crawl mode: 'fast' = no delays, 'gentle' = random delays simulating human browsing")
{
    IsRequired = true
};
modeOption.AddAlias("-m");

var cleanOption = new Option<CleanMode>(
    name: "--clean",
    description: "HTML cleanup: 'none' = as-is, 'clean' = remove scripts/trackers, 'minimal' = content only",
    getDefaultValue: () => CleanMode.Clean)
{
    IsRequired = false
};

var maxWaitOption = new Option<int>(
    name: "--max-wait",
    description: "Maximum delay between pages in ms (gentle mode only, default: 3000)",
    getDefaultValue: () => 3000)
{
    IsRequired = false
};
maxWaitOption.AddAlias("-w");

var includeOption = new Option<string[]>(
    name: "--include",
    description: "Additional paths to include (relative to start URL, e.g. ../articles/)")
{
    IsRequired = false,
    AllowMultipleArgumentsPerToken = true
};

var excludeOption = new Option<string[]>(
    name: "--exclude",
    description: "Paths to exclude from crawling (relative to start URL, e.g. tickets/)")
{
    IsRequired = false,
    AllowMultipleArgumentsPerToken = true
};

var rootCommand = new RootCommand("WebScan - Website crawler with JavaScript rendering (Playwright)")
{
    urlOption,
    cookiesOption,
    outputOption,
    limitOption,
    modeOption,
    cleanOption,
    maxWaitOption,
    includeOption,
    excludeOption
};

rootCommand.SetHandler(async context =>
{
    var url = context.ParseResult.GetValueForOption(urlOption)!;
    var cookiesPath = context.ParseResult.GetValueForOption(cookiesOption);
    var output = context.ParseResult.GetValueForOption(outputOption)!;
    var limit = context.ParseResult.GetValueForOption(limitOption);
    var mode = context.ParseResult.GetValueForOption(modeOption);
    var clean = context.ParseResult.GetValueForOption(cleanOption);
    var maxWait = context.ParseResult.GetValueForOption(maxWaitOption);
    var includes = context.ParseResult.GetValueForOption(includeOption);
    var excludes = context.ParseResult.GetValueForOption(excludeOption);

    Console.WriteLine("╔══════════════════════════════════════╗");
    Console.WriteLine("║          WebScan Crawler             ║");
    Console.WriteLine("╚══════════════════════════════════════╝");
    Console.WriteLine();

    // Validate URL
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
        (uri.Scheme != "http" && uri.Scheme != "https"))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] Invalid URL. Please provide a full URL (https://...)");
        Console.ResetColor();
        return;
    }

    // Load cookies if specified
    Microsoft.Playwright.Cookie[]? cookies = null;
    if (!string.IsNullOrEmpty(cookiesPath))
    {
        try
        {
            var parsedCookies = CookieParser.ParseNetscapeCookieFile(cookiesPath);
            cookies = parsedCookies.ToArray();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Failed to read cookie file: {ex.Message}");
            Console.ResetColor();
            return;
        }
    }

    // Create output directory
    Directory.CreateDirectory(output);

    // Start crawler
    var crawler = new WebCrawler(url, output, cookies, limit, mode, clean, maxWait, includes, excludes);
    await crawler.CrawlAsync();
});

return await rootCommand.InvokeAsync(args);
