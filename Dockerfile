#FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
#WORKDIR /src
#
#COPY TGBot.csproj .
#RUN dotnet restore
#
#COPY . .
#RUN dotnet publish -c Release -o /app/publish
#
#FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
#WORKDIR /app
#COPY --from=build /app/publish .
#
#ENTRYPOINT ["dotnet", "TGBot.dll"]
#
# Этап сборки (используем стабильную версию SDK)
FROM ://microsoft.com AS build
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
COPY TGBot.csproj .
RUN dotnet restore

# Копируем весь код и компилируем его
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Этап запуска (используем оптимизированный runtime)
FROM ://microsoft.com AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TGBot.dll"]
