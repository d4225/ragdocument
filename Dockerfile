# Build and publish the ASP.NET Core application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SmartDocumentRAG.API.csproj", "./"]
RUN dotnet restore "SmartDocumentRAG.API.csproj"

COPY . .
RUN dotnet publish "SmartDocumentRAG.API.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

# Run with the smaller ASP.NET Core runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartDocumentRAG.API.dll"]
