using FinFlow.Application;
using FinFlow.Infrastructure;
using FinFlow.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFinFlowWorker(builder.Configuration);

var host = builder.Build();
host.Run();
