ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS card-build
ARG ARIBB25_TAG=v0.2.9
ARG ARIBB25_COMMIT=a2225c6f3b92092f2e8a62b21f2990e44b561658
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates cmake g++ gcc git make pkg-config libpcsclite-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
RUN git clone --depth 1 --branch "${ARIBB25_TAG}" \
        https://github.com/tsukumijima/libaribb25.git libaribb25 \
    && test "$(git -C libaribb25 rev-parse HEAD)" = "${ARIBB25_COMMIT}" \
    && cmake -S libaribb25 -B build -DCMAKE_BUILD_TYPE=Release \
    && cmake --build build -j"$(nproc)" \
    && mkdir -p /out/card \
    && cp -P build/libaribb25.so* /out/card/

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS ffmpeg-build
ARG ARIBCAPTION_TAG=v1.1.2
ARG ARIBCAPTION_COMMIT=c64c23b8905ba514b87c9789269e9f66f949ffe0
ARG FFMPEG_VERSION=6.1.6
ARG FFMPEG_SHA256=d4fcb164028dd3beee5d92c0ac72e46aac6973c75ea12dc14de07bf8f407370a
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates cmake curl g++ gcc git make nasm pkg-config xz-utils \
        libdrm-dev libfontconfig-dev libfreetype-dev libva-dev libx264-dev zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
RUN git clone --depth 1 --branch "${ARIBCAPTION_TAG}" \
        https://github.com/xqq/libaribcaption.git libaribcaption \
    && test "$(git -C libaribcaption rev-parse HEAD)" = "${ARIBCAPTION_COMMIT}" \
    && cmake -S libaribcaption -B aribcaption-build -DCMAKE_BUILD_TYPE=Release \
        -DARIBCC_SHARED_LIBRARY=ON -DARIBCC_USE_FONTCONFIG=ON -DARIBCC_USE_FREETYPE=ON -DARIBCC_BUILD_TESTS=OFF \
    && cmake --build aribcaption-build -j"$(nproc)" \
    && cmake --install aribcaption-build --prefix /usr/local
RUN curl -fsSLO "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" \
    && echo "${FFMPEG_SHA256}  ffmpeg-${FFMPEG_VERSION}.tar.xz" | sha256sum -c - \
    && tar xf "ffmpeg-${FFMPEG_VERSION}.tar.xz" \
    && cd "ffmpeg-${FFMPEG_VERSION}" \
    && PKG_CONFIG_PATH=/usr/local/lib/pkgconfig ./configure --prefix=/usr/local \
        --disable-doc --disable-debug --disable-ffplay \
        --enable-gpl --enable-libx264 --enable-vaapi --enable-libdrm \
        --enable-libaribcaption --enable-libfreetype --enable-libfontconfig \
    && make -j"$(nproc)" \
    && make install \
    && mkdir -p /out/ffmpeg/bin /out/ffmpeg/lib \
    && cp /usr/local/bin/ffmpeg /usr/local/bin/ffprobe /out/ffmpeg/bin/ \
    && cp -P /usr/local/lib/libaribcaption.so* /out/ffmpeg/lib/

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
    && apt-get install -y --no-install-recommends fontconfig fonts-noto-cjk intel-media-va-driver libdrm2 libfreetype6 libva-drm2 libva2 libx264-164 \
    && rm -rf /var/lib/apt/lists/* \
    && fc-cache -f
COPY docker/fonts.conf /etc/fonts/local.conf
COPY --from=ffmpeg-build /out/ffmpeg/bin/ /usr/local/bin/
COPY --from=ffmpeg-build /out/ffmpeg/lib/ /usr/local/lib/
RUN ldconfig

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS driver-develop
RUN apt-get update \
    && apt-get install -y --no-install-recommends libpcsclite1 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=card-build /out/card/ /usr/local/lib/
RUN ldconfig

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-noto-cjk intel-media-va-driver libdrm2 libfreetype6 libva-drm2 libva2 libx264-164 libpcsclite1 \
    && rm -rf /var/lib/apt/lists/* \
    && fc-cache -f
COPY docker/fonts.conf /etc/fonts/local.conf

COPY --from=card-build /out/card/ /usr/local/lib/
COPY --from=ffmpeg-build /out/ffmpeg/bin/ /usr/local/bin/
COPY --from=ffmpeg-build /out/ffmpeg/lib/ /usr/local/lib/
RUN ldconfig

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
