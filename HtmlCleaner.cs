// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Harald Lapp

using System.Text.RegularExpressions;

namespace WebScan;

/// <summary>
/// Controls how the saved HTML is cleaned up.
/// </summary>
public enum CleanMode
{
    /// <summary>
    /// No cleanup — HTML as rendered by the browser.
    /// </summary>
    None,

    /// <summary>
    /// Remove scripts, trackers, and external fonts.
    /// CSS stylesheets are preserved.
    /// </summary>
    Clean,

    /// <summary>
    /// Remove everything: no CSS, no JS, no fonts, no external resources.
    /// Only pure HTML content with images. Adds minimal base CSS for readability.
    /// </summary>
    Minimal
}

/// <summary>
/// Cleans HTML by removing scripts, trackers, external resources etc.
/// </summary>
public static class HtmlCleaner
{
    /// <summary>
    /// Cleans HTML based on the selected mode.
    /// </summary>
    public static string Clean(string html, CleanMode mode)
    {
        if (mode == CleanMode.None)
            return html;

        if (mode == CleanMode.Clean)
            return CleanHtml(html);

        if (mode == CleanMode.Minimal)
            return MinimalHtml(html);

        return html;
    }

    /// <summary>
    /// Clean mode: remove scripts and trackers, keep CSS.
    /// </summary>
    private static string CleanHtml(string html)
    {
        // Remove all <script> tags (including content)
        html = Regex.Replace(html, @"<script\b[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);

        // Remove all <noscript> tags
        html = Regex.Replace(html, @"<noscript\b[^>]*>[\s\S]*?</noscript>", "", RegexOptions.IgnoreCase);

        // Remove tracking iframes (Facebook, Google, Bing, etc.)
        html = Regex.Replace(html, @"<iframe\b[^>]*(?:facebook|google|bing|doubleclick|analytics|gtm|tagmanager)[^>]*>[\s\S]*?</iframe>", "", RegexOptions.IgnoreCase);

        // Remove tiny iframes (often trackers)
        html = Regex.Replace(html, @"<iframe\b[^>]*(?:width\s*=\s*[""']?[01][""']?|height\s*=\s*[""']?[01][""']?)[^>]*>[\s\S]*?</iframe>", "", RegexOptions.IgnoreCase);

        // Remove external script preload links
        html = Regex.Replace(html, @"<link\b[^>]*\bas\s*=\s*[""']script[""'][^>]*/?>", "", RegexOptions.IgnoreCase);

        // Remove tracking-related HTML comments
        html = Regex.Replace(html, @"<!--\s*(?:Google|Facebook|Bing|GTM|Analytics)[\s\S]*?-->", "", RegexOptions.IgnoreCase);

        // Remove external font links (Google Fonts, Font Awesome CDN)
        html = Regex.Replace(html, @"<link\b[^>]*href\s*=\s*[""'][^""']*(?:fonts\.googleapis\.com|fonts\.gstatic\.com|use\.fontawesome\.com)[^""']*[""'][^>]*/?>", "", RegexOptions.IgnoreCase);

        // Remove preconnect/prefetch to tracking domains
        html = Regex.Replace(html, @"<link\b[^>]*href\s*=\s*[""'][^""']*(?:facebook\.net|google-analytics\.com|googletagmanager\.com|doubleclick\.net|bing\.com|connect\.facebook)[^""']*[""'][^>]*/?>", "", RegexOptions.IgnoreCase);

        // Remove tracking meta tags (e.g. facebook domain verification)
        html = Regex.Replace(html, @"<meta\b[^>]*(?:facebook|google-site-verification|msvalidate|p:domain_verify)[^>]*/?>", "", RegexOptions.IgnoreCase);

        // Remove tracking data attributes
        html = Regex.Replace(html, @"\s+data-(?:gtm|ga|fb|analytics|tracking)[a-z\-]*\s*=\s*[""'][^""']*[""']", "", RegexOptions.IgnoreCase);

        // Reduce excessive blank lines
        html = Regex.Replace(html, @"\n\s*\n\s*\n", "\n\n");

        return html;
    }

    /// <summary>
    /// Minimal mode: remove everything except pure HTML content and images.
    /// </summary>
    private static string MinimalHtml(string html)
    {
        // First apply clean mode
        html = CleanHtml(html);

        // Remove all <style> tags (including content)
        html = Regex.Replace(html, @"<style\b[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);

        // Remove all <link rel="stylesheet">
        html = Regex.Replace(html, @"<link\b[^>]*rel\s*=\s*[""']stylesheet[""'][^>]*/?>", "", RegexOptions.IgnoreCase);

        // Remove all remaining CSS link tags
        html = Regex.Replace(html, @"<link\b[^>]*\.css[^>]*/?>", "", RegexOptions.IgnoreCase);

        // Remove all style attributes
        html = Regex.Replace(html, @"\s+style\s*=\s*[""'][^""']*[""']", "", RegexOptions.IgnoreCase);

        // Remove all class attributes (useless without CSS)
        html = Regex.Replace(html, @"\s+class\s*=\s*[""'][^""']*[""']", "", RegexOptions.IgnoreCase);

        // Remove all <iframe> tags
        html = Regex.Replace(html, @"<iframe\b[^>]*>[\s\S]*?</iframe>", "", RegexOptions.IgnoreCase);

        // Remove empty div/span tags (often layout containers without content)
        html = Regex.Replace(html, @"<(div|span)\b[^>]*>\s*</\1>", "", RegexOptions.IgnoreCase);
        // Second pass for nested empty containers
        html = Regex.Replace(html, @"<(div|span)\b[^>]*>\s*</\1>", "", RegexOptions.IgnoreCase);

        // Insert minimal base CSS for readability
        html = Regex.Replace(html, @"(<head\b[^>]*>)", """
            $1
            <style>
                body { font-family: system-ui, -apple-system, sans-serif; max-width: 960px; margin: 2rem auto; padding: 0 1rem; line-height: 1.6; color: #333; }
                img { max-width: 100%; height: auto; }
                a { color: #0066cc; }
                table { border-collapse: collapse; width: 100%; }
                td, th { border: 1px solid #ddd; padding: 8px; text-align: left; }
                h1, h2, h3 { color: #222; }
            </style>
            """, RegexOptions.IgnoreCase);

        // Reduce excessive blank lines
        html = Regex.Replace(html, @"\n\s*\n\s*\n", "\n\n");

        return html;
    }
}
