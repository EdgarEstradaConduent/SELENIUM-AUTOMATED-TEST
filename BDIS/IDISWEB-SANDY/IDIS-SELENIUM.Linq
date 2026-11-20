<Query Kind="Program">
  <Reference Relative="..\msedgedriver.exe">C:\Selenium\msedgedriver.exe</Reference>
  <Reference Relative="..\SeleniumExtras.WaitHelpers.dll">C:\Selenium\SeleniumExtras.WaitHelpers.dll</Reference>
  <Reference Relative="..\WebDriver.dll">C:\Selenium\WebDriver.dll</Reference>
  <Reference Relative="..\WebDriver.Support.dll">C:\Selenium\WebDriver.Support.dll</Reference>
  <Reference>System.Windows.Forms</Reference>
  <Namespace>OpenQA.Selenium</Namespace>
  <Namespace>OpenQA.Selenium.Edge</Namespace>
  <Namespace>OpenQA.Selenium.Support.UI</Namespace>
  <Namespace>SeleniumExtras.WaitHelpers</Namespace>
  <Namespace>System.Windows.Forms</Namespace>
  <Namespace>System.Diagnostics</Namespace>
  <Namespace>System.Runtime.InteropServices</Namespace>
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
// IDIS Selenium Automation
// Source of truth: IDIS-SELENIUM.Linq (legacy) + logs
// ============================================================================

// 1. SETTINGS
public static class ProgramSettings
{
    public static string TestName = "IDIS";
    public static string BaseDirectory = @"C:\Selenium\IDISWEB-SANDY";
    public static string LogFolder = @"C:\Selenium\IDISWEB-SANDY\log";
    public static string ConfigPath => Path.Combine(BaseDirectory, "config.properties");
}

public enum TestExecutionMode { F5Only, FullTest }

// 2. PROFILES
public class UrlTestProfile
{
    public string WebKey { get; set; }
    public string ConfigKey { get; set; }
    public string Url { get; set; }
}

// 3a. LEGACY RESULT CLASSES (used by preserved test methods)
public class InternalStepLog
{
    public string Timestamp { get; set; }
    public string StepDescription { get; set; }
    public string ResultDetails { get; set; }
    public string ColorHex { get; set; }
    public string ScreenshotFileName { get; set; }
}

public class TestWebResult
{
    public string WebKey { get; set; }
    public string Url { get; set; }
    public string Status { get; set; }
    public string StartTime { get; set; }
    public string EndTime { get; set; }
    public string ErrorMessage { get; set; } = "";
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

public class EnvironmentTestResult
{
    public string WebKey { get; set; }
    public string TargetUrl { get; set; }
    public string Status { get; set; } = "PASSED";
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
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
    public string Username { get; private set; }
    public string Password { get; private set; }
    public List<UrlTestProfile> UrlProfiles { get; private set; }
    public int ElementTimeoutSeconds { get; private set; } = 30;
    public int PageLoadTimeoutSeconds { get; private set; } = 60;
    public string WebhookProxyHost { get; private set; } = "";
    public int WebhookProxyPort { get; private set; } = 9191;

    // IDIS test data
    public string CaShipmentNbr { get; private set; }
    public string UsShipmentNbr { get; private set; }
    public string AddUser { get; private set; }
    public string AddUserPass { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string MawbNbr { get; private set; }
    public string BldgID { get; private set; }
    public string PackageID { get; private set; }
    public string Region { get; private set; }
    public string District { get; private set; }

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
    public string GetRaw(string key) => _values.TryGetValue(key, out var v) ? v : null;

    private void Validate()
    {
        Username = Require("username");
        Password = Require("password");
        WebhookUrl = Require("powerAutomateWebhookUrl");
        Redactor.Add(Username);
        Redactor.Add(Password);
        Redactor.Add(WebhookUrl);

        CaShipmentNbr = Require("caShipmentNbr");
        UsShipmentNbr = Require("usShipmentNbr");
        AddUser = Require("addUser");
        AddUserPass = Require("addUserPass");
        FirstName = Require("firstName");
        LastName = Require("lastName");
        MawbNbr = Require("mawbNbr");
        BldgID = Require("bldgID");
        PackageID = Require("packageID");
        Region = GetOptional("region");
        District = GetOptional("district");

        var configuredLogo = GetOptional("upsLogoPath");
        UpsLogoPath = string.IsNullOrWhiteSpace(configuredLogo)
            ? Path.Combine(BaseDirectory, "ups-logo.png")
            : ResolvePath(configuredLogo);

        ElementTimeoutSeconds = GetInt("elementTimeoutSeconds", 30);
        PageLoadTimeoutSeconds = GetInt("pageLoadTimeoutSeconds", 60);
        WebhookProxyHost = GetOptional("webhookProxyHost");
        WebhookProxyPort = GetInt("webhookProxyPort", 9191);
    }

    private void BuildProfiles()
    {
        UrlProfiles = new List<UrlTestProfile>();
        var keys = new[] { "f5Url", "webServerUrl2", "webServerUrl3", "webServerUrl4", "webServerUrl5", "webServerUrl6", "webServerUrl7", "webServerUrl8", "webServerUrl9" };
        var labels = new[] { "F5", "SU6IDISWEB2", "SU6IDISWEB3", "SU6IDISWEB4", "SU6IDISWEB5", "SU6IDISWEB6", "SU6IDISWEB7", "SU6IDISWEB8", "SU6IDISWEB9" };
        for (int i = 0; i < keys.Length; i++)
        {
            if (_values.TryGetValue(keys[i], out var url) && !string.IsNullOrWhiteSpace(url))
            {
                UrlProfiles.Add(new UrlTestProfile { WebKey = labels[i], ConfigKey = keys[i], Url = url.Trim() });
            }
        }
        if (UrlProfiles.Count == 0) throw new ConfigurationException("No URL profiles configured.");
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
        Write("STEP", $"Environment={s.EnvironmentName}\tStep={s.StepName}\tStatus={s.Status}\tDetails={_redactor.Redact(s.Details)}\tScreenshot={s.ScreenshotPath}");
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
    public static string LogoPath { get; set; } = Path.Combine(@"C:\Selenium\IDISWEB-SANDY", "ups-logo.png");
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

    public static TestExecutionMode? SelectExecutionMode()
    {
        return RunOnSta(() =>
        {
            using (var form = new Form())
            {
                form.Text = "IDIS Test Execution Mode";
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
                    "IDIS Test Execution Mode",
                    "IDIS automated web evaluation");

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

// 14. IDIS WORKFLOW (legacy tests preserved as static methods, invoked by runner)
public static class LegacyTests
{
    // Global state set by IdisTestRunner before running
    public static IWebDriver driver;
    public static WebDriverWait wait;
    public static Dictionary<string, string> cfg;
    public static string baseUrl, usrName, usrPass, usrNameTxtBox, usrPassTxtBox, loginBtn;
    public static string caShipNbr, usShipNbr, mawbNumber, bldgID, packageID, region, district;
    public static string addNewUser, addNewUserPass, addNewUserFName, addNewUserLName;
    public static string logPath, logFolder;
    public static List<InternalStepLog> accumulatedSteps;
    public static bool currentStepFailed = false;

    // Legacy helper methods
    public static IAlert WaitAlert(int seconds)
    {
        try
        {
            var waitAlert = new WebDriverWait(driver, TimeSpan.FromSeconds(seconds));
            return waitAlert.Until(ExpectedConditions.AlertIsPresent());
        }
        catch { return null; }
    }

    public static IWebElement FluentWait(By locator, int timeoutSeconds = 30, int pollingSeconds = 5)
    {
        try
        {
            var waitFluent = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            waitFluent.PollingInterval = TimeSpan.FromSeconds(pollingSeconds);
            waitFluent.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
            return waitFluent.Until(d => d.FindElement(locator));
        }
        catch { return null; }
    }

    public static IWebElement WaitForVisible(By locator, int timeoutSeconds = 15)
    {
        try
        {
            var waitVisible = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
            return waitVisible.Until(ExpectedConditions.ElementIsVisible(locator));
        }
        catch { return null; }
    }

    public static void BackToParent()
    {
        try
        {
            var handles = driver.WindowHandles;
            if (handles.Count > 0) driver.SwitchTo().Window(handles[0]);
        }
        catch (Exception e)
        {
            LogStep("Back function failed", e.ToString(), "#F56565");
            try { driver.Navigate().Back(); } catch { }
        }
    }

    public static void LogStep(string step, string result, string color = "#48BB78", string screenshotFileName = "")
    {
        var ts = DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        try { File.AppendAllText(logPath, $"{ts}\t{step}\t{result}{Environment.NewLine}"); } catch { }
        $@"[{ts}] [STEP: {step}] -> {result}".Dump();
        if (accumulatedSteps != null)
        {
            accumulatedSteps.Add(new InternalStepLog
            {
                Timestamp = ts,
                StepDescription = step,
                ResultDetails = result,
                ColorHex = color,
                ScreenshotFileName = screenshotFileName
            });
        }
        if (color == "#F56565") currentStepFailed = true;
    }

    private static string TakeScreenshot(IWebDriver drv, string savePath)
    {
        try
        {
            if (drv != null)
            {
                Screenshot screenshot = ((ITakesScreenshot)drv).GetScreenshot();
                screenshot.SaveAsFile(savePath);
                return savePath;
            }
        }
        catch (Exception ex)
        {
            $@"[WARNING] Error writing filesystem screenshot link: {ex.Message}".Dump();
        }
        return null;
    }

    static void AssignConfigVarsForRun()
    {
        baseUrl = cfg["url"];
        usrName = cfg.TryGetValue("usrName", out var u) ? u : cfg["username"];
        usrPass = cfg.TryGetValue("usrPass", out var p) ? p : cfg["password"];
        usrNameTxtBox = cfg["usrNameTxtBox"];
        usrPassTxtBox = cfg["usrPassTxtBox"];
        loginBtn = cfg["loginBtn"];
        caShipNbr = cfg["caShipmentNbr"];
        usShipNbr = cfg["usShipmentNbr"];
        addNewUser = cfg["addUser"];
        addNewUserPass = cfg["addUserPass"];
        addNewUserFName = cfg["firstName"];
        addNewUserLName = cfg["lastName"];
        mawbNumber = cfg["mawbNbr"];
        bldgID = cfg["bldgID"];
        packageID = cfg["packageID"];
        region = cfg.TryGetValue("region", out var r) ? r : "";
        district = cfg.TryGetValue("district", out var d) ? d : "";
    }
    // Legacy test methods (preserved from IDIS-SELENIUM.Linq original)
    static void loginCase()
        {
            try
            {
                try
                {
                    var header = driver.FindElement(By.XPath("/html/body/div/div[1]/div[2]/h1"));
                    var strHeader = header.Text;
                    if (strHeader == "Your connection is not private")
                    {
                        driver.FindElement(By.XPath("/html/body/div/div[2]/button[3]")).Click();
                        driver.FindElement(By.XPath("/html/body/div/div[3]/p[2]/a")).Click();
                    }
                }
                catch { /* Ignore */ }

                var username = driver.FindElement(By.XPath(usrNameTxtBox));
                username.SendKeys(usrName);
                LogStep("Input Username", usrName);

                var password = driver.FindElement(By.XPath(usrPassTxtBox));
                password.SendKeys(usrPass);
                LogStep("Input Password", "******");

                driver.FindElement(By.XPath(loginBtn)).Click();
                LogStep("Login Button Clicked", "OK");

                LogStep("Login test Complete", "OK");
            }
            catch (Exception e)
            {
                LogStep("Login Failed", e.ToString(), "#F56565");
            }
        }

        static void printButton()
        {
            try
            {
                LogStep("Print: Lookup Export Country/Date", "Start");
                exportDate();

                var printButton = FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/table[2]/tbody/tr[1]/td[2]/a[3]"));
                printButton.Click();
                LogStep("Print: Test Complete", "OK");
                Thread.Sleep(60000);;
                driver.Navigate().Back();
                Thread.Sleep(60000);
                FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
            }
            catch (Exception e)
            {
                LogStep("Print: Failed", e.ToString(), "#F56565");
            }
        }

        static void printToFile()
        {
            try
            {
                LogStep("Print to File: Lookup Export Country/Date", "Start");
                exportDate();

                var printToFile = FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/table[2]/tbody/tr[2]/td[2]/a[1]"));
                printToFile.Click();
                LogStep("Print to File: Test Complete", "OK");

                FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
            }
            catch (Exception e)
            {
                LogStep("Print to File: Failed", e.ToString(), "#F56565");
            }
        }

        static void exportDate()
        {
            try
            {
                LogStep("Lookup Export Country/Date", "Start");

                var lookupExpDate = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[1]/tbody/tr[12]/td/div/a"));
                lookupExpDate.Click();
                LogStep("Lookup Export Country/Date", "Lookup link");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/img")).Click();
                LogStep("Lookup Export Country/Date", "Click Start Date Picker");

                driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
                LogStep("Lookup Export Country/Date", "Click Start Date Picker < button");

                driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[4]/td[6]/font/a/b")).Click();
                LogStep("Lookup Export Country/Date", "Click Start Date Picker 2nd friday button");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/img")).Click();
                LogStep("Lookup Export Country/Date", "Click End Date Picker");

                driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
                LogStep("Lookup Export Country/Date", "Click End Date Picker < button");

                driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[4]/td[6]/font/a/b")).Click();
                LogStep("Lookup Export Country/Date", "Click End Date Picker 2nd saturday button");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input")).SendKeys("US");

                var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[19]/td[2]/input"));
                submitButton.Click();
                LogStep("Lookup Export Country/Date", "submit button click");

                var alert = WaitAlert(40);
                alert.Accept();
                LogStep("Lookup Export Country/Date", "ok 1st alert");

                var row1AddToCart = FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/table/tbody/tr[2]/td[13]/a"));
                row1AddToCart.Click();
                LogStep("Lookup Export Country/Date", "1st row add to cart click");
            }
            catch (Exception e)
            {
                LogStep("Lookup Export Country/Date: Failed", e.ToString(), "#F56565");
            }
        }

        static void emailButton()
        {
            try
            {
                LogStep("Email Button: Lookup Export Country/Date", "Start");
                exportDate();
			
    			Thread.Sleep(60000);
			
                var emailBut = FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/table[2]/tbody/tr[2]/td[2]/a[2]"));
                emailBut.Click();
                LogStep("Email Button", "Test Complete");

                var searchButton = FluentWait(By.XPath("/html/body/form/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
                searchButton.Click();
            }
            catch (Exception e)
            {
                LogStep("Email Button: Failed", e.ToString(), "#F56565");
            }
        }

        static void shipmentID()
        {
            try
            {
                LogStep("Lookup Shipment ID", "Start");
                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[1]/tbody/tr[2]/td/div/a")).Click();
                LogStep("Lookup Shipment ID", "Click Shipment ID link");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[1]/input")).SendKeys(caShipNbr);
                LogStep("Lookup Shipment ID", "Shipment id input");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input")).Click();
                LogStep("Lookup Shipment ID", "click submit button");

                LogStep("Lookup Shipment ID", "success");
            }
            catch (Exception e)
            {
                LogStep("Lookup Shipment ID: Failed", e.ToString(), "#F56565");
            }
        }

        static void shipmentIDHistory()
        {
            try
            {
                LogStep("Lookup Shipment ID History", "Start");
                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[1]/tbody/tr[2]/td/div/a")).Click();
                LogStep("Lookup Shipment ID History", "Click Shipment ID link");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[1]/input")).SendKeys(caShipNbr);
                LogStep("Lookup Shipment ID History", "Shipment id input");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input")).Click();
                LogStep("Lookup Shipment ID History", "click submit button");

                LogStep("Lookup Shipment ID History", "success");
            }
            catch (Exception e)
            {
                LogStep("Lookup Shipment ID History: Failed", e.ToString(), "#F56565");
            }
        }

        static void printFileViewer()
        {
            try
            {
                LogStep("Print from Viewer", "Start");
                shipmentID();
                driver.FindElement(By.XPath("/html/body/map/area[1]")).Click();
                LogStep("Print from Viewer", "Click print");
                LogStep("Print from Viewer", "Test Complete");
                driver.Navigate().Back();
                driver.Navigate().Back();
                driver.Navigate().Back();
            }
            catch (Exception e)
            {
                LogStep("Print from Viewer: Failed", e.ToString(), "#F56565");
            }
        }

        static void printAllPagesViewer()
        {
            try
            {
                LogStep("Print all pages from Viewer", "Start");
                shipmentID();
                driver.FindElement(By.XPath("/html/body/map/area[2]")).Click();
                LogStep("Print all pages from Viewer", "Click print all pages");
                LogStep("Print all pages from Viewer", "Test Complete");
                driver.Navigate().Back();
                driver.Navigate().Back();
                driver.Navigate().Back();
            }
            catch (Exception e)
            {
                LogStep("Print all pages from Viewer: Failed", e.ToString(), "#F56565");
            }
        }

        static void viewerFunction()
        {
            try
            {
                LogStep("Viewer Function", "Start");
                shipmentID();
                driver.FindElement(By.XPath("/html/body/map/area[3]")).Click(); LogStep("Viewer Function", "Click Zoom");
                driver.FindElement(By.XPath("/html/body/map/area[4]")).Click(); LogStep("Viewer Function", "Click Actual Size");
                driver.FindElement(By.XPath("/html/body/map/area[5]")).Click(); LogStep("Viewer Function", "Click Larger");
                driver.FindElement(By.XPath("/html/body/map/area[6]")).Click(); LogStep("Viewer Function", "Click Smaller");
                driver.FindElement(By.XPath("/html/body/map/area[7]")).Click(); LogStep("Viewer Function", "Click Fit Screen");
                driver.FindElement(By.XPath("/html/body/map/area[8]")).Click(); LogStep("Viewer Function", "Click Best Fit");
                driver.FindElement(By.XPath("/html/body/map/area[9]")).Click(); LogStep("Viewer Function", "Click Rotate Left");
                driver.FindElement(By.XPath("/html/body/map/area[10]")).Click(); LogStep("Viewer Function", "Click Rotate Right");
                LogStep("Viewer Function", "Test Complete");

                var searchButton = FluentWait(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
                searchButton.Click();
            }
            catch (Exception e)
            {
                LogStep("Viewer Function: Failed", e.ToString(), "#F56565");
            }
        }


        static void addUser()
        {
            try
            {
                LogStep("Add User", "Start");
                driver.FindElement(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[4]")).Click();
                LogStep("Add User", "Click User Link");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/input")).SendKeys(addNewUserFName);
                LogStep("Add User", "Input First Name");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[3]/td[2]/input")).SendKeys(addNewUserLName);
                LogStep("Add User", "Input Last Name");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[4]/td[2]/input")).SendKeys(addNewUser);
                LogStep("Add User", "Input UserName");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[5]/td[2]/input")).SendKeys(addNewUserPass);
                LogStep("Add User", "Input User Password");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[6]/td[2]/input")).SendKeys(addNewUserPass);
                LogStep("Add User", "Input Confirmed Password");

                var userGroup = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[7]/td[2]/select"));
                new SelectElement(userGroup).SelectByValue("2");
                LogStep("Add User", "Select User Group");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[8]/td[2]/input")).SendKeys("0000");
                LogStep("Add User", "Input Building ID");

                var userRegion = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[9]/td[2]/select"));
                new SelectElement(userRegion).SelectByValue("02");
                LogStep("Add User", "Select Region");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[10]/td[2]/input")).SendKeys("jeffrey.aguanta@conduent.com");
                LogStep("Add User", "Input Email Address");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[11]/td[2]/input")).SendKeys("jeffrey.aguanta@conduent.com");
                LogStep("Add User", "Input Confirmed Email Address");

                driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/center/input[1]")).Click();
                LogStep("Add User", "Click Add Key");

                var alert = WaitAlert(40); alert.Accept();
                var alert2 = WaitAlert(40); alert2.Accept();

                LogStep("Add User", "Test Complete");
            }
            catch (Exception e)
            {
                LogStep("Add User: Failed", e.ToString(), "#F56565");
            }
        }

    static void modifyUser()
    {
        try
        {
            LogStep("Modify User", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/center[1]/form/span[2]/font/input[1]")).SendKeys(addNewUser);
            LogStep("Modify User", "Input UserName");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/center[1]/form/span[2]/font/input[2]")).Click();
            LogStep("Modify User", "Click Submit Button");

            var fn = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[2]/td[2]/input");
            driver.FindElement(fn).Clear();
            driver.FindElement(fn).SendKeys("Updated First Name");
            LogStep("Modify User", "Input First Name");

            var ln = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[3]/td[2]/input");
            driver.FindElement(ln).Clear();
            driver.FindElement(ln).SendKeys("Selenium");
            LogStep("Modify User", "Input Last Name");

            var userGroup = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[6]/td[2]/select"));
            new SelectElement(userGroup).SelectByValue("2");
            LogStep("Modify User", "Select User Group");

            var bldg = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[8]/td[2]/input");
            driver.FindElement(bldg).Clear();
            driver.FindElement(bldg).SendKeys("0000");
            LogStep("Modify User", "Input Building ID");

            var reg = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[9]/td[2]/select"));
            new SelectElement(reg).SelectByValue("02");
            LogStep("Modify User", "Select Region");

            var email = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[10]/td[2]/input");
            driver.FindElement(email).Clear();
            driver.FindElement(email).SendKeys("jeffrey.aguanta@conduent.com");
            LogStep("Modify User", "Input Email Address");

            var email2 = By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/table/tbody/tr[11]/td[2]/input");
            driver.FindElement(email2).Clear();
            driver.FindElement(email2).SendKeys("jeffrey.aguanta@conduent.com");
            LogStep("Modify User", "Input Confirmed Email Address");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[1]/center/input[1]")).Click();
            LogStep("Modify User", "Click Modify Button");

            var alert = WaitAlert(40); alert.Accept();

            LogStep("Modify User", "Test Complete");
        }
        catch (Exception e)
        {
            LogStep("Modify User: Failed", e.ToString(), "#F56565");
        }
    }

    static void deleteUser()
    {
        try
        {
            LogStep("Delete User", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[2]/table/tbody/tr/td[2]/input")).SendKeys(addNewUser);
            LogStep("Delete User", "Input to be deleted User ID");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/form[2]/center/input[1]")).Click();
            LogStep("Delete User", "Click Delete Button");

            var a1 = WaitAlert(40); a1.Accept();
            var a2 = WaitAlert(40); a2.Accept();

            LogStep("Delete User", "Test Complete");
        }
        catch (Exception e)
        {
            LogStep("Delete User: Failed", e.ToString(), "#F56565");
        }
    }

    static void updateProfile()
    {
        try
        {
            LogStep("Update Profile", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[6]")).Click();
            LogStep("Update Profile", "Click Update Profile Link");

            var fn = By.XPath("/html/body/table/tbody/tr[2]/td/table/tbody/tr[3]/td/div/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input");
            driver.FindElement(fn).Clear();
            driver.FindElement(fn).SendKeys("ACS");
            LogStep("Update Profile", "Update First Name");

            var ln = By.XPath("/html/body/table/tbody/tr[2]/td/table/tbody/tr[3]/td/div/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/input");
            driver.FindElement(ln).Clear();
            driver.FindElement(ln).SendKeys("dis.helpdesk");
            LogStep("Update Profile", "Update Last Name");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td/table/tbody/tr[3]/td/div/table/tbody/tr[2]/td[2]/table/tbody/tr[10]/td[2]/input")).Click();
            LogStep("Update Profile", "Click Submit Button");

            LogStep("Update Profile", "Test Complete");
        }
        catch (Exception e)
        {
            LogStep("Update Profile: Failed", e.ToString(), "#F56565");
        }
    }


     static void Mwab()
    {
        try
        {
            LogStep("MWAB Lookup", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]")).Click();
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[1]/tbody/tr[10]/td/div/a")).Click();
            LogStep("MWAB Lookup", "Click MAWB Link");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input")).SendKeys(mawbNumber);
            LogStep("MWAB Lookup", "Input MAWB Number");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[7]/td[2]/input")).Click();
            LogStep("MWAB Lookup", "Click Submit Button");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/table/tbody/tr[2]/td[6]/a")).Click();
            LogStep("MWAB Lookup", "Click Add to Cart");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[2]")).Click();
            LogStep("MWAB Lookup", "Test Complete");

            var searchButton = FluentWait(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
            searchButton.Click();
        }
        catch (Exception e)
        {
            LogStep("MWAB Lookup: Failed", e.ToString(), "#F56565");
        }
    } 
 

    static void ImgSiteVolume()
    {
        try
        {
            LogStep("Image Site Volume - Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[2]/td/div/a")).Click();
            LogStep("Image Site Volume - Reports", "Link Clicked");

            var Buildingid = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input"));
            Buildingid.SendKeys(bldgID);
            LogStep("Image Site Volume - Reports", "Enter Building ID");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/img")).Click();
            LogStep("Image Site Volume - Reports", "Start Date Picker Clicked");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
            LogStep("Image Site Volume - Reports", "Click Start Date Picker < button");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
            LogStep("Image Site Volume - Reports", "Click Start Date Picker 4th friday button");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/img")).Click();
            LogStep("Image Site Volume - Reports", "Click End Date Picker");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
            LogStep("Image Site Volume - Reports", "Click End Date Picker < button");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
            LogStep("Image Site Volume - Reports", "Click Start Date Picker 4th friday button");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/select")).SendKeys("23:00");
            LogStep("Image Site Volume - Reports", "Input Start Time");

            var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[7]/td[2]/input"));
            submitButton.Click();
            LogStep("Image Site Volume - Reports", "submit button click");

            var totalCount = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/form/table[3]/tbody/tr/td[1]")).Text;
            if (totalCount == "Total Batches:0")
            {
                LogStep("Image Site Volume - Reports", "Test Complete with zero result");
            }
            else
            {
                LogStep("Image Site Volume - Reports", "Test Complete");
            }

            System.Threading.Thread.Sleep(1000);
            driver.Navigate().Back();
        }
        catch (Exception e)
        {
            LogStep("Image Site Volume - Reports: Failed", e.ToString(), "#F56565");
        }
    }


    static void ShipmentDetail()
    {
        try
        {
            LogStep("Shipment Detail - Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[3]/td/div/a")).Click();
            LogStep("Shipment Detail - Reports", "Link Clicked");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[1]/input")).SendKeys(usShipNbr);
            LogStep("Shipment Detail", "Input Shipment Number");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input")).Click();
            LogStep("Shipment Detail", "Submit Button Clicked");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/table/tbody/tr[3]/td[1]/a")).Click();
            LogStep("Shipment Detail", "Click Result");

            LogStep("Shipment Detail - Reports", "Test Complete");

            var searchButton = FluentWait(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
            searchButton.Click();
        }
        catch (Exception e)
        {
            LogStep("Shipment Detail - Reports: Failed", $"Please Check if this shipment number is still valid {usShipNbr}. {e}", "#F56565");
        }
    }

    static void PackageDetail()
    {
        try
        {
            LogStep("Package Detail - Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[4]/td/div/a")).Click();
            LogStep("Package Detail - Reports", "Link Clicked");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[1]/input")).SendKeys(packageID);
            LogStep("Package Detail", "Input Package ID");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input")).Click();
            LogStep("Package Detail", "Submit Button Clicked");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/table/tbody/tr[3]/td[1]/a")).Click();
            LogStep("Package Detail", "Click Result");

            LogStep("Package Detail - Reports", "Test Complete");

            var searchButton = FluentWait(By.XPath("/html/body/table/tbody/tr[1]/td/table/tbody/tr/td[3]/a[1]"));
            searchButton.Click();
        }
        catch (Exception e)
        {
            LogStep("Package Detail - Reports: Failed", $"Please Check if this Package ID number is still valid {packageID}. {e}", "#F56565");
        }
    }

    static void KeyedNoDocsImg()
    {
        try
        {
            LogStep("Keyed no Docs Imaged - Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[5]/td/div/a")).Click();
            LogStep("Keyed no Docs Imaged - Reports", "Link Clicked");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input")).SendKeys("US");
            LogStep("Keyed no Docs Imaged - Reports", "Input US on the Data Capture Country");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[13]/td[2]/input")).Click();
            LogStep("Keyed no Docs Imaged - Reports", "Submit Button Clicked");

            var totalCount = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/table[3]/tbody/tr/td[2]")).Text;

            if (totalCount == "0")
                LogStep("Keyed no Docs Imaged - Reports", "Test Complete with 0 result");
            else
                LogStep("Keyed no Docs Imaged - Reports", $"Test Complete with {totalCount} result");

            System.Threading.Thread.Sleep(10000);
            driver.Navigate().Back();
        }
        catch (Exception e)
        {
            LogStep("Keyed no Docs Imaged - Reports: Failed", e.ToString(), "#F56565");
        }
    }

    static void DocsImgNotKeyed()
    {
        try
        {
            LogStep("Docs Imaged not Keyed - Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[6]/td/div/a")).Click();
            LogStep("DocsImgNotKeyed - Reports", "Link Clicked");

            var Buildingid = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input"));
            Buildingid.SendKeys(bldgID);
            LogStep("DocsImgNotKeyed - Reports", "Enter Building ID");
        
            driver.FindElement(By.XPath("//html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[7]/td[2]/input")).Click();
            LogStep("DocsImgNotKeyed - Reports", "Submit Button Clicked");

            var totalCount = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td/font/table[3]/tbody/tr/td[2]")).Text;

            if (totalCount == "0")
                LogStep("Docs Imaged not Keyed - Reports", "Test Complete with 0 result");
            else
                LogStep("Docs Imaged not Keyed - Reports", $"Test Complete with {totalCount} result");

            System.Threading.Thread.Sleep(10000);
            driver.Navigate().Back();
        
        }
        catch (Exception e)
        {
            LogStep("Docs Imaged not Keyed - Reports: Failed", e.ToString(), "#F56565");
        }
    }

    static void OCRConfig()
    {
        try
        {
            LogStep("OCR Configuration - Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[18]/td/div/a")).Click();
            LogStep("OCR Report - Reports", "Link Clicked");

            var existingConfig = FluentWait(By.XPath("/html/body/div/div[2]/div[3]/div/div[2]/div[1]/text()")).Text;
            LogStep("OCR Configuration - Reports", existingConfig);
            LogStep("OCR Configuration - Reports", "Test Complete");
        }
        catch
        {
            LogStep("OCR Configuration - Reports", "Test Complete");
        }
    }


    static void OCRReport()
    {
        try
        {
            LogStep("OCR Report - Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[2]/tbody/tr[17]/td/div/a")).Click();
            LogStep("OCR Report - Reports", "Link Clicked");

            driver.SwitchTo().Frame(FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/iframe")));

            FluentWait(By.XPath("/html/body/form/div[2]/table/tbody/tr[2]/td[2]/select")).SendKeys("00:00");
            LogStep("OCR Report - Reports", "Input Start Time");

            FluentWait(By.XPath("/html/body/form/div[2]/table/tbody/tr[2]/td[3]/select")).SendKeys("01:00");
            LogStep("OCR Report - Reports", "Input End Time");

            var submitButton = driver.FindElement(By.XPath("/html/body/form/div[2]/table/tbody/tr[9]/td/button"));
            submitButton.Click();
            LogStep("OCR Report - Reports", "submit button click");
        
            System.Threading.Thread.Sleep(90000);
        
            if (FluentWait(By.XPath("/html/body/form/table/tbody/tr[1]/td[1]")) != null)
                LogStep("OCR Report - Reports", "Test Complete");
            else
                LogStep("OCR Report - Reports", "Test failed with 0 result", "#F56565");
        
            driver.Navigate().Back();

        }
        catch (Exception e)
        {
            LogStep("OCR Report - Reports: Test Fail", e.ToString(), "#F56565");
            try { driver.Navigate().Back(); } catch { }
        }
    }

    static void IDISSummary()
    {
        try
        {
            LogStep("IDIS Summary - Admin Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[2]/td/div/a")).Click();
            LogStep("IDIS Summary - Admin Reports", "Link Clicked");

            var Buildingid = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[4]/td[2]/input"));
            Buildingid.SendKeys(bldgID);
            LogStep("IDIS Summary - Admin Reports", "Enter Building ID");

            var cntryCode = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/input"));
            cntryCode.SendKeys("US");
            LogStep("IDIS Summary - Admin Reports", "Enter Country Code");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/img")).Click();
            LogStep("IDIS Summary - Admin Reports", "Start Date Picker Clicked");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
            LogStep("IDIS Summary - Admin Reports", "Click Start Date Picker < button");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
            LogStep("IDIS Summary - Admin Reports", "Click Start Date Picker 4th friday button");

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/img")).Click();
            LogStep("IDIS Summary - Admin Reports", "Click End Date Picker");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
            LogStep("IDIS Summary - Admin Reports", "Click End Date Picker < button");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
            LogStep("IDIS Summary - Admin Reports", "Click Start Date Picker 4th friday button");

            var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[7]/td[2]/input"));
            submitButton.Click();
            LogStep("IDIS Summary - Admin Reports", "submit button click");

            var searchResult = FluentWait(By.XPath("/html/body/table[2]/tbody/tr[2]/td/table/tbody/tr[5]/td[1]"));

            if (bldgID == searchResult.Text)
                LogStep("IDIS Summary - Admin Reports", "Test Complete");
            else
                LogStep("IDIS Summary - Admin Reports", "Test Complete with no result rendered");

            FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
        }
        catch (Exception e)
        {
            LogStep("IDIS Summary - Admin Reports: Test Failed", e.ToString(), "#F56565");
            try { driver.Navigate().Back(); } catch { }
        }
    }

    static void SuspendedSitesUT()
    {
        try
        {
            LogStep("Suspended Site (UT) - Admin Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[3]/td/div/a")).Click();
            LogStep("Suspended Site (UT) - Admin Reports", "Link Clicked");

            var cntryCode = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input"));
            cntryCode.SendKeys("US");
            LogStep("Suspended Site (UT) - Admin Reports", "Enter Country Code");

            var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input"));
            submitButton.Click();
            LogStep("Suspended Site (UT) - Admin Reports", "submit button click");

            var searchResult = FluentWait(By.XPath("/html/body/table[2]/tbody/tr[2]/td/table/tbody/tr[3]/td[2]"));
            if (string.IsNullOrEmpty(searchResult.Text))
                LogStep("Suspended Site (UT) - Admin Reports", "Test Complete with no result rendered");
            else
                LogStep("Suspended Site (UT) - Admin Reports", "Test Complete");

            FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
        }
        catch (Exception e)
        {
            LogStep("Suspended Site (UT) - Admin Reports: Test Failed", e.ToString(), "#F56565");
            try { driver.Navigate().Back(); } catch { }
        }
    }

    static void SuspendedSitesTX()
    {
        try
        {
            LogStep("Suspended Site (TX) - Admin Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[4]/td/div/a")).Click();
            LogStep("Suspended Site (TX) - Admin Reports", "Link Clicked");

            var cntryCode = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/input"));
            cntryCode.SendKeys("US");
            LogStep("Suspended Site (TX) - Admin Reports", "Enter Country Code");

            var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input"));
            submitButton.Click();
            LogStep("Suspended Site (TX) - Admin Reports", "submit button click");

            var searchResult = FluentWait(By.XPath("/html/body/table[2]/tbody/tr[2]/td/table/tbody/tr[3]/td[2]"));
            if (searchResult.Text == "0")
                LogStep("Suspended Site (TX) - Admin Reports", "Test Complete with no result rendered");
            else
                LogStep("Suspended Site (TX) - Admin Reports", "Test Complete");

            FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();
        }
        catch (Exception e)
        {
            LogStep("Suspended Site (TX) - Admin Reports: Test Failed", e.ToString(), "#F56565");
            try { driver.Navigate().Back(); } catch { }
        }
    }

    static void SitesNoImg()
    {
        try
        {
            LogStep("Sites That Did Not Image - Admin Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[5]/td/div/a")).Click();
            LogStep("Sites That Did Not Image - Admin Reports", "Link Clicked");

            /*       var reg = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/input"));
            reg.SendKeys(region);
            LogStep("Sites That Did Not Image - Admin Reports", "Enter Region");

            var dist = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[3]/td[2]/input"));
            dist.SendKeys(district);
            LogStep("Sites That Did Not Image - Admin Reports", "Enter District"); */

            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[1]/td[2]/img")).Click();
            LogStep("Sites That Did Not Image - Admin Report", "Date Picker Clicked");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[1]/td[1]/a[2]/font")).Click();
            LogStep("Sites That Did Not Image - Admin Reports", "Click Date Picker < button");

            driver.FindElement(By.XPath("/html/body/div/form/table/tbody/tr[1]/td/table/tbody/tr[6]/td[6]/font/a/b")).Click();
            LogStep("Sites That Did Not Image - Admin Report", "Click Date Picker 4th friday button");

            var submitButton = driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/form/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td[2]/table/tbody/tr[4]/td[2]/input"));
            submitButton.Click();
            LogStep("Sites That Did Not Image - Admin Reports", "submit button click");

            var searchResult = FluentWait(By.XPath("/html/body/table[2]/tbody/tr[2]/td/table/tbody/tr[3]/td[2]"));
            if (searchResult.Text == "07")
                LogStep("Sites That Did Not Image - Admin Reports", "Test Complete");
            else
                LogStep("Sites That Did Not Image - Admin Reports", "Test Complete with no result rendered");

            FluentWait(By.XPath("/html/body/table[1]/tbody/tr/td/table/tbody/tr/td[3]/a[2]")).Click();

            LogStep("Sites That Did Not Image - Admin Reports", "Test Complete");
        }
        catch (Exception e)
        {
            LogStep("Sites That Did Not Image - Admin Reports: Test Failed", e.ToString(), "#F56565");
            try { driver.Navigate().Back(); } catch { }
        }
    }

    static void BuildingConfig()
    {
        try
        {
            LogStep("Buildings Configuration - Admin Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[6]/td/div/a")).Click();
            LogStep("Buildings Configuration - Admin Reports", "Link Clicked");

            driver.SwitchTo().Frame(FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/iframe")));
		
    		Thread.Sleep(60000);
		
            /*var searchResult = FluentWait(By.XPath("/html/body/form/table/tbody/tr[1]/td[1]"));
    		if (searchResult.Text == "us")
                LogStep("Buildings Configuration - Admin Reports", "Test Complete");
            else
                LogStep("Buildings Configuration - Admin Reports", "Test Failed", "#F56565");*/
		
    		if (FluentWait(By.XPath("/html/body/form/table/tbody/tr[1]/td[1]")) != null)
    		    LogStep("Buildings Configuration - Admin Reports", "Test Complete");
            else
                LogStep("Buildings Configuration - Admin Reports", "Test Failed", "#F56565");
			
            driver.Navigate().Back();
        }
        catch (Exception e)
        {
            LogStep("Buildings Configuration - Admin Reports: Test Failed", e.ToString(), "#F56565");
            try { driver.Navigate().Back(); } catch { }
        }
    }

    static void AutoClass()
    {
        try
        {
            LogStep("Auto Classification - Admin Reports", "Start");
            driver.FindElement(By.XPath("/html/body/table/tbody/tr[2]/td[1]/table[4]/tbody/tr[7]/td/div/a")).Click();
            LogStep("Auto Classification - Admin Reports", "Link Clicked");

            driver.SwitchTo().Frame(FluentWait(By.XPath("/html/body/table/tbody/tr[2]/td[2]/table/tbody/tr[2]/td/iframe")));
        
            Thread.Sleep(60000);
        
            var searchResult = WaitForVisible(By.XPath("/html/body/app-root/app-odcconfiguration/div/div/div[1]/table/tbody/tr[1]/td[2]"));
            if (searchResult.Text == "us")
                LogStep("Auto Classification - Admin Reports", "Test Complete");
            else
                LogStep("Auto Classification - Admin Reports", "Test Failed", "#F56565");

            driver.Navigate().Back();
        
        }
        catch (Exception e)
        {
            LogStep("Auto Classification - Admin Reports: Test Failed", e.ToString(), "#F56565");
            try { driver.Navigate().Back(); } catch { }
        }
    }
}

// 15. IDIS TEST RUNNER
public class IdisTestRunner
{
    private readonly ConfigurationManager _config;
    private readonly ExecutionLogger _logger;
    private readonly EdgeDriverManager _edgeMgr;
    private readonly Action<string> _progress;
    private readonly List<TestStepResult> _allSteps = new List<TestStepResult>();

    public IdisTestRunner(ConfigurationManager config, ExecutionLogger logger, Action<string> progress)
    {
        _config = config;
        _logger = logger;
        _progress = progress;
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
            var env = RunEnvironment(urlProfile);
            summary.Environments.Add(env);
            if (env.Status != "PASSED") summary.GlobalStatus = "FAILED";
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

        // Set up legacy globals
        LegacyTests.cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Populate cfg from config
        LegacyTests.cfg["url"] = urlProfile.Url;
        LegacyTests.cfg["usrName"] = _config.Username;
        LegacyTests.cfg["usrPass"] = _config.Password;
        LegacyTests.cfg["usrNameTxtBox"] = _config.GetRaw("usrNameTxtBox");
        LegacyTests.cfg["usrPassTxtBox"] = _config.GetRaw("usrPassTxtBox");
        LegacyTests.cfg["loginBtn"] = _config.GetRaw("loginBtn");
        LegacyTests.cfg["caShipmentNbr"] = _config.CaShipmentNbr;
        LegacyTests.cfg["usShipmentNbr"] = _config.UsShipmentNbr;
        LegacyTests.cfg["addUser"] = _config.AddUser;
        LegacyTests.cfg["addUserPass"] = _config.AddUserPass;
        LegacyTests.cfg["firstName"] = _config.FirstName;
        LegacyTests.cfg["lastName"] = _config.LastName;
        LegacyTests.cfg["mawbNbr"] = _config.MawbNbr;
        LegacyTests.cfg["bldgID"] = _config.BldgID;
        LegacyTests.cfg["packageID"] = _config.PackageID;
        LegacyTests.cfg["region"] = _config.Region;
        LegacyTests.cfg["district"] = _config.District;
        LegacyTests.cfg["username"] = _config.Username;
        LegacyTests.cfg["password"] = _config.Password;

        LegacyTests.logFolder = ProgramSettings.LogFolder;
        LegacyTests.logPath = Path.Combine(ProgramSettings.LogFolder, ProgramSettings.TestName + "WebApplication.txt");
        LegacyTests.accumulatedSteps = new List<InternalStepLog>();

        LegacyTests.baseUrl = urlProfile.Url;
        LegacyTests.usrName = _config.Username;
        LegacyTests.usrPass = _config.Password;
        LegacyTests.usrNameTxtBox = _config.GetRaw("usrNameTxtBox");
        LegacyTests.usrPassTxtBox = _config.GetRaw("usrPassTxtBox");
        LegacyTests.loginBtn = _config.GetRaw("loginBtn");
        LegacyTests.caShipNbr = _config.CaShipmentNbr;
        LegacyTests.usShipNbr = _config.UsShipmentNbr;
        LegacyTests.addNewUser = _config.AddUser;
        LegacyTests.addNewUserPass = _config.AddUserPass;
        LegacyTests.addNewUserFName = _config.FirstName;
        LegacyTests.addNewUserLName = _config.LastName;
        LegacyTests.mawbNumber = _config.MawbNbr;
        LegacyTests.bldgID = _config.BldgID;
        LegacyTests.packageID = _config.PackageID;
        LegacyTests.region = _config.Region;
        LegacyTests.district = _config.District;

        EdgeDriver driver = null;
        WebDriverWait wait = null;
        try
        {
            driver = _edgeMgr.CreateDriver(_logger);
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(_config.PageLoadTimeoutSeconds);
            driver.Manage().Window.Size = new System.Drawing.Size(1400, 900);
            wait = new WebDriverWait(driver, TimeSpan.FromSeconds(_config.ElementTimeoutSeconds));

            LegacyTests.driver = driver;
            LegacyTests.wait = wait;

            var testSteps = new (string Name, Action Action)[]
            {
                ("Login", LegacyTests.loginCase),
                ("Print Button", LegacyTests.printButton),
                ("Print to File", LegacyTests.printToFile),
                ("Email Button", LegacyTests.emailButton),
                ("Print from Viewer", LegacyTests.printFileViewer),
                ("Viewer Function", LegacyTests.viewerFunction),
                ("Add User", LegacyTests.addUser),
                ("Modify User", LegacyTests.modifyUser),
                ("Delete User", LegacyTests.deleteUser),
                ("Update Profile", LegacyTests.updateProfile),
                ("MWAB Lookup", LegacyTests.Mwab),
                ("Image Site Volume", LegacyTests.ImgSiteVolume),
                ("Shipment Detail", LegacyTests.ShipmentDetail),
                ("Package Detail", LegacyTests.PackageDetail),
                ("Keyed no Docs Imaged", LegacyTests.KeyedNoDocsImg),
                ("OCR Configuration", LegacyTests.OCRConfig),
                ("OCR Report", LegacyTests.OCRReport),
                ("Suspended Site (UT)", LegacyTests.SuspendedSitesUT),
                ("Suspended Site (TX)", LegacyTests.SuspendedSitesTX),
                ("Sites That Did Not Image", LegacyTests.SitesNoImg),
                ("Buildings Configuration", LegacyTests.BuildingConfig),
                ("Auto Classification", LegacyTests.AutoClass),
                ("IDIS Summary", LegacyTests.IDISSummary)
            };

            foreach (var (name, action) in testSteps)
            {
                LegacyTests.currentStepFailed = false;
                var stepResult = new TestStepResult
                {
                    EnvironmentName = urlProfile.WebKey,
                    StepName = name
                };
                try
                {
                    _progress?.Invoke($"{urlProfile.WebKey} / {name}: starting");
                    action();
                    stepResult.Status = LegacyTests.currentStepFailed ? "FAILED" : "PASSED";
                    stepResult.Details = LegacyTests.currentStepFailed ? "Step reported failure internally." : "Executed successfully.";
                    _logger.LogStep(stepResult);
                    _progress?.Invoke($"{urlProfile.WebKey} / {name}: {stepResult.Status}");
                }
                catch (Exception ex)
                {
                    stepResult.Status = "FAILED";
                    stepResult.Details = _config.Redactor.Redact(ex.Message);
                    var ssName = $"{urlProfile.WebKey}_{name.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.png".Replace("/", "_");
                    var ssPath = ScreenshotHelper.Capture(driver, Path.Combine(ProgramSettings.LogFolder, "Screenshots"), ssName);
                    stepResult.ScreenshotPath = string.IsNullOrEmpty(ssPath) ? "" : Path.Combine("Screenshots", ssName);
                    env.Status = "FAILED";
                    env.ErrorMessage = _config.Redactor.Redact(ex.Message);
                    _logger.LogStep(stepResult);
                    _logger.LogError($"{urlProfile.WebKey} / {name}", ex);
                    _progress?.Invoke($"{urlProfile.WebKey} / {name}: FAILED - {stepResult.Details}");
                }
                _allSteps.Add(stepResult);
                if (stepResult.Status == "FAILED") env.Status = "FAILED";
            }
        }
        catch (EdgeDriverInitializationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            env.Status = "FAILED";
            env.ErrorMessage = _config.Redactor.Redact(ex.Message);
            _logger.LogError($"Environment {urlProfile.WebKey}", ex);
        }
        finally
        {
            try { driver?.Quit(); } catch { }
        }
        env.EndTime = DateTimeOffset.Now;
        return env;
    }

    public List<TestStepResult> GetAllSteps() => _allSteps;
}

// 16. MAIN
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

    var runner = new IdisTestRunner(config, logger, message => { message.Dump(); });
    RunSummary summary;
    try
    {
        summary = runner.Run(mode);
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
