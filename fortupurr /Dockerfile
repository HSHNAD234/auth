
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# คัดลอกโปรเจกต์และ restore dependencies
COPY . .
RUN dotnet restore

# Publish โปรเจกต์ .NET
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000

# ใช้ชื่อ DLL จากชื่อโปรเจกต์โดยตรง
ENTRYPOINT ["sh", "-c", "dotnet \"$(find /app -maxdepth 1 -name '*.dll' -printf '%f\\n' | head -n 1)\""]
