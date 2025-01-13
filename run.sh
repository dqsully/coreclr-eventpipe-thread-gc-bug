#!/bin/bash

dotnet build -c Release

for threads in 0 10 100 500 1000 5000 10000; do
    echo "Threads: $threads";
    dotnet bin/Release/net9.0/eventpipe-proof.dll $threads 500000
    echo
done
