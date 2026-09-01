ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS driver-build
ARG RID=linux-x64
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Carina.Contracts/Carina.Contracts.csproj src/Carina.Contracts/
COPY src/Carina.Driver/Carina.Driver.csproj src/Carina.Driver/
RUN dotnet restore src/Carina.Driver/Carina.Driver.csproj -r ${RID} -p:PublishAot=true
COPY src/Carina.Contracts/ src/Carina.Contracts/
COPY src/Carina.Driver/ src/Carina.Driver/
RUN dotnet publish src/Carina.Driver/Carina.Driver.csproj -c Release -r ${RID} -p:PublishAot=true -o /out/driver \
    && rm -f /out/driver/*.dbg /out/driver/*.pdb

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS app-build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Carina.Contracts/Carina.Contracts.csproj src/Carina.Contracts/
COPY src/Carina.Domain/Carina.Domain.csproj src/Carina.Domain/
COPY src/Carina.Broadcast/Carina.Broadcast.csproj src/Carina.Broadcast/
COPY src/Carina.Infrastructure/Carina.Infrastructure.csproj src/Carina.Infrastructure/
COPY src/Carina.Api/Carina.Api.csproj src/Carina.Api/
COPY src/Carina.Db/Carina.Db.csproj src/Carina.Db/
RUN dotnet restore src/Carina.Api/Carina.Api.csproj \
    && dotnet restore src/Carina.Db/Carina.Db.csproj
COPY src/ src/
RUN dotnet publish src/Carina.Api/Carina.Api.csproj -c Release --no-restore -o /out/app \
    && dotnet publish src/Carina.Db/Carina.Db.csproj -c Release --no-restore -o /out/db

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS develop
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg intel-media-va-driver \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg intel-media-va-driver \
    && rm -rf /var/lib/apt/lists/*

ARG CARINA_UID=10001
ARG CARINA_GID=10001
RUN groupadd --gid ${CARINA_GID} carina \
    && useradd --uid ${CARINA_UID} --gid carina --no-create-home --shell /usr/sbin/nologin carina

WORKDIR /opt/carina
COPY --from=driver-build /out/driver ./driver
COPY --from=app-build /out/app ./app
COPY --from=app-build /out/db ./db
COPY docker/entrypoint.sh /usr/local/bin/carina

RUN chmod 0755 /usr/local/bin/carina

ENV CARINA_ROLE=app \
    CARINA_DRIVER_SOCKET=/run/carina/driver.sock \
    DOTNET_hostBuilder__reloadConfigOnChange=false

ENTRYPOINT ["/usr/local/bin/carina"]
