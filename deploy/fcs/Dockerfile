FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/FluentControl.FcsHost/ src/FluentControl.FcsHost/
COPY src/FluentControl.FcsGateway/FcsNode.cs src/FluentControl.FcsGateway/FcsScripts.cs src/FluentControl.FcsGateway/FcsManagement.cs src/FluentControl.FcsGateway/
COPY tools/gateway-scripts/ tools/gateway-scripts/
COPY tools/fcs-manager/ tools/fcs-manager/
RUN dotnet publish src/FluentControl.FcsHost/FluentControl.FcsHost.csproj -c Release -a $TARGETARCH --self-contained false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY LICENSE LICENSE-MIT-LEGACY.md ./
COPY --from=build /out/ ./
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/*
RUN mkdir /data && chown $APP_UID:$APP_UID /data
USER $APP_UID
ENV FCS_PYTHON=/usr/bin/python3 FCS_DATA=/data ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FluentControl.FcsHost.dll"]
