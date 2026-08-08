# Development image. One image, one entrypoint, four roles.
# The distribution image that combines this backend with the web frontend is built
# elsewhere; nothing here reaches outside this repository.
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/

RUN dotnet restore src/Carina.Driver/Carina.Driver.csproj \
    && dotnet restore src/Carina.Api/Carina.Api.csproj \
    && dotnet restore src/Carina.Db/Carina.Db.csproj

RUN dotnet publish src/Carina.Driver/Carina.Driver.csproj -c Release --no-restore -o /out/driver \
    && dotnet publish src/Carina.Api/Carina.Api.csproj -c Release --no-restore -o /out/app \
    && dotnet publish src/Carina.Db/Carina.Db.csproj -c Release --no-restore -o /out/db

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

# The group owns the driver socket; both ids are configurable so the image does not
# collide with accounts that already exist on the host.
ARG CARINA_UID=10001
ARG CARINA_GID=10001
RUN groupadd --gid ${CARINA_GID} carina \
    && useradd --uid ${CARINA_UID} --gid carina --no-create-home --shell /usr/sbin/nologin carina

WORKDIR /opt/carina
COPY --from=build /out/driver ./driver
COPY --from=build /out/app ./app
COPY --from=build /out/db ./db
COPY docker/entrypoint.sh /usr/local/bin/carina

RUN chmod 0755 /usr/local/bin/carina

# Configuration is read once at startup and validated there; watching it for changes
# would only cost an inotify instance per process.
ENV CARINA_ROLE=app \
    CARINA_DRIVER_SOCKET=/run/carina/driver.sock \
    DOTNET_hostBuilder__reloadConfigOnChange=false

ENTRYPOINT ["/usr/local/bin/carina"]
