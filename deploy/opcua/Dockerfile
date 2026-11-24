FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY LICENSE ./
COPY deploy/opcua/ deploy/opcua/
COPY docs/fcs-opcua.md docs/fcs-opcua.md
COPY src/FluentControl.FcsOpcUa/ src/FluentControl.FcsOpcUa/
RUN dotnet publish src/FluentControl.FcsOpcUa -c Release -a $TARGETARCH --self-contained false -o /out
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /out/ ./
COPY LICENSE ./
COPY deploy/opcua/licenses/ ./third-party-licenses/
RUN mkdir /data && chown $APP_UID:$APP_UID /data
USER $APP_UID
EXPOSE 4840
ENTRYPOINT ["dotnet", "FluentControl.FcsOpcUa.dll"]
CMD ["--config", "/data/opcua.json"]
