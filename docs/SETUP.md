# Setup

This setup document will show how to get EventXperience running on your local machine for further development or local demos. This documentation will show the minimal setup needed to run the project with or without Docker.

## Requirements

There are two ways to run the project currently. The recommended approach is Docker as its minimal, works regardless of what environment (OS, Node version etc) that you may have, and more importantly, creates a PostgreSQL and Redis container without local installation. You may use your own local Node, PostgreSQL and Redis if you wish, but setup of PostgreSQL and Redis is tedious if you do not have already installed.

### Docker (recommended)

Install Docker if you do not have it yet.

- [Windows Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Linux Docker Engine](https://docs.docker.com/engine/)

Verify Docker is working with:

```bash
   docker --version
```

### Locally (not recommended)

- [Node.js v24.x](https://nodejs.org/en/download)
- [.NET 10.x](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [PostgreSQL](https://www.postgresql.org/download/)
- [Redis](https://redis.io/downloads/)
- [Kafka](https://kafka.apache.org/quickstart)

[Installing Redis on Windows](https://redis.io/docs/latest/operate/oss_and_stack/install/archive/install-redis/install-redis-on-windows/)  
[Installing Kafka on Windows](https://kafka.apache.org/quickstart)

Alternatively, you can use a cloud instance of PostgreSQL, Redis, Kafka, and Elasticsearch - however it is a lot for this project

Verify your local environment is working with:

```bash
  node --version
  dotnet --version
  psql -U appuser -d appdb -c "SELECT version();"
  redis-cli ping
  kafka-topics --version
```

Once verification of the tools are successful, then you are ready to install and run EventXperience

## Before installation

### 1. Clone the project

Clone EventXperience using Git.

```bash
  git clone https://github.com/thomastran117/EventXperience.git
```

### Setup .env

A minimal .env is needed to complete setup, as EF Core migrations can't be applied to PostgreSQL without the connection string. Run the following command in the root directory

Run 
```bash
./app env
```

These two scripts will scaffold the exact template needed for both frontend, backend and workers, with defaults configured for local development.


## Running with Docker (recommended)

Although Docker normally works using the standard docker-compose, a new setup must apply the EF Core migrations before the app can run. A shell script is provided that starts the docker container, applies the migration and continues to run it to further smooth development and setup.

Start Docker with the following command at the root directory:

```bash
  ../app docker
```

> **Upgrading from a MySQL-era checkout:** the database moved from MySQL to PostgreSQL, and the
> compose volume was renamed `mysql_data` → `postgres_data`. Drop the old volumes first, otherwise
> the backend starts against an empty Postgres while the stale MySQL volume lingers:
>
> ```bash
>   docker compose down -v
> ```

## Running locally

To run the app locally, we will need to install dependencies and then apply migrations before the app can boot up succesfully. Two shell scripts are provided to automate this.

setup script will install all dependencies and apply the EF Core migration to PostgreSQL
run-app will run both the frontend and backend in one terminal

Paste the following scripts into the terminal:
```bash
  ./app setup
  ./app local
```
Stop the servers with Ctrl^C

## Accessing the application

The frontend is avaliable at http://localhost:3040 and the backend is at http://localhost:8040. Remember that the server is prefixed with API.

I recommend accesing the server through the React frontend as it handles request bodies, JWT access/refresh token management and UI display on your behalf.

Congrats! You now have installed EventXperience and should be able to use it.

Refer to [DEVELOPERS.md](\DEVELOPERS.md)and [CONFIGURATION.md](\CONFIGURATION.md) for more information
