<Query Kind="Program">
  <Reference>System.Windows.Forms</Reference>
  <Namespace>System</Namespace>
  <Namespace>System.Collections.Generic</Namespace>
  <Namespace>System.Diagnostics</Namespace>
  <Namespace>System.Drawing</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>System.IO</Namespace>
  <Namespace>System.Linq</Namespace>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Runtime.InteropServices</Namespace>
  <Namespace>System.Text</Namespace>
  <Namespace>System.Text.Json.Nodes</Namespace>
  <Namespace>System.Text.RegularExpressions</Namespace>
  <Namespace>System.Threading</Namespace>
  <Namespace>System.Windows.Forms</Namespace>
  <Namespace>OpenQA.Selenium</Namespace>
  <Namespace>OpenQA.Selenium.DevTools</Namespace>
  <Namespace>OpenQA.Selenium.Edge</Namespace>
  <Namespace>OpenQA.Selenium.Support.UI</Namespace>
  <IncludeUncapsulator>false</IncludeUncapsulator>
</Query>

// ============================================================================
// BDIS Selenium automation for LINQPad 8
//
// The functional workflow in this file is derived from NEW BDIS.side:
//   1. Log in to the target environment.
//   2. Select Canada and run the Canada workflow.
//   3. Log out, log in again, select the United States and run the same
//      workflow with United States parameters.
//   4. Aggregate both country results into one result per target URL.
//
// The legacy BDIS.side file is obsolete and must not be used as a reference.
// The email test is conditional: it only runs when the page itself reports
// SU6BDISWEB5, matching the conditional recorded in NEW BDIS.side.
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
    public int ImageUploadPauseSeconds { get; private set; }
    public int PrintWindowPauseSeconds { get; private set; }
    public string WebhookProxyHost { get; private set; }
    public int WebhookProxyPort { get; private set; }
    public List<CountryTestProfile> CountryProfiles { get; private set; }
    public SecretRedactor Redactor { get; private set; }

    /// <summary>
    /// Returns true when the Power Automate webhook must be sent through the
    /// configured proxy. An empty host disables the proxy and uses a direct
    /// connection.
    /// </summary>
    public bool HasWebhookProxy
    {
        get { return !string.IsNullOrWhiteSpace(WebhookProxyHost); }
    }

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

        // The defaults match the values used by NEW BDIS.side. The image pause
        // gives the upload widget time to finish before the rotation commands.
        ElementTimeoutSeconds = GetInt("elementTimeoutSeconds", 30);
        PageLoadTimeoutSeconds = GetInt("pageLoadTimeoutSeconds", 60);
        UploadTimeoutSeconds = GetInt("uploadTimeoutSeconds", 120);
        ImageUploadPauseSeconds = GetInt("imageUploadPauseSeconds", 60);
        PrintWindowPauseSeconds = GetInt("printWindowPauseSeconds", 30);

        WebhookProxyHost = GetOptional("webhookProxyHost");
        WebhookProxyPort = GetInt("webhookProxyPort", 9191);
        if (WebhookProxyPort < 1 || WebhookProxyPort > 65535)
        {
            throw new ConfigurationException(
                $"Configuration property 'webhookProxyPort' must be between 1 and 65535. Actual value: '{WebhookProxyPort}'.");
        }

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
/// Shared UPS visual identity for the WinForms dialogs and the HTML report.
/// The logo is embedded as a Base64 PNG so the report remains a single,
/// self-contained file and the dialogs do not depend on external assets.
/// </summary>
public static class UpsBranding
{
    public const string Brown = "#351C15";
    public const string Gold = "#FFB500";
    public const string White = "#FFFFFF";
    public const string LightGray = "#F5F3F0";
    public const string DarkText = "#2B1A12";
    public const string MutedText = "#6B5A50";

    public const string LogoBase64 = "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";

    public static readonly Color BrownColor = ColorTranslator.FromHtml(Brown);
    public static readonly Color GoldColor = ColorTranslator.FromHtml(Gold);
    public static readonly Color LightGrayColor = ColorTranslator.FromHtml(LightGray);
    public static readonly Color DarkTextColor = ColorTranslator.FromHtml(DarkText);

    public static Image LoadLogo()
    {
        using (var stream = new MemoryStream(Convert.FromBase64String(LogoBase64)))
        using (var source = Image.FromStream(stream))
        {
            // The bitmap copy keeps the image valid after the stream is
            // disposed, which GDI+ requires for some PNG streams.
            return new Bitmap(source);
        }
    }

    /// <summary>
    /// Applies the UPS header, logo and border to a dialog. The returned value
    /// is the recommended top coordinate for the dialog content, below the
    /// header.
    /// </summary>
    public static int ApplyDialogChrome(Form form, string title, string subtitle)
    {
        form.BackColor = LightGrayColor;
        form.ForeColor = DarkTextColor;

        form.Paint += (sender, args) =>
        {
            using (var brownPen = new Pen(BrownColor, 5))
            using (var goldPen = new Pen(GoldColor, 2))
            {
                var outer = new Rectangle(0, 0, form.ClientSize.Width - 1, form.ClientSize.Height - 1);
                args.Graphics.DrawRectangle(brownPen, outer);

                var inner = new Rectangle(4, 4, form.ClientSize.Width - 9, form.ClientSize.Height - 9);
                args.Graphics.DrawRectangle(goldPen, inner);
            }
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = BrownColor
        };

        var titleLabel = new Label
        {
            Text = title,
            Left = 22,
            Top = 14,
            Width = 420,
            Height = 30,
            ForeColor = GoldColor,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold)
        };

        var subtitleLabel = new Label
        {
            Text = subtitle,
            Left = 24,
            Top = 50,
            Width = 420,
            Height = 22,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9F)
        };

        var logo = new PictureBox
        {
            Image = LoadLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Width = 86,
            Height = 70,
            Left = Math.Max(440, form.ClientSize.Width - 108),
            Top = 9,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        header.Controls.Add(titleLabel);
        header.Controls.Add(subtitleLabel);
        header.Controls.Add(logo);
        form.Controls.Add(header);

        return header.Height + 18;
    }

    public static void StylePrimaryButton(Button button)
    {
        button.BackColor = BrownColor;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = GoldColor;
        button.FlatAppearance.BorderSize = 2;
        button.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    public static void StyleSecondaryButton(Button button)
    {
        button.BackColor = Color.White;
        button.ForeColor = BrownColor;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BrownColor;
        button.FlatAppearance.BorderSize = 2;
        button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }
}

/// <summary>
/// Generates the UPS-branded HTML dashboard. The table keeps the existing
/// environment and country context; no target URL column or footer is added.
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
        var skippedSteps = steps?.Count(s => s.Status == "SKIPPED") ?? 0;
        var statusColor = globalStatus == "PASSED"
            ? "#2F855A"
            : globalStatus == "NOT_EXECUTED" ? "#B7791F" : "#C53030";

        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset='utf-8'><title>");
        sb.Append(System.Net.WebUtility.HtmlEncode(testName));
        sb.Append(" Automation Dashboard</title>");
        sb.Append("<style>");
        sb.Append("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 32px; background-color: " + UpsBranding.LightGray + "; color: " + UpsBranding.DarkText + "; }");
        sb.Append(".header-container { display: flex; justify-content: space-between; align-items: center; background: " + UpsBranding.Brown + "; border-bottom: 5px solid " + UpsBranding.Gold + "; border-radius: 12px; padding: 22px 28px; margin-bottom: 28px; box-shadow: 0 6px 18px rgba(53,28,21,0.25); }");
        sb.Append(".header-title h2 { color: " + UpsBranding.Gold + "; margin: 0; font-size: 28px; font-weight: 700; letter-spacing: 0.3px; }");
        sb.Append(".header-subtitle { color: #F5F3F0; margin-top: 6px; font-size: 14px; }");
        sb.Append(".header-right { display: flex; flex-direction: column; align-items: flex-end; gap: 10px; }");
        sb.Append(".ups-logo { height: 62px; width: auto; }");
        sb.Append(".timestamp { color: #F5F3F0; font-size: 13px; text-align: right; }");
        sb.Append(".summary-deck { display: flex; gap: 18px; margin-bottom: 28px; flex-wrap: wrap; }");
        sb.Append(".card { flex: 1; min-width: 190px; background: #FFFFFF; border-radius: 10px; padding: 16px 20px; box-shadow: 0 4px 12px rgba(53,28,21,0.10); border-left: 6px solid " + UpsBranding.Brown + "; }");
        sb.Append(".card.status-card { border-left-color: " + statusColor + "; }");
        sb.Append(".card.agent-card { border-left-color: " + UpsBranding.Gold + "; }");
        sb.Append(".card.mode-card { border-left-color: #8A6A4F; }");
        sb.Append(".card-title { font-size: 12px; text-transform: uppercase; color: " + UpsBranding.MutedText + "; font-weight: 700; letter-spacing: 1px; }");
        sb.Append(".card-value { font-size: 20px; font-weight: 700; margin-top: 6px; color: " + UpsBranding.Brown + "; }");
        sb.Append("table { width: 100%; border-collapse: collapse; background: #FFFFFF; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 14px rgba(53,28,21,0.12); }");
        sb.Append("th { background-color: " + UpsBranding.Brown + "; color: " + UpsBranding.Gold + "; font-weight: 700; text-transform: uppercase; font-size: 13px; letter-spacing: 0.5px; padding: 15px; text-align: left; }");
        sb.Append("td { padding: 15px; border-bottom: 1px solid #E8E1DA; font-size: 14px; vertical-align: top; color: " + UpsBranding.DarkText + "; }");
        sb.Append("tr:last-child td { border-bottom: none; }");
        sb.Append("tr:hover { background-color: #FFF7E0; }");
        sb.Append(".badge { padding: 4px 8px; border-radius: 4px; font-weight: 700; font-size: 11px; display: inline-block; text-transform: uppercase; }");
        sb.Append(".badge-success { background-color: #2F855A; color: #FFFFFF; }");
        sb.Append(".badge-danger { background-color: #C53030; color: #FFFFFF; }");
        sb.Append(".badge-warning { background-color: #B7791F; color: #FFFFFF; }");
        sb.Append(".badge-skipped { background-color: #718096; color: #FFFFFF; }");
        sb.Append("pre { background: #FFF5F5; padding: 12px; border-radius: 6px; border-left: 4px solid #C53030; white-space: pre-wrap; color: #742A2A; font-size: 12px; font-family: 'Courier New', Courier, monospace; margin-top: 8px; }");
        sb.Append(".screenshot-link { display: inline-block; margin-top: 8px; color: " + UpsBranding.Brown + "; text-decoration: none; font-weight: 700; font-size: 13px; border-bottom: 2px solid " + UpsBranding.Gold + "; }");
        sb.Append(".screenshot-link:hover { color: #8A6A4F; }");
        sb.Append("</style></head><body>");

        sb.Append("<div class='header-container'>");
        sb.Append("<div class='header-title'><h2>Automated Test Execution Dashboard</h2><div class='header-subtitle'>");
        sb.Append(System.Net.WebUtility.HtmlEncode(testName));
        sb.Append(" Web Application Evaluation</div></div>");
        sb.Append("<div class='header-right'><img class='ups-logo' alt='UPS' src='data:image/png;base64,");
        sb.Append(UpsBranding.LogoBase64);
        sb.Append("'><div class='timestamp'><strong>Execution Date:</strong> ");
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.Append("</div></div></div>");

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
        if (skippedSteps > 0)
        {
            sb.Append(" (");
            sb.Append(skippedSteps);
            sb.Append(" skipped)");
        }
        sb.Append("</div></div>");
        sb.Append("</div>");

        sb.Append("<table>");
        sb.Append("<tr><th style='width:5%;'>#</th><th style='width:12%;'>Environment</th><th style='width:12%;'>Country</th><th style='width:26%;'>Test Step Context</th><th style='width:45%;'>Execution Details / Logs</th></tr>");

        if (steps != null)
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var isSkipped = step.Status == "SKIPPED";
                var badgeClass = step.Status == "PASSED"
                    ? "badge-success"
                    : isSkipped ? "badge-skipped"
                    : step.Status == "NOT_EXECUTED" ? "badge-warning" : "badge-danger";
                var badgeText = step.Status == "PASSED"
                    ? "PASSED"
                    : isSkipped ? "SKIPPED"
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

                if (step.Status != "PASSED" && !isSkipped)
                {
                    sb.Append("<pre>");
                    sb.Append(System.Net.WebUtility.HtmlEncode(step.Details ?? ""));
                    sb.Append("</pre>");

                    if (!string.IsNullOrWhiteSpace(step.ScreenshotPath))
                    {
                        sb.Append("<br><a class='screenshot-link' href='");
                        sb.Append(System.Net.WebUtility.HtmlEncode(step.ScreenshotPath));
                        sb.Append("' target='_blank'>");
                        sb.Append("View Error Screenshot");
                        sb.Append("</a>");
                    }
                }
                else
                {
                    sb.Append("<span style='color: #5A4A42;'>");
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

    private const int MaxAttempts = 3;

    public void Send(string testName, string user, List<EnvironmentTestResult> results, string globalStatus)
    {
        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
        {
            _logger.LogInfo("Power Automate reporting skipped because powerAutomateWebhookUrl is empty.");
            return;
        }

        var payload = BuildPayload(testName, user, results, globalStatus);

        // The payload never contains credentials. The redactor is still applied
        // to the diagnostic copy so no registered secret can leak into the log.
        var diagnosticPayload = _config.Redactor.Redact(payload);

        try
        {
            _logger.LogInfo($"Power Automate endpoint host: {new Uri(_config.WebhookUrl).Host}");
            _logger.LogInfo(_config.HasWebhookProxy
                ? $"Power Automate proxy: {_config.WebhookProxyHost}:{_config.WebhookProxyPort}"
                : "Power Automate proxy: direct connection.");
            _logger.LogInfo($"Power Automate payload: {diagnosticPayload}");
            $"\n[Payload JSON sent to Webhook]:\n{diagnosticPayload}\n".Dump();
        }
        catch (Exception exception)
        {
            _logger.LogError("Power Automate payload diagnostics", exception);
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using (var handler = new HttpClientHandler())
                {
                    if (_config.HasWebhookProxy)
                    {
                        handler.Proxy = new WebProxy(_config.WebhookProxyHost, _config.WebhookProxyPort);
                        handler.UseProxy = true;
                    }

                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);

                        using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
                        using (var response = client.PostAsync(_config.WebhookUrl, content).GetAwaiter().GetResult())
                        {
                            var statusCode = (int)response.StatusCode;
                            var responseBody = Truncate(
                                _config.Redactor.Redact(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()));

                            if (response.IsSuccessStatusCode)
                            {
                                _logger.LogInfo(
                                    $"Power Automate notification sent successfully (attempt {attempt}/{MaxAttempts}). Status code: {statusCode}. Response: {responseBody}");
                                return;
                            }

                            _logger.LogInfo(
                                $"Power Automate notification failed (attempt {attempt}/{MaxAttempts}). Status code: {statusCode}. Response: {responseBody}");

                            if (!IsTransientStatusCode(statusCode) || attempt == MaxAttempts)
                            {
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.LogError($"Power Automate notification attempt {attempt}/{MaxAttempts}", exception);

                if (attempt == MaxAttempts)
                {
                    return;
                }
            }

            Thread.Sleep(TimeSpan.FromSeconds(2 * attempt));
        }
    }

    private string BuildPayload(
        string testName,
        string user,
        List<EnvironmentTestResult> results,
        string globalStatus)
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
            resultsJson.Append($"\"startTime\":\"{ToUtcIso(result.StartTime)}\",");
            resultsJson.Append($"\"endTime\":\"{ToUtcIso(result.EndTime)}\",");
            resultsJson.Append($"\"error\":\"{JsonHelper.Escape(result.ErrorMessage)}\"");
            resultsJson.Append('}');
        }

        resultsJson.Append(']');

        return "{"
            + $"\"testName\":\"{JsonHelper.Escape(testName)}\","
            + $"\"user\":\"{JsonHelper.Escape(user)}\","
            + $"\"globalStatus\":\"{JsonHelper.Escape(globalStatus)}\","
            + $"\"results\":{resultsJson}"
            + "}";
    }

    /// <summary>
    /// The existing flow calls convertFromUtc on these values, so they must be
    /// serialized as UTC. A local offset (for example -06:00) makes
    /// convertFromUtc fail inside Append_to_array_variable.
    /// </summary>
    private static string ToUtcIso(DateTimeOffset value)
    {
        return value.ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static bool IsTransientStatusCode(int statusCode)
    {
        return statusCode == 429 || (statusCode >= 500 && statusCode <= 599);
    }

    private static string Truncate(string value, int maxLength = 2000)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.Length <= maxLength
            ? value
            : value.Substring(0, maxLength) + "...";
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
/// Selects a country from the login country list. NEW BDIS.side does not
/// validate the Packet Print href, so no post-login country assertion is made.
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
/// Implements the reusable country workflow derived from NEW BDIS.side. All
/// selectors are ID or CSS selectors recorded in NEW BDIS.side; absolute XPath
/// expressions from the legacy script are not used.
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
        CloseNewWindowAndReturn(driver, wait, existingWindowCount, mainWindowHandle, _config.PrintWindowPauseSeconds);
    }

    /// <summary>
    /// Detects the web server reported by the current page, exactly as the
    /// executeScript command in NEW BDIS.side. The result drives the email
    /// conditional and is never used to decide pass/fail by itself.
    /// </summary>
    public string DetectWebServer(IWebDriver driver)
    {
        const string script =
            "var t = document.body.innerText.toUpperCase();"
            + "return [\"SU6BDISWEB2\",\"SU6BDISWEB3\",\"SU6BDISWEB4\",\"SU6BDISWEB5\"]"
            + ".find(function(s){return t.includes(s);}) || \"UNKNOWN\";";

        try
        {
            var result = ((IJavaScriptExecutor)driver).ExecuteScript(script);
            var webServer = Convert.ToString(result, CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrWhiteSpace(webServer) ? "UNKNOWN" : webServer.ToUpperInvariant();
        }
        catch
        {
            // A diagnostic failure must not prevent the remaining workflow.
            return "UNKNOWN";
        }
    }

    /// <summary>
    /// NEW BDIS.side runs the email test only when the page text reports
    /// SU6BDISWEB5. Every other detected server skips the email step.
    /// </summary>
    public static bool IsEmailRequired(string webServer)
    {
        return !string.IsNullOrWhiteSpace(webServer)
            && webServer.IndexOf("SU6BDISWEB5", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void DocumentQueryEmail(IWebDriver driver, SeleniumWaitHelper wait, int timeoutSeconds)
    {
        try
        {
            wait.WaitClickable(By.Id("MainContent_btnEmail")).Click();

            var emailToBox = wait.WaitVisible(By.Id("MainContent_txtEmailTo"));
            emailToBox.Click();
            emailToBox.Clear();
            emailToBox.SendKeys(_config.EmailRecipient);

            var emailBodyBox = wait.WaitVisible(By.Id("MainContent_txtEmailBody"));
            emailBodyBox.Click();
            emailBodyBox.Clear();
            emailBodyBox.SendKeys("BDIS E-mail Test");

            wait.WaitClickable(By.Id("MainContent_BtnSend")).Click();
            wait.WaitForPageSourceContains($"E-mail successfully sent to {_config.EmailRecipient}.", timeoutSeconds);
        }
        finally
        {
            // Returning to the Document Query page makes the following Packet
            // Print navigation deterministic for both countries. This also runs
            // when the confirmation is not displayed.
            TryReturnToDocumentQuery(driver);
        }
    }

    private static void TryReturnToDocumentQuery(IWebDriver driver)
    {
        try
        {
            // Dismiss a JavaScript alert if the backend raised one.
            driver.SwitchTo().Alert().Dismiss();
        }
        catch
        {
            // No alert is open.
        }

        try
        {
            var documentQueryLink = driver.FindElements(By.Id("hlDocumentQuery")).FirstOrDefault();
            if (documentQueryLink != null && documentQueryLink.Displayed)
            {
                documentQueryLink.Click();
                return;
            }
        }
        catch
        {
            // The link may be blocked by a modal or a browser alert.
        }

        try
        {
            driver.Navigate().Refresh();
        }
        catch
        {
            // Best-effort recovery only. The original email error is preserved.
        }
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
        CloseNewWindowAndReturn(driver, wait, existingWindowCount, mainWindowHandle, _config.PrintWindowPauseSeconds);
    }

    public void UploadImage(IWebDriver driver, SeleniumWaitHelper wait)
    {
        wait.WaitClickable(By.Id("hlManipulateImage")).Click();

        // NEW BDIS.side activates the Upload File control and then targets the
        // file input. Selenium IDE cannot do that in Microsoft Edge, but the
        // WebDriver session can: DOM.setFileInputFiles attaches the file
        // without automating the native Windows file dialog.
        var devToolsSession = TryGetDevToolsSession(driver);
        var fileInput = FindFileInput(driver);

        if (fileInput == null)
        {
            ClickUploadButton(driver);

            try
            {
                fileInput = wait.WaitPresent(
                    By.CssSelector("input[type='file']"),
                    Math.Min(_config.ElementTimeoutSeconds, 10));
            }
            catch (WebDriverTimeoutException)
            {
                fileInput = null;
            }

            if (fileInput == null)
            {
                // The chooser interception can prevent older builds from
                // materializing the input. Fall back to the normal click.
                DisableFileChooserInterception(devToolsSession);
                ClickUploadButton(driver);
                fileInput = wait.WaitPresent(
                    By.CssSelector("input[type='file']"),
                    _config.ElementTimeoutSeconds);
            }
        }

        if (fileInput == null)
        {
            throw new Exception("The image file input was not found on the Manipulate Images page.");
        }

        var uploadMethod = "";
        if (TrySetFileWithDevTools(devToolsSession, _config.ImageFilePath))
        {
            uploadMethod = "DOM.setFileInputFiles";
        }
        else if (TrySetFileWithSendKeys(driver, fileInput, _config.ImageFilePath))
        {
            uploadMethod = "WebElement.SendKeys";
        }
        else if (TrySetFileWithJavaScript(driver, fileInput, _config.ImageFilePath))
        {
            uploadMethod = "JavaScript DataTransfer";
        }
        else
        {
            throw new Exception(
                "The image file could not be attached to the upload control. "
                + "The DevTools, SendKeys and JavaScript DataTransfer strategies all failed.");
        }

        // The preview confirms that the application received the file. The
        // upload method is intentionally not written to the report.
        wait.WaitVisible(By.CssSelector(".iv-viewer > img"), _config.UploadTimeoutSeconds);

        // NEW BDIS.side waits before the rotation commands. The pause gives the
        // image widget time to finish processing the uploaded file.
        Thread.Sleep(TimeSpan.FromSeconds(_config.ImageUploadPauseSeconds));
    }

    public void EditImage(IWebDriver driver, SeleniumWaitHelper wait)
    {
        // NEW BDIS.side no longer clicks the preview image before rotating.
        wait.WaitVisible(By.CssSelector(".iv-viewer > img"));
        wait.WaitClickable(By.CssSelector(".fa-rotate-left")).Click();
        wait.WaitClickable(By.CssSelector(".fa-rotate-right")).Click();
        wait.WaitClickable(By.CssSelector(".fa-search-plus")).Click();
        wait.WaitClickable(By.CssSelector(".fa-search-minus")).Click();
    }

    private static void ClickUploadButton(IWebDriver driver)
    {
        var uploadButton = driver.FindElements(By.CssSelector(".formbuttonblue")).FirstOrDefault();
        if (uploadButton == null)
        {
            throw new Exception("The Upload File control was not found.");
        }

        uploadButton.Click();
    }

    private static IWebElement FindFileInput(IWebDriver driver)
    {
        // Prefer the body-level input recorded in NEW BDIS.side, then any file
        // input. Hidden inputs are valid because they are populated by CDP or
        // JavaScript rather than by a real mouse click.
        var selectors = new[] { "body > input[type='file']", "input[type='file']" };
        foreach (var selector in selectors)
        {
            var element = driver.FindElements(By.CssSelector(selector)).FirstOrDefault();
            if (element != null)
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a DevTools session and asks Page to intercept the native file
    /// chooser. This is best effort: when the Edge version does not support the
    /// command, the upload falls back to the other strategies.
    /// </summary>
    private static DevToolsSession TryGetDevToolsSession(IWebDriver driver)
    {
        try
        {
            var edgeDriver = driver as EdgeDriver;
            if (edgeDriver == null)
            {
                return null;
            }

            var session = edgeDriver.GetDevToolsSession();

            try
            {
                session.SendCommand(
                    "Page.enable",
                    new JsonObject(),
                    CancellationToken.None,
                    null,
                    true);
            }
            catch
            {
                // Interception is optional.
            }

            try
            {
                session.SendCommand(
                    "Page.setInterceptFileChooserDialog",
                    new JsonObject { ["enabled"] = true },
                    CancellationToken.None,
                    null,
                    true);
            }
            catch
            {
                // Older Edge versions do not expose this command.
            }

            return session;
        }
        catch
        {
            return null;
        }
    }

    private static void DisableFileChooserInterception(DevToolsSession session)
    {
        if (session == null)
        {
            return;
        }

        try
        {
            session.SendCommand(
                "Page.setInterceptFileChooserDialog",
                new JsonObject { ["enabled"] = false },
                CancellationToken.None,
                null,
                true);
        }
        catch
        {
            // Best effort only.
        }
    }

    /// <summary>
    /// Attaches the file through the Chromium DevTools protocol. This works in
    /// Microsoft Edge because it targets the DOM node directly instead of using
    /// the Selenium IDE file-upload command.
    /// </summary>
    private static bool TrySetFileWithDevTools(DevToolsSession session, string filePath)
    {
        if (session == null || string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            var document = session.SendCommand(
                "DOM.getDocument",
                JsonNode.Parse("{\"depth\":-1}"),
                CancellationToken.None,
                null,
                true);

            if (!document.Result.HasValue
                || !document.Result.Value.TryGetProperty("root", out var root)
                || !root.TryGetProperty("nodeId", out var rootNodeIdValue))
            {
                return false;
            }

            var rootNodeId = rootNodeIdValue.GetInt32();

            foreach (var selector in new[] { "body > input[type='file']", "input[type='file']" })
            {
                var query = session.SendCommand(
                    "DOM.querySelector",
                    new JsonObject
                    {
                        ["nodeId"] = rootNodeId,
                        ["selector"] = selector
                    },
                    CancellationToken.None,
                    null,
                    true);

                if (!query.Result.HasValue
                    || !query.Result.Value.TryGetProperty("nodeId", out var nodeIdValue))
                {
                    continue;
                }

                var nodeId = nodeIdValue.GetInt32();
                if (nodeId <= 0)
                {
                    continue;
                }

                session.SendCommand(
                    "DOM.setFileInputFiles",
                    new JsonObject
                    {
                        ["files"] = new JsonArray { filePath },
                        ["nodeId"] = nodeId
                    },
                    CancellationToken.None,
                    null,
                    true);

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetFileWithSendKeys(IWebDriver driver, IWebElement fileInput, string filePath)
    {
        try
        {
            var javascriptExecutor = (IJavaScriptExecutor)driver;
            javascriptExecutor.ExecuteScript(
                "arguments[0].style.display='block';"
                + "arguments[0].style.visibility='visible';"
                + "arguments[0].style.opacity='1';",
                fileInput);

            fileInput.SendKeys(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetFileWithJavaScript(IWebDriver driver, IWebElement fileInput, string filePath)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(filePath);
            var base64 = Convert.ToBase64String(fileBytes);
            var fileName = Path.GetFileName(filePath);

            const string script =
                "var input = arguments[0];"
                + "var binary = atob(arguments[1]);"
                + "var bytes = new Uint8Array(binary.length);"
                + "for (var i = 0; i < binary.length; i++) { bytes[i] = binary.charCodeAt(i); }"
                + "var file = new File([bytes], arguments[2], { type: 'image/tiff' });"
                + "var transfer = new DataTransfer();"
                + "transfer.items.add(file);"
                + "input.files = transfer.files;"
                + "input.dispatchEvent(new Event('input', { bubbles: true }));"
                + "input.dispatchEvent(new Event('change', { bubbles: true }));";

            ((IJavaScriptExecutor)driver).ExecuteScript(script, fileInput, base64, fileName);
            return true;
        }
        catch
        {
            return false;
        }
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
        string mainWindowHandle,
        int pauseBeforeCloseSeconds)
    {
        var newWindowHandle = wait.WaitForNewWindowHandle(existingWindowCount, 30);
        driver.SwitchTo().Window(newWindowHandle);

        // NEW BDIS.side waits before closing the print preview window.
        Thread.Sleep(TimeSpan.FromSeconds(pauseBeforeCloseSeconds));

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

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    /// <summary>
    /// Forces a WinForms dialog to the foreground. TopMost alone is not reliable
    /// when the script runs from LINQPad while another application is active.
    /// </summary>
    private static void BringToForeground(Form form)
    {
        if (form == null || form.IsDisposed)
        {
            return;
        }

        try
        {
            var foregroundWindow = GetForegroundWindow();
            var foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
            var currentThread = GetWindowThreadProcessId(form.Handle, out _);

            var attached = false;
            if (foregroundThread != 0 && currentThread != 0 && foregroundThread != currentThread)
            {
                attached = AttachThreadInput(currentThread, foregroundThread, true);
            }

            ShowWindow(form.Handle, SwRestore);
            BringWindowToTop(form.Handle);
            form.Activate();
            form.BringToFront();
            SetForegroundWindow(form.Handle);

            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
        catch
        {
            // Foreground activation is a usability improvement, never fatal.
        }
    }

    public static TestExecutionMode? SelectExecutionMode()
    {
        return RunOnSta(() =>
        {
            using (var form = new Form())
            {
                form.Text = "BDIS Test Execution Mode";
                form.Width = 600;
                form.Height = 370;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.TopMost = true;
                form.ShowInTaskbar = true;
                form.WindowState = FormWindowState.Normal;
                form.Shown += (sender, args) =>
                {
                    BringToForeground(form);
                };

                var contentTop = UpsBranding.ApplyDialogChrome(
                    form,
                    "BDIS Test Execution Mode",
                    "UPS automated web evaluation");

                var promptLabel = new Label
                {
                    Text = "Select the type of test to execute:",
                    Left = 24,
                    Top = contentTop,
                    Width = 540,
                    Height = 25,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = UpsBranding.DarkTextColor,
                    BackColor = Color.Transparent
                };

                TestExecutionMode? selectedMode = null;

                var f5Button = new Button
                {
                    Text = "F5 / File Server Test",
                    Left = 24,
                    Top = contentTop + 40,
                    Width = 250,
                    Height = 50
                };

                var fullButton = new Button
                {
                    Text = "Full Test (All URLs)",
                    Left = 300,
                    Top = contentTop + 40,
                    Width = 250,
                    Height = 50
                };

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    Left = 300,
                    Top = contentTop + 108,
                    Width = 250,
                    Height = 38,
                    DialogResult = DialogResult.Cancel
                };

                UpsBranding.StylePrimaryButton(f5Button);
                UpsBranding.StylePrimaryButton(fullButton);
                UpsBranding.StyleSecondaryButton(cancelButton);

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
                form.ActiveControl = f5Button;

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
                form.Width = 800;
                form.Height = 760;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.TopMost = true;
                form.ShowInTaskbar = true;
                form.WindowState = FormWindowState.Normal;
                form.Shown += (sender, args) => BringToForeground(form);

                var contentTop = UpsBranding.ApplyDialogChrome(
                    form,
                    "Edge WebDriver Initialization Error",
                    "The browser driver must match Microsoft Edge");

                var instructionsLabel = new Label
                {
                    Text = instructions,
                    Left = 24,
                    Top = contentTop,
                    Width = 740,
                    Height = 300,
                    AutoSize = false,
                    BackColor = Color.Transparent
                };

                var detailsBox = new RichTextBox
                {
                    Text = technicalDetails ?? "",
                    Left = 24,
                    Top = contentTop + 310,
                    Width = 740,
                    Height = 190,
                    ReadOnly = true,
                    ScrollBars = RichTextBoxScrollBars.Vertical
                };

                var downloadButton = new Button
                {
                    Text = "Open Edge WebDriver Download Page",
                    Left = 24,
                    Top = contentTop + 520,
                    Width = 300,
                    Height = 38
                };

                var closeButton = new Button
                {
                    Text = "Close",
                    Left = 640,
                    Top = contentTop + 520,
                    Width = 124,
                    Height = 38,
                    DialogResult = DialogResult.OK
                };

                UpsBranding.StyleSecondaryButton(downloadButton);
                UpsBranding.StylePrimaryButton(closeButton);

                downloadButton.Click += (sender, args) => OpenEdgeWebDriverPage();

                form.Controls.Add(instructionsLabel);
                form.Controls.Add(detailsBox);
                form.Controls.Add(downloadButton);
                form.Controls.Add(closeButton);
                form.AcceptButton = closeButton;
                form.CancelButton = closeButton;
                form.ActiveControl = closeButton;

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
                form.Width = 760;
                form.Height = 580;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.TopMost = true;
                form.ShowInTaskbar = true;
                form.WindowState = FormWindowState.Normal;
                form.Shown += (sender, args) => BringToForeground(form);

                var contentTop = UpsBranding.ApplyDialogChrome(
                    form,
                    title,
                    "BDIS automated web evaluation");

                var messageLabel = new Label
                {
                    Text = message ?? "",
                    Left = 24,
                    Top = contentTop,
                    Width = 700,
                    Height = 70,
                    AutoSize = false,
                    BackColor = Color.Transparent
                };

                var detailsBox = new RichTextBox
                {
                    Text = details ?? "",
                    Left = 24,
                    Top = contentTop + 80,
                    Width = 700,
                    Height = 260,
                    ReadOnly = true,
                    ScrollBars = RichTextBoxScrollBars.Vertical
                };

                var closeButton = new Button
                {
                    Text = "Close",
                    Left = 600,
                    Top = contentTop + 350,
                    Width = 124,
                    Height = 38,
                    DialogResult = DialogResult.OK
                };

                UpsBranding.StylePrimaryButton(closeButton);

                form.Controls.Add(messageLabel);
                form.Controls.Add(detailsBox);
                form.Controls.Add(closeButton);
                form.AcceptButton = closeButton;
                form.CancelButton = closeButton;
                form.ActiveControl = closeButton;

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

        // The order below follows the functional order recorded in NEW BDIS.side.
        // Each RunStep call stops the remaining country steps when it fails.
        if (!RunStep(countryResult, driver, "Login", () =>
                _loginService.Login(driver, urlProfile.Url, wait, countryProfile)))
        {
            return FinishCountry(countryResult);
        }

        // Diagnostics only: confirms which backend actually served the request.
        // This is written to the local log and never to the HTML report.
        LogEffectiveBackend(driver, urlProfile.WebKey, countryProfile.DisplayName);

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

        // NEW BDIS.side detects the web server after the print preview closes
        // and runs the email test only when the page reports SU6BDISWEB5.
        var detectedWebServer = workflow.DetectWebServer(driver);
        _logger.LogInfo(
            $"Detected web server for {urlProfile.WebKey} / {countryProfile.DisplayName}: {detectedWebServer}");

        if (BdisWorkflow.IsEmailRequired(detectedWebServer))
        {
            if (!RunStep(countryResult, driver, "Document Query - Email", () =>
                    workflow.DocumentQueryEmail(driver, wait, _config.PageLoadTimeoutSeconds)))
            {
                return FinishCountry(countryResult);
            }
        }
        else
        {
            RecordSkippedStep(
                countryResult,
                "Document Query - Email",
                $"Email test skipped because the detected web server '{detectedWebServer}' is not SU6BDISWEB5.");
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

    /// <summary>
    /// Records a step and returns true when execution may continue. A failed
    /// step fails the country and stops the remaining country steps.
    /// </summary>
    private bool RunStep(
        CountryTestResult countryResult,
        IWebDriver driver,
        string stepName,
        Action action)
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
            var safeDetails = _config.Redactor.Redact(exception.Message);

            stepResult.Status = "FAILED";
            stepResult.Details = safeDetails;

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
            _logger.LogStep(stepResult);

            countryResult.Status = "FAILED";
            countryResult.FailedStep = stepName;
            countryResult.ErrorMessage = safeDetails;

            _logger.LogError($"{stepResult.EnvironmentName} / {countryResult.CountryName} / {stepName}", exception);

            _progress?.Invoke(
                $"{stepResult.EnvironmentName} / {countryResult.CountryName} / {stepName}: FAILED - {safeDetails}");

            return false;
        }
    }

    /// <summary>
    /// Records a step that NEW BDIS.side intentionally did not execute, such as
    /// the email test on a server other than SU6BDISWEB5. A skipped step never
    /// changes the country, environment or global status and is not sent as an
    /// error to Power Automate.
    /// </summary>
    private void RecordSkippedStep(CountryTestResult countryResult, string stepName, string details)
    {
        var now = DateTimeOffset.Now;
        var stepResult = new TestStepResult
        {
            EnvironmentName = countryResult.EnvironmentName,
            CountryName = countryResult.CountryName,
            StepName = stepName,
            Status = "SKIPPED",
            Details = details,
            ExecutedBy = _currentUser,
            StartTime = now,
            EndTime = now
        };

        countryResult.Steps.Add(stepResult);
        _logger.LogStep(stepResult);

        _logger.LogInfo(
            $"{stepResult.EnvironmentName} / {countryResult.CountryName} / {stepName}: SKIPPED - {details}");

        _progress?.Invoke(
            $"{stepResult.EnvironmentName} / {countryResult.CountryName} / {stepName}: SKIPPED");
    }

    /// <summary>
    /// Diagnostics only. Confirms which backend actually served the request by
    /// logging the effective URL and the footer text. Never written to the report.
    /// </summary>
    private void LogEffectiveBackend(IWebDriver driver, string webKey, string countryName)
    {
        try
        {
            var footer = driver.FindElements(By.CssSelector(".bcopyright")).FirstOrDefault();
            var footerText = footer?.Text?.Trim() ?? "";

            _logger.LogInfo(
                $"Effective page for {webKey} / {countryName}: {driver.Url} | Footer: {footerText}");
        }
        catch
        {
            // Diagnostics must never affect the test result.
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

        var skippedCount = summary.Steps.Count(step => step.Status == "SKIPPED");
        logger.LogInfo($"Steps skipped by the NEW BDIS.side conditional: {skippedCount}.");

        // Report generation and webhook delivery are independent: a report
        // failure must never prevent the Power Automate notification.
        try
        {
            HtmlReportGenerator.Generate(
                htmlReportPath,
                summary.Steps,
                ProgramSettings.TestName,
                selectedMode,
                currentUser,
                summary.GlobalStatus);

            logger.LogInfo($"HTML report generated at: {htmlReportPath}");
        }
        catch (Exception exception)
        {
            logger.LogError("HTML report generation", exception);
        }

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
