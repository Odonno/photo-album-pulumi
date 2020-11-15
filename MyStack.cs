using System.Linq;
using Pulumi;
using Pulumi.Azure.AppInsights;
using Pulumi.Azure.AppService;
using Pulumi.Azure.AppService.Inputs;
using Pulumi.Azure.Core;
using Pulumi.Azure.Sql;
using Pulumi.Azure.Storage;
using Pulumi.Random;

class MyStack : Stack
{
    public MyStack()
    {
        // Create an Azure Resource Group
        var resourceGroup = new ResourceGroup("resourceGroup");

        // Create an Azure Storage Account
        var storageAccount = new Account("storage", new AccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountReplicationType = "LRS",
            AccountTier = "Standard"
        });

        // Create an Azure SQL Server
        var config = new Config();
        var username = config.Get("sqlAdmin") ?? "pulumi";
        var password = new RandomPassword("password", new RandomPasswordArgs
        {
            Length = 16,
            Special = true
        }).Result;

        var sqlServer = new SqlServer("sql", new SqlServerArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AdministratorLogin = username,
            AdministratorLoginPassword = password,
            Version = "12.0",
        });

        // Create an Azure SQL Server Database
        var database = new Database("db", new DatabaseArgs
        {
            ResourceGroupName = resourceGroup.Name,
            ServerName = sqlServer.Name,
            RequestedServiceObjectiveName = "S0",
        });

        // Create an Azure App Service Plan
        var appServicePlan = new Plan("asp", new PlanArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Kind = "App",
            Sku = new PlanSkuArgs
            {
                Tier = "Basic",
                Size = "B1"
            }
        });

        // Create an Azure App Service (Back)
        var appInsightsBackend = new Insights("appInsights-back", new InsightsArgs
        {
            ApplicationType = "web",
            ResourceGroupName = resourceGroup.Name
        });

        var backend = new AppService("backend", new AppServiceArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AppServicePlanId = appServicePlan.Id,
            AppSettings =
            {
                {"APPINSIGHTS_INSTRUMENTATIONKEY", appInsightsBackend.InstrumentationKey},
                {"APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsBackend.InstrumentationKey.Apply(key => $"InstrumentationKey={key}")},
                {"ApplicationInsightsAgent_EXTENSION_VERSION", "~2"},
            },
            ConnectionStrings =
            {
                new AppServiceConnectionStringArgs
                {
                    Name = "DefaultConnection",
                    Type = "SQLAzure",
                    Value = Output.Tuple<string, string, string>(sqlServer.Name, database.Name, password).Apply(t =>
                    {
                        (string server, string database, string pwd) = t;
                        return
                            $"Server=tcp:{server}​​.database.windows.net,1433;Initial Catalog={database}​​;Persist Security Info=False;User ID={username}​​;Password={pwd}​​;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
                    }),
                },
                new AppServiceConnectionStringArgs
                {
                    Name = "BlobStorage",
                    Type = "Custom",
                    Value = storageAccount.PrimaryConnectionString
                }
            },
        });

        // Create an Azure App Service (Front)
        var appInsightsFrontend = new Insights("appInsights-front", new InsightsArgs
        {
            ApplicationType = "web",
            ResourceGroupName = resourceGroup.Name
        });

        var frontend = new AppService("frontend", new AppServiceArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AppServicePlanId = appServicePlan.Id,
            AppSettings =
            {
                {"APPINSIGHTS_INSTRUMENTATIONKEY", appInsightsFrontend.InstrumentationKey},
                {"APPLICATIONINSIGHTS_CONNECTION_STRING", appInsightsFrontend.InstrumentationKey.Apply(key => $"InstrumentationKey={key}")},
                {"ApplicationInsightsAgent_EXTENSION_VERSION", "~2"},
            },
        });

        // Apply SQL firewall exceptions
        var firewallRules = backend.OutboundIpAddresses.Apply(
            ips => ips
                .Split(",")
                .Select(
                    ip => new FirewallRule($"FR{ip}", new FirewallRuleArgs
                    {
                        ResourceGroupName = resourceGroup.Name,
                        StartIpAddress = ip,
                        EndIpAddress = ip,
                        ServerName = sqlServer.Name
                    })
                )
                .ToList()
        );

        // Export values
        BackendUrl = backend.DefaultSiteHostname;
        FrontendUrl = frontend.DefaultSiteHostname;
    }

    [Output]
    public Output<string> BackendUrl { get; set; }

    [Output]
    public Output<string> FrontendUrl { get; set; }
}
