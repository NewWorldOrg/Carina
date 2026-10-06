ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS ffmpeg-build
ARG ARIBCAPTION_TAG=v1.1.2
ARG ARIBCAPTION_COMMIT=c64c23b8905ba514b87c9789269e9f66f949ffe0
ARG FFMPEG_VERSION=6.1.6
ARG FFMPEG_SHA256=d4fcb164028dd3beee5d92c0ac72e46aac6973c75ea12dc14de07bf8f407370a
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates cmake curl g++ gcc git make nasm patch pkg-config xz-utils \
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
COPY patches/ffmpeg-aribcaption-erase-on-clear.patch /src/patches/
RUN curl -fsSLO "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" \
    && echo "${FFMPEG_SHA256}  ffmpeg-${FFMPEG_VERSION}.tar.xz" | sha256sum -c - \
    && tar xf "ffmpeg-${FFMPEG_VERSION}.tar.xz" \
    && cd "ffmpeg-${FFMPEG_VERSION}" \
    && patch -p1 --fuzz=0 < /src/patches/ffmpeg-aribcaption-erase-on-clear.patch \
    && PKG_CONFIG_PATH=/usr/local/lib/pkgconfig ./configure --prefix=/usr/local \
        --disable-doc --disable-debug --disable-ffplay \
        --enable-gpl --enable-libx264 --enable-vaapi --enable-libdrm \
        --enable-libaribcaption --enable-libfreetype --enable-libfontconfig \
    && make -j"$(nproc)" \
    && make install \
    && mkdir -p /out/ffmpeg/bin /out/ffmpeg/lib \
    && cp /usr/local/bin/ffmpeg /usr/local/bin/ffprobe /out/ffmpeg/bin/ \
    && cp -P /usr/local/lib/libaribcaption.so* /out/ffmpeg/lib/ \
    && mkdir -p /out/notices/ffmpeg/source /out/notices/libaribcaption \
    && cp COPYING.GPLv2 COPYING.LGPLv2.1 LICENSE.md /out/notices/ffmpeg/ \
    && cp "/src/ffmpeg-${FFMPEG_VERSION}.tar.xz" /src/patches/ffmpeg-aribcaption-erase-on-clear.patch /out/notices/ffmpeg/source/ \
    && sed -n 's/^#define FFMPEG_CONFIGURATION "\(.*\)"$/\1/p' config.h | tr ' ' '\n' > /out/notices/ffmpeg/source/configure-options.txt \
    && test -s /out/notices/ffmpeg/source/configure-options.txt \
    && cp /src/libaribcaption/LICENSE /out/notices/libaribcaption/

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

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

RUN sed 's/^Types: deb$/Types: deb-src/' /etc/apt/sources.list.d/ubuntu.sources > /etc/apt/sources.list.d/ubuntu-src.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-noto-cjk intel-media-va-driver libdrm2 libfreetype6 libva-drm2 libva2 libx264-164 libpcsclite1 \
    && mkdir -p /usr/share/doc/carina/x264/source \
    && cd /usr/share/doc/carina/x264/source \
    && apt-get source --download-only "x264=$(dpkg-query -W -f '${source:Version}' libx264-164)" \
    && rm /etc/apt/sources.list.d/ubuntu-src.sources \
    && rm -rf /var/lib/apt/lists/* \
    && fc-cache -f
COPY docker/fonts.conf /etc/fonts/local.conf

COPY --from=ffmpeg-build /out/ffmpeg/bin/ /usr/local/bin/
COPY --from=ffmpeg-build /out/ffmpeg/lib/ /usr/local/lib/
COPY --from=ffmpeg-build /out/notices/ /usr/share/doc/carina/
COPY LICENSE /usr/share/doc/carina/LICENSE
RUN ldconfig

RUN groupadd --gid 10001 carina \
    && useradd --uid 10001 --gid carina --no-create-home --shell /usr/sbin/nologin carina \
    && install -d -o 10001 -g 10001 -m 0770 /var/lib/carina/keys

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
