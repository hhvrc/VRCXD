# The SDK stage is glibc (Ubuntu), not Alpine: Grpc.Tools' bundled protoc is a glibc binary and
# cannot start on musl. The runtime image (API.Dockerfile) stays Alpine.
# Pinned to an exact SDK so a rebuild cannot silently pick up another one (keep in step with global.json).
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-resolute AS build-common
WORKDIR /src

COPY --link Vrchat/*.csproj Vrchat/
COPY --link Common/*.csproj Common/
COPY --link *.props .
# Copied so the SDK pin is actually enforced in the image.
COPY --link global.json .
RUN dotnet restore Common/Common.csproj

COPY --link Vrchat/. Vrchat/
COPY --link Common/. Common/

RUN dotnet build --no-restore -c Release Common/Common.csproj

FROM build-common AS test-common
WORKDIR /src

COPY --link Common.Tests/*.csproj Common.Tests/
RUN dotnet restore Common.Tests/Common.Tests.csproj
COPY --link Common.Tests/. Common.Tests/
RUN dotnet build --no-restore -c Release Common.Tests/Common.Tests.csproj
ENTRYPOINT ["dotnet", "test", "--no-build", "-c", "Release", "Common.Tests/Common.Tests.csproj"]
