﻿using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using Serilog;

using Insania.Shared.Contracts.Services;
using Insania.Shared.Middleware;
using Insania.Shared.Messages;
using Insania.Shared.Services;

using Insania.News.BusinessLogic;
using Insania.News.Database.Contexts;
using Insania.News.Middleware;
using Insania.News.Models.Mapper;

//Создания экземпляра постройки веб-приложения
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

//Получение сервисов веб-приложения
IServiceCollection services = builder.Services;

//Получение конфигурации веб-приложения
IConfiguration configuration = builder.Configuration
    .AddJsonFile("appsettings.json", false, true)
#if DEBUG
    .AddJsonFile("appsettings.Development.json", true, false)
#else
    .AddJsonFile("appsettings.Production.json", true, false)
#endif
    .Build();

//Введение переменных для токена
var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(configuration["TokenSettings:Key"]!));
var issuer = configuration["TokenSettings:Issuer"];
var audience = configuration["TokenSettings:Audience"];

//Добавление параметров авторизации
services
    .AddAuthorizationBuilder()
    .AddPolicy("Bearer", new AuthorizationPolicyBuilder()
    .AddAuthenticationSchemes("Bearer")
    .RequireAuthenticatedUser().Build());

//Добавление параметров аутентификации
services
    .AddAuthentication(options => {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // указывает, будет ли валидироваться издатель при валидации токена
            ValidateIssuer = true,
            // строка, представляющая издателя
            ValidIssuer = issuer,
            // будет ли валидироваться потребитель токена
            ValidateAudience = true,
            // установка потребителя токена
            ValidAudience = audience,
            // будет ли валидироваться время существования
            ValidateLifetime = true,
            // установка ключа безопасности
            IssuerSigningKey = key,
            // валидация ключа безопасности
            ValidateIssuerSigningKey = true,
        };
    });

//Внедрение зависимостей сервисов
services.AddSingleton(_ => configuration); //конфигурация
services.AddScoped<ITransliterationSL, TransliterationSL>(); //сервис транслитерации
services.AddNewsBL(); //сервисы работы с бизнес-логикой в зоне новостей

//Добавление контекстов бд в коллекцию сервисов
services.AddDbContext<NewsContext>(options =>
{
    string connectionString = configuration.GetConnectionString("News") ?? throw new Exception(ErrorMessages.EmptyConnectionString);
    options.UseNpgsql(connectionString);
}); //бд новостей
services.AddDbContext<LogsApiNewsContext>(options =>
{
    string connectionString = configuration.GetConnectionString("LogsApiNews") ?? throw new Exception(ErrorMessages.EmptyConnectionString);
    options.UseNpgsql(connectionString);
}); //бд логов api в зоне новостей

//Установка игнорирования типов даты и времени
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

//Добавление параметров сериализации и десериализации json
services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.WriteIndented = true;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

//Добавление параметров логирования
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose()
    .WriteTo.File(path: configuration["LoggingOptions:FilePath"]!, rollingInterval: RollingInterval.Day)
    .WriteTo.Debug()
    .CreateLogger();
services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(Log.Logger, dispose: true));

//Добавление параметров документации
services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Insania API", Version = "v1" });

    var filePath = Path.Combine(AppContext.BaseDirectory, "Insania.News.ApiRead.xml");
    options.IncludeXmlComments(filePath);

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        In = ParameterLocation.Header,
        Name = "Authorization",
        Description = "Авторизация по ключу приложения",
        Scheme = "Bearer"
    });
    options.OperationFilter<AuthenticationRequirementsOperationFilter>();
});

//Добавление корсов
services.AddCors(options =>
{
    options.AddPolicy("BadPolicy", policyBuilder => policyBuilder
        .SetIsOriginAllowed(origin => true)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials()
    );

    options.DefaultPolicyName = "BadPolicy";
});

//Добавление контроллеров
services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

//Добавление параметров преобразования моделей
services.AddAutoMapper(typeof(NewsMappingProfile));

//Построение веб-приложения
WebApplication app = builder.Build();

//Добавление параметров конвейера запросов
app.UseMiddleware<LoggingMiddleware>();

//Подключение маршрутизации
app.UseRouting();

//Подключение аутентификации
app.UseAuthentication();

//Подключение авторизации
app.UseAuthorization();

//Подключение сваггера
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Insania API V1");
});

//Подключение корсов
app.UseCors();

//Подключение маршрутизации контроллеров
app.MapControllers();

//Запуск веб-приложения
app.Run();