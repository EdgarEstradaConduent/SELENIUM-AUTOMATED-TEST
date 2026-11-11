<Query Kind="Program">
  <Reference Relative="..\msedgedriver.exe">C:\Selenium\msedgedriver.exe</Reference>
  <Reference Relative="..\SeleniumExtras.WaitHelpers.dll">C:\Selenium\SeleniumExtras.WaitHelpers.dll</Reference>
  <Reference Relative="..\WebDriver.dll">C:\Selenium\WebDriver.dll</Reference>
  <Reference Relative="..\WebDriver.Support.dll">C:\Selenium\WebDriver.Support.dll</Reference>
  <Namespace>OpenQA.Selenium</Namespace>
  <Namespace>OpenQA.Selenium.DevTools</Namespace>
  <Namespace>OpenQA.Selenium.Edge</Namespace>
  <Namespace>OpenQA.Selenium.Support.UI</Namespace>
  <Namespace>System.Drawing</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Runtime.InteropServices</Namespace>
  <Namespace>System.Text.Json.Nodes</Namespace>
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
// ICA Selenium Automation
// Source of truth: ICA WEB.side
// ============================================================================

// 1. SETTINGS
public static class ProgramSettings
{
    public static string TestName = "ICA";
    public static string BaseDirectory = @"C:\Selenium\ICAWEB(BOC)";
    public static string LogFolder = @"C:\Selenium\ICAWEB(BOC)\log";
	public static string ConfigPath => Path.Combine(BaseDirectory, "SANDY.properties");
	///public static string ConfigPath => Path.Combine(BaseDirectory, "EAST.properties");
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
            ? Path.Combine(@"C:\Selenium\ICAWEB(BOC)", "ups-logo.png")
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
    public static string LogoPath { get; set; } = Path.Combine(@"C:\Selenium\ICAWEB(BOC)", "ups-logo.png");
    public const string LogoBase64 = "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";

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
                sb.Append("\"startTime\":\"" + e.StartTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'") + "\",");
				sb.Append("\"endTime\":\"" + e.EndTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'") + "\",");
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

        // Duty type select may trigger a postback; re-find on stale reference.
        SelectDutyType(driver, "'Low Value' Shipment", "03");

        var shipBox = _wait.WaitVisible(By.Id("ShipmentNbrTextBox"));
        shipBox.Click();
        shipBox.Clear();
        shipBox.SendKeys(profile.ShipmentNbr);

        SetCalendarDate(driver, profile.ShipStartDate);

        var findBtn = _wait.WaitClickable(By.Id("FindShipmentButton"));
        findBtn.Click();

        var gridItem = _wait.WaitClickable(By.CssSelector(".griditem > td > a > img"));
        gridItem.Click();

        _wait.WaitClickable(By.LinkText("View/Print")).Click();

        // print window (side file: pause 30s then close)
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
        // side file: after print window -> Cart (no back-to-results here)
    }

    public void ReportHighValueShipment(IWebDriver driver, CountryTestProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ShipmentNbrHigh)) return;
        GoToReports(driver);

        SelectDutyType(driver, "'High Value' Shipment", "01");

        var shipBox = _wait.WaitVisible(By.Id("ShipmentNbrTextBox"));
        shipBox.Click();
        shipBox.Clear();
        shipBox.SendKeys(profile.ShipmentNbrHigh);

        // side file: click the label line (tr 22) before FindShipment
        try { _wait.WaitClickable(By.CssSelector("tr:nth-child(22) > .crModuleLabelLine")).Click(); } catch { }

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
        // side file: after print window -> Cart -> Cart(1) -> delete -> back to results
    }

    private void SelectDutyType(IWebDriver driver, string label, string valueFallback)
    {
        By dutyBy = By.XPath("/html/body/table[2]/tbody/tr/td[2]/form/table/tbody/tr/td/table/tbody/tr[8]/td/select");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var dutyType = _wait.WaitVisible(dutyBy);
                var dutySelect = new SelectElement(dutyType);
                try { dutySelect.SelectByText(label); }
                catch { try { dutySelect.SelectByValue(valueFallback); } catch { } }
                return;
            }
            catch (StaleElementReferenceException)
            {
                if (attempt == 1) throw;
                Thread.Sleep(500);
            }
        }
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
        // side file: after print window -> SelectAllToCartButton (handled in CartSelectAllAndDelete)
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

// 16. DIALOG SERVICE
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

    private static void BringToForeground(Form form)
    {
        if (form == null || form.IsDisposed) return;
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
                form.Text = "ICA Test Execution Mode";
                form.Width = 600;
                form.Height = 370;
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
                    "ICA Test Execution Mode",
                    "ICA automated web evaluation");

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

// 17. ICA TEST RUNNER
public class IcaTestRunner
{
    private readonly ConfigurationManager _config;
    private readonly ExecutionLogger _logger;
    private readonly IcaLoginService _login;
    private readonly EdgeDriverManager _edgeMgr;
    private readonly Action<string> _progress;
    private readonly List<TestStepResult> _allSteps = new List<TestStepResult>();

    public IcaTestRunner(ConfigurationManager config, ExecutionLogger logger, Action<string> progress)
    {
        _config = config;
        _logger = logger;
        _progress = progress;
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
                _progress?.Invoke($"{stepResult.EnvironmentName} / {countryProfile.DisplayName} / {stepName}: PASSED");
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
                _progress?.Invoke($"{stepResult.EnvironmentName} / {countryProfile.DisplayName} / {stepName}: FAILED - {stepResult.Details}");
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
    TestExecutionMode? selectedMode = null;
    try
    {
        selectedMode = DialogService.SelectExecutionMode();
    }
    catch (Exception ex)
    {
        (ex.Message).Dump();
        return;
    }
    if (selectedMode == null)
    {
        "Execution cancelled by the user.".Dump();
        return;
    }
    var mode = selectedMode.Value;

    var configPath = ProgramSettings.ConfigPath;
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

    var runner = new IcaTestRunner(config, logger, message => { message.Dump(); });
    RunSummary summary;
    try
    {
        summary = runner.Run(mode);
    }
    catch (EdgeDriverInitializationException ex)
    {
        logger.LogError("Driver init", ex);
        ("Driver initialization failed: " + ex.Message).Dump();
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