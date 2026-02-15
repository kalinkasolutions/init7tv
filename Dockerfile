FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet_build_env

WORKDIR /app

COPY ./ ./ 
RUN dotnet restore --disable-parallel

RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

COPY --from=dotnet_build_env /app/out ./

EXPOSE 5001

ENTRYPOINT ["dotnet", "Init7Tv.Api.dll"]