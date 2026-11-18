# syntax = devthefuture/dockerfile-x

FROM ./docker/Base.Dockerfile#build-common AS build-api

COPY --link API/*.csproj API/
RUN dotnet restore API/API.csproj

COPY --link API/. API/

# The release version (publish.yml passes the tag without its v); unset in master builds, which
# keep the version in Directory.Build.props.
ARG VERSION=
RUN dotnet publish --no-restore -c Release API/API.csproj -o /app ${VERSION:+-p:Version=$VERSION}

# final is the final runtime stage for running the app
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-alpine3.24 AS final-api
WORKDIR /app

COPY --link --from=build-api /app .
COPY docker/appsettings.API.json /app/appsettings.Container.json

EXPOSE 80
ENTRYPOINT ["dotnet", "VRCX.API.dll"]
