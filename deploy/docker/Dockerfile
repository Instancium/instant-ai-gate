# Multi-stage build for Linux container deployment
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/InstantAIGate.Server/InstantAIGate.Server.csproj", "src/InstantAIGate.Server/"]
COPY ["src/InstantAIGate.Core/InstantAIGate.Core.csproj", "src/InstantAIGate.Core/"]
COPY ["src/InstantAIGate.Native/InstantAIGate.Native.csproj", "src/InstantAIGate.Native/"]
COPY ["src/InstantAIGate.SSR/InstantAIGate.SSR.csproj", "src/InstantAIGate.SSR/"]
RUN dotnet restore "src/InstantAIGate.Server/InstantAIGate.Server.csproj"
COPY . .
WORKDIR "/src/src/InstantAIGate.Server"
RUN dotnet build "InstantAIGate.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "InstantAIGate.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "InstantAIGate.Server.dll"]
