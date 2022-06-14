# BOE Auctions

Application that fetches the data of the current auctions into a database.

## Requirements

- Docker and Docker compose OR a Postgres database
- .NET 6

## How to use

1. Launch the database with `docker-compose up` or `docker-compose up -d`
2. Execute the program with `dotnet run`. It will fetch all the auctions and load them into the database
3. Now it's time to analyze the data in the database, in any way you want!