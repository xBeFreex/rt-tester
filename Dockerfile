FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY rt-tester.csproj .
RUN dotnet restore rt-tester.csproj
COPY . .
RUN dotnet publish rt-tester.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5028
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5028
ENTRYPOINT ["dotnet", "rt-tester.dll"]
