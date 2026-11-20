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
  <Namespace>System.Diagnostics</Namespace>
  <Namespace>System.Runtime.InteropServices</Namespace>
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
// WFMS Selenium Automation
// Source of truth: WFMS.side
// ============================================================================

// 1. SETTINGS
public static class ProgramSettings
{
    public static string TestName = "WFMS";
    public static string BaseDirectory = @"C:\Selenium\WFMS";
    public static string LogFolder = @"C:\Selenium\WFMS\log";
    public static string ConfigPath => Path.Combine(BaseDirectory, "config.properties");
}

// 2. PROFILES
public class UrlTestProfile
{
    public string WebKey { get; set; }
    public string Url { get; set; }
}

// 3. RESULTS
public class TestStepResult
{
    public string EnvironmentName { get; set; }
    public string StepName { get; set; }
    public string Status { get; set; } // PASSED / FAILED
    public string Details { get; set; }
    public string ScreenshotPath { get; set; }
}

public class RunSummary
{
    public string GlobalStatus { get; set; } = "PASSED";
    public List<TestStepResult> Steps { get; set; } = new List<TestStepResult>();
}

public class ConfigurationException : Exception
{
    public ConfigurationException(string msg) : base(msg) { }
}

public class EdgeDriverInitializationException : Exception
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
    public string UpsLogoPath { get; private set; }
    public string WebhookUrl { get; private set; }
    public string AuthToken { get; private set; }
    public string ShipmentId { get; private set; }
    public string PackageId { get; private set; }
    public string TargetUrl { get; private set; }
    public int ElementTimeoutSeconds { get; private set; } = 30;
    public int PageLoadTimeoutSeconds { get; private set; } = 60;
    public int PrintWindowPauseSeconds { get; private set; } = 15;
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
        TargetUrl = Require("f5Url");
        AuthToken = Require("authToken");
        ShipmentId = Require("shipmentId");
        PackageId = Require("packageId");
        WebhookUrl = Require("powerAutomateWebhookUrl");

        Redactor.Add(AuthToken);
        Redactor.Add(WebhookUrl);

        var configuredLogo = GetOptional("upsLogoPath");
        UpsLogoPath = string.IsNullOrWhiteSpace(configuredLogo)
            ? Path.Combine(BaseDirectory, "ups-logo.png")
            : ResolvePath(configuredLogo);

        ElementTimeoutSeconds = GetInt("elementTimeoutSeconds", 30);
        PageLoadTimeoutSeconds = GetInt("pageLoadTimeoutSeconds", 60);
        PrintWindowPauseSeconds = GetInt("printWindowPauseSeconds", 15);
        WebhookProxyHost = GetOptional("webhookProxyHost");
        WebhookProxyPort = GetInt("webhookProxyPort", 9191);
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
        Write("STEP", $"Step={s.StepName}\tStatus={s.Status}\tDetails={_redactor.Redact(s.Details)}\tScreenshot={s.ScreenshotPath}");
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
}

// 9. UPS BRANDING
public static class UpsBranding
{
    public static Color BrownColor = Color.FromArgb(53, 28, 21);
    public static Color GoldColor = Color.FromArgb(255, 181, 0);
    public static Color LightGrayColor = Color.FromArgb(245, 243, 240);
    public static Color DarkTextColor = Color.FromArgb(43, 26, 18);
    public static string LogoPath { get; set; } = Path.Combine(@"C:\Selenium\WFMS", "ups-logo.png");
    public const string LogoBase64 = "iVBORw0KGgoAAAANSUhEUgAAAKAAAACgCAYAAACLz2ctAAAABGdBTUEAALGPC/xhBQAAACBjSFJNAAB6JgAAgIQAAPoAAACA6AAAdTAAAOpgAAA6mAAAF3CculE8AAAAUGVYSWZNTQAqAAAACAACARIAAwAAAAEAAQAAh2kABAAAAAEAAAAmAAAAAAADoAEAAwAAAAEAAQAAoAIABAAAAAEAAACgoAMABAAAAAEAAACgAAAAALUxN5cAAAIyaVRYdFhNTDpjb20uYWRvYmUueG1wAAAAAAA8eDp4bXBtZXRhIHhtbG5zOng9ImFkb2JlOm5zOm1ldGEvIiB4OnhtcHRrPSJYTVAgQ29yZSA2LjAuMCI+CiAgIDxyZGY6UkRGIHhtbG5zOnJkZj0iaHR0cDovL3d3dy53My5vcmcvMTk5OS8wMi8yMi1yZGYtc3ludGF4LW5zIyI+CiAgICAgIDxyZGY6RGVzY3JpcHRpb24gcmRmOmFib3V0PSIiCiAgICAgICAgICAgIHhtbG5zOmV4aWY9Imh0dHA6Ly9ucy5hZG9iZS5jb20vZXhpZi8xLjAvIgogICAgICAgICAgICB4bWxuczp0aWZmPSJodHRwOi8vbnMuYWRvYmUuY29tL3RpZmYvMS4wLyI+CiAgICAgICAgIDxleGlmOlBpeGVsWURpbWVuc2lvbj44MDA8L2V4aWY6UGl4ZWxZRGltZW5zaW9uPgogICAgICAgICA8ZXhpZjpQaXhlbFhEaW1lbnNpb24+ODAwPC9leGlmOlBpeGVsWERpbWVuc2lvbj4KICAgICAgICAgPGV4aWY6Q29sb3JTcGFjZT4xPC9leGlmOkNvbG9yU3BhY2U+CiAgICAgICAgIDx0aWZmOk9yaWVudGF0aW9uPjE8L3RpZmY6T3JpZW50YXRpb24+CiAgICAgIDwvcmRmOkRlc2NyaXB0aW9uPgogICA8L3JkZjpSREY+CjwveDp4bXBtZXRhPgo1TxkPAABAAElEQVR4Ae29B4Bd1XUuvO690/uMRqM26hJIAkmAKDYdDDam2IYY3B38XIjjGicvTl6K8UucvBcneYmTP8XGDnZs4xjHDUy1DcamCCOaQF2olxmNRtPrLf/3rbXXuWeuRkLAXEB49sy5e++11167fWftcvbZR2TSTNbAZA1M1sBkDUzWwGQNTNbAZA1M1sBkDUzWwGQNTNbAZA1M1sBkDUzWwGQNTNbAZA1M1sBkDUzWwGu1BhKv1YK9sHLlkm/+hJTe+ZBkZY3kEDfb+vpby997wcFpnZ17Uum+ftCGpLu9XYaG2hBcEYkfjlzmaJk2R+pbWsBRIeX1LUr85bpZBx788Vv64UkuerOkFi8SufOfZEQkwbR+o81vCABzCVklJbLmMyW/97v1s7J9z1WVSrtMaa48MZnpn1pVmZiSTKZOqa3IphpqE7naSsmVlyVrM9nE4mQiXZpKZnOlyZwkgM+kYgbuXE5yuazksukIQFnSJCkZSUkmk5BMLiXDCE4mZevoSOZg71Aq0TeYSg6ly3rSufSagZHK4Yw0jvYMpNf29tcPJMrmDNy26do9F144T26+UQGajYS/Rh2vIQACZGjra39vd9n+x/68ZllLz9w5s0oW1deMzq+uHKwsL02d0txQUltdW7awrjpVU1uZyJUkpK68NFFSnhIpBbAAG0lm0eaZDK4RyWVG4cxIljSCDeEEXRbBCQAvlx1BOHSg6jECMig05iTmTiQTAK9VdRZ2Gmyj2aSMIstDmRLJJEq6+0dKM72Dyd7+QdnWPVTRP5yteKI/U3uoa6B8bc/A9M69mRU79o28q+/+m19bwDyOAZhLQqul3lz1PxoWtOxZMK+18pSW6YmlNVWJFU2NZc0N9RVzm+orqutrK1IVZaVSkkpIKgEdlgagMgAPwJUdSQNco5JL0w9UpeGmDRoYVbvlgLYsEQ";

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
        sb.Append("<table><tr><th style='width:8%;'>#</th><th style='width:40%;'>Step</th><th style='width:52%;'>Details</th></tr>");
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            bool isFailed = s.Status == "FAILED";
            string badge = isFailed ? "badge-danger" : "badge-success";
            sb.Append("<tr><td>" + (i + 1) + "</td>");
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
            for (int i = 0; i < summary.Steps.Count; i++)
            {
                var s = summary.Steps[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append("\"stepName\":\"" + JsonHelper.Escape(s.StepName) + "\",");
                sb.Append("\"status\":\"" + JsonHelper.Escape(s.Status) + "\",");
                sb.Append("\"details\":\"" + JsonHelper.Escape(s.Details) + "\"");
                sb.Append("}");
            }
            sb.Append("]}");
            var payload = sb.ToString();
            _logger.LogInfo("Power Automate payload: " + payload);
            "Sending Power Automate webhook request...".Dump();
            using (var client = new HttpClient())
            {
                if (!string.IsNullOrWhiteSpace(_config.WebhookProxyHost))
                {
                    _logger.LogInfo("Proxy configured: " + _config.WebhookProxyHost + ":" + _config.WebhookProxyPort);
                }
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var resp = client.PostAsync(_config.WebhookUrl, content).Result;
                _logger.LogInfo("Power Automate response: " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                $"Power Automate webhook response: {(int)resp.StatusCode} {resp.ReasonPhrase}".Dump();
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

// 13. DIALOG SERVICE
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

// 14. WFMS WORKFLOW
public class WfmsWorkflow
{
    private readonly ConfigurationManager _config;
    private readonly SeleniumWaitHelper _wait;
    private readonly Action<string> _progress;

    public WfmsWorkflow(ConfigurationManager config, SeleniumWaitHelper wait, Action<string> progress)
    {
        _config = config;
        _wait = wait;
        _progress = progress;
    }

    public void TestShipmentId(IWebDriver driver)
    {
        _progress?.Invoke("Shipment ID: navigating to WFMS URL");
        driver.Navigate().GoToUrl(_config.TargetUrl);
        driver.Manage().Window.Size = new System.Drawing.Size(1140, 1070);

        _progress?.Invoke("Shipment ID: entering auth token");
        var authToken = _wait.WaitVisible(By.Id("authToken"));
        authToken.Click();
        authToken.Clear();
        authToken.SendKeys(_config.AuthToken);

        _progress?.Invoke("Shipment ID: entering shipment id " + _config.ShipmentId);
        var shipmentBox = _wait.WaitVisible(By.Id("ShipmentId"));
        shipmentBox.Click();
        shipmentBox.Clear();
        shipmentBox.SendKeys(_config.ShipmentId);

        _progress?.Invoke("Shipment ID: clicking Button1 to load image");
        _wait.WaitClickable(By.Id("Button1")).Click();

        _progress?.Invoke("Shipment ID: waiting 30s for image to load");
        Thread.Sleep(30000);

        _wait.WaitClickable(By.CssSelector(".fa-rotate-left")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-rotate-right")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-th")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-th")).Click();
        _wait.WaitClickable(By.CssSelector(".btn-group-vertical:nth-child(2) > .btn:nth-child(1)")).Click();
        _wait.WaitClickable(By.CssSelector(".btn-group-vertical:nth-child(2) > .btn:nth-child(1)")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-search-minus")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-search-minus")).Click();
        _wait.WaitClickable(By.CssSelector(".btn-group-vertical:nth-child(3) > .btn:nth-child(2)")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-arrows-v")).Click();

        _progress?.Invoke("waiting 10s before print window");
        Thread.Sleep(10000);

        // capture handles before the click that opens the print window
        var mainWindow = driver.CurrentWindowHandle;
        var existing = new HashSet<string>(driver.WindowHandles);

        _progress?.Invoke("opening print window");
        _wait.WaitClickable(By.CssSelector(".btn-primary:nth-child(2)")).Click();

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
    }

    public void TestPackageId(IWebDriver driver)
    {
        _progress?.Invoke("Package ID: navigating to WFMS URL");
        driver.Navigate().GoToUrl(_config.TargetUrl);
        driver.Manage().Window.Size = new System.Drawing.Size(1140, 1070);

        _progress?.Invoke("Package ID: entering auth token");
        var authToken = _wait.WaitVisible(By.Id("authToken"));
        authToken.Click();
        authToken.Clear();
        authToken.SendKeys(_config.AuthToken);

        _progress?.Invoke("Package ID: entering package id " + _config.PackageId);
        var packageBox = _wait.WaitVisible(By.Id("PackageIds"));
        packageBox.Click();
        packageBox.Clear();
        packageBox.SendKeys(_config.PackageId);

        _progress?.Invoke("Package ID: clicking Button1 to load image");
        _wait.WaitClickable(By.Id("Button1")).Click();

        _progress?.Invoke("Package ID: waiting 30s for image to load");
        Thread.Sleep(30000);

        _wait.WaitClickable(By.CssSelector(".fa-rotate-left")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-rotate-right")).Click();
        _wait.WaitClickable(By.CssSelector(".btn-group-vertical:nth-child(1) > .btn:nth-child(3)")).Click();
        _wait.WaitClickable(By.CssSelector(".btn-group-vertical:nth-child(1) > .btn:nth-child(3)")).Click();
        _wait.WaitClickable(By.CssSelector(".btn-group-vertical:nth-child(2) > .btn:nth-child(1)")).Click();
        _wait.WaitClickable(By.CssSelector(".btn-group-vertical:nth-child(2) > .btn:nth-child(1)")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-search-minus")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-search-minus")).Click();
        _wait.WaitClickable(By.CssSelector(".btn-group-vertical:nth-child(3) > .btn:nth-child(2)")).Click();
        _wait.WaitClickable(By.CssSelector(".fa-arrows-v")).Click();

        _progress?.Invoke("waiting 10s before print window");
        Thread.Sleep(10000);

        // capture handles before the click that opens the print window
        var mainWindow = driver.CurrentWindowHandle;
        var existing = new HashSet<string>(driver.WindowHandles);

        _progress?.Invoke("opening print window");
        _wait.WaitClickable(By.CssSelector(".btn-primary:nth-child(2)")).Click();

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
    }
}

// 15. WFMS TEST RUNNER
public class WfmsTestRunner
{
    private readonly ConfigurationManager _config;
    private readonly ExecutionLogger _logger;
    private readonly WfmsWorkflow _workflow;
    private readonly EdgeDriverManager _edgeMgr;
    private readonly Action<string> _progress;
    private readonly List<TestStepResult> _allSteps = new List<TestStepResult>();

    public WfmsTestRunner(ConfigurationManager config, ExecutionLogger logger, Action<string> progress)
    {
        _config = config;
        _logger = logger;
        _progress = progress;
        _edgeMgr = new EdgeDriverManager(logger);
    }

    public RunSummary Run()
    {
        var summary = new RunSummary();
        var url = _config.TargetUrl;

        EdgeDriver driver = null;
        SeleniumWaitHelper wait = null;
        try
        {
            driver = _edgeMgr.CreateDriver(_logger);
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(_config.PageLoadTimeoutSeconds);
            driver.Manage().Window.Size = new System.Drawing.Size(1140, 1070);
            wait = new SeleniumWaitHelper(driver, _config);

            var workflow = new WfmsWorkflow(_config, wait, _progress);

            RunStep(driver, "Shipment ID - Full Test", () => workflow.TestShipmentId(driver));
            RunStep(driver, "Package ID - Full Test", () => workflow.TestPackageId(driver));
        }
        catch (EdgeDriverInitializationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("WFMS run", ex);
        }
        finally
        {
            try { driver?.Quit(); } catch { }
        }

        summary.GlobalStatus = _allSteps.Any(s => s.Status == "FAILED") ? "FAILED" : "PASSED";
        summary.Steps = _allSteps;
        return summary;
    }

    private void RunStep(IWebDriver driver, string stepName, Action action)
    {
        var stepResult = new TestStepResult
        {
            EnvironmentName = "F5",
            StepName = stepName
        };
        try
        {
            _progress?.Invoke($"F5 / {stepName}: starting");
            action();
            stepResult.Status = "PASSED";
            stepResult.Details = "Executed successfully.";
            _logger.LogStep(stepResult);
            _progress?.Invoke($"F5 / {stepName}: PASSED");
        }
        catch (Exception ex)
        {
            stepResult.Status = "FAILED";
            stepResult.Details = _config.Redactor.Redact(ex.Message);
            var ssName = $"F5_{stepName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.png".Replace("/", "_");
            var ssPath = ScreenshotHelper.Capture(driver, Path.Combine(ProgramSettings.LogFolder, "Screenshots"), ssName);
            stepResult.ScreenshotPath = string.IsNullOrEmpty(ssPath) ? "" : Path.Combine("Screenshots", ssName);
            _logger.LogStep(stepResult);
            _logger.LogError($"F5 / {stepName}", ex);
            _progress?.Invoke($"F5 / {stepName}: FAILED - {stepResult.Details}");
        }
        _allSteps.Add(stepResult);
    }

    public List<TestStepResult> GetAllSteps() => _allSteps;
}

// 16. MAIN
void Main()
{
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

    var runner = new WfmsTestRunner(config, logger, message => { message.Dump(); });
    RunSummary summary;
    try
    {
        summary = runner.Run();
    }
    catch (EdgeDriverInitializationException ex)
    {
        logger.LogError("Driver init", ex);
        var safeDetails = config.Redactor.Redact(ex.TechnicalDetails);
        ("Driver initialization failed: " + ex.Message).Dump();
        DialogService.ShowEdgeDriverFailureDialog(safeDetails);
        logger.LogInfo("Execution stopped because Microsoft Edge WebDriver could not be initialized.");
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
    ("Final global status: " + summary.GlobalStatus).Dump();
    ("HTML report successfully generated at: " + htmlPath).Dump();
}