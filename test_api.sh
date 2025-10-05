#!/bin/bash

# Test script to reproduce the add book error

echo "Starting the application..."
cd /mnt/work/bookWise
nohup dotnet run --project BookWise.Web --urls "http://localhost:5063" > app.log 2>&1 &
APP_PID=$!

echo "Waiting for application to start..."
sleep 20

echo "Testing API..."
curl -s -X POST http://localhost:5063/api/books \
  -H "Content-Type: application/json" \
  -d '{
    "Title": "Test Book",
    "Author": "Test Author", 
    "Status": "plan-to-read",
    "IsFavorite": false
  }' 

echo -e "\n\nChecking application logs..."
tail -n 20 app.log

echo "Stopping application..."
kill $APP_PID