# The Obscura browser reads Finviz. The no-render build is enough: the app only takes pages as served.
FROM alpine:3.20 AS obscura
ARG TARGETARCH
ARG OBSCURA_VERSION=v0.2.6
RUN arch=$([ "$TARGETARCH" = "arm64" ] && echo aarch64 || echo x86_64) \
 && wget -qO- "https://github.com/h4ckf0r0day/obscura/releases/download/${OBSCURA_VERSION}/obscura-${arch}-linux-no-render.tar.gz" \
  | tar xz -C /tmp obscura

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ src/
# Restore needs the source present: it decides from the project contents whether to bring in blazor.web.js.
RUN dotnet publish src/BuyWindow.Web/BuyWindow.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
COPY --from=obscura /tmp/obscura /opt/obscura/obscura
RUN mkdir -p /data/keys && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/buywindow.db" \
    DataProtection__KeysPath=/data/keys \
    Finviz__ObscuraPath=/opt/obscura/obscura
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "BuyWindow.Web.dll"]
