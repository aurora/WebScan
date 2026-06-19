// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Harald Lapp

namespace WebScan;

/// <summary>
/// Controls crawl speed and behavior.
/// </summary>
public enum CrawlMode
{
    /// <summary>
    /// Crawl as fast as possible, no delays between pages.
    /// </summary>
    Fast,

    /// <summary>
    /// Crawl gently: random delays, randomized URL order,
    /// simulates human browsing behavior.
    /// </summary>
    Gentle
}
