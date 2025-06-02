using BulkSMPosting.Logics.BLL;
using Microsoft.Extensions.Configuration;


class Program
{
    private readonly IConfiguration _configuration;
    static async Task Main(string[] args)
    {
        var builder = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json");

        IConfiguration configuration = builder.Build();

        SMBLL smBLL = new SMBLL(configuration);
      
        try
        {
            await smBLL.SMPost();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in scheduled task: {ex.Message}");
        }

        Console.WriteLine("API Calls Scheduled.");
        Console.ReadLine();
    }
}