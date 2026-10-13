FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY apps/api/ReadingHub.Api/ReadingHub.Api.csproj apps/api/ReadingHub.Api/
RUN dotnet restore apps/api/ReadingHub.Api/ReadingHub.Api.csproj
COPY apps/api/ReadingHub.Api/ apps/api/ReadingHub.Api/
RUN dotnet publish apps/api/ReadingHub.Api/ReadingHub.Api.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReadingHub.Api.dll"]
