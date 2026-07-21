using DotNet.Vault.Configuration.Core;
using DotNet.Vault.Configuration.Core.Extensions;
using Microsoft.Extensions.Configuration;

Console.WriteLine("DotNet.Vault.Configuration - Basic Example");
Console.WriteLine("==========================================");

try
{
    var config = new ConfigurationBuilder()
        .AddVault(options =>
        {
            options.Uri = new Uri("http://localhost:8200");
            options.Authentication.Method = "token";
            options.Authentication.Token = new TokenAuthenticationOptions
            {
                Token = "myroot"
            };
            options.Kv.Enabled = true;
            options.Kv.BackendPath = "secret";
            options.Kv.Version = 2;
            options.Kv.ApplicationName = "myapp";
            options.FailFast = true;
        })
        .Build();

    Console.WriteLine();
    Console.WriteLine("Vault Configuration loaded successfully!");
    Console.WriteLine($"Configuration keys: {config.AsEnumerable().Count()}");

    foreach (var kvp in config.AsEnumerable())
    {
        Console.WriteLine($"  {kvp.Key} = {kvp.Value}");
    }
    
    Console.WriteLine();
    Console.WriteLine("Smoke test PASSED");
    Environment.Exit(0);
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"Smoke test FAILED: {ex.Message}");
    Console.WriteLine(ex.ToString());
    Environment.Exit(1);
}
