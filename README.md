# BOE Auctions

Application that fetches the data of the current BOE auctions into a database, and notifies a telegram channel about them.

Every run will fetch the existing data from the database to avoid sending duplicates and save time.

## Requirements

- Docker and Docker compose OR a Postgres database
- .NET 6

## How to use

### Required environment variables

- `POSTGRES_CONNECTION_STRING`: The connection string to the database where the auctions will be saved.
  <br/>Example: `Host=localhost;Username=postgres;Password=postgres;Database=postgres`
- `TELEGRAM_BOT_TOKEN`: _(Optional)_ The token of the bot that will send the messages.
  <br/>Example: `123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZ`
- `TELEGRAM_CHAT_ID`: _(Optional)_ The ID of the chat where the bot will send the messages.
  <br/>Example: `123456789`

### Manual execution

1. Launch the database with `docker-compose up` or `docker-compose up -d`
2. Execute the program with `dotnet run`. It will fetch all the auctions and load them into the database. Then, it will send Telegram notifications

### Automatic execution with GitHub actions

Currently, the repository has a workflow configured that will launch the program once every day