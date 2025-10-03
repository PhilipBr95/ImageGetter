using ImageGetter.Models;

namespace ImageGetter
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddSingleton<Services.IImageService, Services.ImageService>();
            builder.Services.AddTransient<Settings>(s =>
            {
                var settings = new Settings
                {
                    Password = Environment.GetEnvironmentVariable("IMAGEGETTER_PASSWORD")
                };

                if (string.IsNullOrEmpty(settings.Password))
                {
                    throw new Exception("IMAGEGETTER_PASSWORD environment variable not set");
                }

                return settings;
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            //app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }

}