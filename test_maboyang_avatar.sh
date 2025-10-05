#!/bin/bash

# Test script to verify the avatar caching functionality with 马伯庸

echo "Testing avatar caching with 马伯庸..."

# Wait a moment for app to be ready
sleep 5

echo "Adding book with author 马伯庸..."

curl -s -X POST http://localhost:5062/api/books \
  -H "Content-Type: application/json" \
  -d '{
    "Title": "长安的荔枝",
    "Author": "马伯庸",
    "Description": "描述唐代长安的历史小说",
    "Quote": "在历史的长河中，总有一些故事值得铭记",
    "Category": "历史小说",
    "Isbn": "9787547734568",
    "DoubanSubjectId": "36593622",
    "Status": "plan-to-read",
    "IsFavorite": false,
    "PersonalRating": null,
    "PublicRating": 8.5,
    "CoverImageUrl": "https://img2.doubanio.com/view/subject/l/public/s34062755.jpg"
  }' | jq .

echo ""
echo "Book added! Now checking Explore page at http://localhost:5062/Explore"
echo "The author avatar should be cached and served from local storage."