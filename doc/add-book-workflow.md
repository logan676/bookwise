# Adding a Book: End-to-End Workflow

This document explains what happens when you add a book to your library, with a focus on how recommended authors are generated and how author information is fetched and enriched behind the scenes.

- UI entrypoint: `BookWise.Web/Pages/AddBook.cshtml.cs:1`
- API entrypoint: `BookWise.Web/Program.cs:320`
- Author resolution: `BookWise.Web/Services/Authors/AuthorResolver.cs:1`
- Recommendation pipeline: `BookWise.Web/Services/Recommendations/*`
- Community content + author profile fetch: `BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:1`

## High-Level Flow

1. Validate request and normalize data.
2. Resolve the author (find or create; set placeholder avatar if none).
3. Create the `Book` entity, sync quote snapshot, and save.
4. Schedule background jobs:
   - Author recommendations for the author you just added.
   - Community content fetch (quotes, comments) and author profile enrichment via Douban.
5. Explore page later shows aggregated recommendations and author cards.

---

## 1) Request Validation and Normalization

- Both the Razor Page (`AddBook.cshtml.cs`) and the API (`POST /api/books`) accept a `CreateBookRequest`.
- `CreateBookRequest.WithNormalizedData()` trims, bounds, and coerces fields:
  - Title/Author/Description/Quote/Cover/Category/Publisher are trimmed and capped to fixed lengths.
  - `ISBN` is parsed to extract a valid ISBN-13 or ISBN-10 segment; other characters ignored.
  - `DoubanSubjectId` accepts either a numeric id or a full `https://book.douban.com/subject/{id}/` URL; it normalizes to `{id}`.
  - Ratings are clamped to [0, 5] and rounded to 1 decimal place.

References:
- Normalize helpers and `ToEntity(...)`: `BookWise.Web/Program.cs:640`
- ISBN parsing: `BookWise.Web/Program.cs:804`
- Douban subject id normalization: `BookWise.Web/Program.cs:924`

## 2) Author Resolution

- The system ensures a canonical author row using `AuthorResolver.GetOrCreateAsync(...)`:
  - Normalizes the display name and builds a lowercase `NormalizedName` for uniqueness.
  - Updates an existing author if found; otherwise creates a new one.
  - If no external avatar is supplied, sets a neutral local placeholder (`/img/author-placeholder.svg`).

Reference: `BookWise.Web/Services/Authors/AuthorResolver.cs:1`

## 3) Book Creation and Quote Snapshot

- A `Book` is created and linked to the resolved author.
- If a quote was provided, a quote “snapshot” is created or updated in `book.Quotes` with origin `Snapshot`.
- Custom remarks (your own notes) are normalized and added to `book.Remarks`.

References:
- ToEntity + remarks + quote snapshot creation: `BookWise.Web/Program.cs:665`
- Sync snapshot on create/update: `BookWise.Web/Program.cs:967`

## 4) Background Jobs Scheduled on Create

Once the book is saved, two async pipelines are scheduled.

- Author recommendations: `IAuthorRecommendationScheduler.ScheduleRefreshForAuthorsAsync(...)`
- Community content + author profile: `IBookCommunityContentScheduler.ScheduleFetchAsync(bookId, DoubanSubjectId)`

Reference (scheduling): `BookWise.Web/Program.cs:360`

---

## Recommended Authors: Algorithm and Pipeline

This is a producer/consumer pipeline backed by a background worker.

- Queue and worker:
  - Scheduler enqueues a `AuthorRecommendationWorkItem` for the focus author(s): `AuthorRecommendationScheduler.cs`.
  - Background service consumes the queue and invokes `AuthorRecommendationRefresher.RefreshAsync(...)`: `AuthorRecommendationWorker.cs`.

- Focus set and library context:
  - The refresher loads all library authors (authors with at least one book).
  - If the work item is a partial refresh, it filters to the requested focus author(s); otherwise full refresh for all authors.

- Retrieval (DeepSeek LLM-backed):
  - Calls `IDeepSeekRecommendationClient.GetRecommendedAuthorsAsync(focusAuthor, libraryAuthors)`.
  - The client builds a constrained JSON-only prompt that:
    - Names the focus author (the one just added or updated).
    - Provides a limited context of known library authors (`MaxAuthorContextCount`, default 50) to bias suggestions.
    - Asks for a strict JSON array of items: `{ name, rationale, imageUrl, confidence }`, capped to `RecommendationCount` (default 6).
  - Sends the request to the configured `DeepSeekOptions.Endpoint` with `Authorization: Bearer {ApiKey}`.
  - Parses the JSON; if multiple choices are returned, uses the first valid JSON array that parses.

- Persistence and de-duplication:
  - Existing recommendations for `FocusAuthor` are removed and replaced (idempotent refresh per focus author).
  - For each suggestion:
    - `RecommendedAuthor` is deduped case-insensitively within the refresh batch.
    - Fields are truncated to model limits (`FocusAuthor`/`RecommendedAuthor` 200, `Rationale` 1000, `ImageUrl` 500).
    - `ConfidenceScore` is clamped to [0, 1].
    - Row is stored in `AuthorRecommendations` with a unique index on `(FocusAuthor, RecommendedAuthor)`.

Key files:
- Refresher (focus set, persistence): `BookWise.Web/Services/Recommendations/AuthorRecommendationRefresher.cs:1`
- Client (prompt + parse): `BookWise.Web/Services/Recommendations/DeepSeekRecommendationClient.cs:1`
- Queue/worker: `BookWise.Web/Services/Recommendations/AuthorRecommendationScheduler.cs:1`, `AuthorRecommendationWorker.cs:1`
- Options (tuning): `BookWise.Web/Options/DeepSeekOptions.cs:1`
- Model: `BookWise.Web/Models/AuthorRecommendation.cs:1`

### How the UI selects which recommendations to display

The Explore page loads stored recommendations and surfaces a concise set:
- Orders by `ConfidenceScore` desc, then `GeneratedAt` desc.
- Deduplicates by `RecommendedAuthor`, keeping the highest-confidence recent one.
- Returns up to 6 items, filling missing images from an existing author avatar if present, otherwise a placeholder.

Reference: `BookWise.Web/Pages/Explore.cshtml.cs:185`

---

## Author Info Fetching and Enrichment (Douban)

Triggered when the book has a normalized `DoubanSubjectId`.

1. Safety checks
   - Retrieves the book (with `Quotes`, `Remarks`, `AuthorDetails`).
   - Skips if the book was deleted or if the `DoubanSubjectId` changed since scheduling.

2. Fetch author metadata
   - Extracts the Douban author/personage id from the book’s subject page (`/subject/{id}/`), trying multiple CSS/XPath selectors.
   - Canonicalizes the id via `https://book.douban.com/author/{id}/` redirects to a `personage/{id}` when possible.
   - Fetches the richer `https://www.douban.com/personage/{id}/` page first, then falls back to `https://book.douban.com/author/{id}/` if needed.
   - Parses:
     - Avatar URL: prefers `og:image`, falls back to first portrait image; normalized to https and upscaled for known Douban sizes.
     - Summary/introduction: prefers explicit modules; otherwise `og:description`.
     - Metadata: gender, birth date/place, occupation, other names, website, Douban profile URL, type.
   - Verifies the avatar URL via HEAD/GET to ensure it’s a valid, non-empty image; clears it if verification fails.
   - Writes the author fields on the `Author` row and persists these changes early to avoid loss if later steps fail.

3. Community quotes and remarks (top 3 each)
   - Quotes: GET `subject/{id}/blockquotes?sort=score`, parse `figure` nodes, de-duplicate by text, map to `BookQuote` with origin `Community`.
   - Remarks: GET `subject/{id}/comments/?status=P`, parse comment items, de-duplicate by content, map to `BookRemark` of type `Community`.
   - Replaces prior community-sourced quotes/remarks for this book atomically, then saves.

References:
- Refresher (all logic): `BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:1`
- Scheduler/worker: `BookWise.Web/Services/CommunityContent/BookCommunityContentScheduler.cs:1`, `BookCommunityContentWorker.cs:1`

### Avatar caching (serving optimized local URLs)

- For external avatar URLs (like Douban), the app exposes an avatar cache service:
  - On-demand endpoint: `/api/authors/photo?name=...` resolves to a cached local URL when ready, otherwise a placeholder.
  - Scheduled background task periodically downloads and stores avatars under `wwwroot/cache/avatars`.

References:
- API endpoints: `BookWise.Web/Program.cs:240`
- Cache service: `BookWise.Web/Services/Caching/AvatarCacheService.cs:1`
- Background service: `BookWise.Web/Services/Background/AvatarCacheBackgroundService.cs:1`

---

## Data Model Overview

- `Authors`
  - Identity: `Name`, `NormalizedName`
  - Profile: `AvatarUrl`, `ProfileSummary`, `ProfileNotableWorks`, `Gender`, `BirthDate`, `BirthPlace`, `Occupation`, `OtherNames`, `WebsiteUrl`
  - Douban linkage: `DoubanAuthorId`, `DoubanAuthorType`, `DoubanProfileUrl`, `ProfileRefreshedAt`

- `Books`
  - Core: `Title`, `Author`, `AuthorId`, `Description`, `CoverImageUrl`, `Category`, `Publisher`, `ISBN`, `DoubanSubjectId`, `Status`, `IsFavorite`, `PersonalRating`, `PublicRating`
  - Relations: `Quotes` (snapshot + community), `Remarks` (mine + community)

- `AuthorRecommendations`
  - `FocusAuthor`, `RecommendedAuthor`, optional `Rationale`, `ImageUrl`, `ConfidenceScore`, `GeneratedAt`
  - Unique index `(FocusAuthor, RecommendedAuthor)` prevents duplicates on refresh

Reference: `BookWise.Web/Data/BookWiseContext.cs:1`

---

## Failure Handling and Idempotency

- Validation errors are returned to the caller; the book is not created.
- Background jobs are best-effort and safe to re-run:
  - Recommendation refresh replaces prior rows for a focus author.
  - Community content refresh replaces the book’s community quotes/remarks and re-saves author profile fields.
  - Network failures are logged and skipped; subsequent runs can fill in gaps.

---

## Configuration

- DeepSeek client uses `DeepSeekOptions`:
  - `Endpoint` (URL), `ApiKey`, `Model` (default `deepseek-chat`)
  - `RecommendationCount` (default 6)
  - `MaxAuthorContextCount` (default 50)

Reference: `BookWise.Web/Options/DeepSeekOptions.cs:1`

---

## Summary

When you add a book, the system immediately persists the book and author, then kicks off two background tasks: one that uses an LLM to generate recommended authors tailored to your library, and one that fetches and normalizes author profiles plus top community quotes/remarks from Douban. The Explore page later surfaces the highest-confidence suggestions and enriched author cards.

