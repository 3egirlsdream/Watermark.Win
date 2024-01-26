#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Watermark.Web/Watermark.Web/Watermark.Web.csproj", "./../"]
COPY ["Watermark.Web/Watermark.Web.Client/Watermark.Web.Client.csproj", "./../"]
RUN dotnet restore "./../Watermark.Web.csproj"
COPY . .
WORKDIR "/src/../."
RUN dotnet build "Watermark.Web/Watermark.Web/Watermark.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Watermark.Web/Watermark.Web/Watermark.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Watermark.Web.dll"]