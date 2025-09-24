# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
# USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY . /src
WORKDIR /src
RUN dotnet build "TreeLightsWeb/TreeLightsWeb.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "TreeLightsWeb/TreeLightsWeb.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final


# For OpenCV
RUN apt-get update && apt-get install -y \
    apt-utils \
    libgdiplus \
    libc6-dev \
    libgtk2.0-dev \
    libtbb-dev \
    libatlas-base-dev \
    libvorbis-dev \
    libxvidcore-dev \
    libopencore-amrnb-dev \
    libopencore-amrwb-dev \
    x264 \
    v4l-utils \
    libwebp-dev \
    tesseract-ocr \
    libtesseract-dev \
    libleptonica-dev \
    libtiff-dev \
    libavcodec-dev \
    libavformat-dev \
    libswscale-dev \
    libxine2-dev \
    libv4l-dev
RUN apt-get clean && rm -rf /var/lib/apt/lists/*




WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TreeLightsWeb.dll"]