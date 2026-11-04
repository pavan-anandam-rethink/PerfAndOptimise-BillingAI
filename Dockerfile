FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
EXPOSE 7400
ENV ASPNETCORE_URLS=http://+:7400

# icu is required because globalization invariant mode is off (same as the legacy image).
RUN apk --no-cache add icu-libs icu-data-full

WORKDIR /app
RUN adduser -u 5678 --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /

ENV USE_NET6_ARTIFACTS_CREDENTIAL_PROVIDER=true
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh | sh

COPY ./global.json ./nuget.config ./Directory.Build.props ./Directory.Packages.props ./CodeMetricsConfig.txt ./.editorconfig ./coverlet.runsettings ./*.sln ./
COPY ./src/ ./src/
COPY ./tests/ ./tests/
COPY ./samples/ ./samples/
COPY ./deployment/ ./deployment/

ARG FEED_ACCESSTOKEN
ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://rethinkfirst.pkgs.visualstudio.com/RethinkFirst/_packaging/Rethink-Common/nuget/v3/index.json\", \"password\":\"${FEED_ACCESSTOKEN}\"},{\"endpoint\":\"https://rethinkfirst.pkgs.visualstudio.com/RethinkFirst/_packaging/RethinkBHFeed/nuget/v3/index.json\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}"

RUN dotnet restore

RUN dotnet tool install --tool-path /tools dotnet-reportgenerator-globaltool --version 5.4.1
RUN dotnet test --no-restore --settings coverlet.runsettings --collect:"XPlat Code Coverage" --logger "trx" --results-directory ./TestResults
RUN /tools/reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:"Html;Cobertura"

ARG ENTRY_PROJECT=Rethink.BH.Billing.ClearingHouse.Inbound.Api
WORKDIR "/src/${ENTRY_PROJECT}"
RUN dotnet publish "${ENTRY_PROJECT}.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="0"
COPY --from=build /app/publish .
COPY --from=build /coveragereport ./coveragereport
COPY --from=build /TestResults ./tests

ENTRYPOINT ["dotnet", "website.dll"]

HEALTHCHECK CMD wget --fail http://localhost:7400/healthcheck/live || exit 1
