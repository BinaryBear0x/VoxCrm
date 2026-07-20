FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build
WORKDIR /src
COPY . .
RUN dotnet restore VoxCrm.Web/VoxCrm.Web.csproj \
    && dotnet publish VoxCrm.Web/VoxCrm.Web.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1fa23fc4872d95fd71c2833ebe65d7e84a43b2d51a31d119516852f13d9505a7
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /keys \
    && chown app:app /keys
COPY --from=build --chown=app:app /out .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "VoxCrm.Web.dll"]
