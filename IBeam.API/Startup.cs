using System;
using System.Text;
using IBeam.Mappings;
using IBeam.Utilities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ServiceStack;

namespace IBeam.Portal.API
{
    public class Startup
    {
        private const string OriginsAllowed = "originsAllowed";

        private readonly IConfiguration _config;
        
        public Startup(IConfiguration config)
        {
            _config = config;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpContextAccessor();

            Registrations.RegisterServices(services);
            Registrations.RegisterRepositories(services);

            services.AddControllers();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "IBeam.API", Version = "v1" });
            });

            services.AddCors(options =>
            {
                options.AddPolicy(OriginsAllowed,
                    builder =>
                    {
                        //builder.AllowAnyOrigin();
                        builder.WithOrigins(
                            "http://localhost:4200",
                            "https://localhost:44354"
                        );
                        builder.WithMethods("GET", "POST", "DELETE");
                        builder.AllowCredentials();
                        builder.AllowAnyHeader();
                        //builder.AllowAnyMethod();
                    });
            });
            
            var appSettingsSection = _config.GetSection("BaseAppSettings");
            services.Configure<BaseAppSettings>(appSettingsSection);
            
            Licensing.RegisterLicense(_config.GetSection("servicestack").GetValue<string>("license"));


            // Check if a given license key string is valid.
            IronPdf.License.LicenseKey = "";
            // Check if IronPdf is licensed successfully 
            bool is_licensed = IronPdf.License.IsLicensed;

            //services.AddAutoMapper(typeof(MappingProfiles).Assembly);

            var appSettings = appSettingsSection.Get<BaseAppSettings>();
            var key = Encoding.ASCII.GetBytes(appSettings.Secret);

            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(x =>
            {
                x.SaveToken = true;
                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                };
            });
            
            services.AddHttpsRedirection(options =>
            {
                options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
                options.HttpsPort = 443;
            });

            services.AddRazorTemplating();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment() || env.IsEnvironment("local"))
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "IBeam.API v1"));
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseCors(OriginsAllowed);

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }
    }
}