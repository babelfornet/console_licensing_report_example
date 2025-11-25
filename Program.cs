using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Babel.Licensing;

class Program
{
    static async Task Main()
    {
        var app = new LicensingReportApplication();
        await app.RunAsync();
    }
}

class LicensingReportApplication
{
    private const string ServiceUrl = "http://localhost:5000";
    private const string ClientId = "ACME EquiTrack";
    private const string UserKey = "25MPF-5QETJ-V2H7N-TZO8F";
    private const string PublicKey = "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDE1VRiIdr6fiVZKve7NVgjIvGdRiRx0Mjjm+Yzf6tLbzFnxLs0fat5EoRcubxx0QQQDfydsJBE/fc7cwRWSrE2xK6X4Eb4W8O47pCMjqvTQZfDqQywEZJrLlxpp9hlKz6FDYX4SagrjmP1gdw8olo+n+IBz8ubkNxRhvycikxuDQIDAQAB";

    private readonly BabelReporting _reporting;
    private readonly BabelLicensing _licensing;

    public LicensingReportApplication()
    {
        _reporting = CreateReportingClient();
        _licensing = CreateLicensingClient();
    }

    public async Task RunAsync()
    {
        while (true)
        {
            DisplayMenu();
            string? choice = Console.ReadLine();

            if (!await HandleMenuChoiceAsync(choice))
                break;

            Console.WriteLine();
        }
    }

    private BabelReporting CreateReportingClient()
    {
        var reporting = new BabelReporting();

        reporting.Configuration.ClientId = ClientId;
        reporting.Configuration.ServiceUrl = ServiceUrl;
        reporting.Configuration.UseHttp(http => {
            http.Timeout = TimeSpan.FromSeconds(3);
            http.Handler = new HttpClientHandler() {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
        });

        reporting.BeforeSendReport += (s, e) => {
            e.Report.Properties.Add("cmdline", Environment.CommandLine);
            e.Report.Properties.Add("username", Environment.UserName);
        };

        return reporting;
    }

    private BabelLicensing CreateLicensingClient()
    {
        var config = new BabelLicensingConfiguration()
        {
            ServiceUrl = ServiceUrl,
            SignatureProvider = RSASignature.FromKeys(PublicKey),
            ClientId = ClientId
        };

        config.UseHttp(http => {
            http.Timeout = TimeSpan.FromSeconds(10);
            http.Handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
        });

        // Register custom license factory to handle MemoryRestriction
        config.LicenseFactory = new CustomLicenseFactory();

        return new BabelLicensing(config);
    }

    private void DisplayMenu()
    {
        Console.WriteLine();
        Console.WriteLine("=== Licensing Report Menu ===");

        string[] entries = new[]
        {
            "1. Validate License (simulate usage)",
            "2. Send License Report",
            "3. Exit"
        };

        foreach (var entry in entries)
            Console.WriteLine(entry);

        Console.Write($"Select an option [1-{entries.Length}]: ");
    }

    private async Task<bool> HandleMenuChoiceAsync(string? choice)
    {
        switch (choice)
        {
            case "1":
                await ValidateLicenseAsync();
                break;
            case "2":
                await SendLicenseReportAsync();
                break;
            case "3":
                return false;
            default:
                Console.WriteLine("Invalid choice. Please try again.");
                break;
        }
        return true;
    }

    private async Task SendLicenseReportAsync()
    {
        var result = await _reporting.SendLicenseReportAsync(UserKey);
        if (result.IsError)
        {
            Console.WriteLine($"Error sending LicenseReport: {result.Error}");
            return;
        }
        Console.WriteLine("LicenseReport sent.");
    }

    private async Task ValidateLicenseAsync()
    {
        try
        {
            // Acquire floating license
            var requestResult = await _licensing.RequestFloatingLicenseAsync(UserKey, typeof(Program));
            Console.WriteLine($"Floating license {requestResult.License.Id} acquired.");

            try
            {
                // Validate the license (this will be tracked)
                var result = await _licensing.ValidateLicenseAsync(UserKey, typeof(Program));
                var license = result.License;

                Console.WriteLine($"License validated: {license.Id}");

                foreach (var feature in license.Features)
                {
                    Console.WriteLine($"  Feature: {feature.Name} - {feature.Description}");
                }

                foreach (var field in license.Fields)
                {
                    Console.WriteLine($"  Field: {field.Name} = {field.Value}");
                }

                foreach (var restriction in license.Restrictions)
                {
                    Console.WriteLine($"  Restriction: {restriction.Name}");

                    // Access type-specific properties to ensure tracking
                    switch (restriction)
                    {
                        case TrialRestriction trial:
                            Console.WriteLine($"    ExpireDays: {trial.ExpireDays}, RunCount: {trial.RunCount}, RunInstances: {trial.RunInstances}, TimeLeft: {trial.TimeLeft}");
                            break;
                        case HardwareRestriction hardware:
                            Console.WriteLine($"    HardwareKey: {hardware.HardwareKey}");
                            break;
                        case BetaRestriction beta:
                            Console.WriteLine($"    BuildType: {beta.BuildType}");
                            break;
                        case DomainRestriction domain:
                            Console.WriteLine($"    Domain: {domain.Domain}");
                            break;
                        case UsageRestriction usage:
                            Console.WriteLine($"    Usage: {usage.Usage}");
                            break;
                        case MemoryRestriction memory:
                            Console.WriteLine($"    TotalMemory: {memory.TotalMemory}");
                            break;
                    }
                }

                // Simulate multiple accesses to demonstrate tracking
                var reportingFeature = license.Features.FirstOrDefault(f => f.Name == "Reporting");
                var field2Field = license.Fields.FirstOrDefault(f => f.Name == "field2");

                for (int i = 0; i < 5; i++)
                {
                    var _ = reportingFeature?.Data;
                    var __ = field2Field?.Value;

                    // Access all restrictions
                    foreach (var restriction in license.Restrictions)
                    {
                        var ___ = restriction.Name;
                    }
                }

                Console.WriteLine($"\nLicense validation completed.");
            }
            finally
            {
                // Always release the floating license when done
                await _licensing.ReleaseFloatingLicenseAsync(UserKey);
                Console.WriteLine("Floating license released.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error validating license: {ex.Message}");
        }
    }
}

#region Custom License Factory

class MemoryRestriction : Restriction, ILicenseSerializable
{
    public override string Name => "Memory";

    public long TotalMemory { get; set; }

    public MemoryRestriction()
    {
    }

    public override ValidationResult Validate(ILicenseContext context, Type type, object instance)
    {
        ISystemInformation sys = (ISystemInformation)context.GetService(typeof(ISystemInformation));

        // Cannot run on this machine
        long totalMem = ToMegabytes(sys.TotalPhysicalMemory);
        if (totalMem < TotalMemory)
            return ValidationResult.Invalid;

        return base.Validate(context, type, instance);
    }

    private static long ToMegabytes(long bytes)
    {
        return bytes / 1024 / 1024;
    }

    public void Read(object state)
    {
        if (state is XmlReader xmlReader)
        {
            xmlReader.MoveToContent();

            string? totalMemory = xmlReader.GetAttribute("totalMemory");
            if (totalMemory != null)
                TotalMemory = XmlConvert.ToInt64(totalMemory);

            bool isEmptyElement = xmlReader.IsEmptyElement;
            xmlReader.ReadStartElement();
            if (!isEmptyElement)
                xmlReader.ReadEndElement();
        }
        else if (state is BinaryReader binaryReader)
        {
            TotalMemory = binaryReader.ReadInt32();
        }
    }

    public void Write(object state)
    {
        if (state is XmlWriter xmlWriter)
        {
            if (TotalMemory != 0)
                xmlWriter.WriteAttributeString("totalMemory", XmlConvert.ToString(TotalMemory));
        }
        else if (state is BinaryWriter binaryWriter)
        {
            binaryWriter.Write(TotalMemory);
        }
    }
}

class CustomLicenseFactory : ILicenseFactory
{
    public Feature CreateFeature(string name)
    {
        return new Feature(name);
    }

    public Field CreateField(string name)
    {
        return new Field(name);
    }

    public Restriction CreateRestriction(string type)
    {
        string[] tokens = type.Split(':');
        string restriction = tokens[0];

        switch (restriction)
        {
            case "Memory":
                var memory = new MemoryRestriction();
                if (tokens.Length > 1)
                    memory.TotalMemory = long.Parse(tokens[1]);
                return memory;
            default:
                return null!;
        }
    }

    public ISignatureProvider CreateSignatureProvider(string name)
    {
        return null!;
    }
}

#endregion
