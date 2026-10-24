FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The project file is at the repository root.
COPY ["FortuPurr.AuthApi.csproj", "./"]
RUN dotnet restore "./FortuPurr.AuthApi.csproj"

COPY . .
RUN dotnet publish "./FortuPurr.AuthApi.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "FortuPurr.AuthApi.dll"]
