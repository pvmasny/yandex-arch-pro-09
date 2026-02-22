using BionicProAuth.Extensions;
using BionicProAuth.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",  // Фронтенд на localhost
                "http://frontend:3000"     // Фронтенд в Docker сети
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()  // Важно для cookies
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10)); // Кэшируем preflight запросы
    });
});

builder.Services.AddAuthServices(builder.Configuration);

var app = builder.Build();

app.UseCors("FrontendPolicy");  // CORS должен быть самым первым

if (true || app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseMiddleware<SessionRotationMiddleware>();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();


app.Run();