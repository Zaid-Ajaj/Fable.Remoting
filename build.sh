#!/bin/bash
dotnet tool restore
dotnet paket restore
dotnet run --project ./build/Build.fsproj "$@"
