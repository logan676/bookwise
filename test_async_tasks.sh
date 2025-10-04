#!/bin/bash

# Test the new async tasks functionality
echo "Testing book creation with async tasks..."

# Create a book with Douban subject ID to trigger async tasks
curl -X POST http://localhost:5063/api/books \
  -H "Content-Type: application/json" \
  -d '{
    "Title": "长安的荔枝",
    "Author": "马伯庸",
    "Description": "描述唐代长安的历史小说",
    "Quote": "在历史的长河中，总有一些故事值得铭记",
    "Category": "历史小说",
    "Isbn": "9787547734568",
    "DoubanSubjectId": "35073226",
    "Status": "plan-to-read",
    "IsFavorite": false,
    "PersonalRating": null,
    "PublicRating": 8.5,
    "AuthorAvatarUrl": "https://i.pravatar.cc/160?u=马伯庸"
  }' | jq

echo -e "\nBook created! The async tasks should now be running in the background..."
echo "Check the application logs to see the community content refresh progress."