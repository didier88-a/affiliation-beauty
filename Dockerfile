FROM mcr.microsoft.com/playwright/dotnet:v1.60.0-noble

WORKDIR /app

COPY . .

WORKDIR /app/wsaffiliation/wsaffiliation

RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

WORKDIR /app/publish

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "wsaffiliation.dll"]