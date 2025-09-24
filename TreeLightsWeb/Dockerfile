# This is used to get OpenCV libraries for OpenCvSharp
FROM docker.io/shimat/ubuntu18-dotnetcore3.1-opencv4.5.0:20201030 AS opencvsharp-env
WORKDIR .

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

WORKDIR /app
COPY --from=publish /app/publish .


# For OpenCvSharp https://github.com/shimat/opencvsharp/issues/983
COPY --from=opencvsharp-env usr/lib/ /usr/lib/   
COPY --from=opencvsharp-env lib/x86_64-linux-gnu/ /usr/lib/ 
# added these after trial and error
RUN cd .. && cd etc && mkdir fonts
COPY --from=opencvsharp-env etc/fonts/ /etc/fonts
# cleared originally provided libOpenCvSharpExtern.so file and copied one from opencvsharp docker image
RUN rm -rf /app/runtimes/ubuntu.18.04-x64/native/*
COPY --from=opencvsharp-env artifacts/ /app/runtimes/ubuntu.18.04-x64/native/




ENTRYPOINT ["dotnet", "TreeLightsWeb.dll"]