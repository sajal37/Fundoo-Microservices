# Fundoo Microservices

## Applications

- Fundoo.ApiGateway: HTTPS port 7000, routes requests using YARP.
- Fundoo.User.Api: HTTPS port 7001, EF Core and PostgreSQL for accounts.
- Fundoo.Notes.Api: HTTPS port 7002, ADO.NET using Npgsql for notes and labels.
- Fundoo.Email.Api: HTTPS port 7003, sends email through Gmail SMTP.

The User and Notes APIs use separate PostgreSQL databases. Both validate JWTs using the same signing key. The User API calls the Email microservice (API, Application, and Domain projects) over HTTP for forgot-password email delivery; the Email microservice (API, Application, and Domain projects) uses the same simple SMTP approach as the earlier Fundoo project.

## Requirements

- .NET 8 SDK
- PostgreSQL
- Visual Studio or .NET CLI

## Database setup

Create two databases in PostgreSQL:

- fundoo_user
- fundoo_notes

Set User Secrets for the User and Notes APIs as shown in the command list supplied with this project. For the Email microservice (API, Application, and Domain projects), run these commands from the solution folder, replacing the values with your own Gmail address and Gmail App Password:

```powershell
dotnet user-secrets init --project src/Services/Email/Fundoo.Email.Api
dotnet user-secrets set "EmailSettings:Email" "your-email@gmail.com" --project src/Services/Email/Fundoo.Email.Api
dotnet user-secrets set "EmailSettings:Password" "your-gmail-app-password" --project src/Services/Email/Fundoo.Email.Api
```

Do not put real credentials in appsettings.json. Run EF migrations for the User database. For Notes, run `notes-schema.sql` against `fundoo_notes` using pgAdmin or psql because Notes uses ADO.NET, not EF Core.

## Run

Set the four projects as startup projects in Visual Studio: Fundoo.ApiGateway, Fundoo.User.Api, Fundoo.Notes.Api and Fundoo.Email.Api. Start each project in a separate terminal if using the CLI. Use the Gateway at https://localhost:7000 for normal requests.

## Notes

Password-reset email is sent by Fundoo.Email.Api. The email contains the reset token, which is entered in the reset-password request. This is a basic learning implementation, not a production email system.
