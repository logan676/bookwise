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
    private const int MaxItems = 10;

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
        var quotes = await FetchCommunityQuotesAsync(client, workItem.DoubanSubjectId, cancellationToken);
        var remarks = await FetchCommunityRemarksAsync(client, workItem.DoubanSubjectId, cancellationToken);

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
}
