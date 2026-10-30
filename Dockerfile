FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ src/
# Restore needs the source present: it decides from the project contents whether to bring in blazor.web.js.
RUN dotnet publish src/BuyWindow.Web/BuyWindow.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data/keys && chown -R app:app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/buywindow.db" \
    DataProtection__KeysPath=/data/keys
EXPOSE 8080
VOLUME /data
ENTRYPOINT ["dotnet", "BuyWindow.Web.dll"]
