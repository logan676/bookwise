# BookWise

BookWise is a lightweight ASP.NET Core web application for keeping a personal library. It lets you search, add, update, favorite, and delete books that are stored locally in a SQLite database. The UI mirrors the provided mock-up and the backend exposes a small REST API that the client-side script consumes.

Whether you're skimming the catalog or wiring in new features, this document walks through the essentials for getting the application running locally, understanding the project layout, and troubleshooting common setup issues.

## Features
- Browse your library with instant search by title or author.
- Save new books with description, category, rating, and favorite flag.
- Toggle favorites and delete entries directly from the grid.
- Minimal REST API implemented with ASP.NET Core Minimal APIs.
- SQLite persistence handled through Entity Framework Core with automatic migrations and seed data.

## Tech Stack
- .NET 8 / ASP.NET Core Razor Pages
- Entity Framework Core + SQLite
- Vanilla JavaScript for client interactions
- CSS styled to match the provided mockup

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- (Optional) [SQLite CLI](https://sqlite.org/download.html) if you want to inspect the database manually
- (Optional) Install the `dotnet-ef` CLI globally so migrations can be applied without a local tool manifest:
  ```bash
  dotnet tool install --global dotnet-ef
  ```

### Clone and Run
```bash
# Clone the repository
git clone git@github.com:logan676/bookwise.git
cd bookwise

# Restore and build
dotnet restore
dotnet build

# Apply database migrations (creates bookwise.db with seed data)
dotnet ef database update \
  --project BookWise.Web/BookWise.Web.csproj \
  --startup-project BookWise.Web/BookWise.Web.csproj

# Run the web app
dotnet run --project BookWise.Web/BookWise.Web.csproj
```

The app listens on `https://localhost:7240` and `http://localhost:5240` by default. Opening the home page shows the search bar, empty state, and seeded sample books.

If you prefer hot reload while iterating on Razor Pages or CSS/JS, run the project with the `watch` command instead:

```bash
dotnet watch --project BookWise.Web/BookWise.Web.csproj
```

### Project Structure

```
BookWise.Web/
├── Data/                # EF Core DbContext, migrations, and seed helpers
├── Models/              # Domain models and DTOs shared across API + UI
├── Pages/               # Razor Pages for the UI experience
├── wwwroot/             # Static assets (CSS, JS, fonts)
└── Program.cs           # Minimal API endpoints and Razor Pages setup
```

### SQLite Database
- The database file lives at `BookWise.Web/Data/bookwise.db`.
- When the app starts, it ensures the database exists, applies pending migrations, and seeds a few books if the table is empty.
- You can reset the database by deleting `bookwise.db` and rerunning the migration command. Seed data will be recreated automatically.

### API Endpoints
| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| `GET`  | `/api/books?search=term&onlyFavorites=true&category=genre` | Returns up to 25 books filtered by optional query parameters. |
| `GET`  | `/api/books/{id}` | Returns a single book by ID. |
| `POST` | `/api/books` | Creates a new book; expects JSON matching the `CreateBookRequest` schema. |
| `PUT`  | `/api/books/{id}` | Updates a book in place. |
| `DELETE` | `/api/books/{id}` | Removes a book. |

All endpoints return standard HTTP status codes and validation errors are emitted as RFC 7807 problem responses.

## Development Notes
- Client-side behavior lives in `BookWise.Web/wwwroot/js/site.js`.
- Styles are defined in `BookWise.Web/wwwroot/css/site.css`.
- Minimal API requests reuse the validation attributes defined on `CreateBookRequest` and `UpdateBookRequest` in `Program.cs`.
- Razor Pages and API endpoints share the same project so you can refactor shared logic (such as validation helpers) without having to juggle multiple projects.

## Troubleshooting

- **`dotnet ef` command not found** – Make sure the `dotnet-ef` tool is installed globally (see prerequisites) or use a tool manifest with `dotnet new tool-manifest`.
- **Database schema mismatch** – Delete `BookWise.Web/Data/bookwise.db` and rerun `dotnet ef database update` to regenerate the file with the latest migrations.
- **HTTPS certificate prompts on first run** – Trust the development certificate with `dotnet dev-certs https --trust` if the browser refuses the local HTTPS endpoint.
- **Static assets not updating** – When using `dotnet run`, browser caching may hold on to stale CSS/JS. Run with `dotnet watch` for automatic rebuilds and disable cache in DevTools while testing.

Feel free to customize the UI or extend the data model to support additional metadata such as reading status, notes, or reviews.
