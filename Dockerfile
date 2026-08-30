FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG VERSION=0.0.0-dev
WORKDIR /app

COPY Inscribed.sln Directory.Build.props ./
COPY src/Inscribed.Domain/Inscribed.Domain.csproj              src/Inscribed.Domain/
COPY src/Inscribed.Application/Inscribed.Application.csproj    src/Inscribed.Application/
COPY src/Inscribed.Infrastructure/Inscribed.Infrastructure.csproj src/Inscribed.Infrastructure/
COPY src/Inscribed.Auth/Inscribed.Auth.csproj                  src/Inscribed.Auth/
COPY src/Inscribed.Auth.Issuer/Inscribed.Auth.Issuer.csproj    src/Inscribed.Auth.Issuer/
COPY src/Inscribed.Api/Inscribed.Api.csproj                    src/Inscribed.Api/
COPY src/Inscribed.Cli/Inscribed.Cli.csproj                    src/Inscribed.Cli/

RUN dotnet restore

COPY src/ src/

RUN dotnet publish src/Inscribed.Api/Inscribed.Api.csproj \
    -c Release \
    -o /app/publish \
    -p:Version=$VERSION \
    --no-restore

RUN dotnet publish src/Inscribed.Cli/Inscribed.Cli.csproj \
    -c Release \
    -o /app/publish \
    -p:Version=$VERSION \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "Inscribed.Api.dll"]
