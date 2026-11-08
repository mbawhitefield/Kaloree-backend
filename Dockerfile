FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/Kaloree.Api/Kaloree.Api.csproj src/Kaloree.Api/
COPY src/Kaloree.Api/packages.lock.json src/Kaloree.Api/
RUN dotnet restore src/Kaloree.Api/Kaloree.Api.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/Kaloree.Api/Kaloree.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Kaloree.Api.dll"]
