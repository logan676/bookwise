#!/bin/bash

# Test script to verify the add book functionality

echo "Starting application..."
cd BookWise.Web
dotnet run --urls "http://localhost:5065" &
APP_PID=$!

# Wait for application to start
echo "Waiting for application to start..."
sleep 20

echo "Testing add book API..."

# Test 1: Valid request
echo "Test 1: Valid book request"
curl -s -X POST http://localhost:5065/api/books \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Test Book", 
    "author": "Test Author",
    "status": "plan-to-read",
    "isFavorite": false
  }' | jq .

echo ""
echo "Test 2: Invalid request (missing required fields)"
curl -s -X POST http://localhost:5065/api/books \
  -H "Content-Type: application/json" \
  -d '{
    "title": "", 
    "author": "",
    "status": "invalid-status",
    "isFavorite": false
  }' | jq .

# Clean up
echo ""
echo "Stopping application..."
kill $APP_PID