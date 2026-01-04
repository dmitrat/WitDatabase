# WitDatabase Identity Server Sample

A simple Identity Server demo application showcasing **WitDatabase** integration with **OpenIddict** using ASP.NET Core MVC.

## Features

- **ASP.NET Core Identity** - User authentication and authorization
- **OpenIddict** - OAuth 2.0 / OpenID Connect server
- **WitDatabase** - Embedded database storage via Entity Framework Core
- **Bootstrap 5** - Modern responsive UI

## Functionality

- User login/logout
- User management (list, activate/deactivate, delete)
- Role-based authorization (Administrator role required for user management)

## Getting Started

### Prerequisites

- .NET 10.0 SDK

### Run the Application

```bash
cd Samples/OutWit.Database.Samples.IdentityServer
dotnet run
```

The application will start at `https://localhost:7200` (or `http://localhost:5200`).

### Default Credentials

- **Email:** `admin@example.com`
- **Password:** `Admin123!`

## OpenIddict Endpoints

| Endpoint | URL |
|----------|-----|
| Authorization | `/connect/authorize` |
| Token | `/connect/token` |

## Project Structure

```
OutWit.Database.Samples.IdentityServer/
??? Controllers/
?   ??? AccountController.cs    # Login/Logout handling
?   ??? HomeController.cs       # Home pages
?   ??? UsersController.cs      # User management
??? Data/
?   ??? ApplicationDbContext.cs # EF Core DbContext
??? Models/
?   ??? ApplicationUser.cs      # Custom Identity user
?   ??? ApplicationRole.cs      # Custom Identity role
?   ??? LoginViewModel.cs       # Login form model
?   ??? UserViewModel.cs        # User list model
??? Services/
?   ??? DatabaseInitializerService.cs  # DB migration & seeding
?   ??? UserService.cs          # User operations
??? Views/
?   ??? Account/                # Login views
?   ??? Home/                   # Home views
?   ??? Shared/                 # Layout
?   ??? Users/                  # User management views
??? Program.cs                  # Application entry point
```

## Technology Stack

- ASP.NET Core 10
- Entity Framework Core 10
- ASP.NET Core Identity
- OpenIddict 6.x
- WitDatabase (via EF Core provider)
- Bootstrap 5.3
