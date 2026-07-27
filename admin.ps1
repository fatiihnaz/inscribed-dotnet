#!/usr/bin/env pwsh
$running = docker compose ps --status running --services 2>$null

if ($running -contains 'api') {
    docker compose exec api dotnet Inscribed.Cli.dll @args
}
else {
    docker compose run --rm admin @args
}

exit $LASTEXITCODE
