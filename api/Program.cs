using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Lấy connection string từ Environment Variables (do Docker Compose truyền vào)
var masterConnStr = builder.Configuration["ConnectionStrings:MasterDb"];
var slaveConnStr = builder.Configuration["ConnectionStrings:SlaveDb"];

// Tự động tạo bảng nếu chưa có (Chỉ chạy trên Master)
using (var conn = new NpgsqlConnection(masterConnStr))
{
    conn.Open();
    using var cmd = new NpgsqlCommand(@"
        CREATE TABLE IF NOT EXISTS products (
            id SERIAL PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            price DECIMAL NOT NULL
        )", conn);
    cmd.ExecuteNonQuery();
}

// 1. POST /products -> Ghi vào MASTER DB
app.MapPost("/products", async (ProductDto product) =>
{
    using var conn = new NpgsqlConnection(masterConnStr);
    await conn.OpenAsync();
    
    using var cmd = new NpgsqlCommand("INSERT INTO products (name, price) VALUES (@n, @p) RETURNING id", conn);
    cmd.Parameters.AddWithValue("n", product.Name);
    cmd.Parameters.AddWithValue("p", product.Price);
    
    var insertedId = await cmd.ExecuteScalarAsync();
    
    return Results.Ok(new { Message = "Created successfully", Id = insertedId, product.Name, product.Price });
});

// 2. GET /products -> Đọc từ SLAVE DB
app.MapGet("/products", async () =>
{
    var products = new List<object>();
    
    using var conn = new NpgsqlConnection(slaveConnStr);
    await conn.OpenAsync();
    
    using var cmd = new NpgsqlCommand("SELECT id, name, price FROM products", conn);
    using var reader = await cmd.ExecuteReaderAsync();
    
    while (await reader.ReadAsync())
    {
        products.Add(new { Id = reader.GetInt32(0), Name = reader.GetString(1), Price = reader.GetDecimal(2) });
    }

    // Trả về Data + Server Metadata để chứng minh Load Balancer hoạt động
    return Results.Ok(new 
    { 
        processed_by = Environment.MachineName, // Lấy tên container xử lý request
        data = products 
    });
});

app.Run("http://0.0.0.0:8080");

record ProductDto(string Name, decimal Price);