# syntax=docker/dockerfile:1.7

# ----- Build -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY api.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ----- Runtime ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ENV ASPNETCORE_URLS=http://+:5000 \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN groupadd --system kuvox \
 && useradd --system --gid kuvox --home /app --shell /usr/sbin/nologin kuvox

WORKDIR /app

COPY --from=build --chown=kuvox:kuvox /app/publish .

USER kuvox

EXPOSE 5000

ENTRYPOINT ["dotnet", "api.dll"]
