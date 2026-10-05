using wsaffiliation;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// ============================================================
// HTTP CLIENT
// ============================================================

builder.Services.AddHttpClient();


// ============================================================
// NAYA SEARCH SERVICE
// Singleton = un seul cache mémoire pour toute l'application
// ============================================================

builder.Services.AddSingleton<NayaSearchService>();

// Lance automatiquement le chargement du cache
// et son rafraîchissement périodique
builder.Services.AddHostedService(
    serviceProvider =>
        serviceProvider.GetRequiredService<NayaSearchService>()
);


// ============================================================
// CORS
// ============================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("ShopifyCors", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});


var app = builder.Build();


// ============================================================
// SWAGGER
// ============================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


// ============================================================
// HTTPS
// ============================================================

app.UseHttpsRedirection();


// ============================================================
// CORS
// ============================================================

app.UseCors("ShopifyCors");


// ============================================================
// AUTHORIZATION
// ============================================================

app.UseAuthorization();


// ============================================================
// CONTROLLERS
// ============================================================

app.MapControllers();


app.Run();