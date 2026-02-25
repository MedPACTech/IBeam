using IBeam.Communications.Abstractions;
using IBeam.Communications.Sms.AzureCommunications;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Options;
using IBeam.Identity.Repositories.AzureTable.Extensions;
using IBeam.Identity.Services;
using IBeam.Identity.Services.Otp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Swagger (Swashbuckle v10+ / Microsoft.OpenApi v2+)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "IBeam Identity API",
        Version = "v1"
    });

    // JWT Bearer "Authorize" support
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT token}"
    });

    // Swashbuckle v10+ expects a delegate and the reference uses the OpenApiDocument
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

//builder.Services.Configure<SmsDefaultsOptions>(
//    builder.Configuration.GetSection("IBeam:Communications:Sms:Defaults"));

// Register your identity framework (AzureTable selected by config)
//builder.Services.AddIBeamIdentityServices(builder.Configuration);

// Register IBeam Identity core services
builder.Services.AddIBeamCommunications(builder.Configuration);

//// Register All Auth services
builder.Services.AddIBeamIdentityServices(builder.Configuration);

////builder.Services.AddIBeamIdentityAuthPasswordService();

//// Register Azure Table repository implementation
builder.Services.AddIBeamIdentityAzureTable(builder.Configuration);

//// Register Azure SMS provider (if using SMS for OTP)
builder.Services.AddIBeamCommunicationsSmsAzure(builder.Configuration);

//// Register your communication adapter for OTP/email/SMS
builder.Services.AddScoped<IIdentityCommunicationSender, IdentityCommunicationAdapter>();

builder.Services.AddIBeamIdentityAuthOtpService();
//// Register Data Protection (required for token providers)
builder.Services.AddDataProtection();

// JWT validation (matches your JwtTokenService settings)
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
jwt.Validate();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            //ValidateIssuer = jwt.ValidateIssuer,
            //ValidateAudience = jwt.ValidateAudience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,

            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "IBeam Identity API v1");
        c.RoutePrefix = "swagger"; // go to /swagger
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
