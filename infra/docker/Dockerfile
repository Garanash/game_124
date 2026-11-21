# One image recipe for every server process: docker build --build-arg PROJECT=Server.Gateway .
ARG DOTNET_VERSION=9.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG PROJECT
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY server/ server/
COPY tools/ tools/
RUN dotnet publish server/src/${PROJECT}/${PROJECT}.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
ARG PROJECT
ENV REALMFORGE_ENTRY=Realmforge.${PROJECT}.dll \
    REALMFORGE_CONTENT=/app/content \
    DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production
WORKDIR /app
COPY --from=build /app ./
COPY content/ ./content/
USER app
ENTRYPOINT ["sh", "-c", "exec dotnet \"$REALMFORGE_ENTRY\""]
