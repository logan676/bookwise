# Add Book Flow and Quotes Handling

This document explains what happens when you add a book to the library, with a focus on how quotes are created, fetched from the community, and displayed. It includes the exact logic and file references so you can cross‑check the behavior.

## Overview
- The API normalizes your input and creates a `Book` record.
- A snapshot of your provided quote (if any) is synced into `BookQuotes`.
- A background worker then fetches top community quotes and remarks from Douban and attaches them to the book.
- The Explore page renders “Quote of the Day” and grouped quotes using a recency‑based ordering.

Key components:
- API endpoints and quote snapshot logic: `BookWise.Web/Program.cs:360`, `BookWise.Web/Program.cs:967`, `BookWise.Web/Program.cs:1002`
- Community fetch worker: `BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:1`
- Explore page display logic: `BookWise.Web/Pages/Explore.cshtml.cs:292`

## Step‑by‑Step: Adding a Book
1) Request normalization and validation
- The create request is normalized (trims lengths, normalizes rating, status, ISBN, Douban Subject Id).
- Reference: `BookWise.Web/Program.cs:360` (create handler logs normalized payload and validates).

2) Author resolution
- The author is looked up or created, then normalized.
- Reference: `BookWise.Web/Program.cs:378`.

3) Entity creation
- The request is mapped to a `Book` entity.
- Reference: `BookWise.Web/Program.cs:381`.

4) Sync quote snapshot
- The system mirrors the book’s `Quote` into the `BookQuotes` table as a snapshot entry (`Origin = Snapshot`).
- Reference: call site `BookWise.Web/Program.cs:384`; implementation `BookWise.Web/Program.cs:967` and `BookWise.Web/Program.cs:1002`.

5) Save to database
- The book (and its snapshot quote, if any) are saved.
- Reference: `BookWise.Web/Program.cs:387` then `BookWise.Web/Program.cs:390`.

6) Schedule community content fetch
- Queues a background job to fetch community quotes and remarks (requires a Douban Subject Id).
- Reference: `BookWise.Web/Program.cs:403`–`BookWise.Web/Program.cs:411`, scheduler `BookWise.Web/Services/CommunityContent/BookCommunityContentScheduler.cs:1`, queue and worker `BookWise.Web/Services/CommunityContent/BookCommunityContentQueue.cs:1`, `BookWise.Web/Services/CommunityContent/BookCommunityContentWorker.cs:1`.

## Quote Snapshot: Exact Algorithm
When a book is created or updated, `SyncQuoteSnapshot(Book book)` ensures there is exactly one snapshot quote that mirrors `book.Quote`.

- If `book.Quote` is empty: remove any existing snapshot from `book.Quotes`.
- If `book.Quote` is present:
  - If no snapshot exists: create one using `CreateQuoteSnapshot` and add it.
  - If a snapshot exists: update its fields (do not replace `AddedOn`).

Reference: `BookWise.Web/Program.cs:967` (sync), `BookWise.Web/Program.cs:1002` (create).

CreateQuoteSnapshot details:
- `Text` = `quote` (trimmed to 500 by callers)
- `Author` = `book.Author` or `"Unknown"` if blank
- `Source` = `book.Title` (if present)
- `BackgroundImageUrl` = `book.CoverImageUrl` (if present)
- `Origin` = `Snapshot`
- `AddedOn` = current UTC time

Reference: `BookWise.Web/Program.cs:1002`.

Important nuance:
- Updating a book’s quote later updates the existing snapshot fields but does not reset `AddedOn` (so it won’t jump to the top in recency‑based displays). Reference: existing snapshot update path in `BookWise.Web/Program.cs:989`–`BookWise.Web/Program.cs:1000`.

## Community Quotes: Exact Algorithm
Community quotes are fetched asynchronously by the background worker if the book has a Douban Subject Id.

Worker entry point and guards:
- Validates the book still exists and the Douban Subject Id matches the queued work item.
- Reference: `BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:24`–`BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:40`.

Fetch quotes from Douban:
- Endpoint: `GET subject/{subjectId}/blockquotes?sort=score` (top quotes by Douban score).
- Parse the HTML and build up to `MaxItems = 3` quotes.
- For each `<figure>` in the list:
  - Extract visible text fragments directly under the `<figure>` (skip markup), normalize whitespace, and join into `Text`.
  - Deduplicate by `Text` (HashSet) to avoid repeats.
  - Extract `<figcaption>` as `Source` if present.
- Reference:
  - Constant `MaxItems = 3`: `BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:17`
  - Fetch/parse: `BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:483`

Persist quotes:
- Remove prior community quotes for this book.
- Map each scraped quote to an entity:
  - `BookId` = current book
  - `Text` = trimmed to 500
  - `Author` = normalized `book.Author`
  - `Source` = trimmed to 200 (from figcaption)
  - `Origin` = `Community`
  - `AddedOn` = current UTC time (import time)
- Reference:
  - Removal: `BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:81`–`BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:87`
  - Mapping: `BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:636`

Community remarks follow a similar top‑N pattern (not the main focus here). Reference: `BookWise.Web/Services/CommunityContent/BookCommunityContentRefresher.cs:566`.

## Display Order: How Quotes Are Evaluated
The Explore page determines display order based on `AddedOn` recency, not Douban score:

- Load recent quotes, then sort client‑side by `AddedOn` descending.
  - “Quote of the Day” = newest quote overall.
  - Remaining quotes are grouped by book, groups sorted by each group’s most recent `AddedOn`.
  - Within each group, quotes are sorted by `AddedOn` descending and truncated (default 4).
- Reference: `BookWise.Web/Pages/Explore.cshtml.cs:292` (query), `BookWise.Web/Pages/Explore.cshtml.cs:313`–`BookWise.Web/Pages/Explore.cshtml.cs:352` (ordering and grouping), `BookWise.Web/Pages/Explore.cshtml.cs:324`–`BookWise.Web/Pages/Explore.cshtml.cs:336` (quote of the day and fallback background).

Implications:
- Community quotes are fetched from a score‑sorted source, but once imported, BookWise displays them by import time (`AddedOn`).
- Snapshot quotes keep their original `AddedOn`; editing the book’s `Quote` later does not bump recency.
- If multiple community quotes are imported in one run, they share very similar `AddedOn` values; order among exact ties is not guaranteed (generally follows insertion, but do not rely on it).

Book details page behavior:
- Shows the book’s featured quote from `Book.Quote` only (not the community list). Reference: `BookWise.Web/Pages/BookDetails.cshtml:132`.

## Data Model and Constraints
- Entity: `BookQuote` with fields `Text`, `Author`, `Source`, `BackgroundImageUrl`, `Origin`, `AddedOn`, FK `BookId`.
- Length limits: `Text` 500, `Author` 200, `Source` 200, `BackgroundImageUrl` 500.
- Origin enum: `Snapshot` or `Community`.
- Defaults/indices configured in EF model.
- References: model `BookWise.Web/Models/BookQuote.cs:5`, configuration `BookWise.Web/Data/BookWiseContext.cs:139`.

## Error Handling and Edge Cases
- Missing Douban Subject Id: community fetch is skipped. Reference: scheduler `BookCommunityContentScheduler` and early returns in refresher.
- Subject Id changed after queueing: skip fetch to avoid mismatched imports. Reference: `BookCommunityContentRefresher` early return.
- Network/parse errors: worker logs and continues; existing data remains intact (previous community quotes are removed only if a new set is being applied in that run).
- Unicode punctuation and HTML entities are normalized before storage to keep quotes clean.

## Example Timeline
- T0: User adds a book with a quote → snapshot created (`AddedOn = T0`).
- T0 + Δ: Background worker imports top 3 community quotes → each has `AddedOn ≈ T0 + Δ`.
- Explore page:
  - Quote of the Day: one of the newly imported community quotes (most recent `AddedOn`).
  - Group for this book: shows up to 4 newest quotes for the book (community first due to recency), snapshot may appear after them if older.
- Later edit of book’s quote updates snapshot text but not its `AddedOn`, so it keeps its place in ordering.

## Why keep both Book.Quote and BookQuotes?
- `Book.Quote` gives a single “featured quote” for the book detail page.
- `BookQuotes` holds both the snapshot and community quotes for discovery experiences (Explore page).
- The snapshot ensures the featured quote also participates in discovery without coupling the detail page to community imports.

---

If you want display order to consider Douban score or to always prioritize the snapshot quote within a book, options include:
- Add a `DisplayRank` column and set it during import; then sort by `(DisplayRank, AddedOn)`.
- Or re‑sort within a book by `(Origin == Snapshot desc, AddedOn desc)`.
- Or update snapshot `AddedOn` when the book’s quote changes to bump it in recency.
