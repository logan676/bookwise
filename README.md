# BookWise

BookWise is a lightweight ASP.NET Core web application for keeping a personal library. It lets you search, add, update, favorite, and delete books that are stored locally in a SQLite database. The UI mirrors the provided mock-up and the backend exposes a small REST API that the client-side script consumes.

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

### Clone and Run
```bash
# Clone the repository
git clone git@github.com:logan676/bookwise.git
cd bookwise

# Restore and build
dotnet restore
dotnet build

# Apply database migrations (creates bookwise.db with seed data)
dotnet ef database update --project BookWise.Web/BookWise.Web.csproj --startup-project BookWise.Web/BookWise.Web.csproj

# Run the web app
dotnet run --project BookWise.Web/BookWise.Web.csproj
```

The app listens on `https://localhost:7240` and `http://localhost:5240` by default. Opening the home page shows the search bar, empty state, and seeded sample books.

### SQLite Database
- The database file lives at `BookWise.Web/Data/bookwise.db`.
- When the app starts, it ensures the database exists, applies pending migrations, and seeds a few books if the table is empty.

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

Feel free to customize the UI or extend the data model to support additional metadata such as reading status, notes, or reviews.
