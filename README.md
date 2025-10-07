# BookWise

BookWise is a lightweight ASP.NET Core application for curating a personal library. The backend exposes a small REST API backed by SQLite, while the Razor Pages frontend renders the shelves, author explorations, and client-side interactions that sit on top of that API.

## Architecture Overview
- **Frontend** – Razor Pages stored under `BookWise.Web/Pages` with a shared layout (`Pages/Shared/_Layout.cshtml`), component partials, and client-side behavior in `wwwroot/js/site.js`. Styling is provided by `wwwroot/css/site.css`, which defines the design system used across pages.
- **Backend** – Minimal API endpoints declared in `Program.cs` use Entity Framework Core (`BookWiseContext`) to persist `Book` entities to SQLite. The same project hosts the Razor Pages frontend, so both layers share models and validation attributes.

## Frontend Implementation
- **Dashboard shelves (`Pages/Index.cshtml`)** – Groups the authenticated user's books into status buckets (`reading`, `read`, and `plan-to-read`) using `IndexModel.OnGetAsync`. When no books exist the user is redirected to the add flow.
- **Add book experience (`Pages/AddBook.cshtml`)** – Presents a search-first UI with a scripted preview list and an `addBook` helper. The sample data and interactions live inline in the Razor page and can be swapped for real API calls.
- **Explore hub (`Pages/Explore.cshtml`)** – Renders a tabbed interface with authors, quotes, and recommendations sourced from the strongly-typed data assembled in `ExploreModel`.
- **Shared layout and cards** – `_Layout.cshtml` defines navigation, while `_BookShelfItem.cshtml` renders individual book tiles that are reused across sections.
- **Client-side script (`wwwroot/js/site.js`)** – Powers search, filtering, add-book form submission, and favorite toggles by calling `/api/books` with the Fetch API. It manages DOM state for result lists, empty states, optimistic UI, and basic error handling.
- **Styling (`wwwroot/css/site.css`)** – Uses CSS custom properties to standardize colors, spacing, and components (navigation, cards, forms). The file mirrors the mock-up with responsive flexbox and grid layouts.

## Backend Implementation
- **Program bootstrap (`Program.cs`)** – Configures Razor Pages, registers `BookWiseContext`, runs SQLite migrations, and seeds three starter books when the database is empty.
- **Book entity (`Models/Book.cs`)** – Describes the persisted model with validation attributes (title/author required, cover URL, category, ISBN, status, rating, timestamps).
- **Entity Framework Core (`Data/BookWiseContext.cs`)** – Provides the `DbContext`, enforces indexes, and stamps `UpdatedAt` on modifications.
- **REST API (`Program.cs`)** – `/api/books` is exposed as a minimal API group supporting:
  - query-based search with optional `search`, `onlyFavorites`, and `category` filters;
  - CRUD operations (`GET`, `POST`, `PUT`, `DELETE`) with validation driven by `CreateBookRequest` and `UpdateBookRequest` records;
  - JSON problem details when validation fails.

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- (Optional) [SQLite CLI](https://sqlite.org/download.html) for inspecting the database
- (Optional) `dotnet-ef` CLI for manual migrations:
  ```bash
  dotnet tool install --global dotnet-ef
  ```

### Install & Run
```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Apply database migrations (creates BookWise.Web/Data/bookwise.db)
dotnet ef database update \
  --project BookWise.Web/BookWise.Web.csproj \
  --startup-project BookWise.Web/BookWise.Web.csproj

# Launch the app
dotnet run --project BookWise.Web/BookWise.Web.csproj
```
The server listens on `https://localhost:7240` and `http://localhost:5240` by default.

For hot reload during UI work:
```bash
dotnet watch --project BookWise.Web/BookWise.Web.csproj
```
This refreshes Razor markup, static assets, and API changes on save.

## Project Structure
```
BookWise.Web/
├── Data/                # EF Core DbContext, migrations, and seed helpers
├── Models/              # Domain models shared by UI and API
├── Pages/               # Razor Pages, view models, and partials
├── wwwroot/             # Static assets (CSS, JS, fonts, icons)
└── Program.cs           # App bootstrap + minimal API definitions
```

## API Endpoints
| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| `GET`  | `/api/books?search=term&onlyFavorites=true&category=genre` | Returns up to 25 books filtered by optional query parameters. |
| `GET`  | `/api/books/{id}` | Returns a single book by ID. |
| `POST` | `/api/books` | Creates a new book; expects JSON matching `CreateBookRequest`. |
| `PUT`  | `/api/books/{id}` | Updates a book in place using `UpdateBookRequest`. |
| `DELETE` | `/api/books/{id}` | Removes a book permanently. |

All endpoints return standard HTTP status codes. Validation failures emit RFC 7807 problem responses, including member-specific error messages.

`CreateBookRequest` and `UpdateBookRequest` both require `status` to be one of `plan-to-read`, `reading`, or `read` in addition to `title` and `author`.

## Data Model & Persistence
- Database file lives at `BookWise.Web/Data/bookwise.db` (ignored by Git).
- `Book` fields cover descriptive metadata, read status (`plan-to-read`, `reading`, `read`), `IsFavorite`, rating, and timestamps.
- Migrations run automatically on startup; deleting the `.db` file and rerunning migrations resets the dataset with seeded titles.

## Development Notes
- JavaScript logic in `wwwroot/js/site.js` is self-contained and can be modularized if the project grows.
- CSS is plain, but the `Styles` directory is available if you prefer to introduce SCSS or PostCSS.
- Razor Pages and API live in the same project, simplifying shared validation and DTO reuse.

## Troubleshooting
- **`dotnet ef` command not found** – Install the global tool or add a tool manifest (see prerequisites).
- **Schema changes not applied** – Delete `BookWise.Web/Data/bookwise.db` and run the migration command again.
- **HTTPS warnings on first run** – Trust the development certificate with `dotnet dev-certs https --trust`.
- **Static assets seem stale** – When not using `dotnet watch`, clear your browser cache or restart the app so CSS/JS changes are re-served.

Feel free to tailor the UI, expand the book schema, or replace the Add Book stub with a real search integration.

## Health Check
- Endpoint: `/healthz` is mapped via `app.MapHealthChecks("/healthz")` in `BookWise.Web/Program.cs`.
- Local test:
  - `curl -i http://localhost:5240/healthz`
  - Expects HTTP 200 when the app is healthy.
- Azure Web App/Slot configuration:
  - In the Azure Portal, open your Web App (or `Deployment slots` → select the `staging` slot).
  - Go to `Monitoring` → `Health check`.
  - Toggle `Enable/On`.
  - Set `Path` to `/healthz` (must start with `/`).
  - Save. Azure will probe the endpoint and use it for slot warmup and restarts.
