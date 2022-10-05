FROM mcr.microsoft.com/dotnet/sdk:6.0-jammy

# for communication & testing purposes
RUN [ "apt-get", "update" ]
RUN [ "apt-get", "install", "-y", "netcat" ]

# should be built in an output directory lmao
WORKDIR /app
COPY . .

RUN [ "dotnet", "DockerTest.dll", "test" ]
CMD [ "dotnet", "DockerTest.dll", "daemon" ]