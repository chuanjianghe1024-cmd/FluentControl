FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/FluentControl.FcsHost/ src/FluentControl.FcsHost/
COPY src/FluentControl.FcsGateway/FcsNode.cs src/FluentControl.FcsGateway/FcsNode.cs
RUN dotnet publish src/FluentControl.FcsHost/FluentControl.FcsHost.csproj -c Release -a $TARGETARCH --self-contained false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY LICENSE LICENSE-MIT-LEGACY.md ./
COPY --from=build /out/ ./
RUN mkdir /data && chown $APP_UID:$APP_UID /data
USER $APP_UID
ENV FCS_DATA=/data ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FluentControl.FcsHost.dll"]
