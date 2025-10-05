# Category and Publisher Field Fix

## Problem

The Books table was incorrectly storing publisher information in the `Category` column when no actual category/genre could be determined from Douban book data. This was caused by the `InferCategory` method falling back to publisher information when no real category could be found.

## Solution

### 1. Database Schema Changes

- Added a new `Publisher` column to the Books table (max length: 200)
- The `Category` column remains unchanged to preserve existing valid category data
- Applied migration: `20251005052650_FixPublisherAndCategoryData`

### 2. Model Updates

- Updated `Book` model to include `Publisher` property
- Updated `CreateBookRequest` record to include `Publisher` parameter
- Updated `UpdateBookRequest` record to include `Publisher` parameter
- Updated `BookSuggestion` record to include `Publisher` parameter

### 3. API Logic Changes

- **Fixed `InferCategory` method**: Removed publisher fallbacks (`出版社`, `出品方`) - these now only return true category information (tags, series, keyword matching)
- **Added `ExtractPublisher` method**: New method specifically for extracting publisher information from Douban book data
- **Updated `ParseBookDetails`**: Now calls both `InferCategory` and `ExtractPublisher` to populate both fields correctly
- Updated all request/response handling to include publisher information

### 4. Frontend Changes

- Updated JavaScript in `AddBook.cshtml` to handle publisher data
- Added publisher badge display in search results
- Updated book data extraction and payload building to include publisher

### 5. Database Context Updates

- Added Publisher property configuration in `BookWiseContext`
- Set appropriate max length constraint (200 characters)

## Testing

### API Response Example

**Before Fix:**

```json
{
  "title": "长安的荔枝",
  "category": "湖南文艺出版社", // Publisher incorrectly in category
  "publisher": null
}
```

**After Fix:**

```json
{
  "title": "长安的荔枝",
  "category": "博集天卷·马伯庸作品", // Actual series/category
  "publisher": "湖南文艺出版社" // Publisher in correct field
}
```

### Verified Functionality

1. ✅ Book search properly separates category and publisher data
2. ✅ Adding books via API works with both fields
3. ✅ Existing books retain their current category data
4. ✅ New publisher field is properly null for existing records
5. ✅ Frontend displays both category and publisher badges
6. ✅ Database migration applied successfully

## Data Migration

Existing books in the database are not automatically migrated because:

1. Most existing category data appears to be legitimate categories/series names
2. The previous logic was inconsistent - some books got real categories, others got publishers
3. Manual review would be needed to identify which specific records need publisher data moved

If needed, a manual data migration could be performed by:

1. Identifying books where category looks like a publisher name
2. Moving that data to the publisher field
3. Clearing or updating the category field appropriately

## Files Modified

### Backend

- `BookWise.Web/Models/Book.cs` - Added Publisher property
- `BookWise.Web/Data/BookWiseContext.cs` - Added Publisher configuration
- `BookWise.Web/Program.cs` - Updated all DTOs, API logic, and Douban parsing
- `BookWise.Web/Pages/AddBook.cshtml.cs` - Fixed constructor call
- `BookWise.Web/Migrations/20251005052650_FixPublisherAndCategoryData.cs` - New migration

### Frontend

- `BookWise.Web/Pages/AddBook.cshtml` - Updated JavaScript for publisher handling

The fix ensures that going forward, books added from Douban will have proper category information in the Category field and publisher information in the Publisher field, providing better data organization and searchability.
