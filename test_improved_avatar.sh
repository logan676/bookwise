#!/bin/bash

# Test the improved author avatar functionality
echo "Testing improved author avatar extraction..."

# Create a book with Douban subject ID to trigger improved avatar fetching
echo "Adding book '长安的荔枝' by 马伯庸 with Douban subject ID 36593622..."
curl -X POST http://localhost:5064/api/books \
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
    "AuthorAvatarUrl": "https://i.pravatar.cc/160?u=马伯庸"
  }' | jq

echo ""
echo "Waiting 10 seconds for async processing..."
sleep 10

echo ""
echo "Checking author information to see if avatar was updated:"
curl -s http://localhost:5064/api/books | jq '.[] | select(.title == "长安的荔枝") | {title, author, authorDetails}'

echo ""
echo "Test completed. Check the logs for avatar extraction information."