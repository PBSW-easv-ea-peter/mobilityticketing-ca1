#!/usr/bin/env bash
# Lecture 11 (cloud storage) helpers, adapted from the starter's lab.sh.
# Source this file from the repository root in Git Bash: source ./lab-l11.sh

# pwd -W gives a Windows path (C:/...) in Git Bash, which docker and dotnet accept
# even with MSYS_NO_PATHCONV set. Elsewhere it falls back to pwd -P.
LAB_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && { pwd -W 2>/dev/null || pwd -P; })"
LAB_BUCKET=mobility-archive-lab
LAB_KEY=validation-exports/2026-11-20/validations.jsonl

lab_compose() {
    docker compose --project-directory "$LAB_ROOT" -f "$LAB_ROOT/compose.yaml" "$@"
}

# MSYS_NO_PATHCONV stops Git Bash from rewriting container paths such as /work.
s3() {
    MSYS_NO_PATHCONV=1 lab_compose run --rm -T aws-l11 "$@"
}

# SQL is read from stdin; additional arguments are forwarded to psql.
sql() {
    MSYS_NO_PATHCONV=1 lab_compose exec -T postgres-l11 psql -X -U mobility -d mobility_l11 \
        -v ON_ERROR_STOP=1 "$@"
}

show_reports() {
    sql -c 'SELECT report_id, row_count, supersedes, version_id, sha256 FROM reports ORDER BY report_id;'
}

# The lab asks for this shorthand. DOTNET_ROLL_FORWARD lets the net8.0 app
# run on a machine that only has a newer .NET runtime installed.
report() {
    DOTNET_ROLL_FORWARD=Major dotnet run --project "$LAB_ROOT/src/MobilityTicketing.Reports" -- "$@"
}
