# WebScan

[![Built with Claude](https://img.shields.io/badge/Built%20with-Claude%20Opus%204-blueviolet?logo=anthropic&logoColor=white)](https://anthropic.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)

A C# command-line tool that crawls websites using **Playwright** (Chromium) and saves them as static HTML. Ideal for pages whose content is built via JavaScript and that sit behind a login.

> 🤖 This project was built with [Claude Opus 4](https://anthropic.com) by Anthropic.

## Features

- **JavaScript rendering** via Playwright/Chromium — captures dynamically loaded content
- **Netscape cookie file** support — pass login sessions via cookies.txt
- **Scope restriction** — only crawls pages at the same level or deeper than the start URL
- **Image download** — images are saved locally, paths rewritten in HTML
- **Link rewriting** — internal links are rewritten to local HTML files
- **Stealth mode** — realistic user agent, anti-bot-detection measures
- **Crawl modes** — `fast` (no delays) or `gentle` (random delays simulating human browsing)
- **HTML cleanup** — `clean` (remove scripts/trackers), `minimal` (content only), or `none`
- **Page limit** — optional limit on number of pages (for testing)
- **Configurable delay** — `--max-wait` controls maximum delay in gentle mode

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)

## Installation

```bash
# Clone the repository
git clone <repo-url>
cd webscan

# Restore dependencies
dotnet restore

dotnet build
```

Chromium is downloaded automatically on the first run (Windows and Linux, no PowerShell required).
On Linux (e.g. Ubuntu/Distrobox) the browser may need system libraries, if launching fails:
`sudo apt install libnss3 libatk-bridge2.0-0 libgbm1 libasound2t64 libxkbcommon0 libxdamage1 libxrandr2 libcups2 libpango-1.0-0`

## Usage

```bash
# Basic usage (fast mode, default cleanup)
dotnet run -- --url https://example.com/help --mode fast

# With cookie file and custom output directory
dotnet run -- --url https://example.com/help --cookies cookies.txt --output ./my-pages --mode gentle

# Test with limit of 2 pages, minimal HTML
dotnet run -- --url https://example.com/help --cookies cookies.txt --mode fast --limit 2 --clean minimal

# Gentle mode with 2-second max delay
dotnet run -- --url https://example.com/help --cookies cookies.txt --mode gentle --max-wait 2000
```

### Parameters

| Parameter | Short | Required | Description |
|-----------|-------|----------|-------------|
| `--url` | `-u` | ✅ | Start URL to crawl |
| `--mode` | `-m` | ✅ | Crawl mode: `fast` or `gentle` |
| `--cookies` | `-c` | ❌ | Path to Netscape cookie file |
| `--output` | `-o` | ❌ | Output directory (default: `output`) |
| `--limit` | `-l` | ❌ | Max number of pages |
| `--clean` | | ❌ | HTML cleanup: `none`, `clean` (default), `minimal` |
| `--max-wait` | `-w` | ❌ | Max delay between pages in ms (default: 3000, gentle mode only) |
| `--include` | | ❌ | Additional paths to include (relative to start URL, repeatable) |
| `--exclude` | | ❌ | Paths to exclude from crawling (relative to start URL, repeatable) |
| `--skip-downloads` | | ❌ | Do not download linked files (exe, zip, pdf, ...). By default they are saved and links rewritten |
| `--skip-assets` | | ❌ | Do not download stylesheets and the fonts/images they reference (stored in `_assets/` by default; not used with `--clean minimal`) |

### Crawl Modes

- **`fast`** — Crawls as fast as possible with no delays. Good for your own sites or when speed matters.
- **`gentle`** — Simulates human browsing: random delays (up to `--max-wait`), randomized URL order. Gentler on the server and less conspicuous.

### HTML Cleanup Modes

- **`none`** — HTML as rendered by the browser, no modifications.
- **`clean`** (default) — Removes all `<script>` tags, tracking pixels, analytics (Google, Facebook, Bing), external font links, preconnect to tracking domains. CSS is preserved.
- **`minimal`** — Everything from `clean` plus removes all CSS, style/class attributes, iframes, empty containers. Adds minimal base CSS for readability. No external resources loaded at all.

## Include / Exclude Paths

Use `--include` and `--exclude` to fine-tune which paths are crawled. Paths are resolved **relative to the start URL**.

```bash
# Start at /controlpanel/help, also crawl /controlpanel/articles/
dotnet run -- --url https://example.com/controlpanel/help --mode fast \
  --include ../articles/

# Exclude specific sub-paths
dotnet run -- --url https://example.com/controlpanel/help --mode fast \
  --exclude tickets/ --exclude billing/

# Combine both
dotnet run -- --url https://example.com/controlpanel/help --mode fast \
  --include ../articles/ --exclude tickets/
```

**Rules:**
- Without `--include`: only pages at the same level or deeper than the start URL
- With `--include`: only the start URL path + explicitly included paths are crawled
- `--exclude` always wins over includes
- Paths are resolved relative to the start URL (e.g. `../articles/` goes up one level)

## Cookie File

The cookie file must be in **Netscape/Mozilla format** (tab-separated):

```
# Netscape HTTP Cookie File
.example.com	TRUE	/	FALSE	0	session_id	abc123def456
.example.com	TRUE	/	TRUE	1735689600	auth_token	xyz789
```

Lines prefixed with `#HttpOnly_` are recognized as HttpOnly cookies (curl/Cookie-Editor convention).

### Exporting Cookies from Your Browser

1. **Chrome/Edge**: Install the "Get cookies.txt LOCALLY" extension
2. Navigate to the logged-in page and use the extension → click "Export"
3. Save the file as `cookies.txt`

## Output Structure

```
output/
├── controlpanel/
│   ├── help.html
│   └── help/
│       ├── getting-started.html
│       ├── faq.html
│       └── ...
└── _images/
    ├── logo.png
    ├── screenshot.jpg
    └── ...
```

## Redirect Detection

If the start page is redirected (e.g., to a login page), the tool aborts immediately with a clear error message, indicating that the cookies are likely invalid or expired.

## License

[MIT](LICENSE)
