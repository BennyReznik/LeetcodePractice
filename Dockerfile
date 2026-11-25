# Monaco, the editor from VS Code, is copied in so the app works offline.
FROM node:24-alpine AS monaco
WORKDIR /m
RUN npm pack monaco-editor@0.52.2 && tar xzf monaco-editor-0.52.2.tgz

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Practice.csproj ./
RUN dotnet restore
COPY src/ ./
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=monaco /m/package/min/vs ./wwwroot/vs
COPY --from=monaco /m/package/LICENSE ./wwwroot/vs/LICENSE
COPY problems/ ./problems/
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
USER $APP_UID
ENTRYPOINT ["dotnet", "Practice.dll"]
