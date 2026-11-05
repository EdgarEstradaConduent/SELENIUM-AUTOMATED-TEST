<Query Kind="Program">
  <Reference>System.Windows.Forms</Reference>
  <Namespace>System</Namespace>
  <Namespace>System.Collections.Generic</Namespace>
  <Namespace>System.Diagnostics</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>System.IO</Namespace>
  <Namespace>System.Linq</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Text</Namespace>
  <Namespace>System.Text.RegularExpressions</Namespace>
  <Namespace>System.Threading</Namespace>
  <Namespace>System.Windows.Forms</Namespace>
  <Namespace>OpenQA.Selenium</Namespace>
  <Namespace>OpenQA.Selenium.Edge</Namespace>
  <Namespace>OpenQA.Selenium.Support.UI</Namespace>
  <IncludeUncapsulator>false</IncludeUncapsulator>
</Query>

// ============================================================================
// BDIS Selenium automation for LINQPad 8
//
// The functional workflow in this file is derived from BDIS.side:
//   1. Log in to the target environment.
//   2. Select and validate Canada, then run the Canada workflow.
//   3. Log out, log in again, select and validate United States, then run the
//      same workflow with United States parameters.
//   4. Aggregate both country results into one result per target URL.
//
// The existing LINQPad test harness (HTML report, local log, screenshots,
// pass/fail evaluation and Power Automate notification) is preserved.
// All credentials are read from Config.properties and are never logged.
// ============================================================================

/// <summary>
/// Global settings shared by the LINQPad query. Keeping these values in one
/// place avoids hardcoded paths and makes the expected deployment layout clear.
/// </summary>
public static class ProgramSettings
{
    public const string TestName = "BDIS";
    public const string BaseDirectory = @"C:\Selenium\BDISWEB-SANDY";
    public const string EdgeDriverDirectory = @"C:\Selenium";
    public const string EdgeDriverExecutable = @"C:\Selenium\msedgedriver.exe";

    public static string ConfigPath => Path.Combine(BaseDirectory, "Config.properties");
    public static string LogFolder => Path.Combine(BaseDirectory, "log");
}

/// <summary>
/// The two supported execution modes. Using an enum avoids raw string
/// comparisons and makes unsupported selections impossible.
/// </summary>
public enum TestExecutionMode
{
    F5Only,
    FullTest
}

/// <summary>
/// Describes one target environment and the key used in the Power Automate
/// payload.
/// </summary>
public sealed class UrlTestProfile
{
    public string WebKey { get; set; }
    public string ConfigKey { get; set; }
    public string Url { get; set; }
}

/// <summary>
/// Contains the values that differ between Canada and the United States.
/// The workflow itself is shared so the same code path is exercised for both
/// countries.
/// </summary>
public sealed class CountryTestProfile
{
    public string DisplayName { get; set; }
    public string CountryCode { get; set; }
    public string CountrySelectText { get; set; }
    public string DocumentQueryShipmentNumber { get; set; }
    public string PacketPrintShipmentNumber { get; set; }
    public string PacketNameLabel { get; set; }
}

/// <summary>
/// Result of one logical step within a country workflow.
/// </summary>
public sealed class TestStepResult
{
    public string EnvironmentName { get; set; }
    public string CountryName { get; set; }
    public string StepName { get; set; }
    public string Status { get; set; }
    public string Details { get; set; }
    public string ScreenshotPath { get; set; }
    public string ExecutedBy { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
}

/// <summary>
/// Result of running the complete workflow for one country.
/// </summary>
public sealed class CountryTestResult
{
    public string EnvironmentName { get; set; }
    public string CountryName { get; set; }
    public string Status { get; set; }
    public string FailedStep { get; set; }
    public string ErrorMessage { get; set; } = "";
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public List<TestStepResult> Steps { get; } = new List<TestStepResult>();
}

/// <summary>
/// Aggregated result for one target URL. This is the object sent to Power
/// Automate because the existing Parse JSON schema expects one row per
/// environment.
/// </summary>
public sealed class EnvironmentTestResult
{
    public string WebKey { get; set; }
    public string TargetUrl { get; set; }
    public string Status { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public string ErrorMessage { get; set; } = "";
    public List<CountryTestResult> Countries { get; } = new List<CountryTestResult>();
}

/// <summary>
/// Complete result of a test run.
/// </summary>
public sealed class RunSummary
{
    public string GlobalStatus { get; set; } = "PASSED";
    public List<EnvironmentTestResult> Environments { get; } = new List<EnvironmentTestResult>();
    public List<TestStepResult> Steps { get; } = new List<TestStepResult>();
}

/// <summary>
/// Raised when configuration is missing or invalid. The message identifies the
/// property name but never exposes its value.
/// </summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message) { }
}

/// <summary>
/// Raised when Microsoft Edge WebDriver cannot be initialized. The technical
/// details are used by the non-expiring help dialog and the local log.
/// </summary>
public sealed class EdgeDriverInitializationException : Exception
{
    public string TechnicalDetails { get; }

    public EdgeDriverInitializationException(string message, Exception innerException = null)
        : base(message, innerException)
    {
        TechnicalDetails = innerException == null
            ? message
            : message + Environment.NewLine + innerException;
    }
}

/// <summary>
/// Removes configured secrets from any text before it reaches a log, report,
/// dialog or webhook payload.
/// </summary>
public sealed class SecretRedactor
{
    private readonly List<string> _secrets = new List<string>();

    public void Add(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !_secrets.Contains(value))
        {
            _secrets.Add(value);
        }
    }

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var result = text;
        foreach (var secret in _secrets.OrderByDescending(s => s.Length))
        {
            result = result.Replace(secret, "***");
        }

        // Redact common secret-bearing query-string parameters as a second line
        // of defense. This covers values that were not explicitly registered.
        result = Regex.Replace(
            result,
            @"(?i)([?&](?:sig|token|key|secret|password)=)[^&\s]+",
            "$1***");

        return result;
    }
}

/// <summary>
/// Loads and validates Config.properties. Validation intentionally happens
/// before a browser is opened so configuration errors cannot be reported as
/// functional test failures.
/// </summary>
public sealed class ConfigurationManager
{
    private readonly Dictionary<string, string> _values;

    public string ConfigPath { get; private set; }
    public string BaseDirectory { get; private set; }
    public string LogFolder => Path.Combine(BaseDirectory, "log");
    public string ImageFilePath { get; private set; }
    public string Username { get; private set; }
    public string Password { get; private set; }
    public string EmailRecipient { get; private set; }
    public string WebhookUrl { get; private set; }
    public int ElementTimeoutSeconds { get; private set; }
    public int PageLoadTimeoutSeconds { get; private set; }
    public int UploadTimeoutSeconds { get; private set; }
    public List<CountryTestProfile> CountryProfiles { get; private set; }
    public SecretRedactor Redactor { get; private set; }

    private ConfigurationManager(Dictionary<string, string> values, string configPath, string baseDirectory)
    {
        _values = values;
        ConfigPath = configPath;
        BaseDirectory = baseDirectory;
        Redactor = new SecretRedactor();
    }

    public static ConfigurationManager LoadAndValidate(string configPath, string baseDirectory)
    {
        if (!File.Exists(configPath))
        {
            throw new ConfigurationException($"Configuration file was not found at: {configPath}");
        }

        var values = LoadValues(configPath);
        var manager = new ConfigurationManager(values, configPath, baseDirectory);
        manager.Validate();
        return manager;
    }

    /// <summary>
    /// Builds the list of environments selected by the current execution mode.
    /// F5 mode intentionally validates and runs only the F5 URL. Full Test
    /// validates and runs all five URLs from the configuration file.
    /// </summary>
    public List<UrlTestProfile> CreateUrlProfiles(TestExecutionMode mode)
    {
        var profiles = new List<UrlTestProfile>
        {
            new UrlTestProfile
            {
                WebKey = "F5",
                ConfigKey = "f5Url",
                Url = Require("f5Url")
            }
        };

        if (mode == TestExecutionMode.FullTest)
        {
            profiles.Add(new UrlTestProfile
            {
                WebKey = "SU6BDISWEB2",
                ConfigKey = "webServerUrl2",
                Url = Require("webServerUrl2")
            });
            profiles.Add(new UrlTestProfile
            {
                WebKey = "SU6BDISWEB3",
                ConfigKey = "webServerUrl3",
                Url = Require("webServerUrl3")
            });
            profiles.Add(new UrlTestProfile
            {
                WebKey = "SU6BDISWEB4",
                ConfigKey = "webServerUrl4",
                Url = Require("webServerUrl4")
            });
            profiles.Add(new UrlTestProfile
            {
                WebKey = "SU6BDISWEB5",
                ConfigKey = "webServerUrl5",
                Url = Require("webServerUrl5")
            });
        }

        return profiles;
    }

    private void Validate()
    {
        Username = Require("username");
        Password = Require("password");
        EmailRecipient = Require("emailRecipient");

        // The defaults match the recommended values in the refactoring
        // requirements and keep the script runnable with older config files.
        ElementTimeoutSeconds = GetInt("elementTimeoutSeconds", 30);
        PageLoadTimeoutSeconds = GetInt("pageLoadTimeoutSeconds", 60);
        UploadTimeoutSeconds = GetInt("uploadTimeoutSeconds", 120);

        WebhookUrl = GetOptional("powerAutomateWebhookUrl");

        ImageFilePath = Path.Combine(BaseDirectory, "BDISImg", "Image.tif");
        if (!File.Exists(ImageFilePath))
        {
            throw new ConfigurationException($"Required image file was not found at: {ImageFilePath}");
        }

        // Both country profiles are always required because both countries are
        // part of every selected environment's test sequence.
        CountryProfiles = new List<CountryTestProfile>
        {
            new CountryTestProfile
            {
                DisplayName = "Canada",
                CountryCode = "CA",
                CountrySelectText = "Canada",
                DocumentQueryShipmentNumber = Require("caShipmentNbr"),
                PacketPrintShipmentNumber = Require("caShipmentNbrPK"),
                PacketNameLabel = "Commercial Invoice"
            },
            new CountryTestProfile
            {
                DisplayName = "United States",
                CountryCode = "US",
                CountrySelectText = "United States",
                DocumentQueryShipmentNumber = Require("usShipmentNbr"),
                PacketPrintShipmentNumber = Require("usShipmentNbrPK"),
                // The spelling below is the exact label used by the application.
                PacketNameLabel = "CI - Commerical Invoice"
            }
        };

        Redactor.Add(Username);
        Redactor.Add(Password);
        Redactor.Add(WebhookUrl);
        Redactor.Add(EmailRecipient);
    }

    private string Require(string key)
    {
        if (!_values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ConfigurationException($"Missing required configuration property: {key}");
        }

        return value;
    }

    private string GetOptional(string key)
    {
        return _values.TryGetValue(key, out var value) ? value?.Trim() ?? "" : "";
    }

    private int GetInt(string key, int defaultValue)
    {
        var rawValue = GetOptional(key);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new ConfigurationException($"Configuration property '{key}' must be a positive integer. Actual value: '{rawValue}'.");
        }

        return value;
    }

    private static Dictionary<string, string> LoadValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
            {
                continue;
            }

            var parts = line.Split(new[] { '=' }, 2);
            if (parts.Length == 2)
            {
                values[parts[0].Trim()] = parts[1].Trim();
            }
        }

        return values;
    }
}

/// <summary>
/// Writes the human-readable local log. Every value is passed through the
/// secret redactor before it is written.
/// </summary>
public sealed class ExecutionLogger
{
    private readonly string _logPath;

    public SecretRedactor Redactor { get; set; } = new SecretRedactor();

    public ExecutionLogger(string logFolder, string testName)
    {
        Directory.CreateDirectory(logFolder);
        _logPath = Path.Combine(logFolder, testName + "WebApplication.txt");
    }

    public void LogInfo(string message)
    {
        Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tINFO\t{Redactor.Redact(message)}");
    }

    public void LogStep(TestStepResult step)
    {
        var durationSeconds = (step.EndTime - step.StartTime).TotalSeconds;
        var line = string.Join(
            "\t",
            step.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
            "STEP",
            $"Environment={Redactor.Redact(step.EnvironmentName)}",
            $"Country={Redactor.Redact(step.CountryName)}",
            $"Step={Redactor.Redact(step.StepName)}",
            $"Status={step.Status}",
            $"Duration={durationSeconds:F1}s",
            $"Details={Redactor.Redact(step.Details)}",
            $"Screenshot={Redactor.Redact(step.ScreenshotPath)}");

        Append(line);
    }

    public void LogError(string context, Exception exception)
    {
        var typeName = exception?.GetType().Name ?? "UnknownException";
        var message = Redactor.Redact(exception?.Message ?? "");
        var fullDetails = Redactor.Redact(exception?.ToString() ?? "");

        Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tERROR\tContext={context}\tType={typeName}\tMessage={message}");
        Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\tERROR_DETAIL\t{fullDetails}");
    }

    private void Append(string line)
    {
        File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
    }
}

/// <summary>
/// Generates the existing dark-themed HTML dashboard. The table now includes
/// environment and country context so failures can be traced to a specific
/// workflow.
/// </summary>
public static class HtmlReportGenerator
{
    public static void Generate(
        string filePath,
        List<TestStepResult> steps,
        string testName,
        TestExecutionMode? mode,
        string operatorUser,
        string globalStatus)
    {
        var totalSteps = steps?.Count ?? 0;
        var passedSteps = steps?.Count(s => s.Status == "PASSED") ?? 0;
        var statusColor = globalStatus == "PASSED"
            ? "#48BB78"
            : globalStatus == "NOT_EXECUTED" ? "#ECC94B" : "#F56565";

        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset='utf-8'><title>");
        sb.Append(System.Net.WebUtility.HtmlEncode(testName));
        sb.Append(" Automation Dashboard</title>");
        sb.Append("<style>");
        sb.Append("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 40px; background-color: #1A202C; color: #E2E8F0; }");
        sb.Append(".header-container { display: flex; justify-content: space-between; align-items: center; border-bottom: 3px solid #4A5568; padding-bottom: 20px; margin-bottom: 30px; }");
        sb.Append("h2 { color: #ECC94B; margin: 0; font-size: 28px; font-weight: 600; }");
        sb.Append(".timestamp { color: #A0AEC0; font-size: 14px; text-align: right; }");
        sb.Append(".summary-deck { display: flex; gap: 20px; margin-bottom: 30px; }");
        sb.Append(".card { flex: 1; background: #2D3748; border-radius: 8px; padding: 15px 20px; box-shadow: 0 4px 6px rgba(0,0,0,0.2); border-left: 5px solid #4A5568; }");
        sb.Append(".card.status-card { border-left-color: " + statusColor + "; }");
        sb.Append(".card.agent-card { border-left-color: #63B3ED; }");
        sb.Append(".card.mode-card { border-left-color: #B794F4; }");
        sb.Append(".card-title { font-size: 12px; text-transform: uppercase; color: #A0AEC0; font-weight: bold; letter-spacing: 1px; }");
        sb.Append(".card-value { font-size: 20px; font-weight: bold; margin-top: 5px; color: #FFF; }");
        sb.Append("table { width: 100%; border-collapse: collapse; background: #2D3748; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 10px rgba(0,0,0,0.3); }");
        sb.Append("th { background-color: #1A202C; color: #ECC94B; font-weight: 600; text-transform: uppercase; font-size: 13px; letter-spacing: 0.5px; padding: 15px; text-align: left; }");
        sb.Append("td { padding: 15px; border-bottom: 1px solid #4A5568; font-size: 14px; vertical-align: top; }");
        sb.Append("tr:last-child td { border-bottom: none; }");
        sb.Append("tr:hover { background-color: #3A475C; }");
        sb.Append(".badge { padding: 4px 8px; border-radius: 4px; font-weight: bold; font-size: 11px; display: inline-block; text-transform: uppercase; }");
        sb.Append(".badge-success { background-color: #2F855A; color: #C6F6D5; }");
        sb.Append(".badge-danger { background-color: #9B2C2C; color: #FED7D7; }");
        sb.Append(".badge-warning { background-color: #975A16; color: #FEFCBF; }");
        sb.Append("pre { background: #1A202C; padding: 12px; border-radius: 6px; border-left: 4px solid #F56565; white-space: pre-wrap; color: #FEB2B2; font-size: 12px; font-family: 'Courier New', Courier, monospace; margin-top: 8px; }");
        sb.Append(".screenshot-link { display: inline-block; margin-top: 8px; color: #63B3ED; text-decoration: none; font-weight: bold; font-size: 13px; }");
        sb.Append(".screenshot-link:hover { text-decoration: underline; color: #4299E1; }");
        sb.Append("</style></head><body>");

        sb.Append("<div class='header-container'>");
        sb.Append("<div><h2>Automated Test Execution Dashboard</h2><div style='color:#A0AEC0; margin-top:5px;'>");
        sb.Append(System.Net.WebUtility.HtmlEncode(testName));
        sb.Append(" Web Application Evaluation</div></div>");
        sb.Append("<div class='timestamp'><strong>Execution Date:</strong> ");
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.Append("</div></div>");

        sb.Append("<div class='summary-deck'>");
        sb.Append("<div class='card agent-card'><div class='card-title'>PCM Operator Agent</div><div class='card-value'>");
        sb.Append(System.Net.WebUtility.HtmlEncode(operatorUser ?? "Unknown"));
        sb.Append("</div></div>");
        sb.Append("<div class='card mode-card'><div class='card-title'>Execution Mode</div><div class='card-value'>");
        sb.Append(mode.HasValue ? mode.Value.ToString() : "Not selected");
        sb.Append("</div></div>");
        sb.Append("<div class='card status-card'><div class='card-title'>Global Evaluation Status</div><div class='card-value' style='color:");
        sb.Append(statusColor);
        sb.Append(";'>");
        sb.Append(System.Net.WebUtility.HtmlEncode(globalStatus));
        sb.Append("</div></div>");
        sb.Append("<div class='card'><div class='card-title'>Consolidated Steps</div><div class='card-value'>");
        sb.Append(passedSteps);
        sb.Append(" / ");
        sb.Append(totalSteps);
        sb.Append(" Steps Passed");
        sb.Append("</div></div>");
        sb.Append("</div>");

        sb.Append("<table>");
        sb.Append("<tr><th style='width:5%;'>#</th><th style='width:12%;'>Environment</th><th style='width:12%;'>Country</th><th style='width:26%;'>Test Step Context</th><th style='width:45%;'>Execution Details / Logs</th></tr>");

        if (steps != null)
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var badgeClass = step.Status == "PASSED"
                    ? "badge-success"
                    : step.Status == "NOT_EXECUTED" ? "badge-warning" : "badge-danger";
                var badgeText = step.Status == "PASSED"
                    ? "PASSED"
                    : step.Status == "NOT_EXECUTED" ? "NOT EXECUTED" : "ERROR";

                sb.Append("<tr>");
                sb.Append("<td>");
                sb.Append(i + 1);
                sb.Append("</td>");
                sb.Append("<td>");
                sb.Append(System.Net.WebUtility.HtmlEncode(step.EnvironmentName ?? ""));
                sb.Append("</td>");
                sb.Append("<td>");
                sb.Append(System.Net.WebUtility.HtmlEncode(step.CountryName ?? ""));
                sb.Append("</td>");
                sb.Append("<td><strong>");
                sb.Append(System.Net.WebUtility.HtmlEncode(step.StepName ?? ""));
                sb.Append("</strong></td>");
                sb.Append("<td><span class='badge ");
                sb.Append(badgeClass);
                sb.Append("' style='margin-right:10px;'>");
                sb.Append(badgeText);
                sb.Append("</span>");

                if (step.Status != "PASSED")
                {
                    sb.Append("<pre>");
                    sb.Append(System.Net.WebUtility.HtmlEncode(step.Details ?? ""));
                    sb.Append("</pre>");

                    if (!string.IsNullOrWhiteSpace(step.ScreenshotPath))
                    {
                        sb.Append("<br><a class='screenshot-link' href='");
                        sb.Append(System.Net.WebUtility.HtmlEncode(step.ScreenshotPath));
                        sb.Append("' target='_blank'>View Error Screenshot</a>");
                    }
                }
                else
                {
                    sb.Append("<span style='color: #CBD5E0;'>");
                    sb.Append(System.Net.WebUtility.HtmlEncode(step.Details ?? ""));
                    sb.Append("</span>");
                }

                sb.Append("</td></tr>");
            }
        }

        sb.Append("</table></body></html>");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }
}

/// <summary>
/// Builds the exact JSON envelope already accepted by the existing Power
/// Automate flow: metadata plus a nested results array.
/// </summary>
public sealed class PowerAutomateReporter
{
    private readonly ConfigurationManager _config;
    private readonly ExecutionLogger _logger;

    public PowerAutomateReporter(ConfigurationManager config, ExecutionLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public void Send(string testName, string user, List<EnvironmentTestResult> results, string globalStatus)
    {
        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
        {
            _logger.LogInfo("Power Automate reporting skipped because powerAutomateWebhookUrl is empty.");
            return;
        }

        try
        {
            var resultsJson = new StringBuilder();
            resultsJson.Append('[');

            for (var i = 0; i < results.Count; i++)
            {
                if (i > 0)
                {
                    resultsJson.Append(',');
                }

                var result = results[i];
                resultsJson.Append('{');
                resultsJson.Append($"\"webKey\":\"{JsonHelper.Escape(result.WebKey)}\",");
                resultsJson.Append($"\"targetUrl\":\"{JsonHelper.Escape(result.TargetUrl)}\",");
                resultsJson.Append($"\"status\":\"{JsonHelper.Escape(result.Status)}\",");
                resultsJson.Append($"\"startTime\":\"{result.StartTime.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)}\",");
                resultsJson.Append($"\"endTime\":\"{result.EndTime.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)}\",");
                resultsJson.Append($"\"error\":\"{JsonHelper.Escape(result.ErrorMessage)}\"");
                resultsJson.Append('}');
            }

            resultsJson.Append(']');

            var payload = "{"
                + $"\"testName\":\"{JsonHelper.Escape(testName)}\","
                + $"\"user\":\"{JsonHelper.Escape(user)}\","
                + $"\"globalStatus\":\"{JsonHelper.Escape(globalStatus)}\","
                + $"\"results\":{resultsJson}"
                + "}";

            _logger.LogInfo("Sending Power Automate notification.");

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);

                using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
                using (var response = client.PostAsync(_config.WebhookUrl, content).GetAwaiter().GetResult())
                {
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInfo($"Power Automate notification sent successfully. Status code: {(int)response.StatusCode}.");
                    }
                    else
                    {
                        var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        _logger.LogInfo($"Power Automate notification failed. Status code: {(int)response.StatusCode}. Response: {responseBody}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError("Power Automate notification", exception);
        }
    }
}

/// <summary>
/// Escapes values for the manually built JSON payload.
/// </summary>
public static class JsonHelper
{
    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var sb = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (character < ' ')
                    {
                        sb.Append($"\\u{(int)character:X4}");
                    }
                    else
                    {
                        sb.Append(character);
                    }
                    break;
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Creates screenshots with predictable names. Screenshot failures never hide
/// the original test failure.
/// </summary>
public static class ScreenshotHelper
{
    public static string Capture(IWebDriver driver, string folder, string environment, string country, string stepName)
    {
        try
        {
            if (driver == null)
            {
                return null;
            }

            Directory.CreateDirectory(folder);
            var safeName = MakeSafeFileName($"{environment}_{country}_{stepName}");
            var path = Path.Combine(folder, $"Screenshot_Error_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            var screenshot = ((ITakesScreenshot)driver).GetScreenshot();
            File.WriteAllBytes(path, screenshot.AsByteArray);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var characters = value
            .Select(character => invalidCharacters.Contains(character) || character == ' ' ? '_' : character)
            .ToArray();

        return new string(characters);
    }
}

/// <summary>
/// Centralizes explicit Selenium waits. Fixed delays are intentionally avoided
/// because the application is asynchronous and element-based waits are more
/// reliable.
/// </summary>
public sealed class SeleniumWaitHelper
{
    private readonly IWebDriver _driver;
    private readonly ConfigurationManager _config;

    public SeleniumWaitHelper(IWebDriver driver, ConfigurationManager config)
    {
        _driver = driver;
        _config = config;
    }

    public IWebElement WaitVisible(By by)
    {
        return WaitVisible(by, _config.ElementTimeoutSeconds);
    }

    public IWebElement WaitVisible(By by, int timeoutSeconds)
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            return wait.Until(driver =>
            {
                try
                {
                    var element = driver.FindElement(by);
                    return element.Displayed ? element : null;
                }
                catch (NoSuchElementException)
                {
                    return null;
                }
                catch (StaleElementReferenceException)
                {
                    return null;
                }
            });
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new WebDriverTimeoutException(
                $"Element was not visible within {timeoutSeconds} seconds: {by}",
                exception);
        }
    }

    public IWebElement WaitClickable(By by)
    {
        return WaitClickable(by, _config.ElementTimeoutSeconds);
    }

    public IWebElement WaitClickable(By by, int timeoutSeconds)
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            return wait.Until(driver =>
            {
                try
                {
                    var element = driver.FindElement(by);
                    return element.Displayed && element.Enabled ? element : null;
                }
                catch (NoSuchElementException)
                {
                    return null;
                }
                catch (StaleElementReferenceException)
                {
                    return null;
                }
            });
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new WebDriverTimeoutException(
                $"Element was not clickable within {timeoutSeconds} seconds: {by}",
                exception);
        }
    }

    public IWebElement WaitAnyVisible(int timeoutSeconds, params By[] locators)
    {
        IWebElement foundElement = null;
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            wait.Until(driver =>
            {
                foreach (var locator in locators)
                {
                    try
                    {
                        var element = driver.FindElement(locator);
                        if (element != null && element.Displayed)
                        {
                            foundElement = element;
                            return true;
                        }
                    }
                    catch (NoSuchElementException)
                    {
                    }
                    catch (StaleElementReferenceException)
                    {
                    }
                }

                return false;
            });
        }
        catch (WebDriverTimeoutException exception)
        {
            var selectorList = string.Join(", ", locators.Select(locator => locator.ToString()));
            throw new WebDriverTimeoutException(
                $"None of the expected elements became visible within {timeoutSeconds} seconds: {selectorList}",
                exception);
        }

        return foundElement;
    }

    public IWebElement WaitPresent(By by, int timeoutSeconds)
    {
        IWebElement foundElement = null;
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            wait.Until(driver =>
            {
                var elements = driver.FindElements(by);
                if (elements.Count > 0)
                {
                    foundElement = elements[0];
                    return true;
                }

                return false;
            });
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new WebDriverTimeoutException(
                $"Element was not present within {timeoutSeconds} seconds: {by}",
                exception);
        }

        return foundElement;
    }

    public bool IsElementPresent(By by, int timeoutSeconds)
    {
        try
        {
            WaitPresent(by, timeoutSeconds);
            return true;
        }
        catch (WebDriverTimeoutException)
        {
            return false;
        }
    }

    public void WaitForPageSourceContains(string text, int timeoutSeconds)
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            wait.Until(driver => driver.PageSource.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new WebDriverTimeoutException(
                $"The page did not contain the expected text within {timeoutSeconds} seconds.",
                exception);
        }
    }

    public string WaitForNewWindowHandle(int existingWindowCount, int timeoutSeconds)
    {
        string newHandle = null;
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            wait.Until(driver =>
            {
                var handles = driver.WindowHandles;
                if (handles.Count > existingWindowCount)
                {
                    newHandle = handles.Last();
                    return true;
                }

                return false;
            });
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new WebDriverTimeoutException(
                $"A new browser window or tab did not open within {timeoutSeconds} seconds.",
                exception);
        }

        return newHandle;
    }

    public IAlert WaitForAlert(int timeoutSeconds)
    {
        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            return wait.Until(driver =>
            {
                try
                {
                    return driver.SwitchTo().Alert();
                }
                catch (NoAlertPresentException)
                {
                    return null;
                }
            });
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new WebDriverTimeoutException(
                $"The expected confirmation alert did not appear within {timeoutSeconds} seconds.",
                exception);
        }
    }
}

/// <summary>
/// Selects a country and validates the selection using application state that
/// is independent from the dropdown itself.
/// </summary>
public sealed class CountrySelectionService
{
    public void SelectCountry(SelectElement selectElement, CountryTestProfile profile)
    {
        try
        {
            selectElement.SelectByText(profile.CountrySelectText);
        }
        catch (NoSuchElementException)
        {
            try
            {
                selectElement.SelectByValue(profile.CountryCode);
            }
            catch (NoSuchElementException)
            {
                var option = selectElement.Options.FirstOrDefault(candidate =>
                        candidate.Text.IndexOf(profile.CountrySelectText, StringComparison.OrdinalIgnoreCase) >= 0
                        || candidate.Text.IndexOf(profile.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0);

                if (option == null)
                {
                    throw new Exception($"Country '{profile.DisplayName}' was not found in the country list.");
                }

                option.Click();
            }
        }

        var selectedText = selectElement.SelectedOption.Text?.Trim() ?? "";
        if (selectedText.IndexOf(profile.CountrySelectText, StringComparison.OrdinalIgnoreCase) < 0
            && selectedText.IndexOf(profile.CountryCode, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new Exception($"Country selection validation failed. Expected '{profile.CountrySelectText}' but the selected value was '{selectedText}'.");
        }
    }

    public void ValidateCountry(IWebDriver driver, SeleniumWaitHelper wait, CountryTestProfile profile)
    {
        // The Packet Print link contains CountryCode=CA or CountryCode=US. This
        // confirms that the application actually switched country, rather than
        // only changing the dropdown selection.
        var packetPrintLink = wait.WaitVisible(By.Id("hlPacketPrint"));
        var href = packetPrintLink.GetAttribute("href") ?? "";
        var expectedCountryCode = $"CountryCode={profile.CountryCode}";

        if (href.IndexOf(expectedCountryCode, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new Exception($"Country selection could not be confirmed. Expected '{expectedCountryCode}' in the Packet Print link, but the actual link was '{href}'.");
        }
    }
}

/// <summary>
/// Performs login and logout for every target URL using the single universal
/// credential pair. Credentials are never written to logs or exception text.
/// </summary>
public sealed class BdisLoginService
{
    private readonly ConfigurationManager _config;
    private readonly CountrySelectionService _countrySelectionService;

    public BdisLoginService(ConfigurationManager config, CountrySelectionService countrySelectionService)
    {
        _config = config;
        _countrySelectionService = countrySelectionService;
    }

    public void Login(IWebDriver driver, string url, SeleniumWaitHelper wait, CountryTestProfile profile)
    {
        driver.Navigate().GoToUrl(url);

        var userNameBox = wait.WaitVisible(By.Id("MainContent_ebUserID"));
        userNameBox.Click();
        userNameBox.Clear();
        userNameBox.SendKeys(_config.Username);

        var passwordBox = wait.WaitVisible(By.Id("MainContent_ebPassword"));
        passwordBox.Click();
        passwordBox.Clear();
        passwordBox.SendKeys(_config.Password);

        var countrySelectElement = new SelectElement(wait.WaitVisible(By.Id("MainContent_ddlCountry")));
        _countrySelectionService.SelectCountry(countrySelectElement, profile);

        wait.WaitClickable(By.Id("MainContent_btnLogin")).Click();

        try
        {
            wait.WaitAnyVisible(_config.ElementTimeoutSeconds, By.Id("hlDocumentQuery"), By.Id("tbVal1_1"));
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new Exception(
                "Login failed. The expected post-login page was not displayed. Verify the configured credentials, country selection and target environment.",
                exception);
        }

        _countrySelectionService.ValidateCountry(driver, wait, profile);
    }

    public void Logout(IWebDriver driver, SeleniumWaitHelper wait, string url)
    {
        try
        {
            var logoutLink = driver.FindElements(By.Id("lnkbLogOut")).FirstOrDefault()
                             ?? driver.FindElements(By.LinkText("Log Out")).FirstOrDefault();

            if (logoutLink != null)
            {
                logoutLink.Click();
            }
        }
        catch
        {
            // A logout failure is recoverable: the next login navigates to the
            // target URL again and starts from a clean page.
        }

        try
        {
            wait.WaitVisible(By.Id("MainContent_ebUserID"), 15);
        }
        catch (WebDriverTimeoutException)
        {
            driver.Navigate().GoToUrl(url);
            wait.WaitVisible(By.Id("MainContent_ebUserID"));
        }
    }
}

/// <summary>
/// Implements the reusable country workflow derived from BDIS.side. All
/// selectors are ID-based selectors recorded in BDIS.side; absolute XPath
/// expressions from the old script are not used.
/// </summary>
public sealed class BdisWorkflow
{
    private readonly ConfigurationManager _config;

    public BdisWorkflow(ConfigurationManager config)
    {
        _config = config;
    }

    public void DocumentQuerySearch(IWebDriver driver, SeleniumWaitHelper wait, CountryTestProfile profile)
    {
        // Login normally lands on the Document Query page. If it does not, use
        // the recorded Document Query menu link to reach a stable page.
        if (!wait.IsElementPresent(By.Id("tbVal1_1"), 5))
        {
            wait.WaitClickable(By.Id("hlDocumentQuery")).Click();
        }

        var shipmentBox = wait.WaitVisible(By.Id("tbVal1_1"));
        shipmentBox.Click();
        shipmentBox.Clear();
        shipmentBox.SendKeys(profile.DocumentQueryShipmentNumber);

        wait.WaitClickable(By.Id("btnSearch")).Click();
        wait.WaitVisible(By.Id("MainContent_Checkbox1"));
    }

    public void DocumentQueryPrint(IWebDriver driver, SeleniumWaitHelper wait)
    {
        wait.WaitClickable(By.Id("MainContent_Checkbox1")).Click();

        var mainWindowHandle = driver.CurrentWindowHandle;
        var existingWindowCount = driver.WindowHandles.Count;

        wait.WaitClickable(By.Id("MainContent_btnPrint")).Click();
        CloseNewWindowAndReturn(driver, wait, existingWindowCount, mainWindowHandle);
    }

    public void DocumentQueryEmail(IWebDriver driver, SeleniumWaitHelper wait)
    {
        wait.WaitClickable(By.Id("MainContent_btnEmail")).Click();

        var emailToBox = wait.WaitVisible(By.Id("MainContent_txtEmailTo"));
        emailToBox.Click();
        emailToBox.Clear();
        emailToBox.SendKeys(_config.EmailRecipient);

        var emailBodyBox = wait.WaitVisible(By.Id("MainContent_txtEmailBody"));
        emailBodyBox.Click();
        emailBodyBox.Clear();
        emailBodyBox.SendKeys("BDIS TEST");

        wait.WaitClickable(By.Id("MainContent_BtnSend")).Click();
        wait.WaitForPageSourceContains($"E-mail successfully sent to {_config.EmailRecipient}.", _config.PageLoadTimeoutSeconds);

        // Returning to the Document Query page makes the following Packet Print
        // navigation deterministic for both countries.
        wait.WaitClickable(By.Id("hlDocumentQuery")).Click();
    }

    public void PacketPrintSelectAndSearch(IWebDriver driver, SeleniumWaitHelper wait, CountryTestProfile profile)
    {
        wait.WaitClickable(By.Id("hlPacketPrint")).Click();

        var packetNameElement = wait.WaitVisible(By.Id("MainContent_PacketName"));
        var packetNameSelect = new SelectElement(packetNameElement);
        SelectPacketByName(packetNameSelect, profile.PacketNameLabel);

        var shipmentBox = wait.WaitVisible(By.Id("MainContent_Box1"));
        shipmentBox.Click();
        shipmentBox.Clear();
        shipmentBox.SendKeys(profile.PacketPrintShipmentNumber);

        wait.WaitClickable(By.Id("MainContent_GetPacketsBtn")).Click();
        wait.WaitClickable(By.Id("MainContent_Grid1_Remove_0"));
    }

    public void PacketPrintPrint(IWebDriver driver, SeleniumWaitHelper wait)
    {
        wait.WaitClickable(By.Id("MainContent_Grid1_Remove_0")).Click();

        var mainWindowHandle = driver.CurrentWindowHandle;
        var existingWindowCount = driver.WindowHandles.Count;

        wait.WaitClickable(By.Id("MainContent_BtnPrint")).Click();
        CloseNewWindowAndReturn(driver, wait, existingWindowCount, mainWindowHandle);
    }

    public void UploadImage(IWebDriver driver, SeleniumWaitHelper wait)
    {
        wait.WaitClickable(By.Id("hlManipulateImage")).Click();

        // Prefer the HTML file input so the native Windows File Explorer dialog
        // is not automated with mouse or keyboard timing.
        var fileInput = driver.FindElements(By.CssSelector("input[type='file']"))
            .FirstOrDefault(element => element.Enabled || element.Displayed);

        if (fileInput == null)
        {
            // Some builds create the file input only after the Upload File
            // control is activated. This is the only fallback and must be
            // validated against the live application.
            wait.WaitClickable(By.CssSelector(".formbuttonblue")).Click();
            fileInput = wait.WaitPresent(By.CssSelector("input[type='file']"), _config.ElementTimeoutSeconds);
        }

        try
        {
            fileInput.SendKeys(_config.ImageFilePath);
        }
        catch (ElementNotInteractableException)
        {
            // A hidden file input can still be populated after making it
            // visible through JavaScript.
            var javascriptExecutor = (IJavaScriptExecutor)driver;
            javascriptExecutor.ExecuteScript(
                "arguments[0].style.display='block'; arguments[0].style.visibility='visible';",
                fileInput);
            fileInput.SendKeys(_config.ImageFilePath);
        }

        wait.WaitVisible(By.CssSelector(".iv-viewer > img"), _config.UploadTimeoutSeconds);
    }

    public void EditImage(IWebDriver driver, SeleniumWaitHelper wait)
    {
        wait.WaitVisible(By.CssSelector(".iv-viewer > img")).Click();
        wait.WaitClickable(By.CssSelector(".fa-rotate-left")).Click();
        wait.WaitClickable(By.CssSelector(".fa-rotate-right")).Click();
        wait.WaitClickable(By.CssSelector(".fa-search-plus")).Click();
        wait.WaitClickable(By.CssSelector(".fa-search-minus")).Click();
    }

    public void DeleteImage(IWebDriver driver, SeleniumWaitHelper wait)
    {
        wait.WaitVisible(By.CssSelector(".checkbox-select"), _config.ElementTimeoutSeconds);

        var checkbox = driver.FindElements(By.CssSelector(".checkbox-select > .ng-untouched")).FirstOrDefault()
                       ?? driver.FindElements(By.CssSelector(".checkbox-select input[type='checkbox']")).FirstOrDefault();

        if (checkbox == null)
        {
            throw new Exception("The image selection checkbox was not found.");
        }

        checkbox.Click();
        wait.WaitClickable(By.CssSelector(".fa-trash")).Click();

        var alert = wait.WaitForAlert(_config.ElementTimeoutSeconds);
        var alertText = alert.Text;
        alert.Accept();

        const string expectedConfirmation = "Are you sure you want to delete the 1 selected image(s)?";
        if (alertText.IndexOf(expectedConfirmation, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new Exception($"The delete confirmation text did not match the expected value. Actual: '{alertText}'.");
        }
    }

    private static void SelectPacketByName(SelectElement selectElement, string packetName)
    {
        try
        {
            selectElement.SelectByText(packetName);
        }
        catch (NoSuchElementException)
        {
            var option = selectElement.Options.FirstOrDefault(candidate =>
                    candidate.Text.Trim().Equals(packetName, StringComparison.OrdinalIgnoreCase))
                ?? selectElement.Options.FirstOrDefault(candidate =>
                    candidate.Text.IndexOf(packetName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (option == null)
            {
                throw new Exception($"Packet type '{packetName}' was not found in the packet name list.");
            }

            option.Click();
        }

        var selectedText = selectElement.SelectedOption.Text?.Trim() ?? "";
        if (selectedText.IndexOf(packetName, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new Exception($"Packet type selection failed. Expected '{packetName}' but the selected value was '{selectedText}'.");
        }
    }

    private static void CloseNewWindowAndReturn(
        IWebDriver driver,
        SeleniumWaitHelper wait,
        int existingWindowCount,
        string mainWindowHandle)
    {
        var newWindowHandle = wait.WaitForNewWindowHandle(existingWindowCount, 30);
        driver.SwitchTo().Window(newWindowHandle);
        driver.Close();

        if (driver.WindowHandles.Contains(mainWindowHandle))
        {
            driver.SwitchTo().Window(mainWindowHandle);
        }
        else if (driver.WindowHandles.Count > 0)
        {
            driver.SwitchTo().Window(driver.WindowHandles.First());
        }
        else
        {
            throw new Exception("The main browser window was closed unexpectedly after the print window was handled.");
        }
    }
}

/// <summary>
/// Creates Microsoft Edge, validates the driver before startup and converts
/// every startup failure into a dedicated exception so the caller can display
/// the required non-expiring help dialog.
/// </summary>
public sealed class EdgeDriverManager
{
    private static readonly string[] EdgeExecutablePaths =
    {
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    };

    public EdgeDriver CreateDriver(ExecutionLogger logger)
    {
        ValidateDriverExecutable();
        ValidateMajorVersion(logger);

        EdgeDriver driver = null;
        try
        {
            var options = new EdgeOptions();
            options.AddArgument("--ignore-certificate-errors");
            options.AddArgument("--allow-insecure-localhost");

            var service = EdgeDriverService.CreateDefaultService(ProgramSettings.EdgeDriverDirectory);
            service.HideCommandPromptWindow = true;

            driver = new EdgeDriver(service, options);
            return driver;
        }
        catch (Exception exception) when (!(exception is EdgeDriverInitializationException))
        {
            try { driver?.Quit(); } catch { }
            try { driver?.Dispose(); } catch { }

            throw new EdgeDriverInitializationException(
                $"Microsoft Edge WebDriver could not be initialized. {exception.GetType().Name}: {exception.Message}",
                exception);
        }
    }

    private static void ValidateDriverExecutable()
    {
        var driverFile = new FileInfo(ProgramSettings.EdgeDriverExecutable);
        if (!driverFile.Exists)
        {
            throw new EdgeDriverInitializationException(
                $"Microsoft Edge WebDriver was not found at: {ProgramSettings.EdgeDriverExecutable}");
        }

        if (driverFile.Length == 0)
        {
            throw new EdgeDriverInitializationException(
                $"Microsoft Edge WebDriver at {ProgramSettings.EdgeDriverExecutable} is empty or corrupted.");
        }
    }

    private static void ValidateMajorVersion(ExecutionLogger logger)
    {
        var edgeVersion = TryGetEdgeVersion();
        var driverVersion = TryGetDriverVersion();

        if (string.IsNullOrWhiteSpace(edgeVersion) || string.IsNullOrWhiteSpace(driverVersion))
        {
            logger.LogInfo($"Could not determine both browser and driver versions. Edge={edgeVersion ?? "unknown"}, EdgeDriver={driverVersion ?? "unknown"}. Continuing with startup validation.");
            return;
        }

        var edgeMajor = GetMajorVersion(edgeVersion);
        var driverMajor = GetMajorVersion(driverVersion);

        logger.LogInfo($"Detected Microsoft Edge version: {edgeVersion}; EdgeDriver version: {driverVersion}.");

        if (edgeMajor.HasValue && driverMajor.HasValue && edgeMajor.Value != driverMajor.Value)
        {
            throw new EdgeDriverInitializationException(
                $"Microsoft Edge major version ({edgeMajor.Value}) does not match EdgeDriver major version ({driverMajor.Value}). Edge version: {edgeVersion}. EdgeDriver version: {driverVersion}.");
        }
    }

    private static string TryGetEdgeVersion()
    {
        var registryKeys = new[]
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Edge\BLBeacon",
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Edge\BLBeacon"
        };

        foreach (var registryKey in registryKeys)
        {
            try
            {
                var value = Microsoft.Win32.Registry.GetValue(registryKey, "version", null) as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
                // Registry access is best effort. The executable version is the
                // fallback when the registry value is unavailable.
            }
        }

        foreach (var executablePath in EdgeExecutablePaths)
        {
            try
            {
                if (File.Exists(executablePath))
                {
                    var version = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        return version;
                    }
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string TryGetDriverVersion()
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = ProgramSettings.EdgeDriverExecutable,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(processStartInfo))
            {
                if (process == null)
                {
                    return null;
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    return null;
                }

                var output = outputTask.GetAwaiter().GetResult();
                var error = errorTask.GetAwaiter().GetResult();
                var combined = output + " " + error;
                var match = Regex.Match(combined, @"\d+(\.\d+){1,3}");
                return match.Success ? match.Value : combined.Trim();
            }
        }
        catch
        {
            return null;
        }
    }

    private static int? GetMajorVersion(string version)
    {
        var match = Regex.Match(version ?? "", @"^(\d+)");
        return match.Success
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : (int?)null;
    }
}

/// <summary>
/// Provides the required WinForms dialogs. Dialogs are created on an STA
/// thread because LINQPad's main query thread is not guaranteed to be STA.
/// </summary>
public static class DialogService
{
    private const string EdgeDriverDownloadUrl =
        "https://developer.microsoft.com/en-us/microsoft-edge/tools/webdriver?form=MA13LH&ch=1&cs=3487937218";

    public static TestExecutionMode? SelectExecutionMode()
    {
        return RunOnSta(() =>
        {
            using (var form = new Form())
            {
                form.Text = "BDIS Test Execution Mode";
                form.Width = 540;
                form.Height = 250;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.TopMost = true;

                var promptLabel = new Label
                {
                    Text = "Select the type of test to execute:",
                    Left = 20,
                    Top = 20,
                    Width = 480,
                    Height = 25
                };

                TestExecutionMode? selectedMode = null;

                var f5Button = new Button
                {
                    Text = "F5 / File Server Test",
                    Left = 20,
                    Top = 65,
                    Width = 220,
                    Height = 45
                };

                var fullButton = new Button
                {
                    Text = "Full Test (All URLs)",
                    Left = 270,
                    Top = 65,
                    Width = 220,
                    Height = 45
                };

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    Left = 270,
                    Top = 135,
                    Width = 220,
                    Height = 35,
                    DialogResult = DialogResult.Cancel
                };

                f5Button.Click += (sender, args) =>
                {
                    selectedMode = TestExecutionMode.F5Only;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };

                fullButton.Click += (sender, args) =>
                {
                    selectedMode = TestExecutionMode.FullTest;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };

                form.Controls.Add(promptLabel);
                form.Controls.Add(f5Button);
                form.Controls.Add(fullButton);
                form.Controls.Add(cancelButton);
                form.AcceptButton = f5Button;
                form.CancelButton = cancelButton;

                return form.ShowDialog() == DialogResult.OK ? selectedMode : null;
            }
        });
    }

    public static void ShowEdgeDriverFailureDialog(string technicalDetails)
    {
        var instructions = BuildEdgeDriverInstructions();

        RunOnSta(() =>
        {
            using (var form = new Form())
            {
                form.Text = "Microsoft Edge WebDriver Initialization Error";
                form.Width = 780;
                form.Height = 650;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.TopMost = true;

                var instructionsLabel = new Label
                {
                    Text = instructions,
                    Left = 20,
                    Top = 15,
                    Width = 720,
                    Height = 330,
                    AutoSize = false
                };

                var detailsBox = new RichTextBox
                {
                    Text = technicalDetails ?? "",
                    Left = 20,
                    Top = 350,
                    Width = 720,
                    Height = 190,
                    ReadOnly = true,
                    ScrollBars = RichTextBoxScrollBars.Vertical
                };

                var downloadButton = new Button
                {
                    Text = "Open Edge WebDriver Download Page",
                    Left = 20,
                    Top = 555,
                    Width = 290,
                    Height = 35
                };

                var closeButton = new Button
                {
                    Text = "Close",
                    Left = 620,
                    Top = 555,
                    Width = 120,
                    Height = 35,
                    DialogResult = DialogResult.OK
                };

                downloadButton.Click += (sender, args) => OpenEdgeWebDriverPage();

                form.Controls.Add(instructionsLabel);
                form.Controls.Add(detailsBox);
                form.Controls.Add(downloadButton);
                form.Controls.Add(closeButton);
                form.AcceptButton = closeButton;
                form.CancelButton = closeButton;

                form.ShowDialog();
                return true;
            }
        });
    }

    public static void ShowErrorDialog(string title, string message, string details)
    {
        RunOnSta(() =>
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.Width = 720;
                form.Height = 480;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.TopMost = true;

                var messageLabel = new Label
                {
                    Text = message ?? "",
                    Left = 20,
                    Top = 20,
                    Width = 660,
                    Height = 70,
                    AutoSize = false
                };

                var detailsBox = new RichTextBox
                {
                    Text = details ?? "",
                    Left = 20,
                    Top = 100,
                    Width = 660,
                    Height = 280,
                    ReadOnly = true,
                    ScrollBars = RichTextBoxScrollBars.Vertical
                };

                var closeButton = new Button
                {
                    Text = "Close",
                    Left = 560,
                    Top = 395,
                    Width = 120,
                    Height = 35,
                    DialogResult = DialogResult.OK
                };

                form.Controls.Add(messageLabel);
                form.Controls.Add(detailsBox);
                form.Controls.Add(closeButton);
                form.AcceptButton = closeButton;
                form.CancelButton = closeButton;

                form.ShowDialog();
                return true;
            }
        });
    }

    private static string BuildEdgeDriverInstructions()
    {
        var lines = new[]
        {
            "Microsoft Edge WebDriver could not be initialized.",
            "",
            "The installed EdgeDriver may not match the installed Microsoft Edge version.",
            "",
            "How to check the installed Microsoft Edge version:",
            "  1. Open Microsoft Edge.",
            "  2. Navigate to edge://settings/help.",
            "  3. Record the complete version number.",
            "",
            "How to check the current EdgeDriver version:",
            "  1. Open Command Prompt.",
            "  2. Run: C:\\Selenium\\msedgedriver.exe --version",
            "",
            "The major version of Microsoft Edge and EdgeDriver must match.",
            "Download the correct driver from the official Microsoft Edge WebDriver page.",
            "Replace the existing driver in: C:\\Selenium",
            "",
            "Close Microsoft Edge and all existing msedgedriver.exe processes before replacing the executable.",
            "Restart this script after the driver has been updated."
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static void OpenEdgeWebDriverPage()
    {
        foreach (var edgePath in new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            "msedge.exe"
        })
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = edgePath,
                    Arguments = EdgeDriverDownloadUrl,
                    UseShellExecute = true
                });
                return;
            }
            catch
            {
                // Try the next way of opening the page.
            }
        }

        try
        {
            // Fall back to the operating system's default HTTPS handler when
            // Microsoft Edge cannot be started explicitly.
            Process.Start(new ProcessStartInfo
            {
                FileName = EdgeDriverDownloadUrl,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "The Edge WebDriver download page could not be opened automatically."
                + Environment.NewLine
                + "Open this URL manually:"
                + Environment.NewLine
                + EdgeDriverDownloadUrl
                + Environment.NewLine
                + Environment.NewLine
                + exception.Message,
                "Microsoft Edge WebDriver",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static T RunOnSta<T>(Func<T> action)
    {
        T result = default(T);
        Exception capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                result = action();
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (capturedException != null)
        {
            throw new InvalidOperationException("The user interface dialog could not be displayed.", capturedException);
        }

        return result;
    }
}

/// <summary>
/// Orchestrates execution by URL and by country, records every step, evaluates
/// the existing pass/fail semantics and aggregates country results into one
/// environment result.
/// </summary>
public sealed class BdisTestRunner
{
    private readonly ConfigurationManager _config;
    private readonly ExecutionLogger _logger;
    private readonly string _currentUser;
    private readonly Action<string> _progress;
    private readonly EdgeDriverManager _edgeDriverManager;
    private readonly BdisLoginService _loginService;

    public BdisTestRunner(
        ConfigurationManager config,
        ExecutionLogger logger,
        string currentUser,
        Action<string> progress)
    {
        _config = config;
        _logger = logger;
        _currentUser = currentUser;
        _progress = progress;
        _edgeDriverManager = new EdgeDriverManager();
        _loginService = new BdisLoginService(config, new CountrySelectionService());
    }

    public RunSummary Run(TestExecutionMode mode)
    {
        var summary = new RunSummary();
        var urlProfiles = _config.CreateUrlProfiles(mode);

        foreach (var urlProfile in urlProfiles)
        {
            var environmentResult = RunEnvironment(urlProfile);
            summary.Environments.Add(environmentResult);
            summary.Steps.AddRange(environmentResult.Countries.SelectMany(country => country.Steps));

            _logger.LogInfo($"Finished environment {urlProfile.WebKey}: {environmentResult.Status}.");
            _progress?.Invoke($"Finished environment {urlProfile.WebKey}: {environmentResult.Status}");
        }

        summary.GlobalStatus = summary.Environments.Count > 0
            && summary.Environments.All(environment => environment.Status == "PASSED")
                ? "PASSED"
                : "FAILED";

        return summary;
    }

    private EnvironmentTestResult RunEnvironment(UrlTestProfile urlProfile)
    {
        var environmentResult = new EnvironmentTestResult
        {
            WebKey = urlProfile.WebKey,
            TargetUrl = urlProfile.Url,
            StartTime = DateTimeOffset.Now,
            Status = "PASSED"
        };

        _logger.LogInfo($"Starting environment {urlProfile.WebKey} ({urlProfile.ConfigKey}): {urlProfile.Url}");
        _progress?.Invoke($"Starting environment {urlProfile.WebKey}: {urlProfile.Url}");

        EdgeDriver driver = null;
        SeleniumWaitHelper wait = null;

        try
        {
            driver = _edgeDriverManager.CreateDriver(_logger);
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(_config.PageLoadTimeoutSeconds);
            driver.Manage().Window.Maximize();
            wait = new SeleniumWaitHelper(driver, _config);

            foreach (var countryProfile in _config.CountryProfiles)
            {
                // A browser crash or a closed window must not prevent the next
                // country from running, so recreate the driver when necessary.
                if (driver == null || !IsDriverAlive(driver))
                {
                    DisposeDriver(driver);
                    driver = _edgeDriverManager.CreateDriver(_logger);
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(_config.PageLoadTimeoutSeconds);
                    driver.Manage().Window.Maximize();
                    wait = new SeleniumWaitHelper(driver, _config);
                }

                var countryResult = RunCountry(driver, wait, urlProfile, countryProfile);
                environmentResult.Countries.Add(countryResult);

                _logger.LogInfo($"Finished country {countryProfile.DisplayName} for {urlProfile.WebKey}: {countryResult.Status}.");
                _progress?.Invoke($"{urlProfile.WebKey} / {countryProfile.DisplayName}: {countryResult.Status}");
            }

            BuildEnvironmentResult(environmentResult);
        }
        catch (EdgeDriverInitializationException)
        {
            // Driver startup failures are global prerequisites. They must stop
            // the entire run and must not be reported as environment failures.
            throw;
        }
        catch (Exception exception)
        {
            environmentResult.Status = "FAILED";
            environmentResult.ErrorMessage = _config.Redactor.Redact(
                $"Environment: {urlProfile.WebKey} | Error: {exception.GetType().Name}: {exception.Message}");
            _logger.LogError($"Environment {urlProfile.WebKey}", exception);

            // Surface environment-level failures in the HTML report and local
            // log even when no country step had a chance to start.
            var failureStep = new TestStepResult
            {
                EnvironmentName = urlProfile.WebKey,
                CountryName = "Environment",
                StepName = "Environment setup",
                Status = "FAILED",
                Details = environmentResult.ErrorMessage,
                ExecutedBy = _currentUser,
                StartTime = environmentResult.StartTime,
                EndTime = DateTimeOffset.Now
            };

            var failureCountry = new CountryTestResult
            {
                EnvironmentName = urlProfile.WebKey,
                CountryName = "Environment",
                Status = "FAILED",
                FailedStep = "Environment setup",
                ErrorMessage = environmentResult.ErrorMessage,
                StartTime = environmentResult.StartTime,
                EndTime = DateTimeOffset.Now
            };

            failureCountry.Steps.Add(failureStep);
            environmentResult.Countries.Add(failureCountry);
            _logger.LogStep(failureStep);
        }
        finally
        {
            DisposeDriver(driver);
            environmentResult.EndTime = DateTimeOffset.Now;
        }

        return environmentResult;
    }

    private CountryTestResult RunCountry(
        EdgeDriver driver,
        SeleniumWaitHelper wait,
        UrlTestProfile urlProfile,
        CountryTestProfile countryProfile)
    {
        var countryResult = new CountryTestResult
        {
            EnvironmentName = urlProfile.WebKey,
            CountryName = countryProfile.DisplayName,
            Status = "PASSED",
            StartTime = DateTimeOffset.Now
        };

        var workflow = new BdisWorkflow(_config);

        // The order below follows the functional order recorded in BDIS.side.
        // Each RunStep call stops the remaining country steps when it fails.
        if (!RunStep(countryResult, driver, "Login", () =>
                _loginService.Login(driver, urlProfile.Url, wait, countryProfile)))
        {
            return FinishCountry(countryResult);
        }

        if (!RunStep(countryResult, driver, "Document Query - Search", () =>
                workflow.DocumentQuerySearch(driver, wait, countryProfile)))
        {
            return FinishCountry(countryResult);
        }

        if (!RunStep(countryResult, driver, "Document Query - Print", () =>
                workflow.DocumentQueryPrint(driver, wait)))
        {
            return FinishCountry(countryResult);
        }

        if (!RunStep(countryResult, driver, "Document Query - Email", () =>
                workflow.DocumentQueryEmail(driver, wait)))
        {
            return FinishCountry(countryResult);
        }

        if (!RunStep(countryResult, driver, "Packet Print - Select and Search", () =>
                workflow.PacketPrintSelectAndSearch(driver, wait, countryProfile)))
        {
            return FinishCountry(countryResult);
        }

        if (!RunStep(countryResult, driver, "Packet Print - Print", () =>
                workflow.PacketPrintPrint(driver, wait)))
        {
            return FinishCountry(countryResult);
        }

        if (!RunStep(countryResult, driver, "Manipulate Image - Upload", () =>
                workflow.UploadImage(driver, wait)))
        {
            return FinishCountry(countryResult);
        }

        if (!RunStep(countryResult, driver, "Manipulate Image - Edit", () =>
                workflow.EditImage(driver, wait)))
        {
            return FinishCountry(countryResult);
        }

        if (!RunStep(countryResult, driver, "Manipulate Image - Delete", () =>
                workflow.DeleteImage(driver, wait)))
        {
            return FinishCountry(countryResult);
        }

        if (!RunStep(countryResult, driver, "Logout", () =>
                _loginService.Logout(driver, wait, urlProfile.Url)))
        {
            return FinishCountry(countryResult);
        }

        return FinishCountry(countryResult);
    }

    private bool RunStep(CountryTestResult countryResult, IWebDriver driver, string stepName, Action action)
    {
        var stepResult = new TestStepResult
        {
            EnvironmentName = countryResult.EnvironmentName,
            CountryName = countryResult.CountryName,
            StepName = stepName,
            ExecutedBy = _currentUser,
            StartTime = DateTimeOffset.Now
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            action();
            stepResult.Status = "PASSED";
            stepResult.Details = "Executed successfully.";
            stepResult.EndTime = DateTimeOffset.Now;

            countryResult.Steps.Add(stepResult);
            _logger.LogStep(stepResult);

            _progress?.Invoke(
                $"{stepResult.EnvironmentName} / {countryResult.CountryName} / {stepName}: PASSED ({stopwatch.Elapsed.TotalSeconds:F1}s)");

            return true;
        }
        catch (Exception exception)
        {
            stepResult.Status = "FAILED";
            stepResult.Details = _config.Redactor.Redact(exception.Message);

            // The login page can contain the username field, so no screenshot
            // is captured for login failures.
            if (!string.Equals(stepName, "Login", StringComparison.OrdinalIgnoreCase))
            {
                stepResult.ScreenshotPath = ScreenshotHelper.Capture(
                    driver,
                    _config.LogFolder,
                    stepResult.EnvironmentName,
                    countryResult.CountryName,
                    stepName);
            }

            stepResult.EndTime = DateTimeOffset.Now;

            countryResult.Steps.Add(stepResult);
            countryResult.Status = "FAILED";
            countryResult.FailedStep = stepName;
            countryResult.ErrorMessage = stepResult.Details;

            _logger.LogStep(stepResult);
            _logger.LogError($"{stepResult.EnvironmentName} / {countryResult.CountryName} / {stepName}", exception);

            _progress?.Invoke(
                $"{stepResult.EnvironmentName} / {countryResult.CountryName} / {stepName}: FAILED - {stepResult.Details}");

            return false;
        }
    }

    private static CountryTestResult FinishCountry(CountryTestResult countryResult)
    {
        countryResult.EndTime = DateTimeOffset.Now;
        if (countryResult.Status == "PASSED")
        {
            countryResult.ErrorMessage = "";
        }

        return countryResult;
    }

    private void BuildEnvironmentResult(EnvironmentTestResult environmentResult)
    {
        var hasAllCountries = environmentResult.Countries.Count == _config.CountryProfiles.Count;
        var hasFailures = environmentResult.Countries.Any(country => country.Status != "PASSED");

        if (hasAllCountries && !hasFailures)
        {
            environmentResult.Status = "PASSED";
            environmentResult.ErrorMessage = "";
            return;
        }

        environmentResult.Status = "FAILED";
        environmentResult.ErrorMessage = string.Join(
            " | ",
            environmentResult.Countries.Select(FormatCountrySummary));
    }

    private static string FormatCountrySummary(CountryTestResult countryResult)
    {
        if (countryResult.Status == "PASSED")
        {
            return $"{countryResult.CountryName}: PASSED";
        }

        var failedStep = string.IsNullOrWhiteSpace(countryResult.FailedStep)
            ? ""
            : $" at {countryResult.FailedStep}";
        var reason = string.IsNullOrWhiteSpace(countryResult.ErrorMessage)
            ? "the workflow did not complete."
            : countryResult.ErrorMessage;

        return $"{countryResult.CountryName}: FAILED{failedStep} because {reason}";
    }

    private static bool IsDriverAlive(IWebDriver driver)
    {
        try
        {
            var _ = driver.WindowHandles;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void DisposeDriver(EdgeDriver driver)
    {
        try { driver?.Quit(); } catch { }
        try { driver?.Dispose(); } catch { }
    }
}

/// <summary>
/// Entry point for the LINQPad query.
/// </summary>
void Main()
{
    var currentUser = Environment.UserName;
    var logger = new ExecutionLogger(ProgramSettings.LogFolder, ProgramSettings.TestName);
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var htmlReportPath = Path.Combine(
        ProgramSettings.LogFolder,
        $"{ProgramSettings.TestName}WebApplication_{timestamp}.html");

    TestExecutionMode? selectedMode = null;

    logger.LogInfo($"=== STARTING {ProgramSettings.TestName} WEB AUTOMATION TEST ===");
    logger.LogInfo($"Windows user: {currentUser}");
    $"=== STARTING {ProgramSettings.TestName} WEB AUTOMATION TEST ===".Dump();

    try
    {
        selectedMode = DialogService.SelectExecutionMode();
        if (selectedMode == null)
        {
            logger.LogInfo("Execution cancelled by the user. No browser was started.");
            "Execution cancelled by the user.".Dump();
            return;
        }

        logger.LogInfo($"Execution mode: {selectedMode.Value}");

        var config = ConfigurationManager.LoadAndValidate(
            ProgramSettings.ConfigPath,
            ProgramSettings.BaseDirectory);

        logger.Redactor = config.Redactor;
        logger.LogInfo($"Test name: {ProgramSettings.TestName}");
        logger.LogInfo($"Configuration file: {config.ConfigPath}");
        logger.LogInfo($"Image file: {config.ImageFilePath}");
        logger.LogInfo($"Selected environment count: {config.CreateUrlProfiles(selectedMode.Value).Count}");

        var runner = new BdisTestRunner(
            config,
            logger,
            currentUser,
            message => { message.Dump(); });

        var summary = runner.Run(selectedMode.Value);

        HtmlReportGenerator.Generate(
            htmlReportPath,
            summary.Steps,
            ProgramSettings.TestName,
            selectedMode,
            currentUser,
            summary.GlobalStatus);

        logger.LogInfo($"HTML report generated at: {htmlReportPath}");

        if (summary.Environments.Count > 0)
        {
            var reporter = new PowerAutomateReporter(config, logger);
            reporter.Send(
                ProgramSettings.TestName,
                currentUser,
                summary.Environments,
                summary.GlobalStatus);
        }
        else
        {
            logger.LogInfo("Power Automate reporting skipped because no environment results were produced.");
        }

        logger.LogInfo($"Final global status: {summary.GlobalStatus}");
        $"\n=== PROCESS COMPLETED: {summary.GlobalStatus} ===".Dump();
        $"HTML report successfully generated at: {htmlReportPath}".Dump();
    }
    catch (EdgeDriverInitializationException exception)
    {
        logger.LogError("Microsoft Edge WebDriver initialization", exception);
        var safeDetails = logger.Redactor.Redact(exception.TechnicalDetails);
        GeneratePrerequisiteReport(htmlReportPath, selectedMode, currentUser, "Microsoft Edge WebDriver initialization", safeDetails);
        DialogService.ShowEdgeDriverFailureDialog(safeDetails);
        logger.LogInfo("Execution stopped because Microsoft Edge WebDriver could not be initialized.");
    }
    catch (ConfigurationException exception)
    {
        logger.LogError("Configuration validation", exception);
        var safeMessage = logger.Redactor.Redact(exception.Message);
        GeneratePrerequisiteReport(htmlReportPath, selectedMode, currentUser, "Configuration validation", safeMessage);
        DialogService.ShowErrorDialog(
            "BDIS Configuration Error",
            "The test could not start because the configuration is incomplete or invalid.",
            safeMessage);
        logger.LogInfo("Execution stopped because of a configuration error.");
    }
    catch (Exception exception)
    {
        logger.LogError("Unexpected execution error", exception);
        var safeMessage = logger.Redactor.Redact(exception.Message);
        GeneratePrerequisiteReport(htmlReportPath, selectedMode, currentUser, "Unexpected execution error", safeMessage);
        DialogService.ShowErrorDialog(
            "BDIS Execution Error",
            "The test execution stopped unexpectedly.",
            safeMessage);
        logger.LogInfo("Execution stopped because of an unexpected error.");
    }
    finally
    {
        logger.LogInfo($"=== PROCESS FINISHED AT {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ===");
    }
}

/// <summary>
/// Produces a local HTML report for failures that occur before any functional
/// environment result exists.
/// </summary>
private void GeneratePrerequisiteReport(
    string htmlReportPath,
    TestExecutionMode? mode,
    string currentUser,
    string stepName,
    string details)
{
    var steps = new List<TestStepResult>
    {
        new TestStepResult
        {
            EnvironmentName = "N/A",
            CountryName = "N/A",
            StepName = stepName,
            Status = "NOT_EXECUTED",
            Details = details,
            ExecutedBy = currentUser,
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now
        }
    };

    HtmlReportGenerator.Generate(
        htmlReportPath,
        steps,
        ProgramSettings.TestName,
        mode,
        currentUser,
        "NOT_EXECUTED");
}
