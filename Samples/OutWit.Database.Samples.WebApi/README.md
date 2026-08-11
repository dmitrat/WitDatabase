# OutWit.Database.Samples.WebApi

ASP.NET Core Web API sample demonstrating **WitDatabase** with the **ADO.NET provider**.

## Overview

This sample showcases direct database access using the WitDatabase ADO.NET provider (`WitDbConnection`). Unlike the EF Core sample, this demonstrates low-level database operations with full control over SQL queries.

Features demonstrated:
- Direct SQL execution with `WitDbCommand`
- Parameterized queries with `WitDbParameter`
- Data reading with `WitDbDataReader`
- Transaction management
- Bulk operations
- Connection management

## Getting Started

### Prerequisites

- .NET 10.0 SDK
- Windows, Linux, or macOS
- (Optional) A REST client like Postman, curl, or the built-in Swagger UI

### Step 1: Clone the Repository

```bash
git clone https://github.com/dmitrat/WitDatabase.git
cd WitDatabase
```

### Step 2: Navigate to the Sample

```bash
cd Samples/OutWit.Database.Samples.WebApi
```

### Step 3: Build the Project

```bash
dotnet build
```

### Step 4: Run the Application

```bash
dotnet run
```

### Step 5: Access the API

Open your browser and navigate to:

- **Swagger UI**: http://localhost:5184
- **API Base URL**: http://localhost:5184/api

The Swagger UI provides an interactive interface to test all API endpoints.

### Step 6: Test the Endpoints

Using Swagger UI:
1. Click on any endpoint to expand it
2. Click "Try it out"
3. Fill in required parameters
4. Click "Execute"
5. View the response

Or using curl:

```bash
# Get all users
curl http://localhost:5184/api/users

# Create a new user
curl -X POST http://localhost:5184/api/users \
  -H "Content-Type: application/json" \
  -d '{"name": "John Doe", "email": "john@example.com"}'

# Get all products
curl http://localhost:5184/api/products

# Search products
curl "http://localhost:5184/api/products/search?q=laptop"

# Get product statistics
curl http://localhost:5184/api/products/statistics

# Bulk import products
curl -X POST http://localhost:5184/api/products/bulk \
  -H "Content-Type: application/json" \
  -d '[
    {"name": "USB Cable", "description": "USB-C cable", "price": 9.99, "stock": 500},
    {"name": "Webcam", "description": "HD webcam", "price": 79.99, "stock": 50}
  ]'
```

## Features Demonstrated

### Direct ADO.NET Access

```csharp
// Create and open connection
using var connection = new WitDbConnection(connectionString);
connection.Open();

// Execute query with parameters
using var command = connection.CreateCommand();
command.CommandText = "SELECT Id, Name, Email FROM Users WHERE Id = @id";
command.Parameters.Add(new WitDbParameter("@id", userId));

using var reader = await command.ExecuteReaderAsync();
if (await reader.ReadAsync())
{
    var user = new User
    {
        Id = reader.GetInt64(0),
        Name = reader.GetString(1),
        Email = reader.GetString(2)
    };
}
```

### INSERT with RETURNING

```csharp
command.CommandText = """
    INSERT INTO Users (Name, Email) 
    VALUES (@name, @email)
    RETURNING Id, Name, Email, CreatedAt
    """;
command.Parameters.Add(new WitDbParameter("@name", name));
command.Parameters.Add(new WitDbParameter("@email", email));

using var reader = await command.ExecuteReaderAsync();
// Read the newly inserted row
```

### Transaction Management

```csharp
using var connection = new WitDbConnection(connectionString);
connection.Open();
using var transaction = connection.BeginTransaction();

try
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "INSERT INTO Products ...";
    await command.ExecuteNonQueryAsync();

    // More operations...

    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

### Aggregate Queries

```csharp
command.CommandText = """
    SELECT 
        COUNT(*) AS TotalProducts,
        SUM(Stock) AS TotalStock,
        AVG(Price) AS AveragePrice,
        MIN(Price) AS MinPrice,
        MAX(Price) AS MaxPrice
    FROM Products
    """;
```

## API Endpoints

### Users

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/users` | Get all users |
| GET | `/api/users/{id}` | Get user by ID |
| GET | `/api/users/search?q=...` | Search users by name |
| GET | `/api/users/paged?page=1&pageSize=10` | Get paginated users |
| POST | `/api/users` | Create user |
| PUT | `/api/users/{id}` | Update user |
| DELETE | `/api/users/{id}` | Delete user |
| GET | `/api/users/statistics` | Get user statistics |

### Products

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/products` | Get all products |
| GET | `/api/products/{id}` | Get product by ID |
| GET | `/api/products/search?q=...` | Search products |
| GET | `/api/products/paged?page=1&pageSize=10` | Get paginated products |
| GET | `/api/products/low-stock?threshold=10` | Get low stock products |
| GET | `/api/products/price-range?minPrice=0&maxPrice=100` | Get products by price |
| POST | `/api/products` | Create product |
| PUT | `/api/products/{id}` | Update product |
| PATCH | `/api/products/{id}/stock` | Update stock |
| DELETE | `/api/products/{id}` | Delete product |
| GET | `/api/products/statistics` | Get product statistics |
| POST | `/api/products/bulk` | Bulk import products |

## Sample Requests and Responses

### Create User

Request:
```bash
curl -X POST http://localhost:5184/api/users \
  -H "Content-Type: application/json" \
  -d '{"name": "John Doe", "email": "john@example.com"}'
```

Response:
```json
{
  "id": 4,
  "name": "John Doe",
  "email": "john@example.com",
  "createdAt": "2025-02-11T10:30:00Z"
}
```

### Get Product Statistics

Request:
```bash
curl http://localhost:5184/api/products/statistics
```

Response:
```json
{
  "totalProducts": 5,
  "totalStock": 455,
  "averagePrice": 445.99,
  "minPrice": 29.99,
  "maxPrice": 1299.99
}
```

### Bulk Import Products

Request:
```bash
curl -X POST http://localhost:5184/api/products/bulk \
  -H "Content-Type: application/json" \
  -d '[
    {"name": "Product A", "description": "Description A", "price": 10.00, "stock": 100},
    {"name": "Product B", "description": "Description B", "price": 20.00, "stock": 200}
  ]'
```

Response:
```json
{
  "importedCount": 2,
  "message": "Successfully imported 2 products"
}
```

## Connection String Options

Configure in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=myapp.witdb"
  }
}
```

Available options:

```
// File-based database
Data Source=myapp.witdb

// In-memory database
Data Source=:memory:

// With encryption
Data Source=secure.witdb;Password=secret

// LSM-Tree storage - for sustained high-volume ingest into tables with few
// or no secondary indexes. See WitSQL.md 14.9; the default suits most work.
Data Source=./data;Store=lsm

// With parallel writes
Data Source=myapp.witdb;Parallel Mode=Auto

// Full configuration
Data Source=app.witdb;Store=btree;Password=secret;Transactions=true;Parallel Mode=Auto
```

## Project Structure

```
OutWit.Database.Samples.WebApi/
+-- Program.cs                      # Application entry point & ConnectionFactory
+-- Controllers/
|   +-- UsersController.cs          # User API endpoints
|   +-- ProductsController.cs       # Product API endpoints
+-- Models/
|   +-- User.cs                     # User entity and DTOs
|   +-- Product.cs                  # Product entity and DTOs
+-- Services/
|   +-- DatabaseInitializer.cs      # Schema creation and seeding
|   +-- UserService.cs              # User data access
|   +-- ProductService.cs           # Product data access
+-- Properties/
|   +-- launchSettings.json         # Launch profiles
+-- appsettings.json                # Configuration
+-- appsettings.Development.json    # Development configuration
+-- README.md
+-- OutWit.Database.Samples.WebApi.csproj
```

## Key Differences from EF Core Sample

| Aspect | This Sample (ADO.NET) | WebApiEF (EF Core) |
|--------|----------------------|-------------------|
| SQL Control | Full manual control | LINQ-generated |
| Performance | Lower overhead | Higher abstraction |
| Complexity | More code | Less code |
| Flexibility | Maximum | Framework constraints |
| Use Case | Performance-critical | Rapid development |

## Troubleshooting

### Port Already in Use

If port 5184 is already in use, modify `Properties/launchSettings.json` to use a different port.

### Database File Locked

If you see "file is locked" errors, ensure no other instance of the application is running.

### Swagger Not Loading

Ensure the application is running in Development mode. Check `ASPNETCORE_ENVIRONMENT` is set to `Development`.

## Seed Data

The application automatically seeds the following data on first run:

- 3 Users (Alice Johnson, Bob Smith, Carol Williams)
- 5 Products (Laptop, Mouse, Keyboard, Monitor, Headphones)

## Related Projects

- [OutWit.Database.AdoNet](../../Sources/Providers/OutWit.Database.AdoNet/) - ADO.NET provider
- [OutWit.Database.Samples.WebApiEF](../OutWit.Database.Samples.WebApiEF/) - EF Core sample
- [OutWit.Database.Samples.ConsoleApp](../OutWit.Database.Samples.ConsoleApp/) - Console sample
- [OutWit.Database.Samples.BlazorWasm](../OutWit.Database.Samples.BlazorWasm/) - Blazor WASM sample
