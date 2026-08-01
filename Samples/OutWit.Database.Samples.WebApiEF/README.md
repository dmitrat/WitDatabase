# OutWit.Database.Samples.WebApiEF

ASP.NET Core Web API sample demonstrating **WitDatabase** with **Entity Framework Core**.

## Overview

This sample showcases a complete e-commerce API with:
- User management
- Product catalog
- Order processing
- Statistics and reporting

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
cd Samples/OutWit.Database.Samples.WebApiEF
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

- **Swagger UI**: http://localhost:5183
- **API Base URL**: http://localhost:5183/api

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
curl http://localhost:5183/api/users

# Create a new user
curl -X POST http://localhost:5183/api/users \
  -H "Content-Type: application/json" \
  -d '{"name": "John Doe", "email": "john@example.com"}'

# Get all products
curl http://localhost:5183/api/products

# Create an order
curl -X POST http://localhost:5183/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 1,
    "items": [
      {"productId": 1, "quantity": 2},
      {"productId": 2, "quantity": 1}
    ]
  }'
```

## Features Demonstrated

### Entity Framework Core Integration

```csharp
// Configure DbContext with WitDatabase
services.AddDbContext<AppDbContext>(options =>
    options.UseWitDb("Data Source=webapi_ef_sample.witdb"));
```

### Entities with Relationships

```csharp
public class Order
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    
    public User User { get; set; }
    public List<OrderItem> Items { get; set; }
}
```

### LINQ Queries

```csharp
// Join with Include
var orders = await context.Orders
    .Include(o => o.User)
    .Include(o => o.Items)
        .ThenInclude(i => i.Product)
    .Where(o => o.Status == OrderStatus.Pending)
    .OrderByDescending(o => o.OrderDate)
    .ToListAsync();

// Aggregation
var statistics = await context.Orders
    .GroupBy(o => o.Status)
    .Select(g => new { Status = g.Key, Count = g.Count() })
    .ToDictionaryAsync(x => x.Status, x => x.Count);
```

### Fluent Model Configuration

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<User>(entity =>
    {
        entity.HasIndex(e => e.Email).IsUnique();
        entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");
    });

    modelBuilder.Entity<Order>(entity =>
    {
        entity.Property(e => e.TotalAmount).HasColumnType("DECIMAL(15, 2)");
        entity.HasOne(e => e.User)
              .WithMany(u => u.Orders)
              .HasForeignKey(e => e.UserId)
              .OnDelete(DeleteBehavior.Cascade);
    });
}
```

## API Endpoints

### Users

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/users` | Get all users |
| GET | `/api/users/{id}` | Get user by ID |
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
| POST | `/api/products` | Create product |
| PUT | `/api/products/{id}` | Update product |
| PATCH | `/api/products/{id}/stock` | Update stock |
| DELETE | `/api/products/{id}` | Delete product |
| GET | `/api/products/low-stock` | Get low stock products |

### Orders

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/orders` | Get orders (paginated) |
| GET | `/api/orders/{id}` | Get order by ID |
| GET | `/api/orders/user/{userId}` | Get user's orders |
| POST | `/api/orders` | Create order |
| PATCH | `/api/orders/{id}/status` | Update order status |
| POST | `/api/orders/{id}/cancel` | Cancel order |
| GET | `/api/orders/statistics` | Get order statistics |
| GET | `/api/orders/top-products` | Get top selling products |

## Sample Requests

### Create User

```bash
curl -X POST http://localhost:5183/api/users \
  -H "Content-Type: application/json" \
  -d '{"name": "John Doe", "email": "john@example.com"}'
```

Response:
```json
{
  "id": 4,
  "name": "John Doe",
  "email": "john@example.com",
  "createdAt": "2025-02-11T10:30:00Z",
  "orderCount": 0
}
```

### Create Order

```bash
curl -X POST http://localhost:5183/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "userId": 1,
    "items": [
      {"productId": 1, "quantity": 2},
      {"productId": 2, "quantity": 1}
    ]
  }'
```

### Get Statistics

```bash
curl http://localhost:5183/api/orders/statistics
```

Response:
```json
{
  "totalOrders": 5,
  "totalRevenue": 3500.00,
  "averageOrderValue": 700.00,
  "ordersByStatus": {
    "Pending": 2,
    "Completed": 3
  }
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
Data Source=secure.witdb;Encryption=aes-gcm;Password=secret

// LSM-Tree storage
Data Source=./data;Store=lsm

// Read-only mode
Data Source=myapp.witdb;Mode=ReadOnly
```

## Project Structure

```
OutWit.Database.Samples.WebApiEF/
??? Program.cs                      # Application entry point
??? Controllers/
?   ??? UsersController.cs          # User API endpoints
?   ??? ProductsController.cs       # Product API endpoints
?   ??? OrdersController.cs         # Order API endpoints
??? Data/
?   ??? AppDbContext.cs             # DbContext and entities
??? Services/
?   ??? UserService.cs              # User business logic
?   ??? OrderService.cs             # Order business logic
??? appsettings.json                # Configuration
??? appsettings.Development.json    # Development configuration
??? Properties/
?   ??? launchSettings.json         # Launch profiles
??? README.md
??? OutWit.Database.Samples.WebApiEF.csproj
```

## Troubleshooting

### Port Already in Use

If port 5183 is already in use, modify `Properties/launchSettings.json` to use a different port.

### Database File Locked

If you see "file is locked" errors, ensure no other instance of the application is running.

### Swagger Not Loading

Ensure the application is running in Development mode. Check `ASPNETCORE_ENVIRONMENT` is set to `Development`.

### Entity Framework Errors

If you see EF Core errors after schema changes, delete the database file and restart the application. The database will be recreated and seeded automatically.

## Seed Data

The application automatically seeds the following data on first run:

- 3 Users (Alice, Bob, Carol)
- 4 Products (Laptop, Mouse, Keyboard, Monitor)
- 2 Sample Orders

## Related Projects

- [OutWit.Database.EntityFramework](../../Sources/Providers/OutWit.Database.EntityFramework/) - EF Core provider
- [OutWit.Database.AdoNet](../../Sources/Providers/OutWit.Database.AdoNet/) - ADO.NET provider
- [OutWit.Database.Samples.ConsoleApp](../OutWit.Database.Samples.ConsoleApp/) - Console sample
- [OutWit.Database.Samples.BlazorWasm](../OutWit.Database.Samples.BlazorWasm/) - Blazor WASM sample
