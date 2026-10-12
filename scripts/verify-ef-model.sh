#!/usr/bin/env sh
set -eu

dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/Swan.Data
