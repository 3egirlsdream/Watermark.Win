FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY published/ .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER app
ENTRYPOINT ["dotnet", "Watermark.Web.dll"]
