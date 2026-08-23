using EasyAuthLocalEmulator.SampleApp;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication application = builder.Build();

application.MapSampleEndpoints();
application.Run();
