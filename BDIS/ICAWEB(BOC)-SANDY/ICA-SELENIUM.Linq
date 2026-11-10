<Query Kind="Program">
  <Reference Relative="..\msedgedriver.exe">C:\Selenium\msedgedriver.exe</Reference>
  <Reference Relative="..\SeleniumExtras.WaitHelpers.dll">C:\Selenium\SeleniumExtras.WaitHelpers.dll</Reference>
  <Reference Relative="..\WebDriver.dll">C:\Selenium\WebDriver.dll</Reference>
  <Reference Relative="..\WebDriver.Support.dll">C:\Selenium\WebDriver.Support.dll</Reference>
  <Reference>System.Windows.Forms</Reference>
  <Namespace>OpenQA.Selenium</Namespace>
  <Namespace>OpenQA.Selenium.Edge</Namespace>
  <Namespace>OpenQA.Selenium.Support.UI</Namespace>
  <Namespace>System.Windows.Forms</Namespace>
  <IncludeUncapsulator>false</IncludeUncapsulator>
</Query>

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Drawing;

// ============================================================================
// ICA Selenium Automation - BDIS-style refactor
// Source of truth: BDIS/ICA WEB.side
// ============================================================================

// 1. SETTINGS
public static class ProgramSettings
{
    public static string TestName = "ICA";
    public static string BaseDirectory = @"C:\Selenium\ICAWEB(BOC)-SANDY";
    public static string LogFolder = @"C:\Selenium\ICAWEB(BOC)-SANDY\log";
}

public enum TestExecutionMode { F5Only, FullTest }

// 2. PROFILES
public class UrlTestProfile
{
    public string WebKey { get; set; }
    public string ConfigKey { get; set; }
    public string Url { get; set; }
}

public class CountryTestProfile
{
    public string DisplayName { get; set; }
    public string CountryCode { get; set; }
    public string CountrySelectText { get; set; }
    public string ShipmentNbr { get; set; }        // low value
    public string ShipmentNbrHigh { get; set; }    // high value (CA only)
    public string ShipStartDate { get; set; }
}

// 3. RESULTS
public class TestStepResult
{
    public string EnvironmentName { get; set; }
    public string CountryName { get; set; }
    public string StepName { get; set; }
    public string Status { get; set; } // PASSED / FAILED / SKIPPED
    public string Details { get; set; }
    public string ScreenshotPath { get; set; }
}

public class CountryTestResult
{
    public string CountryName { get; set; }
    public string Status { get; set; } = "PASSED";
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public List<TestStepResult> Steps { get; set; } = new List<TestStepResult>();
    public string ErrorMessage { get; set; } = "";
}

public class EnvironmentTestResult
{
    public string WebKey { get; set; }
    public string TargetUrl { get; set; }
    public string Status { get; set; } = "PASSED";
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public List<CountryTestResult> Countries { get; set; } = new List<CountryTestResult>();
    public string ErrorMessage { get; set; } = "";
}

public class RunSummary
{
    public string GlobalStatus { get; set; } = "PASSED";
    public List<EnvironmentTestResult> Environments { get; set; } = new List<EnvironmentTestResult>();
}

public class ConfigurationException : Exception
{
    public ConfigurationException(string msg) : base(msg) { }
}

public class EdgeDriverInitializationException : Exception
{
    public EdgeDriverInitializationException(string msg, Exception inner) : base(msg, inner) { }
}

// 4. SECRET REDACTOR
public class SecretRedactor
{
    private readonly List<string> _secrets = new List<string>();
    public void Add(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !_secrets.Contains(value)) _secrets.Add(value);
    }
    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var s in _secrets)
        {
            if (!string.IsNullOrEmpty(s)) text = text.Replace(s, "***REDACTED***");
        }
        return text;
    }
}

// 5. CONFIGURATION MANAGER
public class ConfigurationManager
{
    private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public SecretRedactor Redactor { get; } = new SecretRedactor();
    public string BaseDirectory { get; }
    public string ConfigPath { get; }
    public string ImageFilePath { get; private set; } = "";
    public string UpsLogoPath { get; private set; }
    public string Username { get; private set; }
    public string Password { get; private set; }
    public string WebhookUrl { get; private set; }
    public string EmailRecipient { get; private set; } = "";
    public List<UrlTestProfile> UrlProfiles { get; private set; }
    public List<CountryTestProfile> CountryProfiles { get; private set; }
    public int ElementTimeoutSeconds { get; private set; } = 30;
    public int PageLoadTimeoutSeconds { get; private set; } = 60;
    public int PrintWindowPauseSeconds { get; private set; } = 30;
    public string WebhookProxyHost { get; private set; } = "";
    public int WebhookProxyPort { get; private set; } = 9191;

    public ConfigurationManager(string configPath)
    {
        ConfigPath = configPath;
        BaseDirectory = Path.GetDirectoryName(configPath);
        if (!File.Exists(configPath)) throw new ConfigurationException($"Configuration file not found: {configPath}");
        foreach (var line in File.ReadAllLines(configPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
            var parts = line.Split(new[] { '=' }, 2);
            if (parts.Length == 2) _values[parts[0].Trim()] = parts[1].Trim();
        }
        Validate();
        BuildProfiles();
    }

    private string Require(string key)
    {
        if (!_values.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
            throw new ConfigurationException($"Missing required configuration property: {key}");
        return v.Trim();
    }
    private string GetOptional(string key) => _values.TryGetValue(key, out var v) ? (v?.Trim() ?? "") : "";
    private int GetInt(string key, int def)
    {
        if (_values.TryGetValue(key, out var v) && int.TryParse(v, out var n)) return n;
        return def;
    }
    private string ResolvePath(string p)
    {
        var t = p.Trim();
        return Path.IsPathRooted(t) ? Path.GetFullPath(t) : Path.GetFullPath(Path.Combine(BaseDirectory, t));
    }

    private void Validate()
    {
        var required = new[] { "f5Url", "username", "password", "caShipmentNbr", "usShipmentNbr", "caShipmentNbr01", "shipStartDate", "powerAutomateWebhookUrl" };
        foreach (var k in required) Require(k);

        Username = Require("username");
        Password = Require("password");
        WebhookUrl = Require("powerAutomateWebhookUrl");
        Redactor.Add(Username);
        Redactor.Add(Password);
        Redactor.Add(WebhookUrl);

        var configuredLogo = GetOptional("upsLogoPath");
        UpsLogoPath = string.IsNullOrWhiteSpace(configuredLogo)
            ? Path.Combine(@"C:\Selenium\BDISWEB-SANDY", "ups-logo.png")
            : ResolvePath(configuredLogo);

        ElementTimeoutSeconds = GetInt("elementTimeoutSeconds", 30);
        PageLoadTimeoutSeconds = GetInt("pageLoadTimeoutSeconds", 60);
        PrintWindowPauseSeconds = GetInt("printWindowPauseSeconds", 30);
        WebhookProxyHost = GetOptional("webhookProxyHost");
        if (string.IsNullOrEmpty(WebhookProxyHost) && _values.ContainsKey("webhookProxyHost")) WebhookProxyHost = "";
        WebhookProxyPort = GetInt("webhookProxyPort", 9191);
    }

    private void BuildProfiles()
    {
        UrlProfiles = new List<UrlTestProfile>();
        var keys = new[] { "f5Url", "webServerUrl2", "webServerUrl3", "webServerUrl4", "webServerUrl5" };
        var labels = new[] { "F5", "SU6BOCWEB1", "SU6BOCWEB2", "SU6BOCWEB3", "SU6BOCWEB4" };
        for (int i = 0; i < keys.Length; i++)
        {
            if (_values.TryGetValue(keys[i], out var url) && !string.IsNullOrWhiteSpace(url))
            {
                UrlProfiles.Add(new UrlTestProfile { WebKey = labels[i], ConfigKey = keys[i], Url = url.Trim() });
            }
        }
        if (UrlProfiles.Count == 0) throw new ConfigurationException("No URL profiles configured.");

        var caShip = Require("caShipmentNbr");
        var usShip = Require("usShipmentNbr");
        var caShipHigh = Require("caShipmentNbr01");
        var shipStart = Require("shipStartDate");

        CountryProfiles = new List<CountryTestProfile>
        {
            new CountryTestProfile
            {
                DisplayName = "Canada",
                CountryCode = "CA",
                CountrySelectText = "CANADA (CA)",
                ShipmentNbr = caShip,
                ShipmentNbrHigh = caShipHigh,
                ShipStartDate = shipStart
            },
            new CountryTestProfile
            {
                DisplayName = "United States",
                CountryCode = "US",
                CountrySelectText = "UNITED STATES (US)",
                ShipmentNbr = usShip,
                ShipmentNbrHigh = null,
                ShipStartDate = shipStart
            }
        };
    }

}

// 6. EXECUTION LOGGER
public class ExecutionLogger
{
    private readonly string _logFile;
    private readonly SecretRedactor _redactor;
    public ExecutionLogger(string logFolder, string testName, SecretRedactor redactor)
    {
        _redactor = redactor;
        if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);
        _logFile = Path.Combine(logFolder, testName + "WebApplication.txt");
    }
    public void LogInfo(string msg) => Write("INFO", msg);
    public void LogError(string ctx, Exception ex) => Write("ERROR", $"{ctx}: {ex.GetType().Name}: {_redactor.Redact(ex.Message)}");
    public void LogStep(TestStepResult s)
    {
        Write("STEP", $"Environment={s.EnvironmentName}\tCountry={s.CountryName}\tStep={s.StepName}\tStatus={s.Status}\tDetails={_redactor.Redact(s.Details)}\tScreenshot={s.ScreenshotPath}");
    }
    private void Write(string level, string msg)
    {
        try { File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{level}\t{_redactor.Redact(msg)}{Environment.NewLine}"); }
        catch { }
    }
}

// 7. SCREENSHOT HELPER
public static class ScreenshotHelper
{
    public static string Capture(IWebDriver driver, string folder, string name)
    {
        try
        {
            if (driver == null) return "";
            var ss = ((ITakesScreenshot)driver).GetScreenshot();
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            ss.SaveAsFile(path);
            return path;
        }
        catch { return ""; }
    }
}

// 8. SELENIUM WAIT HELPER
public class SeleniumWaitHelper
{
    private readonly IWebDriver _driver;
    private readonly ConfigurationManager _config;
    public SeleniumWaitHelper(IWebDriver driver, ConfigurationManager config)
    {
        _driver = driver;
        _config = config;
    }
    public IWebElement WaitVisible(By by, int? timeout = null)
    {
        var w = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeout ?? _config.ElementTimeoutSeconds));
        return w.Until(d => { var e = d.FindElement(by); return (e.Displayed) ? e : null; });
    }
    public IWebElement WaitClickable(By by, int? timeout = null)
    {
        var w = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeout ?? _config.ElementTimeoutSeconds));
        return w.Until(SeleniumExtras.WaitHelpers.ExpectedConditions.ElementToBeClickable(by));
    }
    public bool IsElementPresent(By by, int seconds)
    {
        try
        {
            var w = new WebDriverWait(_driver, TimeSpan.FromSeconds(seconds));
            w.Until(d => d.FindElement(by));
            return true;
        }
        catch { return false; }
    }
    public void WaitPageSourceContains(string text, int timeout)
    {
        var w = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeout));
        w.Until(d => d.PageSource.Contains(text));
    }
}

// 9. UPS BRANDING + DIALOGS
public static class UpsBranding
{
    public static Color BrownColor = Color.FromArgb(53, 28, 21);
    public static Color GoldColor = Color.FromArgb(255, 181, 0);
    public static Color LightGrayColor = Color.FromArgb(245, 243, 240);
    public static Color DarkTextColor = Color.FromArgb(43, 26, 18);
    public static string LogoPath { get; set; } = Path.Combine(@"C:\Selenium\BDISWEB-SANDY", "ups-logo.png");
    public const string LogoBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public static Image LoadLogo()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(LogoPath) && File.Exists(LogoPath))
            {
                using (var s = File.OpenRead(LogoPath))
                using (var src = Image.FromStream(s)) return new Bitmap(src);
            }
        }
        catch { }
        try
        {
            using (var ms = new MemoryStream(Convert.FromBase64String(LogoBase64)))
            using (var src = Image.FromStream(ms)) return new Bitmap(src);
        }
        catch { return null; }
    }

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
        var header = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = BrownColor };
        var titleLabel = new Label { Text = title, Left = 22, Top = 14, Width = 420, Height = 30, ForeColor = GoldColor, BackColor = Color.Transparent, Font = new Font("Segoe UI", 14F, FontStyle.Bold) };
        var subtitleLabel = new Label { Text = subtitle, Left = 24, Top = 50, Width = 420, Height = 22, ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9F) };
        var logoPanel = new Panel { Dock = DockStyle.Right, Width = 104, BackColor = Color.Transparent, Padding = new Padding(8) };
        var logo = new PictureBox { Dock = DockStyle.Fill, Image = LoadLogo(), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        logoPanel.Controls.Add(logo);
        header.Controls.Add(logoPanel);
        header.Controls.Add(titleLabel);
        header.Controls.Add(subtitleLabel);
        form.Controls.Add(header);
        return header.Height + 18;
    }
}

// 10. HTML REPORT GENERATOR
public static class HtmlReportGenerator
{
    public static void Generate(string filePath, string testName, List<TestStepResult> steps, string operatorUser, string upsLogoPath)
    {
        int total = steps.Count;
        int failed = steps.Count(s => s.Status == "FAILED");
        int passed = total - failed;
        string globalStatus = failed > 0 ? "FAILED" : "PASSED";
        string statusColor = failed > 0 ? "#C53030" : "#2F855A";

        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset='utf-8'><title>" + testName + " Automation Dashboard</title><style>");
        sb.Append("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 32px; background-color: #F5F3F0; color: #2B1A12; }");
        sb.Append(".header-container { display: flex; justify-content: space-between; align-items: center; background: #351C15; border-bottom: 5px solid #FFB500; border-radius: 12px; padding: 22px 28px; margin-bottom: 28px; box-shadow: 0 6px 18px rgba(53,28,21,0.25); }");
        sb.Append(".header-title h2 { color: #FFB500; margin: 0; font-size: 28px; font-weight: 700; }");
        sb.Append(".header-subtitle { color: #F5F3F0; margin-top: 6px; font-size: 14px; }");
        sb.Append(".header-right { display: flex; flex-direction: column; align-items: flex-end; gap: 10px; }");
        sb.Append(".ups-logo { height: 54px; width: auto; background: transparent; border-radius: 8px; padding: 6px; }");
        sb.Append(".timestamp { color: #F5F3F0; font-size: 13px; text-align: right; }");
        sb.Append(".summary-deck { display: flex; gap: 18px; margin-bottom: 28px; flex-wrap: wrap; }");
        sb.Append(".card { flex: 1; min-width: 190px; background: #FFFFFF; border-radius: 10px; padding: 16px 20px; box-shadow: 0 4px 12px rgba(53,28,21,0.10); border-left: 6px solid #351C15; }");
        sb.Append(".card.status-card { border-left-color: " + statusColor + "; }");
        sb.Append(".card.agent-card { border-left-color: #FFB500; }");
        sb.Append(".card-title { font-size: 12px; text-transform: uppercase; color: #6B5A50; font-weight: 700; letter-spacing: 1px; }");
        sb.Append(".card-value { font-size: 20px; font-weight: 700; margin-top: 6px; color: #351C15; }");
        sb.Append("table { width: 100%; border-collapse: collapse; background: #FFFFFF; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 14px rgba(53,28,21,0.12); }");
        sb.Append("th { background-color: #351C15; color: #FFB500; font-weight: 700; text-transform: uppercase; font-size: 13px; letter-spacing: 0.5px; padding: 15px; text-align: left; }");
        sb.Append("td { padding: 15px; border-bottom: 1px solid #E8E1DA; font-size: 14px; vertical-align: top; color: #2B1A12; }");
        sb.Append("tr:last-child td { border-bottom: none; }");
        sb.Append("tr:hover { background-color: #FFF7E0; }");
        sb.Append(".badge { padding: 4px 8px; border-radius: 4px; font-weight: bold; font-size: 11px; display: inline-block; text-transform: uppercase; }");
        sb.Append(".badge-success { background-color: #2F855A; color: #FFFFFF; }");
        sb.Append(".badge-danger { background-color: #C53030; color: #FFFFFF; }");
        sb.Append(".badge-skipped { background-color: #718096; color: #FFFFFF; }");
        sb.Append("pre { background: #FFF5F5; padding: 12px; border-radius: 6px; border-left: 4px solid #C53030; white-space: pre-wrap; color: #742A2A; font-size: 12px; font-family: 'Courier New', Courier, monospace; margin-top: 8px; }");
        sb.Append(".screenshot-link { display: inline-block; margin-top: 8px; color: #63B3ED; text-decoration: none; font-weight: bold; font-size: 13px; }");
        sb.Append("</style></head><body>");
        sb.Append("<div class='header-container'>");
        sb.Append("<div class='header-title'><h2>Automated Test Execution Dashboard</h2><div class='header-subtitle'>" + testName + " Web Application Evaluation</div></div>");
        sb.Append("<div class='header-right'><img class='ups-logo' alt='UPS' src='" + BuildLogoDataUri(upsLogoPath) + "'><div class='timestamp'><strong>Execution Date:</strong> " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</div></div></div>");
        sb.Append("<div class='summary-deck'>");
        sb.Append("<div class='card agent-card'><div class='card-title'>PCM Operator Agent</div><div class='card-value'>" + (operatorUser ?? "Unknown") + "</div></div>");
        sb.Append("<div class='card status-card'><div class='card-title'>Global Evaluation Status</div><div class='card-value' style='color:" + statusColor + ";'>" + globalStatus + "</div></div>");
        sb.Append("<div class='card'><div class='card-title'>Consolidated Steps</div><div class='card-value'>" + passed + " / " + total + " Steps Passed</div></div></div>");
        sb.Append("<table><tr><th style='width:8%;'>#</th><th style='width:22%;'>Environment / Country</th><th style='width:22%;'>Step</th><th style='width:48%;'>Details</th></tr>");
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            bool isFailed = s.Status == "FAILED";
            string badge = isFailed ? "badge-danger" : (s.Status == "SKIPPED" ? "badge-skipped" : "badge-success");
            sb.Append("<tr><td>" + (i + 1) + "</td>");
            sb.Append("<td>" + s.EnvironmentName + " / " + s.CountryName + "</td>");
            sb.Append("<td><strong>" + System.Net.WebUtility.HtmlEncode(s.StepName) + "</strong></td>");
            sb.Append("<td><span class='badge " + badge + "'>" + s.Status + "</span> ");
            sb.Append(System.Net.WebUtility.HtmlEncode(s.Details ?? ""));
            if (!string.IsNullOrEmpty(s.ScreenshotPath))
                sb.Append("<br><a class='screenshot-link' href='" + s.ScreenshotPath + "' target='_blank'>View Screenshot</a>");
            sb.Append("</td></tr>");
        }
        sb.Append("</table></body></html>");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }
    private static string BuildLogoDataUri(string logoPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                var bytes = File.ReadAllBytes(logoPath);
                var ext = Path.GetExtension(logoPath).ToLowerInvariant();
                var mime = ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" : "image/png";
                return "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
            }
        }
        catch { }
        return "data:image/png;base64," + UpsBranding.LogoBase64;
    }
}

// 11. POWER AUTOMATE REPORTER
public class PowerAutomateReporter
{
    private readonly ConfigurationManager _config;
    private readonly ExecutionLogger _logger;
    public PowerAutomateReporter(ConfigurationManager config, ExecutionLogger logger)
    {
        _config = config;
        _logger = logger;
    }
    public void Send(RunSummary summary, string user)
    {
        if (string.IsNullOrWhiteSpace(_config.WebhookUrl)) { _logger.LogInfo("Webhook skipped: URL empty."); return; }
        try
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"testName\":\"" + JsonHelper.Escape(ProgramSettings.TestName) + "\",");
            sb.Append("\"user\":\"" + JsonHelper.Escape(user) + "\",");
            sb.Append("\"globalStatus\":\"" + JsonHelper.Escape(summary.GlobalStatus) + "\",");
            sb.Append("\"results\":[");
            for (int i = 0; i < summary.Environments.Count; i++)
            {
                var e = summary.Environments[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append("\"webKey\":\"" + JsonHelper.Escape(e.WebKey) + "\",");
                sb.Append("\"targetUrl\":\"" + JsonHelper.Escape(e.TargetUrl) + "\",");
                sb.Append("\"status\":\"" + JsonHelper.Escape(e.Status) + "\",");
                sb.Append("\"startTime\":\"" + e.StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",");
                sb.Append("\"endTime\":\"" + e.EndTime.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",");
                sb.Append("\"error\":\"" + JsonHelper.Escape(e.ErrorMessage) + "\"");
                sb.Append("}");
            }
            sb.Append("]}");
            var payload = sb.ToString();
            _logger.LogInfo("Power Automate payload: " + payload);
            using (var client = new HttpClient())
            {
                if (!string.IsNullOrWhiteSpace(_config.WebhookProxyHost))
                {
                    // proxy not applied to HttpClient default; log only
                    _logger.LogInfo("Proxy configured: " + _config.WebhookProxyHost + ":" + _config.WebhookProxyPort);
                }
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var resp = client.PostAsync(_config.WebhookUrl, content).Result;
                _logger.LogInfo("Power Automate response: " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("PowerAutomate", ex);
        }
    }
}

public static class JsonHelper
{
    public static string Escape(string v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        return v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}

// 12. EDGE DRIVER MANAGER
public class EdgeDriverManager
{
    private readonly ExecutionLogger _logger;
    public EdgeDriverManager(ExecutionLogger logger) { _logger = logger; }
    public EdgeDriver CreateDriver(ExecutionLogger logger)
    {
        try
        {
            var options = new EdgeOptions();
            options.AddArgument("--ignore-certificate-errors");
            options.AddArgument("--allow-insecure-localhost");
            var service = EdgeDriverService.CreateDefaultService(@"C:\Selenium\");
            service.HideCommandPromptWindow = true;
            var driver = new EdgeDriver(service, options);
            _logger.LogInfo("EdgeDriver created successfully.");
            return driver;
        }
        catch (Exception ex)
        {
            _logger.LogError("EdgeDriver creation", ex);
            throw new EdgeDriverInitializationException("Failed to create EdgeDriver", ex);
        }
    }
}

// 13. COUNTRY SELECTION SERVICE (ICA)
public class CountrySelectionService
{
    public void SelectCountry(IWebDriver driver, SeleniumWaitHelper wait, CountryTestProfile profile)
    {
        var ddl = wait.WaitVisible(By.Id("LocationCountryList"));
        var select = new SelectElement(ddl);
        try { select.SelectByText(profile.CountrySelectText); }
        catch
        {
            try { select.SelectByValue(profile.CountryCode); }
            catch { select.SelectByText(profile.CountryCode); }
        }
        wait.WaitClickable(By.Id("SaveButton")).Click();
        try { wait.WaitClickable(By.Id("InfoOKButton")).Click(); } catch { }
    }
}

// 14. ICA LOGIN SERVICE
public class IcaLoginService
{
    private readonly ConfigurationManager _config;
    public IcaLoginService(ConfigurationManager config) { _config = config; }

    public void LoginAndSelectCountry(IWebDriver driver, SeleniumWaitHelper wait, CountryTestProfile profile, string url)
    {
        driver.Navigate().GoToUrl(url);
        var userBox = wait.WaitVisible(By.Id("UserName"));
        userBox.Clear(); userBox.SendKeys(_config.Username);
        var passBox = wait.WaitVisible(By.Id("Password"));
        passBox.Clear(); passBox.SendKeys(_config.Password);
        wait.WaitClickable(By.Id("LoginButton")).Click();
        var cs = new CountrySelectionService();
        cs.SelectCountry(driver, wait, profile);
    }

    public void ReLogin(IWebDriver driver, SeleniumWaitHelper wait, string url)
    {
        driver.Navigate().GoToUrl(url);
        var userBox = wait.WaitVisible(By.Id("UserName"));
        userBox.Clear(); userBox.SendKeys(_config.Username);
        var passBox = wait.WaitVisible(By.Id("Password"));
        passBox.Clear(); passBox.SendKeys(_config.Password);
        wait.WaitClickable(By.Id("LoginButton")).Click();
    }

    public void Logout(IWebDriver driver, SeleniumWaitHelper wait)
    {
        try { wait.WaitClickable(By.LinkText("Log Out")).Click(); }
        catch { }
    }
}

// 15. ICA WORKFLOW
public class IcaWorkflow
{
    private readonly ConfigurationManager _config;
    private readonly SeleniumWaitHelper _wait;
    public IcaWorkflow(ConfigurationManager config, SeleniumWaitHelper wait)
    {
        _config = config;
        _wait = wait;
    }

    // Reports navigation
    private void GoToReports(IWebDriver driver)
    {
        _wait.WaitClickable(By.LinkText("Reports")).Click();
    }

    // Calendar handling: navigate to shipStartDate
    private void SetCalendarDate(IWebDriver driver, string shipStartDate)
    {
        _wait.WaitClickable(By.Id("FromDateCalendar_calbutton")).Click();
        DateTime target;
        if (!DateTime.TryParse(shipStartDate, out target)) return;
        int monthsDiff = (DateTime.Now.Year - target.Year) * 12 + (DateTime.Now.Month - target.Month);
        if (monthsDiff < 0) monthsDiff = 0;
        for (int i = 0; i < monthsDiff; i++)
        {
            try { _wait.WaitClickable(By.LinkText("<")).Click(); Thread.Sleep(300); }
            catch { break; }
        }
        try { _wait.WaitClickable(By.LinkText(target.Day.ToString())).Click(); }
        catch { }
    }

    public void ReportLowValueShipment(IWebDriver driver, CountryTestProfile profile)
    {
        GoToReports(driver);
        var dutyType = _wait.WaitVisible(By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[8]/td/select"));
        var dutySelect = new SelectElement(dutyType);
        try { dutySelect.SelectByText("'Low Value' Shipment"); }
        catch { try { dutySelect.SelectByValue("03"); } catch { } }

        var shipBox = _wait.WaitVisible(By.Id("ShipmentNbrTextBox"));
        shipBox.Clear(); shipBox.SendKeys(profile.ShipmentNbr);

        SetCalendarDate(driver, profile.ShipStartDate);

        var findBtn = _wait.WaitClickable(By.Id("FindShipmentButton"));
        findBtn.Click();

        var gridItem = _wait.WaitClickable(By.CssSelector(".griditem > td > a > img"));
        gridItem.Click();

        _wait.WaitClickable(By.LinkText("View/Print")).Click();

        // print window
        var mainWindow = driver.CurrentWindowHandle;
        var existing = new HashSet<string>(driver.WindowHandles);
        // wait for new window
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string newHandle = null;
        while (DateTime.UtcNow < deadline && newHandle == null)
        {
            foreach (var h in driver.WindowHandles)
            {
                if (!existing.Contains(h) && h != mainWindow) { newHandle = h; break; }
            }
            if (newHandle == null) Thread.Sleep(500);
        }
        if (newHandle != null)
        {
            driver.SwitchTo().Window(newHandle);
            Thread.Sleep(_config.PrintWindowPauseSeconds * 1000);
            driver.Close();
            driver.SwitchTo().Window(mainWindow);
        }

        // back to results
        try { _wait.WaitClickable(By.Id("lnkBackToResults")).Click(); } catch { }
    }

    public void ReportHighValueShipment(IWebDriver driver, CountryTestProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ShipmentNbrHigh)) return;
        GoToReports(driver);
        var dutyType = _wait.WaitVisible(By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[8]/td/select"));
        var dutySelect = new SelectElement(dutyType);
        try { dutySelect.SelectByText("'High Value' Shipment"); }
        catch { try { dutySelect.SelectByValue("01"); } catch { } }

        var shipBox = _wait.WaitVisible(By.Id("ShipmentNbrTextBox"));
        shipBox.Clear(); shipBox.SendKeys(profile.ShipmentNbrHigh);

        SetCalendarDate(driver, profile.ShipStartDate);

        var findBtn = _wait.WaitClickable(By.Id("FindShipmentButton"));
        findBtn.Click();

        var gridItem = _wait.WaitClickable(By.CssSelector(".griditem > td > a > img"));
        gridItem.Click();

        _wait.WaitClickable(By.LinkText("View/Print")).Click();

        var mainWindow = driver.CurrentWindowHandle;
        var existing = new HashSet<string>(driver.WindowHandles);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string newHandle = null;
        while (DateTime.UtcNow < deadline && newHandle == null)
        {
            foreach (var h in driver.WindowHandles)
            {
                if (!existing.Contains(h) && h != mainWindow) { newHandle = h; break; }
            }
            if (newHandle == null) Thread.Sleep(500);
        }
        if (newHandle != null)
        {
            driver.SwitchTo().Window(newHandle);
            Thread.Sleep(_config.PrintWindowPauseSeconds * 1000);
            driver.Close();
            driver.SwitchTo().Window(mainWindow);
        }

        try { _wait.WaitClickable(By.Id("lnkBackToResults")).Click(); } catch { }
    }

    public void ReportShipmentUS(IWebDriver driver, CountryTestProfile profile)
    {
        GoToReports(driver);
        var shipBox = _wait.WaitVisible(By.Id("ShipmentNbrTextBox"));
        shipBox.Clear(); shipBox.SendKeys(profile.ShipmentNbr);

        SetCalendarDate(driver, profile.ShipStartDate);

        var findBtn = _wait.WaitClickable(By.Id("FindShipmentButton"));
        findBtn.Click();

        var gridItem = _wait.WaitClickable(By.CssSelector(".griditem > td > a > img"));
        gridItem.Click();

        _wait.WaitClickable(By.LinkText("View/Print")).Click();

        // regulatory popup handling: click + 10s pause + close dialog
        try
        {
            var popup = _wait.WaitVisible(By.Id("RegulatoryHistoryPopUp"));
            popup.Click();
            Thread.Sleep(10000);
            // close ui dialog
            var closeBtn = driver.FindElements(By.CssSelector(".ui-dialog:nth-child(8) .ui-button-icon")).FirstOrDefault();
            if (closeBtn != null) closeBtn.Click();
            Thread.Sleep(500);
        }
        catch { }

        // click the link in tr3 td4
        try
        {
            var link = _wait.WaitClickable(By.CssSelector("tr:nth-child(3) > td:nth-child(4) > a"));
            link.Click();
        }
        catch { }

        var mainWindow = driver.CurrentWindowHandle;
        var existing = new HashSet<string>(driver.WindowHandles);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string newHandle = null;
        while (DateTime.UtcNow < deadline && newHandle == null)
        {
            foreach (var h in driver.WindowHandles)
            {
                if (!existing.Contains(h) && h != mainWindow) { newHandle = h; break; }
            }
            if (newHandle == null) Thread.Sleep(500);
        }
        if (newHandle != null)
        {
            driver.SwitchTo().Window(newHandle);
            Thread.Sleep(_config.PrintWindowPauseSeconds * 1000);
            driver.Close();
            driver.SwitchTo().Window(mainWindow);
        }

        try { _wait.WaitClickable(By.Id("lnkBackToResults")).Click(); } catch { }
    }

    public void CartDeleteOne(IWebDriver driver)
    {
        try { _wait.WaitClickable(By.LinkText("Cart")).Click(); } catch { }
        try { _wait.WaitClickable(By.LinkText("Cart(1)")).Click(); } catch { }
        try { _wait.WaitClickable(By.Id("lnkDeleteCart")).Click(); } catch { }
        try { _wait.WaitClickable(By.Id("lnkBackToResults")).Click(); } catch { }
    }

    public void CartSelectAllAndDelete(IWebDriver driver)
    {
        try { _wait.WaitClickable(By.Id("SelectAllToCartButton")).Click(); } catch { }
        try { _wait.WaitClickable(By.CssSelector(".ui-button:nth-child(1)")).Click(); } catch { }
        try { _wait.WaitClickable(By.LinkText("Cart(12)")).Click(); } catch { }
        try { _wait.WaitClickable(By.Id("lnkDeleteCart")).Click(); } catch { }
        try { _wait.WaitClickable(By.Id("lnkBackToResults")).Click(); } catch { }
    }

    public void GoHome(IWebDriver driver)
    {
        try { _wait.WaitClickable(By.LinkText("Home")).Click(); } catch { }
    }
}

// 16. DIALOG SERVICE (mode selection)
public class DialogService
{
    public static TestExecutionMode ShowModeDialog()
    {
        using (var form = new Form())
        {
            form.Text = "ICA Automation";
            form.Size = new Size(420, 200);
            form.StartPosition = FormStartPosition.CenterScreen;
            UpsBranding.ApplyDialogChrome(form, "ICA Automation", "Select execution mode");
            var btnF5 = new Button { Text = "F5 / File Server Test", Left = 30, Top = 110, Width = 170, Height = 32 };
            var btnFull = new Button { Text = "Full Test (All URLs)", Left = 210, Top = 110, Width = 170, Height = 32 };
            UpsBranding.StylePrimaryButton(btnF5);
            UpsBranding.StylePrimaryButton(btnFull);
            form.Controls.Add(btnF5);
            form.Controls.Add(btnFull);
            TestExecutionMode result = TestExecutionMode.F5Only;
            bool chosen = false;
            btnF5.Click += (s, e) => { result = TestExecutionMode.F5Only; chosen = true; form.Close(); };
            btnFull.Click += (s, e) => { result = TestExecutionMode.FullTest; chosen = true; form.Close(); };
            form.ShowDialog();
            if (!chosen) throw new Exception("Execution cancelled by the user.");
            return result;
        }
    }
}

// 17. ICA TEST RUNNER
public class IcaTestRunner
{
    private readonly ConfigurationManager _config;
    private readonly ExecutionLogger _logger;
    private readonly IcaLoginService _login;
    private readonly EdgeDriverManager _edgeMgr;
    private readonly List<TestStepResult> _allSteps = new List<TestStepResult>();

    public IcaTestRunner(ConfigurationManager config, ExecutionLogger logger)
    {
        _config = config;
        _logger = logger;
        _login = new IcaLoginService(config);
        _edgeMgr = new EdgeDriverManager(logger);
    }

    public RunSummary Run(TestExecutionMode mode)
    {
        var summary = new RunSummary();
        var urls = mode == TestExecutionMode.F5Only
            ? _config.UrlProfiles.Where(u => u.WebKey == "F5").ToList()
            : _config.UrlProfiles;

        foreach (var urlProfile in urls)
        {
            var envResult = RunEnvironment(urlProfile);
            summary.Environments.Add(envResult);
            if (envResult.Status != "PASSED") summary.GlobalStatus = "FAILED";
        }
        return summary;
    }

    private EnvironmentTestResult RunEnvironment(UrlTestProfile urlProfile)
    {
        var env = new EnvironmentTestResult
        {
            WebKey = urlProfile.WebKey,
            TargetUrl = urlProfile.Url,
            StartTime = DateTimeOffset.Now,
            Status = "PASSED"
        };
        _logger.LogInfo($"Starting environment {urlProfile.WebKey}: {urlProfile.Url}");

        foreach (var countryProfile in _config.CountryProfiles)
        {
            EdgeDriver driver = null;
            SeleniumWaitHelper wait = null;
            try
            {
                driver = _edgeMgr.CreateDriver(_logger);
                driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(_config.PageLoadTimeoutSeconds);
                driver.Manage().Window.Size = new Size(1400, 900);
                wait = new SeleniumWaitHelper(driver, _config);

                var countryResult = RunCountry(driver, wait, urlProfile, countryProfile);
                env.Countries.Add(countryResult);
                if (countryResult.Status != "PASSED") env.Status = "FAILED";
            }
            catch (EdgeDriverInitializationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                env.Status = "FAILED";
                env.ErrorMessage = _config.Redactor.Redact($"Environment: {urlProfile.WebKey} | Error: {ex.GetType().Name}: {ex.Message}");
                _logger.LogError($"Environment {urlProfile.WebKey}", ex);
            }
            finally
            {
                try { driver?.Quit(); } catch { }
            }
        }
        env.EndTime = DateTimeOffset.Now;
        return env;
    }

    private CountryTestResult RunCountry(EdgeDriver driver, SeleniumWaitHelper wait, UrlTestProfile urlProfile, CountryTestProfile countryProfile)
    {
        var countryResult = new CountryTestResult
        {
            CountryName = countryProfile.DisplayName,
            StartTime = DateTimeOffset.Now
        };

        var workflow = new IcaWorkflow(_config, wait);

        Action<string, Action> runStep = (stepName, action) =>
        {
            if (countryResult.Status == "FAILED") return;
            var stepResult = new TestStepResult
            {
                EnvironmentName = urlProfile.WebKey,
                CountryName = countryProfile.DisplayName,
                StepName = stepName
            };
            try
            {
                action();
                stepResult.Status = "PASSED";
                stepResult.Details = "Executed successfully.";
                _logger.LogStep(stepResult);
            }
            catch (Exception ex)
            {
                stepResult.Status = "FAILED";
                stepResult.Details = _config.Redactor.Redact(ex.Message);
                var ssName = $"{urlProfile.WebKey}_{countryProfile.DisplayName}_{stepName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.png".Replace("/", "_");
                var ssPath = ScreenshotHelper.Capture(driver, Path.Combine(ProgramSettings.LogFolder, "Screenshots"), ssName);
                stepResult.ScreenshotPath = string.IsNullOrEmpty(ssPath) ? "" : Path.Combine("Screenshots", ssName);
                countryResult.Status = "FAILED";
                countryResult.ErrorMessage = _config.Redactor.Redact(ex.Message);
                _logger.LogStep(stepResult);
                _logger.LogError($"{urlProfile.WebKey} / {countryProfile.DisplayName} / {stepName}", ex);
            }
            _allSteps.Add(stepResult);
            countryResult.Steps.Add(stepResult);
        };

        // Canada steps
        if (countryProfile.CountryCode == "CA")
        {
            runStep("Login", () => _login.LoginAndSelectCountry(driver, wait, countryProfile, urlProfile.Url));
            runStep("Logout", () => _login.Logout(driver, wait));
            runStep("Re-Login", () => _login.ReLogin(driver, wait, urlProfile.Url));
            runStep("Report - Low Value Shipment", () => workflow.ReportLowValueShipment(driver, countryProfile));
            runStep("Cart - Delete One", () => workflow.CartDeleteOne(driver));
            runStep("Report - High Value Shipment", () => workflow.ReportHighValueShipment(driver, countryProfile));
            runStep("Cart - Delete One", () => workflow.CartDeleteOne(driver));
            runStep("Home", () => workflow.GoHome(driver));
            runStep("Logout", () => _login.Logout(driver, wait));
        }
        else // US
        {
            runStep("Login", () => _login.LoginAndSelectCountry(driver, wait, countryProfile, urlProfile.Url));
            runStep("Logout", () => _login.Logout(driver, wait));
            runStep("Re-Login", () => _login.ReLogin(driver, wait, urlProfile.Url));
            runStep("Report - Shipment", () => workflow.ReportShipmentUS(driver, countryProfile));
            runStep("Cart - Select All and Delete", () => workflow.CartSelectAllAndDelete(driver));
            runStep("Home", () => workflow.GoHome(driver));
            runStep("Logout", () => _login.Logout(driver, wait));
        }

        countryResult.EndTime = DateTimeOffset.Now;
        return countryResult;
    }

    public List<TestStepResult> GetAllSteps() => _allSteps;
}

// 18. MAIN
void Main()
{
    TestExecutionMode mode;
    try { mode = DialogService.ShowModeDialog(); }
    catch (Exception ex)
    {
        (ex.Message).Dump();
        return;
    }

    var configPath = Path.Combine(ProgramSettings.BaseDirectory, "config.properties");
    ConfigurationManager config;
    try { config = new ConfigurationManager(configPath); }
    catch (ConfigurationException ex)
    {
        (ex.Message).Dump();
        return;
    }

    UpsBranding.LogoPath = config.UpsLogoPath;

    var logFolder = ProgramSettings.LogFolder;
    if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);
    var screenshotDir = Path.Combine(logFolder, "Screenshots");
    if (!Directory.Exists(screenshotDir)) Directory.CreateDirectory(screenshotDir);

    var logger = new ExecutionLogger(logFolder, ProgramSettings.TestName, config.Redactor);
    logger.LogInfo("=== STARTING " + ProgramSettings.TestName + " WEB AUTOMATION TEST ===");
    logger.LogInfo("Windows user: " + Environment.UserName);
    logger.LogInfo("Configuration file: " + config.ConfigPath);
    logger.LogInfo("UPS logo file: " + config.UpsLogoPath);

    var runner = new IcaTestRunner(config, logger);
    RunSummary summary;
    try
    {
        summary = runner.Run(mode);
    }
    catch (EdgeDriverInitializationException ex)
    {
        logger.LogError("Driver init", ex);
        "Driver initialization failed: " + ex.Message.Dump();
        return;
    }

    // HTML report
    var htmlPath = Path.Combine(logFolder, ProgramSettings.TestName + "WebApplication.html");
    try
    {
        HtmlReportGenerator.Generate(htmlPath, ProgramSettings.TestName, runner.GetAllSteps(), Environment.UserName, config.UpsLogoPath);
        logger.LogInfo("HTML report generated at: " + htmlPath);
    }
    catch (Exception ex)
    {
        logger.LogError("HTML report", ex);
    }

    // Webhook
    try
    {
        var reporter = new PowerAutomateReporter(config, logger);
        reporter.Send(summary, Environment.UserName);
    }
    catch (Exception ex)
    {
        logger.LogError("Webhook", ex);
    }

    logger.LogInfo("Final global status: " + summary.GlobalStatus);
    logger.LogInfo("=== PROCESS FINISHED ===");
    ("Final global status: " + summary.GlobalStatus).Dump();
}