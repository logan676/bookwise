using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookWise.Web.Data;
using BookWise.Web.Models;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookWise.Web.Services.CommunityContent;

public sealed class BookCommunityContentRefresher : IBookCommunityContentRefresher
{
    private const int MaxItems = 3; // Changed to 3 for top 3 quotes and remarks

    private readonly BookWiseContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BookCommunityContentRefresher> _logger;

    public BookCommunityContentRefresher(
        BookWiseContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<BookCommunityContentRefresher> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task RefreshAsync(BookCommunityContentWorkItem workItem, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workItem.DoubanSubjectId))
        {
            return;
        }

        var book = await _dbContext.Books
            .Include(b => b.Quotes)
            .Include(b => b.Remarks)
            .Include(b => b.AuthorDetails)
            .FirstOrDefaultAsync(b => b.Id == workItem.BookId, cancellationToken);

        if (book is null)
        {
            _logger.LogWarning("Skipped community content refresh because book {BookId} no longer exists.", workItem.BookId);
            return;
        }

        if (!string.Equals(book.DoubanSubjectId, workItem.DoubanSubjectId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Skipping community content refresh for book {BookId} because Douban subject id has changed.",
                workItem.BookId);
            return;
        }

        var client = _httpClientFactory.CreateClient("DoubanBooks");
        
        // Task 1: Fetch community quotes and remarks
        var quotes = await FetchCommunityQuotesAsync(client, workItem.DoubanSubjectId, cancellationToken);
        var remarks = await FetchCommunityRemarksAsync(client, workItem.DoubanSubjectId, cancellationToken);

        // Task 2: Fetch author profile information
        if (book.AuthorDetails != null)
        {
            await FetchAndUpdateAuthorProfileAsync(client, book.AuthorDetails, workItem.DoubanSubjectId, cancellationToken);
            // Persist author updates early to avoid losing them if later steps fail
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist author profile updates for {Author}", book.AuthorDetails.Name);
            }
        }

        var existingQuotes = book.Quotes
            .Where(q => q.Origin == BookQuoteSource.Community)
            .ToList();
        if (existingQuotes.Count > 0)
        {
            _dbContext.BookQuotes.RemoveRange(existingQuotes);
        }

        var existingRemarks = book.Remarks
            .Where(r => r.Type == BookRemarkType.Community)
            .ToList();
        if (existingRemarks.Count > 0)
        {
            _dbContext.BookRemarks.RemoveRange(existingRemarks);
        }

        var normalizedAuthor = CreateBookRequest.TrimToLength(book.Author, 200, allowEmpty: true) ?? book.Author;
        var quoteEntities = MapQuotes(book.Id, normalizedAuthor, quotes);
        var remarkEntities = MapRemarks(book.Id, remarks);

        if (quoteEntities.Count > 0)
        {
            foreach (var entity in quoteEntities)
            {
                book.Quotes.Add(entity);
            }
        }

        if (remarkEntities.Count > 0)
        {
            foreach (var entity in remarkEntities)
            {
                book.Remarks.Add(entity);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Refreshed community content for book {BookId}: {QuoteCount} quotes, {RemarkCount} remarks.",
            book.Id,
            quoteEntities.Count,
            remarkEntities.Count);
    }

    private async Task FetchAndUpdateAuthorProfileAsync(HttpClient client, Author author, string doubanSubjectId, CancellationToken cancellationToken)
    {
        try
        {
            // First, try to extract author ID from the book page
            var authorDoubanId = await ExtractAuthorDoubanIdAsync(client, doubanSubjectId, cancellationToken);
            if (string.IsNullOrWhiteSpace(authorDoubanId))
            {
                _logger.LogWarning("Could not extract Douban author ID for author {AuthorName} from subject {SubjectId}", author.Name, doubanSubjectId);
                return;
            }

            // Fetch author profile from Douban
            var profile = await FetchAuthorProfileAsync(client, authorDoubanId, cancellationToken);
            if (profile == null)
            {
                _logger.LogWarning("Could not fetch author profile for author {AuthorName} with Douban ID {AuthorId}", author.Name, authorDoubanId);
                return;
            }

            // Update author profile information
            author.ProfileSummary = CreateBookRequest.TrimToLength(profile.Summary, 2000);
            author.ProfileNotableWorks = CreateBookRequest.TrimToLength(profile.NotableWorks, 1000);

            // Update avatar metadata
            if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
            {
                author.AvatarUrl = profile.AvatarUrl;
                // author.AvatarStatus = "Verified";
                // author.AvatarSource = CreateBookRequest.TrimToLength(profile.AvatarSource, 50);
                _logger.LogInformation("Updated avatar URL for author {AuthorName} to {AvatarUrl}", author.Name, profile.AvatarUrl);
            }
            else
            {
                // Clear any external placeholders and mark as failed; UI will fallback to self-hosted placeholder
                author.AvatarUrl = null;
                // author.AvatarStatus = "Failed";
                // author.AvatarSource = null;
            }

            // Update additional profile metadata
            author.ProfileGender = CreateBookRequest.TrimToLength(profile.Gender, 20);
            author.ProfileBirthDate = CreateBookRequest.TrimToLength(profile.BirthDate, 50);
            author.ProfileBirthPlace = CreateBookRequest.TrimToLength(profile.BirthPlace, 200);
            author.ProfileOccupation = CreateBookRequest.TrimToLength(profile.Occupation, 200);
            author.ProfileOtherNames = CreateBookRequest.TrimToLength(profile.OtherNames, 200);
            var website = NormalizeAvatarUrl(profile.WebsiteUrl);
            author.ProfileWebsiteUrl = CreateBookRequest.TrimToLength(website, 500);
            author.DoubanAuthorId = CreateBookRequest.TrimToLength(profile.DoubanId, 32);
            author.DoubanAuthorType = CreateBookRequest.TrimToLength(profile.DoubanType, 20);
            author.DoubanProfileUrl = CreateBookRequest.TrimToLength(profile.ProfileUrl, 500);

            author.ProfileRefreshedAt = DateTimeOffset.UtcNow;
            author.UpdatedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("Updated author profile for {AuthorName} (Douban ID: {AuthorId})", author.Name, authorDoubanId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch author profile for {AuthorName}", author.Name);
        }
    }

    private async Task<string?> ExtractAuthorDoubanIdAsync(HttpClient client, string subjectId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync($"subject/{subjectId}/", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = new HtmlDocument();
            document.LoadHtml(html);

            // Try multiple selectors to find an anchor element for the author/personage
            var authorLinkSelectors = new[]
            {
                "//div[@id='info']//span[@class='pl' and (text()='作者:' or text()='作者：' or text()='作者')]/following-sibling::a[1]",
                "//div[@id='info']//a[contains(@href,'/author/') or contains(@href,'/personage/')][1]"
            };

            string? authorHref = null;
            foreach (var selector in authorLinkSelectors)
            {
                var anchor = document.DocumentNode.SelectSingleNode(selector);
                if (anchor != null)
                {
                    authorHref = anchor.GetAttributeValue("href", null);
                    if (!string.IsNullOrWhiteSpace(authorHref))
                    {
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(authorHref))
            {
                // Extract ID from different URL patterns:
                // https://book.douban.com/author/27557670/
                // https://www.douban.com/personage/36696520/
                // /author/27557670/
                // /personage/36696520/
                var patterns = new[]
                {
                    @"/(?:author|personage)/(\d+)/?",
                    @"douban\.com/(?:author|personage)/(\d+)/?"
                };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(authorHref, pattern);
                    if (match.Success)
                    {
                        var authorId = match.Groups[1].Value;
                        _logger.LogInformation("Extracted author Douban ID {AuthorId} from URL {AuthorHref}", authorId, authorHref);
                        // Try to canonicalize via redirect to personage page
                        var canonical = await TryCanonicalizeAuthorIdAsync(authorId, cancellationToken);
                        return canonical ?? authorId;
                    }
                }
                
                _logger.LogWarning("Found author link {AuthorHref} but could not extract ID", authorHref);
            }

            // Final fallback: scan the entire HTML for author/personage id patterns
            {
                var htmlText = document.DocumentNode.OuterHtml ?? html;
                var m = System.Text.RegularExpressions.Regex.Match(htmlText, @"/(?:personage|author)/(\d+)/", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var authorId = m.Groups[1].Value;
                    _logger.LogInformation("Fallback extracted author Douban ID {AuthorId} from subject page HTML", authorId);
                    var canonical = await TryCanonicalizeAuthorIdAsync(authorId, cancellationToken);
                    return canonical ?? authorId;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract author Douban ID from subject {SubjectId}", subjectId);
            return null;
        }
    }

    private async Task<AuthorProfile?> FetchAuthorProfileAsync(HttpClient client, string authorId, CancellationToken cancellationToken)
    {
        try
        {
            // Create a new client for author/person pages since they might be on a different subdomain
            using var authorClient = _httpClientFactory.CreateClient();
            authorClient.Timeout = TimeSpan.FromSeconds(10);
            authorClient.DefaultRequestHeaders.UserAgent.ParseAdd("BookWise/1.0 (+https://bookwise.local)");

            string? avatarUrl = null;
            // string? avatarSource = null;
            string? summary = null;
            string? worksText = null;
            string? gender = null;
            string? birthDate = null;
            string? birthPlace = null;
            string? occupation = null;
            string? otherNames = null;
            string? websiteUrl = null;
            string? doubanType = null;
            string? profileUrl = null;

            // Prefer the personage page for richer profile data (use absolute URI to avoid BaseAddress mutations)
            using (var response = await authorClient.GetAsync($"https://www.douban.com/personage/{authorId}/", cancellationToken))
            {
                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync(cancellationToken);
                    var document = new HtmlDocument();
                    document.LoadHtml(html);

                    // Use og:image as primary avatar source
                    var ogImage = document.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null);
                    avatarUrl = NormalizeAvatarUrl(ogImage);
                    if (string.IsNullOrWhiteSpace(avatarUrl))
                    {
                        // Fallback: first portrait image on personage page
                        var portraitImg = document.DocumentNode.SelectSingleNode("//img[contains(@src,'/view/personage/')]");
                        var src = portraitImg?.GetAttributeValue("src", null);
                        avatarUrl = NormalizeAvatarUrl(src);
                    }
                    if (!string.IsNullOrWhiteSpace(avatarUrl))
                    {
                        // avatarSource = "douban-personage";
                    }

                    // Extract profile summary (prefer the explicit 人物简介/作者简介 section)
                    var summaryNode = document.DocumentNode
                        .SelectSingleNode("//div[@class='bd']/div[@class='intro']") ??
                        document.DocumentNode
                        .SelectSingleNode("//div[@class='intro']");
                    var rawSummary = summaryNode?.InnerText;
                    if (!string.IsNullOrWhiteSpace(rawSummary))
                    {
                        summary = NormalizeWhitespace(HtmlEntity.DeEntitize(rawSummary));
                    }
                    
                    if (string.IsNullOrWhiteSpace(summary))
                    {
                        // Look for a module whose header explicitly says 人物简介 or 作者简介
                        var introModule = document.DocumentNode.SelectSingleNode(
                            "//div[contains(@class,'mod')][.//h2[contains(normalize-space(.),'人物简介') or contains(normalize-space(.),'作者简介')]]");
                        if (introModule is not null)
                        {
                            var introCandidates = introModule.SelectNodes(".//div[contains(@class,'bd')]//*[self::div[contains(@class,'intro')] or self::p]");
                            if (introCandidates is not null && introCandidates.Count > 0)
                            {
                                var pieces = introCandidates
                                    .Select(n => NormalizeWhitespace(HtmlEntity.DeEntitize(n.InnerText)))
                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                    .ToList();
                                if (pieces.Count > 0)
                                {
                                    summary = string.Join(" ", pieces);
                                }
                            }
                            else
                            {
                                // Fallback: any text under the module body
                                var body = introModule.SelectSingleNode(".//div[contains(@class,'bd')]");
                                var text = body?.InnerText;
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    summary = NormalizeWhitespace(HtmlEntity.DeEntitize(text));
                                }
                            }
                        }
                    }
                    
                    if (string.IsNullOrWhiteSpace(summary))
                    {
                        // Final fallback on personage page: og:description
                        var ogDesc = document.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", null);
                        if (!string.IsNullOrWhiteSpace(ogDesc))
                        {
                            summary = NormalizeWhitespace(HtmlEntity.DeEntitize(ogDesc));
                        }
                    }

                    // Extract notable works
                    var worksNodes = document.DocumentNode.SelectNodes("//div[@class='works']//li/a");
                    var notableWorks = worksNodes?
                        .Take(5)
                        .Select(node => NormalizeWhitespace(HtmlEntity.DeEntitize(node.InnerText)))
                        .Where(work => !string.IsNullOrWhiteSpace(work))
                        .ToList();
                    if (notableWorks is not null && notableWorks.Count > 0)
                    {
                        worksText = string.Join(", ", notableWorks);
                    }

                    // Extract labeled facts (best-effort)
                    gender = FirstNonEmpty(
                        ExtractLabeledValue(document, "性别"));
                    birthDate = FirstNonEmpty(
                        ExtractLabeledValue(document, "出生日期"),
                        ExtractLabeledValue(document, "生日"));
                    birthPlace = FirstNonEmpty(
                        ExtractLabeledValue(document, "出生地"),
                        ExtractLabeledValue(document, "籍贯"));
                    occupation = FirstNonEmpty(
                        ExtractLabeledValue(document, "职业"),
                        ExtractLabeledValue(document, "工作"));
                    otherNames = FirstNonEmpty(
                        ExtractLabeledValue(document, "更多中文名"),
                        ExtractLabeledValue(document, "更多外文名"),
                        ExtractLabeledValue(document, "别名"));

                    websiteUrl = ExtractWebsiteUrl(document);
                    doubanType = "personage";
                    profileUrl = $"https://www.douban.com/personage/{authorId}/";
                }
            }

            // Fallback: try author page on book.douban.com if we still don't have an avatar
            if (string.IsNullOrWhiteSpace(avatarUrl) || string.IsNullOrWhiteSpace(summary))
            {
                using var response2 = await authorClient.GetAsync($"https://book.douban.com/author/{authorId}/", cancellationToken);
                if (response2.IsSuccessStatusCode)
                {
                    var html2 = await response2.Content.ReadAsStringAsync(cancellationToken);
                    var document2 = new HtmlDocument();
                    document2.LoadHtml(html2);
                    var ogImage2 = document2.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", null);
                    if (string.IsNullOrWhiteSpace(avatarUrl))
                    {
                        avatarUrl = NormalizeAvatarUrl(ogImage2);
                        if (string.IsNullOrWhiteSpace(avatarUrl))
                        {
                            var firstImg = document2.DocumentNode.SelectSingleNode("//img[contains(@src,'/view/author/') or contains(@src,'/view/personage/')]");
                            var src2 = firstImg?.GetAttributeValue("src", null);
                            avatarUrl = NormalizeAvatarUrl(src2);
                        }
                        if (!string.IsNullOrWhiteSpace(avatarUrl))
                        {
                            // avatarSource = "douban-author";
                        }
                    }

                    var ogDesc = document2.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", null);
                    if (string.IsNullOrWhiteSpace(summary) && !string.IsNullOrWhiteSpace(ogDesc))
                    {
                        summary = NormalizeWhitespace(HtmlEntity.DeEntitize(ogDesc));
                    }

                    websiteUrl ??= ExtractWebsiteUrl(document2);
                    doubanType ??= "author";
                    profileUrl ??= $"https://book.douban.com/author/{authorId}/";
                }
            }

            // Verify avatar URL if present
            if (!string.IsNullOrWhiteSpace(avatarUrl))
            {
                var verified = await VerifyImageUrlAsync(authorClient, avatarUrl!, cancellationToken);
                if (!verified)
                {
                    _logger.LogWarning("Avatar URL did not verify for author {AuthorId}: {AvatarUrl}", authorId, avatarUrl);
                    avatarUrl = null;
                    // avatarSource = null;
                }
            }

            return new AuthorProfile(
                Summary: summary,
                NotableWorks: worksText,
                AvatarUrl: avatarUrl,
                // AvatarSource: avatarSource,
                Gender: gender,
                BirthDate: birthDate,
                BirthPlace: birthPlace,
                Occupation: occupation,
                OtherNames: otherNames,
                WebsiteUrl: websiteUrl,
                DoubanId: authorId,
                DoubanType: doubanType,
                ProfileUrl: profileUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch author profile from Douban for author ID {AuthorId}", authorId);
            return null;
        }
    }

    private async Task<List<CommunityQuote>> FetchCommunityQuotesAsync(HttpClient client, string subjectId, CancellationToken cancellationToken)
    {
        var results = new List<CommunityQuote>(MaxItems);

        try
        {
            using var response = await client.GetAsync($"subject/{subjectId}/blockquotes?sort=score", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Douban blockquotes request for subject {SubjectId} failed with status {Status}.",
                    subjectId,
                    (int)response.StatusCode);
                return results;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var nodes = document.DocumentNode
                .SelectNodes("//div[contains(@class,'blockquote-list')]//li/figure");
            if (nodes is null)
            {
                return results;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var figure in nodes)
            {
                if (results.Count >= MaxItems)
                {
                    break;
                }

                var fragments = figure.ChildNodes
                    .Where(node => node.NodeType == HtmlNodeType.Text)
                    .Select(node => NormalizeWhitespace(HtmlEntity.DeEntitize(node.InnerText)))
                    .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
                    .ToArray();

                var text = string.Join(" ", fragments).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (!seen.Add(text))
                {
                    continue;
                }

                var sourceNode = figure.SelectSingleNode(".//figcaption");
                var source = sourceNode is null ? null : NormalizeWhitespace(HtmlEntity.DeEntitize(sourceNode.InnerText));
                results.Add(new CommunityQuote(text, source));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Douban quotes for subject {SubjectId}.", subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching Douban quotes for subject {SubjectId}.", subjectId);
        }

        return results;
    }

    private async Task<List<CommunityRemark>> FetchCommunityRemarksAsync(HttpClient client, string subjectId, CancellationToken cancellationToken)
    {
        var results = new List<CommunityRemark>(MaxItems);

        try
        {
            using var response = await client.GetAsync($"subject/{subjectId}/comments/?status=P", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Douban comments request for subject {SubjectId} failed with status {Status}.",
                    subjectId,
                    (int)response.StatusCode);
                return results;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var commentNodes = document.DocumentNode
                .SelectNodes("//div[@id='comments']//li[contains(@class,'comment-item')]");
            if (commentNodes is null)
            {
                return results;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in commentNodes)
            {
                if (results.Count >= MaxItems)
                {
                    break;
                }

                var contentNode = node.SelectSingleNode(".//p[contains(@class,'comment-content')]//span[contains(@class,'short')]");
                var content = contentNode is null ? null : NormalizeWhitespace(HtmlEntity.DeEntitize(contentNode.InnerText));
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                if (!seen.Add(content))
                {
                    continue;
                }

                var authorNode = node.SelectSingleNode(".//span[contains(@class,'comment-info')]/a[1]");
                var author = authorNode is null ? null : NormalizeWhitespace(HtmlEntity.DeEntitize(authorNode.InnerText));

                var voteNode = node.SelectSingleNode(".//span[contains(@class,'comment-vote')]//span[contains(@class,'vote-count')]");
                var votes = voteNode is null ? null : NormalizeWhitespace(voteNode.InnerText);

                string? title = author;
                if (!string.IsNullOrWhiteSpace(votes) && int.TryParse(votes, out var voteCount) && voteCount > 0)
                {
                    title = string.IsNullOrWhiteSpace(title)
                        ? $"{voteCount} 有用"
                        : $"{title} · {voteCount} 有用";
                }

                results.Add(new CommunityRemark(content, title));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Douban comments for subject {SubjectId}.", subjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching Douban comments for subject {SubjectId}.", subjectId);
        }

        return results;
    }

    private static List<BookQuote> MapQuotes(int bookId, string normalizedAuthor, IReadOnlyList<CommunityQuote> quotes)
    {
        var entities = new List<BookQuote>(quotes.Count);
        foreach (var quote in quotes)
        {
            var text = CreateBookRequest.TrimToLength(quote.Text, 500, allowEmpty: true);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var entity = new BookQuote
            {
                BookId = bookId,
                Text = text,
                Author = normalizedAuthor,
                Source = CreateBookRequest.TrimToLength(quote.Source, 200),
                Origin = BookQuoteSource.Community,
                AddedOn = DateTimeOffset.UtcNow
            };

            entities.Add(entity);
        }

        return entities;
    }

    private static List<BookRemark> MapRemarks(int bookId, IReadOnlyList<CommunityRemark> remarks)
    {
        var entities = new List<BookRemark>(remarks.Count);
        foreach (var remark in remarks)
        {
            var content = CreateBookRequest.TrimToLength(remark.Content, 4000, allowEmpty: true);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var entity = new BookRemark
            {
                BookId = bookId,
                Title = CreateBookRequest.TrimToLength(remark.Title, 200),
                Content = content,
                Type = BookRemarkType.Community,
                AddedOn = DateTimeOffset.UtcNow
            };

            entities.Add(entity);
        }

        return entities;
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var chars = value
            .Where(ch => !char.IsControl(ch) || ch == '\n' || ch == '\r' || ch == '\t')
            .ToArray();
        var normalized = new string(chars);
        return System.Text.RegularExpressions.Regex.Replace(normalized, "\\s+", " ").Trim();
    }

    private sealed record CommunityQuote(string Text, string? Source);

    private sealed record CommunityRemark(string Content, string? Title);

    private static string? ExtractLabeledValue(HtmlDocument document, string label)
    {
        // Try common structures: <li><span>Label</span> Value</li>
        var node = document.DocumentNode.SelectSingleNode($"//li[span[contains(normalize-space(text()),'{label}')]]");
        if (node is not null)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText);
            text = NormalizeWhitespace(text);
            var parts = text.Split(':', '：');
            if (parts.Length >= 2)
            {
                return parts[1].Trim();
            }
        }

        // Fallback: any element with text starting with label followed by colon
        var any = document.DocumentNode.SelectSingleNode($"//*[contains(normalize-space(text()),'{label}')]");
        if (any is not null)
        {
            var text = NormalizeWhitespace(HtmlEntity.DeEntitize(any.InnerText));
            var idx = text.IndexOf(':');
            if (idx < 0) idx = text.IndexOf('：');
            if (idx >= 0 && idx + 1 < text.Length)
            {
                return text[(idx + 1)..].Trim();
            }
        }

        return null;
    }

    private static string? ExtractWebsiteUrl(HtmlDocument document)
    {
        var selectors = new[]
        {
            "//a[contains(text(),'官网') or contains(text(),'网站') or contains(text(),'主页') or contains(text(),'博客') or contains(translate(text(),'BLOG','blog'),'blog')]",
            "//div[contains(@class,'info')]//a[starts-with(@href,'http')]"
        };

        foreach (var sel in selectors)
        {
            var anchor = document.DocumentNode.SelectSingleNode(sel);
            var href = anchor?.GetAttributeValue("href", null);
            if (!string.IsNullOrWhiteSpace(href))
            {
                if (!href.Contains("douban.com", StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeAvatarUrl(href);
                }
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v;
            }
        }
        return null;
    }

    private static string? NormalizeAvatarUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed[7..];
        }

        // Upgrade known Douban avatar sizes if possible
        if (trimmed.Contains("/view/personage/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Replace("/personage/m/", "/personage/l/", StringComparison.OrdinalIgnoreCase)
                             .Replace("/personage/s/", "/personage/l/", StringComparison.OrdinalIgnoreCase);
        }

        return trimmed;
    }

    private async Task<string?> TryCanonicalizeAuthorIdAsync(string authorId, CancellationToken cancellationToken)
    {
        try
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BookWise/1.0 (+https://bookwise.local)");

            var requestUrl = $"https://book.douban.com/author/{authorId}/";
            using var response = await http.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
            {
                var location = response.Headers.Location?.ToString();
                if (!string.IsNullOrWhiteSpace(location))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(location, @"/personage/(\d+)/?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var canonical = m.Groups[1].Value;
                        if (!string.Equals(canonical, authorId, StringComparison.Ordinal))
                        {
                            _logger.LogInformation("Canonicalized Douban author ID {Original} -> {Canonical}", authorId, canonical);
                        }
                        return canonical;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to canonicalize Douban author ID {AuthorId}", authorId);
        }

        return null;
    }

    private async Task<bool> VerifyImageUrlAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using (var head = new HttpRequestMessage(HttpMethod.Head, url))
            using (var response = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                {
                    if (response.Content.Headers.ContentType?.MediaType is string mediaType && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        var length = response.Content.Headers.ContentLength;
                        if (!length.HasValue || length.Value > 0)
                        {
                            return true;
                        }
                    }
                }
            }

            // Fallback: GET headers if HEAD not supported
            using var get = new HttpRequestMessage(HttpMethod.Get, url);
            using var getResponse = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)getResponse.StatusCode >= 200 && (int)getResponse.StatusCode < 300)
            {
                if (getResponse.Content.Headers.ContentType?.MediaType is string mt && mt.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    var len = getResponse.Content.Headers.ContentLength;
                    if (!len.HasValue || len.Value > 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Avatar verification failed for {Url}", url);
        }

        return false;
    }

    private sealed record AuthorProfile(
        string? Summary,
        string? NotableWorks,
        string? AvatarUrl,
        // string? AvatarSource,
        string? Gender,
        string? BirthDate,
        string? BirthPlace,
        string? Occupation,
        string? OtherNames,
        string? WebsiteUrl,
        string? DoubanId,
        string? DoubanType,
        string? ProfileUrl);
}
